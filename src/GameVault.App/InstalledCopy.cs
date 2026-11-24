using System.IO;
using Microsoft.Win32;
using GameVault.Core;

namespace GameVault.App;

/// <summary>Whether this running copy is the one the installer registered for the current user (the only copy that can be updated in place).</summary>
internal static class InstalledCopy
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{11DE0F05-D06D-4594-A970-85DDE91DC106}_is1";

    public static string? RegisteredInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public static bool IsThisTheInstalledCopy() => InstallLocationRules.IsInstalledCopy(AppContext.BaseDirectory, RegisteredInstallLocation());
}
