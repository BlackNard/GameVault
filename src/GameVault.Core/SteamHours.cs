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
    /// GameVault and Steam hours, and their sum as the total (the owner's choice, issue #62). Time that Steam also recorded is therefore in both figures.
    /// </summary>
    public static PlaytimeTotals Totals(SteamLink link, GameStatistics statistics)
    {
        var recorded = Math.Max(0, statistics.Sessions.Sum(s => s.DurationSeconds) + statistics.Adjustments.Sum(a => a.Seconds));
        var steam = link.Minutes * 60;
        return new(recorded, steam, recorded + steam);
    }
}
