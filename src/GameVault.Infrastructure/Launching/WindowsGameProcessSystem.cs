using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GameVault.Infrastructure.Launching;

[SupportedOSPlatform("windows")]
public sealed class WindowsGameProcessSystem : IGameProcessSystem
{
    public IGameProcess Start(string executable, string workingDirectory)
        => WindowsProcessGroup.Start(executable, workingDirectory);

    public ProcessCandidate[] Find(string executable)
    {
        using var snapshot = Native.CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not inspect running games.");
        var entry = new Native.ProcessEntry { Size = (uint)Marshal.SizeOf<Native.ProcessEntry>() };
        var entries = new List<(int Id, int Parent, string Name)>();
        if (!Native.Process32First(snapshot, ref entry))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read running processes.");
        do { entries.Add(((int)entry.ProcessId, (int)entry.ParentProcessId, entry.Executable)); }
        while (Native.Process32Next(snapshot, ref entry));
        var error = Marshal.GetLastWin32Error();
        if (error != 18) throw new Win32Exception(error, "Windows could not complete the process list.");
        var parents = entries.ToDictionary(e => e.Id, e => e.Parent);
        var matches = new List<ProcessCandidate>();
        foreach (var item in entries.Where(e => string.Equals(e.Name, Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase)))
        {
            using var process = Native.OpenProcess(0x1000, false, item.Id);
            if (process.IsInvalid)
            {
                error = Marshal.GetLastWin32Error();
                if (error is 87 or 6) continue; // Exited between snapshot and inspection.
                throw new Win32Exception(error, "A possible game process could not be inspected. Check its permissions before retrying.");
            }
            var buffer = new StringBuilder(32768); var length = buffer.Capacity;
            if (!Native.QueryFullProcessImageName(process, 0, buffer, ref length))
            {
                error = Marshal.GetLastWin32Error();
                if (Native.GetExitCodeProcess(process, out var exitCode) && exitCode != 259) continue;
                throw new Win32Exception(error, "Windows could not verify a possible game's executable path.");
            }
            if (!string.Equals(Path.GetFullPath(buffer.ToString()), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)) continue;
            if (!Native.GetProcessTimes(process, out var created, out _, out _, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not verify a game process identity.");
            var ancestors = new List<int>();
            var parent = item.Parent;
            while (parent != 0 && !ancestors.Contains(parent))
            {
                ancestors.Add(parent);
                if (!parents.TryGetValue(parent, out parent)) break;
            }
            matches.Add(new(new(item.Id, DateTime.FromFileTimeUtc(created).Ticks), ancestors.ToArray()));
        }
        return matches.ToArray();
    }

    public IGameProcess? Open(ProcessIdentity identity)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(identity.Id);
            var result = new ObservedProcess(process);
            if (result.Identity == identity && !result.HasExited) return result;
            result.Dispose(); return null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { process?.Dispose(); return null; }
        catch { process?.Dispose(); throw; }
    }

    private sealed class ObservedProcess : IGameProcess
    {
        private readonly Process process;
        public ObservedProcess(Process process)
        {
            this.process = process;
            // Retain a process handle, not merely its PID, throughout observation.
            _ = process.SafeHandle;
            Identity = new(process.Id, process.StartTime.ToUniversalTime().Ticks);
        }
        public ProcessIdentity Identity { get; }
        public bool HasExited => process.HasExited;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
        public void Dispose() => process.Dispose();
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ProcessEntry
        {
            public uint Size, Usage, ProcessId;
            public nuint DefaultHeapId;
            public uint ModuleId, Threads, ParentProcessId;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
        }

        [DllImport("kernel32.dll", SetLastError = true)] public static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint id);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);
        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    }
}
