namespace GameVault.Core;

public enum PlaythroughOutcome { Playing, Completed, Abandoned, Retired, Shelved }

/// <summary>One run of a game (ticket #95). <see cref="Minutes"/> is typed by the player; without it the recorded sessions between the dates count.
/// <see cref="Source"/> and <see cref="ExternalId"/> identify a run brought in from elsewhere, so importing again changes it instead of adding one.</summary>
public sealed record Playthrough(Guid Id, string Title, string? Platform, DateOnly? Started, DateOnly? Finished, PlaythroughOutcome Outcome,
    int? Minutes, int HalfStars, string? Review, bool Mastered, string? Source = null, string? ExternalId = null);

/// <summary>Profiles/&lt;game&gt;/playthroughs.json.</summary>
public sealed record PlaythroughList(int SchemaVersion, Guid GameId, Playthrough[] Runs)
{
    public static bool IsValid(PlaythroughList? value, Guid id) => value is { SchemaVersion: 1, Runs: { Length: <= PlaythroughRules.MaxRuns } } && value.GameId == id &&
        value.Runs.All(r => r is not null && PlaythroughRules.Problem(r) is null) && value.Runs.Select(r => r.Id).Distinct().Count() == value.Runs.Length;
}

public static class PlaythroughRules
{
    public const int MaxRuns = 200;

    /// <summary>What is wrong with a run, in words for the player, or null.</summary>
    public static string? Problem(Playthrough run)
    {
        if (run.Id == Guid.Empty) return "The playthrough has no identity.";
        if (string.IsNullOrWhiteSpace(run.Title) || run.Title.Length > 120) return "Enter a title of up to 120 characters.";
        if (run.Platform?.Length > 80) return "The platform may have up to 80 characters.";
        if (run.Started is { } start && run.Finished is { } end && end < start) return "The finish date is before the start date.";
        if (!Enum.IsDefined(run.Outcome)) return "Choose how the run stands.";
        if (run.Minutes is < 0 or > 1_000_000) return "Enter a sensible number of hours.";
        if (!StarRating.IsValid(run.HalfStars)) return "Choose a rating from the stars.";
        if (run.Review?.Length > 4000) return "The review may have up to 4000 characters.";
        if (run.Source is { Length: 0 or > 40 } || run.ExternalId is { Length: 0 or > 200 }) return "The import identity is not valid.";
        return null;
    }

    /// <summary>The list with this run added, or put where the same run (by id or import identity) was.</summary>
    public static Playthrough[] Upsert(IReadOnlyList<Playthrough> runs, Playthrough run)
    {
        var result = runs.ToList();
        var at = result.FindIndex(r => r.Id == run.Id || run.Source is not null && run.ExternalId is not null && r.Source == run.Source && r.ExternalId == run.ExternalId);
        if (at < 0) result.Add(run);
        else result[at] = run with { Id = result[at].Id };
        return [.. result];
    }

    public static long? Seconds(Playthrough run, IReadOnlyList<PlaySpan> sessions, DateOnly today)
    {
        if (run.Minutes is { } typed) return typed * 60L;
        if (run.Started is not { } from) return null;
        var to = run.Finished ?? today;
        return sessions.Where(s => DateOnly.FromDateTime(s.Start) is var day && day >= from && day <= to).Sum(s => s.Seconds);
    }

    /// <summary>"38 of ~62 h": the run against the average time to beat.</summary>
    public static string? AgainstAverage(long? seconds, int? average) =>
        seconds is > 0 && average is > 0 ? $"{Math.Round(seconds.Value / 3600.0)} of ~{Math.Round(average.Value / 3600.0)} h" : null;
}
