using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Lists and favourites (ticket #97).</summary>
public sealed class GameListTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault lists", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;

    public GameListTests()
    {
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        store = new(paths);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private static GameList List(string name, params ListEntry[] entries) => new(Guid.NewGuid(), name, "", false, entries);
    private static ListEntry Entry(string title, Guid? game = null) => new(Guid.NewGuid(), game, title, null);

    [Fact]
    public async Task ListsKeepTheirOrderAndCanHoldGamesThatAreNotInTheLibrary()
    {
        var list = List("Horror", Entry("A", Guid.NewGuid()), Entry("Not owned"), Entry("C", Guid.NewGuid()));
        await store.ChangeListsAsync(d => GameListRules.Upsert(d, list));
        await store.ChangeListsAsync(d => GameListRules.Upsert(d, list with { Ranked = true, Entries = GameListRules.Move(list.Entries, list.Entries[2].Id, -1) }));
        var saved = Assert.Single((await store.LoadListsAsync()).Value!.Lists);
        Assert.True(saved.Ranked);
        Assert.Equal(["A", "C", "Not owned"], saved.Entries.Select(e => e.Title));
        Assert.Equal(["A", "C", "Not owned"], GameListRules.Move(saved.Entries, saved.Entries[0].Id, -1).Select(e => e.Title));   // already first
        await store.ChangeListsAsync(d => GameListRules.Remove(d, list.Id));
        Assert.Empty((await store.LoadListsAsync()).Value!.Lists);
    }

    [Fact]
    public void AGameIsOnAListOnceAndNamesAreNotRepeated()
    {
        var game = Guid.NewGuid();
        var entries = GameListRules.Add(GameListRules.Add([], Entry("A", game)), Entry("A again", game));
        Assert.Single(entries);
        Assert.Equal(2, GameListRules.Add(GameListRules.Add(entries, Entry("Other")), Entry("other")).Length);   // the same title without a game, once
        var document = GameListRules.Upsert(ListsDocument.Empty, List("Horror"));
        Assert.Throws<ArgumentException>(() => GameListRules.Upsert(document, List(" horror ")));
        Assert.Throws<ArgumentException>(() => GameListRules.Upsert(document, List("")));
    }

    [Fact]
    public void AnImportedListReplacesItself()
    {
        var first = GameListRules.Upsert(ListsDocument.Empty, List("Imported") with { Source = "backloggd", ExternalId = "9" });
        var second = GameListRules.Upsert(first, List("Imported, renamed") with { Source = "backloggd", ExternalId = "9" });
        var list = Assert.Single(second.Lists);
        Assert.Equal(("Imported, renamed", first.Lists[0].Id), (list.Name, list.Id));
    }

    [Fact]
    public async Task FavouritesAreSwitchedAndKeptInOrderAndNewerFilesLeftAlone()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await store.ChangeListsAsync(d => GameListRules.ToggleFavourite(d, a));
        await store.ChangeListsAsync(d => GameListRules.ToggleFavourite(d, b));
        Assert.Equal([a, b], (await store.LoadListsAsync()).Value!.Favourites);
        await store.ChangeListsAsync(d => GameListRules.ToggleFavourite(d, a));
        Assert.Equal([b], (await store.LoadListsAsync()).Value!.Favourites);
        var file = Path.Combine(paths.Data, "lists.json");
        File.WriteAllText(file, "{\"schemaVersion\":9}");
        await Assert.ThrowsAnyAsync<IOException>(() => store.ChangeListsAsync(d => GameListRules.ToggleFavourite(d, a)));
        Assert.Equal("{\"schemaVersion\":9}", File.ReadAllText(file));
    }
}
