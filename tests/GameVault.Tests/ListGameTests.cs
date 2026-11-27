using GameVault.Core;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>List entries that are not in the library (ticket #97): their cover and details are looked up by title and kept beside the lists.</summary>
public sealed class ListGameTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault list games", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, .. new byte[40]];

    public ListGameTests()
    {
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        store = new(paths);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private sealed class Source(List<MetadataRequest> asked) : IGameMetadataProvider
    {
        public bool Offline;
        public string Id => SteamStoreProvider.ProviderId;
        public string DisplayName => "Fake";
        public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField> { MetadataField.Description };
        public Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
        {
            asked.Add(request);
            if (Offline) throw new IOException("offline");
            return Task.FromResult<ProviderData?>(new(Description: "About " + request.Name, SteamAppId: "123"));
        }
    }

    [Fact]
    public async Task ATitleIsLookedUpWithoutAnyPathAndKept()
    {
        var asked = new List<MetadataRequest>();
        var source = new Source(asked) { Offline = true };
        var service = new GameMetadataService(store, [source]);
        var entry = Guid.NewGuid();
        await service.RefreshTitleAsync(entry, "Alan Wake", false, CancellationToken.None);
        Assert.Null((await store.LoadListGameInfoAsync(entry)).Value);                  // offline: tried again later
        source.Offline = false;
        await service.RefreshTitleAsync(entry, "Alan Wake", false, CancellationToken.None);
        var info = (await store.LoadListGameInfoAsync(entry)).Value!;
        Assert.Equal(("About Alan Wake", "123"), (info.Get(MetadataField.Description)!.Text, info.SteamAppId));
        Assert.All(asked, r => Assert.Equal(("", (string?)null), (r.ExecutablePath, r.InstallRoot)));
    }

    [Fact]
    public async Task RemovingTheEntryRemovesWhatWasFetchedAndAChosenCoverWins()
    {
        var entry = new ListEntry(Guid.NewGuid(), null, "Alan Wake", null);
        var list = new GameList(Guid.NewGuid(), "Horror", "", false, [entry]);
        await store.ChangeListsAsync(d => GameListRules.Upsert(d, list));
        await store.SaveListGameInfoAsync(GameMetadataDocument.Empty(entry.Id));
        await store.SaveListGameCoverAsync(entry.Id, Png, own: false);
        await store.SaveListGameCoverAsync(entry.Id, Png, own: true);
        Assert.EndsWith(".own.png", store.ListGameCover(entry.Id));
        await store.ForgetListGameCoverAsync(entry.Id);
        Assert.NotNull(store.ListGameCover(entry.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveListGameCoverAsync(entry.Id, [1, 2, 3], own: false));
        await store.ChangeListsAsync(d => GameListRules.Remove(d, list.Id));
        Assert.Null(store.ListGameCover(entry.Id));
        Assert.Empty(Directory.GetFiles(Path.Combine(paths.Data, "ListGames")));
    }
}
