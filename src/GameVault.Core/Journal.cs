namespace GameVault.Core;

/// <summary>An entry the player added by hand (ticket #96). It never changes recorded playtime.</summary>
public sealed record JournalEntry(Guid Id, DateOnly Date, int? Minutes, string Note, string[] Tags, Guid? PlaythroughId, string? Source = null, string? ExternalId = null);

/// <summary>Profiles/&lt;game&gt;/journal.json.</summary>
public sealed record GameJournal(int SchemaVersion, Guid GameId, JournalEntry[] Entries)
{
    public static bool IsValid(GameJournal? value, Guid id) => value is { SchemaVersion: 1, Entries: { Length: <= JournalRules.MaxEntries } } && value.GameId == id &&
        value.Entries.All(e => e is not null && JournalRules.Problem(e) is null) && value.Entries.Select(e => e.Id).Distinct().Count() == value.Entries.Length;
}

/// <summary>One line of the journal: a recorded session (with its note, if any) or an entry.</summary>
public sealed record JournalItem(DateTime When, Guid GameId, string Game, long? Seconds, string? Title, string? Note, string[] Tags, Guid? SessionId, Guid? EntryId);

public static class JournalRules
{
    public const int MaxEntries = 20_000, MaxTags = 8, MaxTagLength = 30;

    public static string[] Tags(IEnumerable<string>? tags) =>
        [.. (tags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    public static string? Problem(JournalEntry entry)
    {
        if (entry.Id == Guid.Empty) return "The entry has no identity.";
        if (entry.Date.Year is < 1970 or > 2100) return "Choose a date between 1970 and 2100.";
        if (entry.Minutes is < 0 or > 100_000) return "Enter a sensible time played.";
        if (entry.Note is null || entry.Note.Length > 8000) return "The note may have up to 8000 characters.";
        if (entry.Tags is null || entry.Tags.Length > MaxTags || entry.Tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > MaxTagLength))
            return $"Use up to {MaxTags} tags of {MaxTagLength} characters.";
        if (entry.Source is { Length: 0 or > 40 } || entry.ExternalId is { Length: 0 or > 200 }) return "The import identity is not valid.";
        return null;
    }

    public static JournalEntry[] Upsert(IReadOnlyList<JournalEntry> entries, JournalEntry entry)
    {
        var result = entries.ToList();
        var at = result.FindIndex(e => e.Id == entry.Id || entry.Source is not null && entry.ExternalId is not null && e.Source == entry.Source && e.ExternalId == entry.ExternalId);
        if (at < 0) result.Add(entry);
        else result[at] = entry with { Id = result[at].Id };
        return [.. result];
    }

    /// <summary>Newest first, for one game or all, between two days (either may be open).</summary>
    public static JournalItem[] Filter(IEnumerable<JournalItem> items, Guid? game, DateOnly? from, DateOnly? to) =>
        [.. items.Where(i => (game is null || i.GameId == game) && (from is null || DateOnly.FromDateTime(i.When) >= from) && (to is null || DateOnly.FromDateTime(i.When) <= to))
            .OrderByDescending(i => i.When).ThenBy(i => i.Game, StringComparer.CurrentCultureIgnoreCase)];
}
