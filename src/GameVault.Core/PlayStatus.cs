namespace GameVault.Core;

/// <summary>What a stored status says more precisely (ticket #94).</summary>
public enum GameStatusDetail { Backlog, Abandoned, Retired }

/// <summary>Every status a player can choose, in the order lists show them.</summary>
public enum PlayStatus { NotStarted, Backlog, Playing, OnHold, Completed, Abandoned, Retired }

/// <summary>
/// The detail behind a game's status (Profiles/&lt;game&gt;/status.json). <c>profile.json</c> keeps one of the original four values, so older versions read
/// every game. <see cref="Source"/> and <see cref="ExternalId"/> are for data brought in from elsewhere later.
/// </summary>
public sealed record StatusDetail(int SchemaVersion, Guid GameId, GameStatusDetail Detail, string? Source = null, string? ExternalId = null)
{
    public static bool IsValid(StatusDetail? value, Guid id) => value is { SchemaVersion: 1 } && value.GameId == id && Enum.IsDefined(value.Detail) &&
        value.Source is null or { Length: > 0 and <= 40 } && value.ExternalId is null or { Length: > 0 and <= 200 };
}

public static class PlayStatuses
{
    public static IReadOnlyList<PlayStatus> All { get; } = Enum.GetValues<PlayStatus>();

    public static string Label(PlayStatus status) => status switch
    { PlayStatus.NotStarted => "Not started", PlayStatus.OnHold => "On hold", _ => status.ToString() };

    /// <summary>The closest of the original four, which is what the profile stores.</summary>
    public static GameStatus Stored(PlayStatus status) => status switch
    {
        PlayStatus.NotStarted or PlayStatus.Backlog => GameStatus.NotStarted,
        PlayStatus.Playing => GameStatus.Playing,
        PlayStatus.Completed => GameStatus.Completed,
        _ => GameStatus.OnHold
    };

    public static GameStatusDetail? Detail(PlayStatus status) => status switch
    { PlayStatus.Backlog => GameStatusDetail.Backlog, PlayStatus.Abandoned => GameStatusDetail.Abandoned, PlayStatus.Retired => GameStatusDetail.Retired, _ => null };

    /// <summary>A detail counts only while it fits the stored status (an older version may have changed that).</summary>
    public static PlayStatus From(GameStatus stored, GameStatusDetail? detail)
    {
        if (detail is { } known)
        {
            var precise = known switch { GameStatusDetail.Backlog => PlayStatus.Backlog, GameStatusDetail.Abandoned => PlayStatus.Abandoned, _ => PlayStatus.Retired };
            if (Stored(precise) == stored) return precise;
        }
        return stored switch { GameStatus.Playing => PlayStatus.Playing, GameStatus.Completed => PlayStatus.Completed, GameStatus.OnHold => PlayStatus.OnHold, _ => PlayStatus.NotStarted };
    }
}
