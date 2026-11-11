using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace GameVault.Infrastructure.Launching;

[SupportedOSPlatform("windows")]
internal static class BrokerPipe
{
    internal static NamedPipeServerStream Create(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new IOException("The current Windows account could not be identified.");
        // CurrentUserOnly also compares elevation/owner SIDs. Use the actual user SID so a
        // same-account elevated helper can connect without granting other users access.
        if (!Native.ConvertStringSecurityDescriptorToSecurityDescriptor($"O:{sid}D:P(A;;GRGW;;;{sid})", 1, out var descriptor, out _))
            throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "Windows could not secure the launch-helper connection.");
        try
        {
            var security = new Native.SecurityAttributes { Size = Marshal.SizeOf<Native.SecurityAttributes>(), Descriptor = descriptor };
            var handle = Native.CreateNamedPipe(@"\\.\pipe\" + name,
                0x40080003, // OVERLAPPED | FIRST_PIPE_INSTANCE | DUPLEX
                0x00000008, // BYTE mode | REJECT_REMOTE_CLIENTS
                1, 4096, 4096, 0, ref security);
            if (handle.IsInvalid)
            { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw NativeLaunchErrors.Create(error, "Windows could not create the launch-helper connection."); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { Native.LocalFree(descriptor); }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SecurityAttributes { internal int Size; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
        [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string value, uint revision, out IntPtr descriptor, out uint size);
        [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances,
            uint outputBuffer, uint inputBuffer, uint defaultTimeout, ref SecurityAttributes security);
        [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr value);
    }
}
