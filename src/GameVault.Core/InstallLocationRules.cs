namespace GameVault.Core;

/// <summary>
/// Decides whether the running copy is the one the installer put on this PC. Only that copy can be updated in place: the release installer
/// upgrades the registered install folder and keeps the player's data beside it. A portable ZIP or a development build keeps the release-page link.
/// </summary>
public static class InstallLocationRules
{
    /// <param name="runningFolder">The folder GameVault.exe runs from.</param>
    /// <param name="registeredInstallLocation">The InstallLocation the installer registered for the current user (null when none).</param>
    public static bool IsInstalledCopy(string? runningFolder, string? registeredInstallLocation)
    {
        if (string.IsNullOrWhiteSpace(runningFolder) || string.IsNullOrWhiteSpace(registeredInstallLocation)) return false;
        try
        {
            var running = Normalize(runningFolder);
            var registered = Normalize(registeredInstallLocation);
            return running.Length > 3 && string.Equals(running, registered, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));
}
