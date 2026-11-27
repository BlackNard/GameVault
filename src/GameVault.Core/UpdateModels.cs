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

/// <summary>When GameVault looks for a newer release on its own (it also looks every few hours while it stays open).</summary>
public static class UpdateCheckRules
{
    public static readonly TimeSpan ReopenInterval = TimeSpan.FromMinutes(10);
    /// <summary>How long the start waits for the answer before the window opens anyway (the notice then arrives a moment later).</summary>
    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Whether to look again when the player brings a running GameVault back (every start of the app itself looks, issue #100): allowed and no check in the
    /// last <see cref="ReopenInterval"/>. A time stamp in the future (a clock that moved back) counts as "not recent".
    /// </summary>
    public static bool ShouldCheckWhenReopened(bool enabled, DateTimeOffset? lastCheck, DateTimeOffset now)
    {
        if (!enabled) return false;
        if (lastCheck is null) return true;
        var since = now - lastCheck.Value;
        return since < TimeSpan.Zero || since >= ReopenInterval;
    }
}
