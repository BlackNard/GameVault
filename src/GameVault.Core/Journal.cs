namespace GameVault.Core;

/// <summary>An entry the player added by hand (ticket #96). It never changes recorded playtime.</summary>
public sealed record JournalEntry(Guid Id, DateOnly Date, int? Minutes, string Note, string[] Tags, Guid? PlaythroughId, string? Source = null, string? ExternalId = null, string? Title = null);

/// <summary>Profiles/&lt;game&gt;/journal.json.</summary>
public sealed record GameJournal(int SchemaVersion, Guid GameId, JournalEntry[] Entries)
{
    public static bool IsValid(GameJournal? value, Guid id) => value is { SchemaVersion: 1, Entries: { Length: <= JournalRules.MaxEntries } } && value.GameId == id &&
        value.Entries.All(e => e is not null && JournalRules.Problem(e) is null) && value.Entries.Select(e => e.Id).Distinct().Count() == value.Entries.Length;
}

/// <summary>Data/journal.json: entries that belong to no game.</summary>
public sealed record GeneralJournal(int SchemaVersion, JournalEntry[] Entries)
{
    public static bool IsValid(GeneralJournal? value) => value is { SchemaVersion: 1, Entries: { Length: <= JournalRules.MaxEntries } } &&
        value.Entries.All(e => e is not null && e.PlaythroughId is null && JournalRules.Problem(e) is null) && value.Entries.Select(e => e.Id).Distinct().Count() == value.Entries.Length;
}

/// <summary>An entry of the journal with its game; <see cref="Guid.Empty"/> and no name when it belongs to none. Session notes are not part of the journal.</summary>
public sealed record JournalItem(Guid GameId, string? Game, JournalEntry Entry);

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
        if (entry.Title?.Length > 200) return "The title may have up to 200 characters.";
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

    /// <summary>Newest first, for one game, no game (<see cref="Guid.Empty"/>) or all (null), between two days (either may be open).</summary>
    public static JournalItem[] Filter(IEnumerable<JournalItem> items, Guid? game, DateOnly? from, DateOnly? to) =>
        [.. items.Where(i => (game is null || i.GameId == game) && (from is null || i.Entry.Date >= from) && (to is null || i.Entry.Date <= to))
            .OrderByDescending(i => i.Entry.Date).ThenBy(i => i.Game, StringComparer.CurrentCultureIgnoreCase)];
}
