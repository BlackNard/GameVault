using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace GameVault.Infrastructure.Launching;

[SupportedOSPlatform("windows")]
internal sealed class WindowsUserSecurity : IDisposable
{
    private readonly IntPtr descriptor;
    internal SecurityAttributes Attributes;

    internal WindowsUserSecurity(string rights)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new IOException("The Windows account could not be identified.");
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor($"O:{sid}D:P(A;;{rights};;;{sid})", 1, out descriptor, out _))
            throw NativeLaunchErrors.Create(Marshal.GetLastWin32Error(), "Windows could not secure game observation.");
        Attributes = new() { Size = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
    }

    public void Dispose() => LocalFree(descriptor);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes { internal int Size; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string value, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr value);
}
