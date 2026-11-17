using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using GameVault.Infrastructure.Capture;
using Microsoft.Win32;
namespace GameVault.App;

/// <summary>Read-only look at this PC for NVIDIA App. Nothing is started, changed or written.</summary>
internal sealed class WindowsNvidiaEnvironment : INvidiaEnvironment
{
    public string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    public string VideosFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsProcessRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try { return processes.Length > 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { return false; }
    }

    public string? RegisteredCaptureFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS", writable: false);
            return key?.GetValue("DefaultPathW") switch
            {
                string text => text.Trim('\0'),
                byte[] bytes => Encoding.Unicode.GetString(bytes).Trim('\0'),
                _ => null
            };
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return null; }
    }
}
