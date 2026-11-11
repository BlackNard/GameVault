using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace GameVault.Infrastructure.Launching;

[SupportedOSPlatform("windows")]
internal sealed class WindowsObservedProcess : IGameProcess
{
    internal SafeProcessHandle Handle { get; }
    public ProcessIdentity Identity { get; }

    internal WindowsObservedProcess(SafeProcessHandle handle)
    {
        Handle = handle;
        if (!Native.GetProcessTimes(handle, out var created, out _, out _, out _))
            throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "Windows could not inspect the game process.");
        Identity = new((int)Native.GetProcessId(handle), DateTime.FromFileTimeUtc(created).Ticks);
    }

    internal static WindowsObservedProcess? Open(ProcessIdentity identity)
    {
        var handle = Native.OpenProcess(0x00101000, false, identity.Id); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error is 87 or 6) return null;
            throw NativeLaunchErrors.Create(error, "Windows could not observe the game process.");
        }
        try
        {
            var result = new WindowsObservedProcess(handle);
            if (result.Identity == identity && !result.HasExited) return result;
            handle.Dispose(); return null;
        }
        catch { handle.Dispose(); throw; }
    }

    public bool HasExited => Native.WaitForSingleObject(Handle, 0) switch
    {
        0 => true,
        258 => false,
        _ => throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "Windows could not check whether the game stopped.")
    };

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        while (!HasExited) await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose() => Handle.Dispose();

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetProcessId(SafeProcessHandle process);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    }
}

internal static class NativeLaunchErrors
{
    internal static Win32Exception Create(int code, string context) => new(code, $"{context} Windows error {code}: {new Win32Exception(code).Message}");
}
