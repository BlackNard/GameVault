using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>The journal (ticket #96): recorded sessions with their notes, and entries added by hand, across all games.</summary>
public sealed class JournalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault journal", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService library;

    public JournalTests()
    {
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        store = new(paths);
        library = new(paths, store);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task<Guid> AddAsync(string name)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "game.exe");
        File.WriteAllText(exe, "fixture only; never executed");
        return (await library.SaveAsync(new(null, name, exe, folder, []))).Profile.Id;
    }

    private static DateOnly D(int d) => new(2026, 5, d);
    private static JournalEntry Entry(DateOnly date, string note = "Wrote this", string? source = null, string? external = null) =>
        new(Guid.NewGuid(), date, 45, note, ["story"], null, source, external);

    [Fact]
    public async Task EntriesAreKeptTidiedAndNeverChangeRecordedPlaytime()
    {
        var id = await AddAsync("Alpha");
        var statistics = Path.Combine(ProfileFolders.Resolve(paths, id), "statistics.json");
        var before = File.ReadAllText(statistics);
        var entry = Entry(D(2)) with { Tags = [" Story ", "story", "boss", ""] };
        await store.SaveJournalEntryAsync(id, entry);
        await store.SaveJournalEntryAsync(id, entry with { Note = "Changed" });
        var saved = Assert.Single((await store.LoadJournalAsync(id)).Value!.Entries);
        Assert.Equal(("Changed", 45), (saved.Note, saved.Minutes!.Value));
        Assert.Equal(["Story", "boss"], saved.Tags);
        Assert.Equal(before, File.ReadAllText(statistics));
        await store.DeleteJournalEntryAsync(id, entry.Id);
        Assert.Empty((await store.LoadJournalAsync(id)).Value!.Entries);
    }

    [Fact]
    public async Task ImportedEntriesDoNotRepeatAndBadOnesAreRefused()
    {
        var id = await AddAsync("Alpha");
        await store.SaveJournalEntryAsync(id, Entry(D(1), "one", "backloggd", "5"));
        await store.SaveJournalEntryAsync(id, Entry(D(1), "two", "backloggd", "5"));
        Assert.Equal("two", Assert.Single((await store.LoadJournalAsync(id)).Value!.Entries).Note);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveJournalEntryAsync(id, Entry(new DateOnly(1800, 1, 1))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveJournalEntryAsync(id, Entry(D(1)) with { Minutes = -3 }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveJournalEntryAsync(id, Entry(D(1)) with { Tags = [.. Enumerable.Range(1, 9).Select(i => "t" + i)] }));
    }

    [Fact]
    public async Task TheJournalListsSessionsWithTheirNotesAndEntriesNewestFirstAcrossGames()
    {
        var alpha = await AddAsync("Alpha");
        var beta = await AddAsync("Beta");
        var session = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 5, 3, 20, 0, 0, TimeSpan.Zero);
        await store.SaveStatisticsAsync(new(1, alpha, [new(session, at, at.AddHours(1), 3600)], []));
        await store.SaveNoteAsync(alpha, session, "The sewers", "Found the key.");
        await store.SaveJournalEntryAsync(beta, Entry(D(4)));
        await store.SaveJournalEntryAsync(alpha, Entry(D(1), "Early"));
        await library.SetArchivedAsync(beta, true);                              // removed games keep their history

        var feed = await new JournalService(store).LoadAsync(TimeZoneInfo.Utc);
        var all = JournalRules.Filter(feed.Items, null, null, null);
        Assert.Equal([("Beta", false), ("Alpha", true), ("Alpha", false)], all.Select(i => (i.Game, i.SessionId is not null)));
        Assert.Equal(("The sewers", "Found the key.", 3600L), (all[1].Title, all[1].Note, all[1].Seconds!.Value));
        Assert.Equal(2, JournalRules.Filter(feed.Items, alpha, null, null).Length);
        Assert.Equal(["Alpha"], JournalRules.Filter(feed.Items, null, D(3), D(3)).Select(i => i.Game));
        Assert.Empty(feed.Warnings);
    }
}
