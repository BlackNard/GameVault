using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Notes;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>The journal (ticket #96): entries added by hand, about a game or about none. Session notes are not part of it.</summary>
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
    public async Task TheJournalListsOnlyEntriesNewestFirstWithAndWithoutAGame()
    {
        var alpha = await AddAsync("Alpha");
        var beta = await AddAsync("Beta");
        var session = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 5, 3, 20, 0, 0, TimeSpan.Zero);
        await store.SaveStatisticsAsync(new(1, alpha, [new(session, at, at.AddHours(1), 3600)], []));
        await store.SaveNoteAsync(alpha, session, "The sewers", "Found the key.");
        await store.SaveJournalEntryAsync(beta, Entry(D(4)));
        await store.SaveJournalEntryAsync(alpha, Entry(D(1), "Early") with { Title = " First " });
        await store.SaveJournalEntryAsync(Guid.Empty, Entry(D(2), "Anything") with { PlaythroughId = Guid.NewGuid() });
        await library.SetArchivedAsync(beta, true);                              // removed games keep their history

        var feed = await new JournalService(store).LoadAsync();
        var all = JournalRules.Filter(feed.Items, null, null, null);
        Assert.Equal(new (string?, string)[] { ("Beta", "Wrote this"), (null, "Anything"), ("Alpha", "Early") }, all.Select(i => (i.Game, i.Entry.Note)));
        Assert.Null(all[1].Entry.PlaythroughId);
        Assert.Equal("First", all[2].Entry.Title);
        Assert.Equal("Anything", Assert.Single(JournalRules.Filter(feed.Items, Guid.Empty, null, null)).Entry.Note);
        Assert.Single(JournalRules.Filter(feed.Items, alpha, null, null));
        Assert.Equal(["Beta"], JournalRules.Filter(feed.Items, null, D(3), D(4)).Select(i => i.Game));
        Assert.Empty(feed.Warnings);
        await store.DeleteJournalEntryAsync(Guid.Empty, all[1].Entry.Id);
        Assert.Empty((await store.LoadGeneralJournalAsync()).Value!.Entries);
    }

    [Fact]
    public async Task EntriesAreExportedIntoTheGamesEntriesFolderOrTheVaultsOwn()
    {
        var alpha = await AddAsync("Alpha");
        var session = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 5, 3, 20, 0, 0, TimeSpan.Zero);
        await store.SaveStatisticsAsync(new(1, alpha, [new(session, at, at.AddHours(1), 3600)], []));
        await store.SaveNoteAsync(alpha, session, "The sewers", "Found the key.");
        await store.SaveJournalEntryAsync(alpha, Entry(D(1), "Early") with { Title = "Plans" });
        await store.SaveJournalEntryAsync(alpha, Entry(D(2), "Later") with { Title = "Plans" });
        await store.SaveJournalEntryAsync(Guid.Empty, Entry(D(2), "Anything"));
        var vault = new ObsidianVault(paths, store);
        var folder = await vault.CreateAsync(vault.DefaultPath);
        Task<ObsidianConflictChoice> Never(ObsidianConflict _) => throw new InvalidOperationException("no conflict expected");

        var items = (await new JournalService(store).LoadAsync()).Items;
        var first = await vault.ExportEntriesAsync(items, Never);
        Assert.Equal((3, 0), (first.Created, first.Failures.Length));
        await vault.ExportAsync((await NoteExports.LoadAsync(store, alpha)).Entries, Never);
        Assert.Equal(["Plans (2).md", "Plans.md"], Names(folder, "Alpha", "Entries"));
        Assert.Equal(["The sewers.md"], Names(folder, "Alpha", "Session notes"));
        Assert.Equal(["Entry.md"], Names(folder, "Entries"));
        var text = File.ReadAllText(Path.Combine(folder, "Alpha", "Entries", "Plans.md"));
        Assert.Contains("date: 2026-05-01\n", text);
        Assert.Contains("tags: [\"story\"]\n", text);
        Assert.Contains("# Plans\n\nEarly", text);

        // Exporting again repeats nothing; a changed entry updates its own note.
        var changed = items.Select(i => i.Entry.Note == "Anything" ? i with { Entry = i.Entry with { Note = "Changed" } } : i).ToArray();
        var again = await vault.ExportEntriesAsync(changed, Never);
        Assert.Equal((0, 1, 2), (again.Created, again.Updated, again.Unchanged));
        Assert.Contains("Changed", File.ReadAllText(Path.Combine(folder, "Entries", "Entry.md")));
    }

    private static string[] Names(params string[] parts) => [.. Directory.GetFiles(Path.Combine(parts), "*.md").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];
}
