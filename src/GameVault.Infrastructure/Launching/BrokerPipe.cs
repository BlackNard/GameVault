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
        // CurrentUserOnly also compares elevation/owner SIDs. Use the actual user SID so a
        // same-account elevated helper can connect without granting other users access.
        using var descriptor = new WindowsUserSecurity("GRGW");
        var security = descriptor.Attributes;
            var handle = Native.CreateNamedPipe(@"\\.\pipe\" + name,
                0x40080003, // OVERLAPPED | FIRST_PIPE_INSTANCE | DUPLEX
                0x00000008, // BYTE mode | REJECT_REMOTE_CLIENTS
                1, 4096, 4096, 0, ref security);
            if (handle.IsInvalid)
            { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw NativeLaunchErrors.Create(error, "Windows could not create the launch-helper connection."); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle); }
            catch { handle.Dispose(); throw; }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances,
            uint outputBuffer, uint inputBuffer, uint defaultTimeout, ref WindowsUserSecurity.SecurityAttributes security);
    }
}
