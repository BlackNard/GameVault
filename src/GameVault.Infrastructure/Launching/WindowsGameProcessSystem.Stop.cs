using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameVault.Infrastructure.Launching;

// Closing and ending processes for Stop. Every action names a process by number and start time, so a reused number is never touched.
public sealed partial class WindowsGameProcessSystem : IGameProcessControl
{
    private const int AccessDenied = 5, NotThere = 87;
    private const uint CloseMessage = 0x0010, Terminate = 0x0001, Synchronize = 0x00100000, QueryLimited = 0x1000;

    public RunningProcess[] Running()
    {
        var (entries, parents) = Snapshot();
        var running = new List<RunningProcess>();
        foreach (var item in entries)
        {
            using var process = Native.OpenProcess(QueryLimited, false, item.Id);
            // A process Windows will not describe to us cannot be one of ours.
            if (process.IsInvalid || !Native.GetProcessTimes(process, out var created, out _, out _, out _)) continue;
            running.Add(new(new(new(item.Id, DateTime.FromFileTimeUtc(created).Ticks), Ancestors(item.Parent, parents)), item.Name));
        }
        return [.. running];
    }

    public bool RequestClose(ProcessIdentity identity)
    {
        using (var process = Open(identity, QueryLimited)) if (process is null) return false;
        var windows = new List<IntPtr>();
        Native.EnumWindows((window, _) =>
        {
            Native.GetWindowThreadProcessId(window, out var owner);
            if (owner == identity.Id && Native.IsWindowVisible(window)) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        var asked = false;
        foreach (var window in windows)
        {
            if (Native.PostMessage(window, CloseMessage, IntPtr.Zero, IntPtr.Zero)) { asked = true; continue; }
            var error = Marshal.GetLastWin32Error();
            if (error == AccessDenied) throw new Win32Exception(error, "Windows does not let a normal program close an administrator game.");
        }
        return asked;
    }

    public void End(ProcessIdentity identity)
    {
        using var process = Open(identity, Terminate | Synchronize | QueryLimited);
        if (process is null || Native.WaitForSingleObject(process, 0) == 0) return;
        if (Native.TerminateProcess(process, 1)) return;
        var error = Marshal.GetLastWin32Error();
        if (Native.WaitForSingleObject(process, 0) != 0) throw new Win32Exception(error, "Windows could not end the game process.");
    }

    public Task EndElevatedAsync(bool force, ProcessIdentity[] processes, CancellationToken token) => ElevationBroker.StopElevatedAsync(force, processes, token);

    /// <summary>The live process with exactly this identity, or nothing when it ended.</summary>
    private static SafeProcessHandle? Open(ProcessIdentity identity, uint access)
    {
        var process = Native.OpenProcess(access, false, identity.Id);
        if (process.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); process.Dispose();
            if (error is NotThere or 6) return null;
            throw new Win32Exception(error, "Windows could not open the game process.");
        }
        if (Native.GetProcessTimes(process, out var created, out _, out _, out _) && DateTime.FromFileTimeUtc(created).Ticks == identity.CreatedUtcTicks) return process;
        process.Dispose();
        return null;
    }

    private static partial class Native
    {
        public delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wide, IntPtr value);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    }
}
