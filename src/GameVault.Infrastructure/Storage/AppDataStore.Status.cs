using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string StatusFile(Guid id) => Path.Combine(Folder(id), "status.json");

    /// <summary>The detail behind the game's status. A missing file means the stored status says it all.</summary>
    public Task<StoredDocument<StatusDetail>> LoadStatusDetailAsync(Guid id) => Run(() =>
        documents.Read<StatusDetail>(StatusFile(id), value => StatusDetail.IsValid(value, id)));

    /// <summary>Writes or removes the detail. The profile's own status must already fit it.</summary>
    public Task SaveStatusDetailAsync(Guid id, GameStatusDetail? detail) => Run(() => WriteStatusDetail(id, detail));

    private void WriteStatusDetail(Guid id, GameStatusDetail? detail)
    {
        var file = StatusFile(id);
        var stored = documents.Read<StatusDetail>(file, value => StatusDetail.IsValid(value, id));
        if (detail is not { } wanted)
        {
            if (stored.State != DocumentState.Missing) documents.Delete<StatusDetail>(file, value => StatusDetail.IsValid(value, id));
            return;
        }
        JsonDocuments.RequireWritable(stored);
        if (stored.Value?.Detail != wanted) documents.Write(file, new StatusDetail(1, id, wanted), value => StatusDetail.IsValid(value, id));
    }

    /// <summary>A detail belongs to the status it was chosen with: once that changes it is forgotten. It never stops the status change itself.</summary>
    private void ForgetStatusDetail(Guid id, GameStatus before, GameStatus after)
    {
        if (before == after) return;
        try { WriteStatusDetail(id, null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Sets any status from the game's page: the stored status first, then its detail.</summary>
    public async Task SetPlayStatusAsync(Guid id, PlayStatus status)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentException("Choose a supported game status.");
        // A detail that cannot be written must be found before the status changes.
        if (PlayStatuses.Detail(status) is not null) JsonDocuments.RequireWritable(await LoadStatusDetailAsync(id).ConfigureAwait(false));
        await SetGameStatusAsync(id, PlayStatuses.Stored(status)).ConfigureAwait(false);
        await SaveStatusDetailAsync(id, PlayStatuses.Detail(status)).ConfigureAwait(false);
    }
}
