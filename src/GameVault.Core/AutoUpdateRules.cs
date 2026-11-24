using System.Text.Json;

namespace GameVault.Core;

/// <summary>An update that was downloaded and verified and is waiting to be installed (kept in the updates folder next to the installer file).</summary>
/// <param name="InstallerName">The installer's file name inside the updates folder (never a path).</param>
public sealed record PendingUpdate(string Tag, string Version, string InstallerName, string Sha256, long Size, DateTimeOffset DownloadedAt, int Attempts = 0, DateTimeOffset? LastAttemptAt = null,
    string? SafetyArchive = null, string? FingerprintFile = null);

/// <summary>Whether updates install on their own. Default on; read tolerantly from settings.json before the library is opened (like the startup window).</summary>
public static class UpdatePreferences
{
    public static bool ParseAutoUpdate(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return true;
        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return true;
            var root = document.RootElement;
            var auto = !(root.TryGetProperty("autoUpdate", out var a) && a.ValueKind == JsonValueKind.False);
            var checks = !(root.TryGetProperty("checkForUpdates", out var c) && c.ValueKind == JsonValueKind.False);
            return auto && checks;                      // nothing installs on its own when the player turned the update check off
        }
        catch (JsonException) { return true; }
    }
}

/// <summary>When a found update is downloaded in the background and when a ready one is installed (Steam/Epic style: no prompts, never during play).</summary>
public static class AutoUpdateRules
{
    /// <summary>After this many failed installer starts the update is left alone and the player gets the release-page link instead of a loop.</summary>
    public const int MaxAttempts = 3;

    /// <summary>While GameVault stays open (for example in the tray) it looks again this often.</summary>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Download in the background when automatic updates are on, this copy can be updated in place, the release has a verifiable installer, no game is running
    /// (a download must never compete with play) and this release is not already waiting.
    /// </summary>
    public static bool ShouldDownload(bool autoUpdate, bool canUpdateInPlace, bool gameRunning, PendingUpdate? pending, UpdateInfo update) =>
        autoUpdate && canUpdateInPlace && !gameRunning && update.Installer is not null &&
        !(pending is not null && string.Equals(pending.Tag, update.Tag, StringComparison.Ordinal) && pending.Attempts < MaxAttempts);

    /// <summary>A ready update is installed (when GameVault closes or starts) while automatic updates are on, it is newer than the running app and it has not failed too often.</summary>
    public static bool ShouldInstall(bool autoUpdate, PendingUpdate? pending, ReleaseVersion current) =>
        autoUpdate && pending is not null && pending.Attempts < MaxAttempts && IsNewer(pending, current);

    /// <summary>True when the waiting update is the running version or older (the update has been installed): its leftovers can be removed.</summary>
    public static bool IsInstalledAlready(PendingUpdate pending, ReleaseVersion current) => !IsNewer(pending, current);

    /// <summary>True when the update failed to install repeatedly and should be reported instead of retried.</summary>
    public static bool HasGivenUp(PendingUpdate pending, ReleaseVersion current) => pending.Attempts >= MaxAttempts && IsNewer(pending, current);

    private static bool IsNewer(PendingUpdate pending, ReleaseVersion current) => ReleaseVersion.TryParse(pending.Version, out var version) && version > current;
}
