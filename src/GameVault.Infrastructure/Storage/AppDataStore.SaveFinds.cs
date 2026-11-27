using GameVault.Core;
using GameVault.Infrastructure.Saves;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    public const int MaxSaveFinds = 64;

    public Task<StoredDocument<SaveFindsDocument>> LoadSaveFindsAsync(Guid gameId) => Run(() => ReadSaveFinds(gameId));

    public Task SaveFindsAsync(SaveFindsDocument document) => Run(() => WriteSaveFinds(document));

    /// <summary>Reads, changes and writes under one lock, so the play-time watcher and "Not this folder" never lose each other's change.</summary>
    public Task<SaveFindsDocument> UpdateSaveFindsAsync(Guid gameId, Func<SaveFindsDocument, SaveFindsDocument> change) => Run(() =>
    {
        var current = ReadSaveFinds(gameId);
        JsonDocuments.RequireWritable(current);
        var before = current.Value ?? new SaveFindsDocument(1, gameId, [], [], 0);
        var next = change(before) with { Revision = before.Revision + 1 };
        WriteSaveFinds(next);
        return next;
    });

    private StoredDocument<SaveFindsDocument> ReadSaveFinds(Guid gameId) =>
        documents.Read<SaveFindsDocument>(Path.Combine(Folder(gameId), "save-finds.json"), d => ValidSaveFinds(d, gameId));

    private void WriteSaveFinds(SaveFindsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.GameId == Guid.Empty || LoadProfile(document.GameId).Profile.State != DocumentState.Ready || !ValidSaveFinds(document, document.GameId))
            throw new ArgumentException("The save finds contain invalid values.", nameof(document));
        documents.Write(Path.Combine(Folder(document.GameId), "save-finds.json"), document, d => ValidSaveFinds(d, document.GameId));
    }

    private static bool ValidSaveFinds(SaveFindsDocument d, Guid gameId)
    {
        if (d.SchemaVersion != 1 || d.GameId != gameId || d.Revision < 1 || d.Finds is null || d.Finds.Length > MaxSaveFinds || d.Rejected is null || d.Rejected.Length > MaxSaveFinds) return false;
        static bool Full(string? path) => path is { Length: <= 1024 } && Path.IsPathFullyQualified(path);
        if (d.Finds.Any(f => f is null || !Full(f.Path) || f.Files < 0 || f.Sessions < 0 || f.LastSeen == default) || d.Rejected.Any(r => !Full(r))) return false;
        try
        {
            static bool Distinct(IEnumerable<string> paths, int count) => paths.Select(SaveSourcePolicy.Canonical).Distinct(StringComparer.OrdinalIgnoreCase).Count() == count;
            return Distinct(d.Finds.Select(f => f.Path), d.Finds.Length) && Distinct(d.Rejected, d.Rejected.Length);
        }
        catch (ArgumentException) { return false; }
    }
}
