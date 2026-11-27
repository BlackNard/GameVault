namespace GameVault.Core;

/// <summary>A line of a list: a library game, or only a title for a game that is not (or no longer) in the library.</summary>
public sealed record ListEntry(Guid Id, Guid? GameId, string Title, string? Note, string? Source = null, string? ExternalId = null);

public sealed record GameList(Guid Id, string Name, string Description, bool Ranked, ListEntry[] Entries, string? Source = null, string? ExternalId = null);

/// <summary>The player's lists and favourite games, in their own order (Data/lists.json, ticket #97).</summary>
public sealed record ListsDocument(int SchemaVersion, GameList[] Lists, Guid[] Favourites)
{
    public static ListsDocument Empty => new(1, [], []);

    public static bool IsValid(ListsDocument? value) => value is { SchemaVersion: 1, Lists: { Length: <= GameListRules.MaxLists }, Favourites: not null } &&
        value.Lists.All(l => l is not null && GameListRules.Problem(l) is null) && value.Lists.Select(l => l.Id).Distinct().Count() == value.Lists.Length &&
        value.Favourites.Distinct().Count() == value.Favourites.Length && value.Favourites.Length <= GameListRules.MaxEntries;
}

public static class GameListRules
{
    public const int MaxLists = 200, MaxEntries = 5000;

    public static string? Problem(GameList list)
    {
        if (list.Id == Guid.Empty) return "The list has no identity.";
        if (string.IsNullOrWhiteSpace(list.Name) || list.Name.Length > 80) return "Enter a list name of up to 80 characters.";
        if (list.Description is null || list.Description.Length > 1000) return "The description may have up to 1000 characters.";
        if (list.Entries is null || list.Entries.Length > MaxEntries || list.Entries.Select(e => e?.Id).Distinct().Count() != list.Entries.Length) return "The list's entries are not valid.";
        if (list.Entries.Any(e => e is null || e.Id == Guid.Empty || string.IsNullOrWhiteSpace(e.Title) || e.Title.Length > 200 || e.Note?.Length > 1000)) return "Every entry needs a title; notes may have up to 1000 characters.";
        if (list.Source is { Length: 0 or > 40 } || list.ExternalId is { Length: 0 or > 200 }) return "The import identity is not valid.";
        return null;
    }

    /// <summary>Adds the list, or changes the one with the same id or import identity.</summary>
    public static ListsDocument Upsert(ListsDocument document, GameList list)
    {
        var tidy = list with { Name = list.Name?.Trim() ?? "" };
        if (Problem(tidy) is { } problem) throw new ArgumentException(problem);
        var lists = document.Lists.ToList();
        var at = lists.FindIndex(l => l.Id == tidy.Id || tidy.Source is not null && tidy.ExternalId is not null && l.Source == tidy.Source && l.ExternalId == tidy.ExternalId);
        if (lists.Where((_, i) => i != at).Any(l => string.Equals(l.Name, tidy.Name, StringComparison.CurrentCultureIgnoreCase))) throw new ArgumentException("You already have a list with that name.");
        if (at < 0) lists.Add(tidy);
        else lists[at] = tidy with { Id = lists[at].Id };
        return document with { Lists = [.. lists] };
    }

    public static ListsDocument Remove(ListsDocument document, Guid list) => document with { Lists = [.. document.Lists.Where(l => l.Id != list)] };

    /// <summary>Adds an entry unless the list already has that game (or, without a game, that title).</summary>
    public static ListEntry[] Add(IReadOnlyList<ListEntry> entries, ListEntry entry) =>
        entries.Any(e => entry.GameId is { } game ? e.GameId == game : e.GameId is null && string.Equals(e.Title, entry.Title, StringComparison.CurrentCultureIgnoreCase))
            ? [.. entries] : [.. entries, entry];

    /// <summary>Moves an entry up (-1) or down (+1); at either end nothing changes.</summary>
    public static ListEntry[] Move(IReadOnlyList<ListEntry> entries, Guid entry, int by)
    {
        var result = entries.ToList();
        var from = result.FindIndex(e => e.Id == entry);
        var to = from + by;
        if (from < 0 || to < 0 || to >= result.Count) return [.. result];
        (result[from], result[to]) = (result[to], result[from]);
        return [.. result];
    }

    /// <summary>Puts one item where another is (drag and drop): dragged forward it lands after it, dragged back before it.</summary>
    public static T[] MoveOnto<T>(IReadOnlyList<T> items, Predicate<T> moved, Predicate<T> onto)
    {
        var result = items.ToList();
        var from = result.FindIndex(moved);
        var to = result.FindIndex(onto);
        if (from < 0 || to < 0 || from == to) return [.. result];
        var item = result[from];
        result.RemoveAt(from);
        result.Insert(to, item);
        return [.. result];
    }

    public static ListsDocument ToggleFavourite(ListsDocument document, Guid game) => document with
    { Favourites = document.Favourites.Contains(game) ? [.. document.Favourites.Where(f => f != game)] : [.. document.Favourites, game] };
}
