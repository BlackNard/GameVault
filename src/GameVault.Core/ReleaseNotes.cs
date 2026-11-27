namespace GameVault.Core;

/// <summary>What one version brought, in words for players.</summary>
public sealed record ReleaseNote(string Version, string[] New, string[] Fixed);

/// <summary>
/// The notes shown after an update (ticket #100). They ship inside the app, so they need no network. Write an entry for every release, newest first;
/// the notes on GitHub are prepared from the same text. A test refuses a version without notes.
/// </summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
        new("0.1.2",
            [
                "Stop a running game from GameVault, on its page or beside the running game at the top.",
                "GameVault asks the game to close first, and offers Force stop if it stays open."
            ],
            []),
        new("0.1.1",
            [
                "GameVault now finds where a game saves, backs it up and tells you. \"Not this folder\" undoes a wrong pick.",
                "When it is not sure, it asks. If it finds nothing, you can choose the folder from the notification.",
                "A switch in Settings > Backups: Watch for saves while playing."
            ],
            [
                "Saves of games set up outside a store are found by the game's Steam number.",
                "A missing save folder now says so plainly instead of reporting a failed backup.",
                "Version numbers are now plain, like 0.1.1."
            ]),
        new("0.1.0-preview.8",
            [
                "Journal: write your own entries, about a game or anything else, and export an entry to Obsidian.",
                "Lists and favourites, shown as cover cards you can drag into order. A list can hold games you don't have.",
                "Playthroughs: record each run of a game.",
                "Export session notes to an Obsidian vault.",
                "New statuses: Backlog, Abandoned and Retired.",
                "Export your library and playtime to CSV files.",
                "Animations, with a Reduce motion switch in Settings.",
                "Calmer screens: explanations appear when you point at an item.",
                "This What's new window. Open it again in Settings > Updates.",
                "A data backup now includes the Obsidian vault; older versions cannot restore such a backup."
            ],
            [
                "A new release now shows in Notifications as soon as GameVault starts.",
                "Games are found even when the store writes their name differently, for example \"Silent Hill 2 Remake\"."
            ])
    ];

    /// <summary>Every version's notes up to the running one, newest first.</summary>
    public static ReleaseNote[] UpTo(string current, IEnumerable<ReleaseNote>? notes = null) => Between(null, current, notes);

    /// <summary>
    /// The notes the player has not seen: every version after <paramref name="seen"/> up to the running one. Without a record, a player who used GameVault
    /// before (versions up to preview 7 kept none) gets the running version's notes, and a first install gets nothing.
    /// </summary>
    public static ReleaseNote[] Unseen(string? seen, string current, bool usedBefore, IEnumerable<ReleaseNote>? notes = null)
    {
        if (ReleaseVersion.TryParse(seen, out var last)) return Between(last, current, notes);
        if (!usedBefore || !ReleaseVersion.TryParse(current, out var running)) return [];
        return [.. Between(null, current, notes).Where(n => ReleaseVersion.TryParse(n.Version, out var v) && !(v < running) && !(v > running))];
    }

    /// <summary>The version to remember: the highest seen, so going back to an older version and forward again shows nothing twice.</summary>
    public static string Newest(string? seen, string current) =>
        ReleaseVersion.TryParse(seen, out var last) && ReleaseVersion.TryParse(current, out var running) && last > running ? seen! : current;

    private static ReleaseNote[] Between(ReleaseVersion? after, string current, IEnumerable<ReleaseNote>? notes)
    {
        if (!ReleaseVersion.TryParse(current, out var running)) return [];
        return [.. (notes ?? All)
            .Select(note => (Note: note, Ok: ReleaseVersion.TryParse(note.Version, out var version), Version: version))
            .Where(n => n.Ok && !(n.Version > running) && (after is null || n.Version > after))
            .OrderByDescending(n => n.Version).Select(n => n.Note)];
    }
}
