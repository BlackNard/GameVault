using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>The folders seen while playing and the ones the player rejected, in their own per-game document (issue #111).</summary>
public sealed class SaveFindsPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault finds", Guid.NewGuid().ToString("N"));
    private readonly Guid id = Guid.NewGuid();
    private ApplicationPaths Paths => ApplicationPaths.FromRoot(Path.Combine(root, "app"));
    private AppDataStore Store => new(Paths);
    private string FilePath => Path.Combine(ProfileFolders.Resolve(Paths, id), "save-finds.json");

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task SeedAsync()
    {
        Directory.CreateDirectory(Paths.Root);
        await Store.InitializeAsync();
        await Store.SaveProfileAsync(new(1, id, "Game", Path.Combine(root, "game", "game.exe"), Path.Combine(root, "game"), [], GameStatus.Playing, 20, false, false));
    }

    private SaveFind Find(string name = "saves") => new(Path.Combine(root, name), 2, 1, DateTimeOffset.UtcNow, false);
    private SaveFindsDocument Document() => new(1, id, [Find()], [Path.Combine(root, "other")]);

    [Fact]
    public async Task RoundTripsBesideTheProfile()
    {
        await SeedAsync();
        Assert.Equal(DocumentState.Missing, (await Store.LoadSaveFindsAsync(id)).State);
        var document = Document();
        await Store.SaveFindsAsync(document);
        var read = await Store.LoadSaveFindsAsync(id);
        Assert.Equal(DocumentState.Ready, read.State);
        Assert.Equal(document.Finds[0], read.Value!.Finds[0]);
        Assert.Equal(document.Rejected, read.Value.Rejected);
        Assert.True(File.Exists(FilePath));
    }

    [Theory]
    [InlineData("\"extra\": 1,")]
    public async Task AnUnknownFieldMakesItReadOnly(string extra)
    {
        await SeedAsync();
        await Store.SaveFindsAsync(Document());
        File.WriteAllText(FilePath, File.ReadAllText(FilePath).Insert(1, extra));
        File.Delete(FilePath + ".bak");
        Assert.True((await Store.LoadSaveFindsAsync(id)).IsReadOnly);
        await Assert.ThrowsAsync<IOException>(() => Store.SaveFindsAsync(Document()));
    }

    [Fact]
    public async Task ANewerVersionIsLeftAlone()
    {
        await SeedAsync();
        File.WriteAllText(FilePath, "{\"schemaVersion\":2}");
        Assert.Equal(DocumentState.Unsupported, (await Store.LoadSaveFindsAsync(id)).State);
        await Assert.ThrowsAsync<IOException>(() => Store.SaveFindsAsync(Document()));
    }

    [Fact]
    public async Task InvalidContentIsRefused()
    {
        await SeedAsync();
        var many = Enumerable.Range(0, 65).Select(i => Find("s" + i)).ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveFindsAsync(Document() with { Finds = many }));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveFindsAsync(Document() with { Rejected = many.Select(f => f.Path).ToArray() }));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveFindsAsync(Document() with { Finds = [Find() with { Path = "relative" }] }));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveFindsAsync(Document() with { Finds = [Find("a"), Find("A")] }));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveFindsAsync(Document() with { GameId = Guid.Empty }));
    }

    [Fact]
    public async Task UpdatesAreAppliedOneAtATime()
    {
        await SeedAsync();
        var store = Store;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.UpdateSaveFindsAsync(id, d => d with { Rejected = [.. d.Rejected, Path.Combine(root, "r" + i)] })));
        var read = (await store.LoadSaveFindsAsync(id)).Value!;
        Assert.Equal(20, read.Rejected.Length);
        Assert.Equal(20, read.Revision);
    }
}
