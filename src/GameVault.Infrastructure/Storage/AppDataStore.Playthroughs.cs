using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string PlaythroughFile(Guid id) => Path.Combine(Folder(id), "playthroughs.json");

    public Task<StoredDocument<PlaythroughList>> LoadPlaythroughsAsync(Guid id) => Run(() =>
        documents.Read<PlaythroughList>(PlaythroughFile(id), value => PlaythroughList.IsValid(value, id)));

    public Task SavePlaythroughAsync(Guid id, Playthrough run) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(run);
        if (PlaythroughRules.Problem(run) is { } problem) throw new ArgumentException(problem, nameof(run));
        ChangePlaythroughs(id, runs => PlaythroughRules.Upsert(runs, run));
    });

    public Task DeletePlaythroughAsync(Guid id, Guid runId) => Run(() => ChangePlaythroughs(id, runs => [.. runs.Where(r => r.Id != runId)]));

    private void ChangePlaythroughs(Guid id, Func<Playthrough[], Playthrough[]> change)
    {
        RequireLibraryGame(id);
        var file = PlaythroughFile(id);
        var stored = documents.Read<PlaythroughList>(file, value => PlaythroughList.IsValid(value, id));
        JsonDocuments.RequireWritable(stored);
        documents.Write(file, new PlaythroughList(1, id, change(stored.Value?.Runs ?? [])), value => PlaythroughList.IsValid(value, id));
    }
}
