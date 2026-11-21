using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class LibraryTagTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-tags-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;

    public LibraryTagTests()
    {
        Directory.CreateDirectory(root);
        paths = ApplicationPaths.FromRoot(root);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void Tags_are_trimmed_unique_ignoring_case_limited_and_sorted()
    {
        Assert.Equal(new[] { "Co-op", "rpg" }, TagRules.Parse("  rpg , Co-op;;RPG\n  "));
        Assert.Equal(new[] { "Open world" }, TagRules.Parse("Open    world"));
        Assert.Empty(TagRules.Parse(new string('x', TagRules.MaxTagLength + 1)));
        Assert.Equal(TagRules.MaxTagsPerGame, TagRules.Parse(string.Join(",", Enumerable.Range(0, 40).Select(i => "t" + i))).Length);
        Assert.Empty(TagRules.Parse(null));
    }

    [Fact]
    public void A_game_matches_only_when_it_has_every_selected_tag()
    {
        string[] tags = ["RPG", "Co-op"];
        Assert.True(TagRules.HasAll(tags, []));
        Assert.True(TagRules.HasAll(tags, ["rpg"]));
        Assert.True(TagRules.HasAll(tags, ["rpg", "co-op"]));
        Assert.False(TagRules.HasAll(tags, ["rpg", "Racing"]));
        Assert.False(TagRules.HasAll([], ["rpg"]));
        Assert.Equal(new[] { "Co-op", "racing", "RPG" }, TagRules.AllTags([tags, ["racing", "rpg"]]));
    }

    private sealed record G(string Name, DateTimeOffset? Last, long? Seconds);

    [Fact]
    public void Sorting_orders_by_name_recent_play_or_playtime_with_a_stable_tie_break()
    {
        var now = DateTimeOffset.Now;
        G[] games = [new("Zed", now, 10), new("alpha", now.AddDays(-3), 500), new("Beta", null, null), new("Gamma", now, 10)];
        string Order(string sort) => string.Join(",", LibrarySortOrder.Apply(games, sort, g => g.Name, g => g.Last, g => g.Seconds).Select(g => g.Name));
        Assert.Equal("alpha,Beta,Gamma,Zed", Order("Name"));
        Assert.Equal("Gamma,Zed,alpha,Beta", Order("LastPlayed"));
        Assert.Equal("alpha,Gamma,Zed,Beta", Order("MostPlayed"));
        Assert.Equal("Name", LibrarySortOrder.Normalize("nonsense"));
    }

    [Fact]
    public void Saved_views_remember_tags_and_sort_and_can_be_renamed_and_reordered()
    {
        var list = new SavedViewList();
        Assert.True(list.Save(new("RPGs", "Playing", false, "", ["rpg", "RPG", "co-op"], "MostPlayed")));
        list.Save(new("Short", "All", false, "", null, "bogus"));
        list.Save(new("Third", "All", false, ""));
        Assert.Equal(new[] { "co-op", "rpg" }, list.Views[0].Tags);
        Assert.Equal("MostPlayed", list.Views[0].Sort);
        Assert.Equal("Name", list.Views[1].Sort);
        Assert.False(list.Rename("Short", "rpgs"));          // taken (ignoring case)
        Assert.False(list.Rename("Short", "  "));
        Assert.True(list.Rename("Short", "Quick"));
        Assert.True(list.Move("Third", -2));
        Assert.Equal(new[] { "Third", "RPGs", "Quick" }, list.Views.Select(v => v.Name));
        Assert.False(list.Move("Third", -1));               // already first
        Assert.True(list.Move("Third", 99));
        Assert.Equal("Third", list.Views[^1].Name);
    }

    [Fact]
    public async Task Tags_persist_by_game_id_and_survive_a_reload()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        Assert.Equal(DocumentState.Missing, (await store.LoadLibraryTagsAsync()).State);
        var id = Guid.NewGuid();
        await store.SaveLibraryTagsAsync(new(1, new() { [id] = ["RPG", "Co-op"] }));
        var loaded = await new AppDataStore(paths).LoadLibraryTagsAsync();
        Assert.Equal(new[] { "RPG", "Co-op" }, loaded.Value!.Tags[id]);
    }

    [Fact]
    public async Task Invalid_tag_documents_are_refused_and_unsupported_ones_stay_read_only()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveLibraryTagsAsync(new(1, new() { [Guid.NewGuid()] = [new string('x', 99)] })));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveLibraryTagsAsync(new(2, [])));
        File.WriteAllText(Path.Combine(paths.Data, "library-tags.json"), "{\"schemaVersion\":9,\"tags\":{}}");
        var stored = await store.LoadLibraryTagsAsync();
        Assert.True(stored.IsReadOnly);
        await Assert.ThrowsAsync<IOException>(() => store.SaveLibraryTagsAsync(LibraryTagsDocument.Empty));
    }

    [Fact]
    public async Task Views_saved_by_an_earlier_version_still_load_with_default_tags_and_sort()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        File.WriteAllText(Path.Combine(paths.Data, "library-views.json"), "{\"schemaVersion\":1,\"views\":[{\"name\":\"Old\",\"status\":\"Playing\",\"removed\":false,\"search\":\"\"}]}");
        var stored = await store.LoadLibraryViewsAsync();
        Assert.Equal(DocumentState.Ready, stored.State);
        var list = new SavedViewList(stored.Value!.Views);
        Assert.Equal("Name", list.Views[0].Sort);
        Assert.Empty(list.Views[0].Tags!);
        await store.SaveLibraryViewsAsync(new(1, [.. list.Views]));
    }
}
