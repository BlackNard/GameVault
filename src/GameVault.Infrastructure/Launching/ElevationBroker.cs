using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace GameVault.Infrastructure.Launching;

internal sealed record BrokerRequest(string Executable, string WorkingDirectory);
internal sealed record BrokerHandles(long ProcessHandle, long JobHandle, ProcessIdentity Identity);
internal sealed record BrokerResponse(BrokerHandles? Handles, string? Error, int NativeError = 0);

[SupportedOSPlatform("windows")]
public static class ElevationBroker
{
    internal static IGameProcess Launch(string executable, string workingDirectory) => LaunchWithHelper(executable, workingDirectory, (pipe, parent) =>
    {
        var app = Environment.ProcessPath ?? throw new IOException("GameVault's executable could not be located.");
        var start = new ProcessStartInfo(app) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--launch-helper"); start.ArgumentList.Add(pipe); start.ArgumentList.Add(parent.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(start) ?? throw new IOException("Windows did not start the administrator launch helper.");
    });

    internal static IGameProcess LaunchWithHelper(string executable, string workingDirectory, Func<string, int, Process> startHelper) =>
        LaunchAsync(executable, workingDirectory, startHelper).GetAwaiter().GetResult();

    private static async Task<IGameProcess> LaunchAsync(string executable, string workingDirectory, Func<string, int, Process> startHelper)
    {
        var name = "GameVault.Elevation." + Guid.NewGuid().ToString("N");
        using var pipe = BrokerPipe.Create(name);
        using var helper = startHelper(name, Environment.ProcessId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var peer) || peer != helper.Id)
                throw new IOException("The launch helper's identity could not be verified.");
            await BrokerWire.Send(pipe, new BrokerRequest(executable, workingDirectory), timeout.Token).ConfigureAwait(false);
            var response = await BrokerWire.Read<BrokerResponse>(pipe, timeout.Token).ConfigureAwait(false);
            if (response.Handles is not BrokerHandles handles)
            {
                if (response.NativeError != 0) throw new Win32Exception(response.NativeError, response.Error);
                throw new IOException(response.Error ?? "The administrator launch helper could not launch the game.");
            }
            // Until the helper confirms transfer, it alone is responsible for reclaiming its copies.
            await BrokerWire.Send(pipe, true, timeout.Token).ConfigureAwait(false);
            if (!await BrokerWire.Read<bool>(pipe, timeout.Token).ConfigureAwait(false)) throw new IOException("Game observation transfer was not confirmed.");
            return WindowsProcessGroup.FromTransferred(handles);
        }
        catch (OperationCanceledException)
        { throw new IOException("The administrator launch helper timed out. Check whether the game opened before trying Play again."); }
        catch (EndOfStreamException error)
        { throw new IOException("The administrator launch helper disconnected. Check whether the game opened before trying Play again.", error); }
    }

    // Called only by the dedicated helper entry point; it does not initialize any GameVault data.
    public static async Task<int> RunHelperAsync(string pipeName, int parentId)
    {
        if (!pipeName.StartsWith("GameVault.Elevation.", StringComparison.Ordinal) ||
            !Guid.TryParseExact(pipeName["GameVault.Elevation.".Length..], "N", out _) || parentId <= 0) return 1;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var parent = Native.OpenProcess(0x0040, false, parentId); // Duplicate handles into the exact normal parent.
        if (parent.IsInvalid) return 1;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        WindowsProcessGroup? game = null;
        BrokerHandles? handles = null;
        var transferred = false;
        var responseSent = false;
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var peer) || peer != parentId) return 1;
            var request = await BrokerWire.Read<BrokerRequest>(pipe, timeout.Token).ConfigureAwait(false);
            if (!Path.IsPathFullyQualified(request.Executable) || !File.Exists(request.Executable) ||
                !string.Equals(Path.GetExtension(request.Executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !Path.IsPathFullyQualified(request.WorkingDirectory) || !Directory.Exists(request.WorkingDirectory))
                throw new IOException("The game executable or working directory is invalid.");
            game = (WindowsProcessGroup)WindowsProcessGroup.Start(request.Executable, request.WorkingDirectory);
            handles = game.DuplicateForParent(parent);
            await BrokerWire.Send(pipe, new BrokerResponse(handles, null), timeout.Token).ConfigureAwait(false);
            responseSent = true;
            if (!await BrokerWire.Read<bool>(pipe, timeout.Token).ConfigureAwait(false)) return 1;
            // Commit before sending confirmation: a broken connection may retain read-only handles in
            // the parent until it exits, but must never close a possibly reused handle in that process.
            transferred = true;
            await BrokerWire.Send(pipe, true, timeout.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or JsonException or OperationCanceledException)
        {
            if (pipe.IsConnected && !responseSent)
            {
                try { await BrokerWire.Send(pipe, new BrokerResponse(null, error.Message, (error as Win32Exception)?.NativeErrorCode ?? 0), timeout.Token).ConfigureAwait(false); }
                catch (Exception sendError) when (sendError is IOException or OperationCanceledException) { }
            }
            return 1;
        }
        finally
        {
            if (!transferred && handles is not null)
            { Reclaim(parent, handles.ProcessHandle); Reclaim(parent, handles.JobHandle); }
            game?.Dispose(); // Observation only: no kill-on-close flags, no game termination.
        }
    }

    internal static void Reclaim(SafeProcessHandle parent, long handle)
    {
        if (Native.ReclaimHandle(parent, new IntPtr(handle), new IntPtr(-1), out var copy, 0, false, 3)) copy.Dispose();
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int id);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int id);
        [DllImport("kernel32.dll", EntryPoint = "DuplicateHandle", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReclaimHandle(SafeProcessHandle sourceProcess, IntPtr source,
            IntPtr destination, out SafeFileHandle copy, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    }
}

internal static class BrokerWire
{
    private const int MaximumMessageBytes = 128 * 1024;
    internal static async Task Send<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumMessageBytes) throw new IOException("Launch-helper message is too large.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    internal static async Task<T> Read<T>(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        var length = BitConverter.ToInt32(prefix);
        if (length <= 0 || length > MaximumMessageBytes) throw new IOException("Invalid launch-helper message size.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        try { return JsonSerializer.Deserialize<T>(bytes) ?? throw new IOException("Invalid launch-helper message."); }
        catch (JsonException error) { throw new IOException("Invalid launch-helper message.", error); }
    }
}
