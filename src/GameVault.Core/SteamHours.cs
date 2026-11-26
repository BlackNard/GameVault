namespace GameVault.Core;

/// <summary>A profile's link to a Steam game (Profiles/&lt;game&gt;/steam-link.json). <see cref="Confirmed"/> means the program is not inside Steam's install
/// folder and the player confirmed the match; otherwise the link was proven by the install folder.</summary>
public sealed record SteamLink(int SchemaVersion, Guid GameId, string AppId, string AccountId, string SteamName, bool Confirmed, string ExecutablePath,
    long Minutes, DateTimeOffset ReadAt)
{
    public static bool IsDigits(string? id) => id is { Length: > 0 and <= 12 } && id.All(char.IsAsciiDigit);

    public static bool IsValid(SteamLink value, Guid gameId) => value is { SchemaVersion: 1 } && value.GameId == gameId && IsDigits(value.AppId) && IsDigits(value.AccountId) &&
        value.SteamName is { Length: > 0 and <= 200 } && value.ExecutablePath is { Length: > 0 and <= 1000 } && value.Minutes is >= 0 and <= 60_000_000;
}

/// <summary>How each recorded session was started (Profiles/&lt;game&gt;/session-origins.json): Steam counts the ones started through it.</summary>
public sealed record SessionOrigins(int SchemaVersion, Guid GameId, Guid[] Steam, Guid[] Other)
{
    public static bool IsValid(SessionOrigins value, Guid gameId) => value is { SchemaVersion: 1, Steam: not null, Other: not null } && value.GameId == gameId &&
        value.Steam.Concat(value.Other).Distinct().Count() == value.Steam.Length + value.Other.Length;
}

public sealed record PlaytimeTotals(long GameVaultSeconds, long SteamSeconds, long TotalSeconds);

/// <summary>What the Steam store says about one app, shown so the player can compare it with their profile.</summary>
public sealed record SteamAppSummary(string AppId, string Name, string[] Developers, int? Year, string? ImageUrl);

public static class SteamHours
{
    /// <summary>The same title once case, spaces, punctuation and trademark signs are ignored. Nothing looser than that.</summary>
    public static bool TitlesMatch(string first, string second)
    {
        static string Plain(string text) => new((text ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var a = Plain(first);
        return a.Length > 0 && a == Plain(second);
    }

    /// <summary>
    /// Whether the link still describes this profile. <paramref name="installedAppId"/> is the Steam app whose install folder holds the program now, if any:
    /// that proves the identity (or disproves it). Without one, only the very same program counts, and a confirmed link also needs the titles to match.
    /// </summary>
    public static bool StillSameGame(SteamLink link, string profileName, string executable, string? installedAppId)
    {
        if (installedAppId is not null) return installedAppId == link.AppId;
        return string.Equals(link.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase) && (!link.Confirmed || TitlesMatch(profileName, link.SteamName));
    }

    /// <summary>
    /// GameVault and Steam hours, and a total that never counts the same time twice: time Steam also saw is covered by the larger of the two figures,
    /// time Steam did not see is added. Time of unknown origin (older sessions, typed-in adjustments) counts as seen by Steam only for a proven link.
    /// </summary>
    public static PlaytimeTotals Totals(SteamLink link, GameStatistics statistics, SessionOrigins? origins)
    {
        var steamIds = origins?.Steam.ToHashSet() ?? [];
        var otherIds = origins?.Other.ToHashSet() ?? [];
        long seen = 0, unseen = 0, unknown = statistics.Adjustments.Sum(a => a.Seconds);
        foreach (var session in statistics.Sessions)
            if (steamIds.Contains(session.Id)) seen += session.DurationSeconds;
            else if (otherIds.Contains(session.Id)) unseen += session.DurationSeconds;
            else unknown += session.DurationSeconds;
        var recorded = seen + unseen + unknown;
        if (link.Confirmed) unseen += unknown; else seen += unknown;
        var steam = link.Minutes * 60;
        return new(Math.Max(0, recorded), steam, Math.Max(0, Math.Max(steam, Math.Max(0, seen)) + unseen));
    }
}
