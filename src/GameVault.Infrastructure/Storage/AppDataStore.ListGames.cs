using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

/// <summary>
/// What was fetched for a list entry that is not a library game, kept by the entry's id: Data/ListGames/&lt;id&gt;.json (the same document a library game has),
/// &lt;id&gt;.png (a fetched cover) and &lt;id&gt;.own.png (a cover the player chose, which a lookup never replaces).
/// </summary>
public sealed partial class AppDataStore
{
    private string ListGamesFolder => Path.Combine(paths.Data, "ListGames");
    private string ListGameFile(Guid entry, string ending) => Path.Combine(ListGamesFolder, entry.ToString("N") + ending);

    public Task<StoredDocument<GameMetadataDocument>> LoadListGameInfoAsync(Guid entry) => Run(() =>
        documents.Read<GameMetadataDocument>(ListGameFile(entry, ".json"), value => GameMetadataDocument.IsValid(value, entry)));

    public Task SaveListGameInfoAsync(GameMetadataDocument info) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!GameMetadataDocument.IsValid(info, info.GameId)) throw new ArgumentException("Invalid game information.", nameof(info));
        EnsureRoots();
        managed.EnsureDirectory(ListGamesFolder);
        var file = ListGameFile(info.GameId, ".json");
        JsonDocuments.RequireWritable(documents.Read<GameMetadataDocument>(file, value => GameMetadataDocument.IsValid(value, info.GameId)));
        documents.Write(file, info, value => GameMetadataDocument.IsValid(value, info.GameId));
    });

    /// <summary>The cover to show: the player's own, else the fetched one, else null.</summary>
    public string? ListGameCover(Guid entry) =>
        new[] { ListGameFile(entry, ".own.png"), ListGameFile(entry, ".png") }.FirstOrDefault(File.Exists);

    public Task SaveListGameCoverAsync(Guid entry, byte[] png, bool own) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < 33 || png.Length > 10 * 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ArgumentException("The cover must be a valid PNG of at most 10 MB.");
        EnsureRoots();
        managed.EnsureDirectory(ListGamesFolder);
        var file = ListGameFile(entry, own ? ".own.png" : ".png");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        managed.Check(file);
        try
        {
            File.WriteAllBytes(temporary, png);
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });

    /// <summary>Removes the fetched cover (it belonged to a game that was ruled out); the player's own stays.</summary>
    public Task ForgetListGameCoverAsync(Guid entry) => Run(() =>
    {
        var file = ListGameFile(entry, ".png");
        managed.Check(file);
        if (File.Exists(file)) File.Delete(file);
    });

    /// <summary>Removes what was fetched for an entry (it was renamed); a cover the player chose stays.</summary>
    public Task ForgetListGameAsync(Guid entry) => Run(() =>
    {
        foreach (var ending in new[] { ".json", ".json.bak", ".png" })
        {
            var file = ListGameFile(entry, ending);
            managed.Check(file);
            if (File.Exists(file)) File.Delete(file);
        }
    });

    private void RemoveListGames(IEnumerable<Guid> entries)
    {
        foreach (var entry in entries)
            foreach (var ending in new[] { ".json", ".json.bak", ".png", ".own.png" })
            {
                var file = ListGameFile(entry, ending);
                try
                {
                    managed.Check(file);
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }   // a leftover file does no harm
            }
    }
}
