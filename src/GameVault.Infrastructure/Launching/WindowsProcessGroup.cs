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
    private readonly WindowsObservedProcess process;
    private readonly SafeFileHandle job;

    private WindowsProcessGroup(WindowsObservedProcess process, SafeFileHandle job, string? groupName)
    {
        this.process = process; this.job = job;
        Identity = process.Identity;
        RecoveryGroupName = groupName;
    }

    public ProcessIdentity Identity { get; }
    public string? RecoveryGroupName { get; }
    public bool HasExited => process.HasExited;
    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

    public bool Owns(ProcessCandidate candidate)
    {
        try
        {
            using var child = WindowsObservedProcess.Open(candidate.Identity);
            if (child is null) return false;
            if (!Native.IsProcessInJob(child.Handle, job, out var belongs))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not verify the game's process group.");
            return belongs;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    public static IGameProcess Start(string executable, string workingDirectory) => Start(executable, workingDirectory, "");

    /// <summary>Starts the program with a fixed argument string. The string goes straight to CreateProcess: no shell, no expansion.</summary>
    public static IGameProcess Start(string executable, string workingDirectory, string arguments)
    {
        var groupName = "Local\\GameVault.Game." + Guid.NewGuid().ToString("N");
        using var security = new WindowsUserSecurity("GA");
        var attributes = security.Attributes;
        var job = Native.CreateJobObject(ref attributes, groupName);
        if (job.IsInvalid) { var error = Marshal.GetLastWin32Error(); job.Dispose(); throw NativeLaunchErrors.Create(error, "Windows could not prepare game observation."); }
        SafeProcessHandle? nativeProcess = null;
        var transferred = false;
        try
        {
            var startup = new Native.StartupInfo { Size = Marshal.SizeOf<Native.StartupInfo>() };
            // Assign before user code runs, preventing fast launcher/helper chains from escaping observation.
            if (!Native.CreateProcess(executable, new StringBuilder('"' + executable + '"' + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments)), IntPtr.Zero, IntPtr.Zero,
                false, 0x00000004, IntPtr.Zero, workingDirectory, ref startup, out var info))
                throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "Windows could not launch the game.");
            nativeProcess = new SafeProcessHandle(info.Process, ownsHandle: true);
            using var thread = new SafeWaitHandle(info.Thread, ownsHandle: true);
            try
            {
                if (!Native.AssignProcessToJobObject(job, nativeProcess))
                    throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "The program started, but Windows could not attach observation. It will be left open.");
                var result = new WindowsProcessGroup(new WindowsObservedProcess(nativeProcess), job, groupName);
                transferred = true;
                return result;
            }
            finally
            {
                // Even an observation/setup failure must resume and leave the program alive.
                if (Native.ResumeThread(thread) == uint.MaxValue)
                {
                    transferred = false;
                    throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "Windows could not resume the newly created program.");
                }
            }
        }
        finally
        {
            if (!transferred) { nativeProcess?.Dispose(); job.Dispose(); }
        }
    }

    public void Dispose() { process.Dispose(); job.Dispose(); }

    internal BrokerHandles DuplicateForParent(SafeProcessHandle parent)
    {
        if (!Native.DuplicateHandle(new IntPtr(-1), process.Handle, parent, out var processCopy, 0x00101000, false, 0))
            throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "Windows could not transfer game observation.");
        if (!Native.DuplicateHandle(new IntPtr(-1), job, parent, out var jobCopy, 4, false, 0))
        {
            var error = Marshal.GetLastWin32Error();
            ElevationBroker.Reclaim(parent, processCopy.ToInt64());
            throw NativeLaunchErrors.Create(error, "Windows could not transfer process-group observation.");
        }
        return new(processCopy.ToInt64(), jobCopy.ToInt64(), Identity, RecoveryGroupName);
    }

    internal static IGameProcess FromTransferred(BrokerHandles handles)
    {
        var process = new SafeProcessHandle(new IntPtr(handles.ProcessHandle), true);
        var job = new SafeFileHandle(new IntPtr(handles.JobHandle), true);
        try
        {
            var result = new WindowsProcessGroup(new WindowsObservedProcess(process), job, handles.GroupName);
            if (result.Identity != handles.Identity) throw new IOException("The transferred game process identity did not match.");
            return result;
        }
        catch { process.Dispose(); job.Dispose(); throw; }
    }

    internal static IGameProcess? Recover(string groupName, ProcessCandidate[] candidates)
    {
        const string prefix = "Local\\GameVault.Game.";
        if (!groupName.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(groupName[prefix.Length..], "N", out _)) return null;
        var job = Native.OpenJobObject(4, false, groupName);
        if (job.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); job.Dispose();
            if (error == 2) return null;
            throw NativeLaunchErrors.Create(error, "The saved game process group could not be reopened.");
        }
        var transferred = false;
        try
        {
            foreach (var candidate in candidates)
            {
                var process = WindowsObservedProcess.Open(candidate.Identity);
                if (process is null) continue;
                try
                {
                    if (!Native.IsProcessInJob(process.Handle, job, out var belongs)) throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "Process-group recovery could not be verified.");
                    if (!belongs) continue;
                    transferred = true;
                    return new WindowsProcessGroup(process, job, groupName);
                }
                finally { if (!transferred) process.Dispose(); }
            }
            return null;
        }
        finally { if (!transferred) job.Dispose(); }
    }

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
        public static extern SafeFileHandle CreateJobObject(ref WindowsUserSecurity.SecurityAttributes attributes, string? name);
        [DllImport("kernel32.dll", EntryPoint = "OpenJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle OpenJobObject(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
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
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool DuplicateHandle(IntPtr sourceProcess, SafeHandle source, SafeProcessHandle destination,
            out IntPtr copy, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    }
}
