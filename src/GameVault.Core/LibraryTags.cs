namespace GameVault.Core;

/// <summary>Free-text tags and categories on games (ticket #88). Rules for cleaning, parsing and filtering them.</summary>
public static class TagRules
{
    public const int MaxTagsPerGame = 12;
    public const int MaxTagLength = 30;

    /// <summary>Trims, collapses spaces, drops empty and over-long entries and duplicates (ignoring case, first spelling wins), keeps at most <see cref="MaxTagsPerGame"/> and sorts.</summary>
    public static string[] Normalize(IEnumerable<string?>? tags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<string>();
        foreach (var raw in tags ?? [])
        {
            var tag = string.Join(' ', (raw ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            if (tag.Length == 0 || tag.Length > MaxTagLength || !seen.Add(tag)) continue;
            clean.Add(tag);
            if (clean.Count == MaxTagsPerGame) break;
        }
        return [.. clean.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Splits text typed by the user on commas, semicolons and new lines.</summary>
    public static string[] Parse(string? text) => Normalize((text ?? "").Split([',', ';', '\r', '\n']));

    /// <summary>A game matches when it has every selected tag (ignoring case). No selection matches everything.</summary>
    public static bool HasAll(IReadOnlyCollection<string>? gameTags, IReadOnlyCollection<string>? selected) =>
        selected is null || selected.Count == 0 || selected.All(s => gameTags?.Contains(s, StringComparer.OrdinalIgnoreCase) == true);

    /// <summary>Every tag in use, each once, sorted.</summary>
    public static string[] AllTags(IEnumerable<IEnumerable<string>> perGame) =>
        [.. perGame.SelectMany(t => t).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase)];
}

/// <summary>On-disk form of all game tags (Data/library-tags.json), keyed by game id so a rename, archive or restore never loses them.</summary>
public sealed record LibraryTagsDocument(int SchemaVersion, Dictionary<Guid, string[]> Tags)
{
    public static LibraryTagsDocument Empty => new(1, []);

    public static bool IsValid(LibraryTagsDocument? value) => value is { SchemaVersion: 1, Tags: not null } &&
        value.Tags.Values.All(tags => tags is not null && tags.Length <= TagRules.MaxTagsPerGame && tags.All(t => !string.IsNullOrWhiteSpace(t) && t.Length <= TagRules.MaxTagLength));
}

/// <summary>The library sort orders a saved view can remember.</summary>
public static class LibrarySortOrder
{
    public const string Name = "Name";
    public const string LastPlayed = "LastPlayed";
    public const string MostPlayed = "MostPlayed";
    public static readonly string[] All = [Name, LastPlayed, MostPlayed];

    public static string Normalize(string? sort) => All.FirstOrDefault(s => string.Equals(s, sort, StringComparison.OrdinalIgnoreCase)) ?? Name;

    public static string Label(string sort) => sort switch { LastPlayed => "Recently played", MostPlayed => "Most played", _ => "Name" };

    /// <summary>Orders games by the chosen sort; name order breaks ties so the order is always stable.</summary>
    public static IEnumerable<T> Apply<T>(IEnumerable<T> games, string sort, Func<T, string> name, Func<T, DateTimeOffset?> lastPlayed, Func<T, long?> playedSeconds)
    {
        var byName = games.OrderBy(name, StringComparer.OrdinalIgnoreCase);
        return Normalize(sort) switch
        {
            LastPlayed => games.OrderByDescending(g => lastPlayed(g) ?? DateTimeOffset.MinValue).ThenBy(name, StringComparer.OrdinalIgnoreCase),
            MostPlayed => games.OrderByDescending(g => playedSeconds(g) ?? 0).ThenBy(name, StringComparer.OrdinalIgnoreCase),
            _ => byName
        };
    }
}
