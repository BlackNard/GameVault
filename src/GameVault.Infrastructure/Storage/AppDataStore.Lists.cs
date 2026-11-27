using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string ListsFile => Path.Combine(paths.Data, "lists.json");

    public Task<StoredDocument<ListsDocument>> LoadListsAsync() => Run(() => documents.Read<ListsDocument>(ListsFile, ListsDocument.IsValid));

    /// <summary>Reads, changes and saves the lists in one step, so two quick changes cannot undo each other.</summary>
    public Task<ListsDocument> ChangeListsAsync(Func<ListsDocument, ListsDocument> change) => Run(() =>
    {
        EnsureRoots();
        var stored = documents.Read<ListsDocument>(ListsFile, ListsDocument.IsValid);
        JsonDocuments.RequireWritable(stored);
        var next = change(stored.Value ?? ListsDocument.Empty);
        documents.Write(ListsFile, next, ListsDocument.IsValid);
        return next;
    });
}
