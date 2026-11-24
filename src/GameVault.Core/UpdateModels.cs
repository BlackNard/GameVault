namespace GameVault.Core;

/// <summary>The installer file of a release, as GitHub lists it: where to download it, how big it must be and the SHA-256 GitHub computed for it.</summary>
public sealed record UpdateInstaller(string Name, string Url, long Size, string? Sha256, string? ChecksumUrl);

/// <summary>A published release that is newer than the running app. <see cref="Installer"/> is set when the release carries the installer GameVault can run in place.</summary>
public sealed record UpdateInfo(string Version, string Tag, string Url, bool IsPreRelease, DateTimeOffset? PublishedAt, UpdateInstaller? Installer = null);

public enum UpdateStatus { UpToDate, Available, Failed }

public sealed record UpdateCheckResult(UpdateStatus Status, string CurrentVersion, UpdateInfo? Update = null, string? Message = null)
{
    public string Describe() => Status switch
    {
        UpdateStatus.Available => $"Version {Update!.Version} is available (you have {CurrentVersion}).",
        UpdateStatus.UpToDate => $"You have the latest version ({CurrentVersion}).",
        _ => "Could not check for updates: " + (Message ?? "unknown problem") + "."
    };
}

/// <summary>When the start-up update check may run.</summary>
public static class UpdateCheckRules
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(12);

    /// <summary>
    /// On when the player allows it and no check happened in the last <see cref="MinimumInterval"/>. A time stamp in the future (a clock that
    /// moved back) counts as "not recent", so a wrong clock can never stop the check for ever.
    /// </summary>
    public static bool ShouldCheckAtStart(bool enabled, DateTimeOffset? lastCheck, DateTimeOffset now)
    {
        if (!enabled) return false;
        if (lastCheck is null) return true;
        var since = now - lastCheck.Value;
        return since < TimeSpan.Zero || since >= MinimumInterval;
    }
}
