using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GameVault.Infrastructure.Launching;

// A private Windows job records descendants across intermediate-parent exits.
// It has no limits and NEVER sets JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessGroup : IGameProcess
{
    private readonly Process process;
    private readonly SafeFileHandle job;

    private WindowsProcessGroup(Process process, SafeFileHandle job)
    {
        this.process = process; this.job = job;
        _ = process.SafeHandle;
        Identity = new(process.Id, process.StartTime.ToUniversalTime().Ticks);
    }

    public ProcessIdentity Identity { get; }
    public bool HasExited => process.HasExited;
    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

    public bool Owns(ProcessCandidate candidate)
    {
        if (candidate.Identity.CreatedUtcTicks < Identity.CreatedUtcTicks) return false;
        try
        {
            using var child = Process.GetProcessById(candidate.Identity.Id);
            if (child.StartTime.ToUniversalTime().Ticks != candidate.Identity.CreatedUtcTicks || child.HasExited) return false;
            if (!Native.IsProcessInJob(child.SafeHandle, job, out var belongs))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not verify the game's process group.");
            return belongs;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    public static IGameProcess Start(string executable, string workingDirectory)
    {
        var job = Native.CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not prepare game observation."); }
        Process? process = null;
        var transferred = false;
        try
        {
            var startup = new Native.StartupInfo { Size = Marshal.SizeOf<Native.StartupInfo>() };
            // Assign before user code runs, preventing fast launcher/helper chains from escaping observation.
            if (!Native.CreateProcess(executable, new StringBuilder('"' + executable + '"'), IntPtr.Zero, IntPtr.Zero,
                false, 0x00000004, IntPtr.Zero, workingDirectory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not launch the game. Check its executable and permissions.");
            using var nativeProcess = new SafeProcessHandle(info.Process, ownsHandle: true);
            using var thread = new SafeWaitHandle(info.Thread, ownsHandle: true);
            try
            {
                process = Process.GetProcessById((int)info.ProcessId);
                _ = process.SafeHandle;
                if (!Native.AssignProcessToJobObject(job, nativeProcess))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The program started, but Windows could not attach game observation. It will be left open.");
                var result = new WindowsProcessGroup(process, job);
                transferred = true;
                return result;
            }
            finally
            {
                // Even an observation/setup failure must resume and leave the program alive.
                if (Native.ResumeThread(thread) == uint.MaxValue)
                {
                    transferred = false;
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not resume the newly created program.");
                }
            }
        }
        finally
        {
            if (!transferred) { process?.Dispose(); job.Dispose(); }
        }
    }

    public void Dispose() { process.Dispose(); job.Dispose(); }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct StartupInfo
        {
            public int Size;
            public IntPtr Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XChars, YChars, FillAttribute, Flags;
            public ushort ShowWindow, ReservedBytes;
            public IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags,
            IntPtr environment, string currentDirectory, ref StartupInfo startup, out ProcessInfo information);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool belongs);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern uint ResumeThread(SafeWaitHandle thread);
    }
}
