using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Notes;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>One journal (tickets #96): session notes and hand-added entries share titles, tags, playthroughs and export.</summary>
public sealed class UnifiedJournalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault one journal", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService library;
    private readonly Guid session = Guid.NewGuid();

    public UnifiedJournalTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        File.WriteAllText(Path.Combine(root, "game", "game.exe"), "fixture only; never executed");
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        store = new(paths);
        library = new(paths, store);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task<Guid> AddAsync()
    {
        var id = (await library.SaveAsync(new(null, "Alpha", Path.Combine(root, "game", "game.exe"), Path.Combine(root, "game"), []))).Profile.Id;
        var at = new DateTimeOffset(2026, 5, 3, 20, 0, 0, TimeSpan.Zero);
        await store.SaveStatisticsAsync(new(1, id, [new(session, at, at.AddHours(1), 3600)], []));
        return id;
    }

    [Fact]
    public async Task ASessionCanBeTaggedAndLinkedToAPlaythroughWithoutTouchingItsNote()
    {
        var id = await AddAsync();
        var run = Guid.NewGuid();
        await store.SaveNoteAsync(id, session, "Sewers", "Found the key.");
        var note = File.ReadAllText(Directory.GetFiles(Path.Combine(ProfileFolders.Resolve(paths, id), "Notes", "Sessions"), "*.json").Single());
        await store.SaveSessionDetailAsync(id, session, [" boss ", "Boss", "story"], run);
        var detail = Assert.Single((await store.LoadJournalAsync(id)).Value!.Sessions!);
        Assert.Equal(["boss", "story"], detail.Tags);
        Assert.Equal(run, detail.PlaythroughId);
        Assert.Equal(note, File.ReadAllText(Directory.GetFiles(Path.Combine(ProfileFolders.Resolve(paths, id), "Notes", "Sessions"), "*.json").Single()));
        var item = Assert.Single((await new JournalService(store).LoadAsync(TimeZoneInfo.Utc)).Items);
        Assert.Equal(["boss", "story"], item.Tags);
        await store.SaveSessionDetailAsync(id, session, [], null);                     // nothing left to remember
        Assert.Empty((await store.LoadJournalAsync(id)).Value!.Sessions ?? []);
    }

    [Fact]
    public async Task AddingEntriesKeepsSessionDetailsAndTitles()
    {
        var id = await AddAsync();
        await store.SaveSessionDetailAsync(id, session, ["a"], null);
        var entry = new JournalEntry(Guid.NewGuid(), new(2026, 5, 4), 20, "Thought about it.", [], null, Title: " Plans ");
        await store.SaveJournalEntryAsync(id, entry);
        var journal = (await store.LoadJournalAsync(id)).Value!;
        Assert.Single(journal.Sessions!);
        Assert.Equal("Plans", Assert.Single(journal.Entries).Title);
        await store.DeleteJournalEntryAsync(id, entry.Id);
        Assert.Single((await store.LoadJournalAsync(id)).Value!.Sessions!);
        var item = Assert.Single((await new JournalService(store).LoadAsync(TimeZoneInfo.Utc)).Items);
        Assert.Equal(session, item.SessionId);
    }

    [Fact]
    public async Task AddedEntriesAreExportedWithTheNotes()
    {
        var id = await AddAsync();
        await store.SaveNoteAsync(id, session, "Sewers", "Found the key.");
        var entry = new JournalEntry(Guid.NewGuid(), new(2026, 5, 4), 20, "Thought about it.", ["plan"], null, Title: "Plans");
        await store.SaveJournalEntryAsync(id, entry);
        var catalog = await NoteExports.LoadAsync(store, id);
        Assert.Equal(2, catalog.Entries.Length);
        var added = catalog.Entries.Single(e => e.Added);
        Assert.Equal((entry.Id, "Plans"), (added.Note.SessionId!.Value, added.Note.Title));
        var text = NoteExports.Markdown(catalog.Entries);
        Assert.Contains("Journal entry", text);
        Assert.Contains("Thought about it", text);
        var vault = new ObsidianVault(paths, store);
        await vault.CreateAsync(vault.DefaultPath);
        var result = await vault.ExportAsync(catalog.Entries, _ => Task.FromResult(ObsidianConflictChoice.Cancel));
        Assert.Equal(2, result.Created);
        var plans = File.ReadAllText(Directory.GetFiles(vault.DefaultPath, "Plans.md", SearchOption.AllDirectories).Single());
        Assert.Contains("date: 2026-05-04", plans);
        Assert.Contains($"gamevault-note: {entry.Id}", plans);
        Assert.Equal(2, (await vault.ExportAsync(catalog.Entries, _ => Task.FromResult(ObsidianConflictChoice.Cancel))).Unchanged);
    }
}
