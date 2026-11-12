using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class NoteTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault notes", Guid.NewGuid().ToString("N"));
    private readonly AppDataStore store;
    private readonly GameProfile profile = new(1, Guid.NewGuid(), "Test game", @"D:\Games\Test\game.exe", @"D:\Games\Test", [], GameStatus.Playing, 20, false, false);
    private readonly PlaySession session = new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 3029);
    public NoteTests() { Directory.CreateDirectory(root); store = new(ApplicationPaths.FromRoot(root)); }
    private async Task Seed()
    {
        await store.InitializeAsync(); await store.SaveProfileAsync(profile);
        await store.SaveStatisticsAsync(new(1, profile.Id, [session], []));
    }
    private string NotePath => Path.Combine(root, "Profiles", profile.Id.ToString("N"), "Notes", "Sessions", session.Id.ToString("N") + ".json");
    [Fact] public async Task NoteRoundTripPreservesUnicodeIdentityAndStatisticsAcrossRenameArchive()
    {
        await Seed();
        var statsPath = Path.Combine(root, "Profiles", profile.Id.ToString("N"), "statistics.json");
        var before = File.ReadAllBytes(statsPath);
        var first = await store.SaveNoteAsync(profile.Id, session.Id, "خطة", "مرحبا بالعالم\n\nCafé 🎮 日本語");
        await store.SaveProfileAsync(profile with { Name = "Renamed", IsArchived = true });
        var edited = await store.SaveNoteAsync(profile.Id, session.Id, "Edited", first.Body + "\nNext");
        var read = await new AppDataStore(ApplicationPaths.FromRoot(root)).LoadNotesAsync(profile.Id);
        Assert.Equal(edited, Assert.Single(read.Sessions).Note.Value);
        Assert.Equal(first.CreatedAt, edited.CreatedAt);
        Assert.Equal(before, File.ReadAllBytes(statsPath));
        Assert.Contains("metadata unavailable", read.Sessions[0].Context);
    }
    [Fact] public async Task NewReviewRequiresCompletedButExistingReviewSurvivesReplay()
    {
        await Seed();
        await Assert.ThrowsAsync<IOException>(() => store.SaveNoteAsync(profile.Id, null, "", "Review"));
        await store.SaveProfileAsync(profile with { Status = GameStatus.Completed });
        await store.SaveNoteAsync(profile.Id, null, "", "Review");
        await store.SaveProfileAsync(profile);
        await store.SaveNoteAsync(profile.Id, null, "", "Replay review");
        Assert.Equal("Replay review", (await store.LoadNotesAsync(profile.Id)).Review.Value!.Body);
    }
    [Fact] public async Task ActiveUnknownAndCorruptJournalSessionsCannotReceiveNotes()
    {
        await Seed();
        await Assert.ThrowsAsync<IOException>(() => store.SaveNoteAsync(profile.Id, Guid.NewGuid(), "", "Unknown"));
        await store.SaveStatisticsAsync(new(1, profile.Id, [session with { EndedAt = null }], []));
        await Assert.ThrowsAsync<IOException>(() => store.SaveNoteAsync(profile.Id, session.Id, "", "Active"));
        await store.SaveStatisticsAsync(new(1, profile.Id, [session], []));
        var journals = Path.Combine(root, "Profiles", profile.Id.ToString("N"), "Sessions");
        Directory.CreateDirectory(journals); File.WriteAllText(Path.Combine(journals, session.Id.ToString("N") + ".json"), "broken");
        Assert.False(Assert.Single((await store.LoadNotesAsync(profile.Id)).Sessions).CanEdit);
        await Assert.ThrowsAsync<IOException>(() => store.SaveNoteAsync(profile.Id, session.Id, "", "Uncertain"));
    }
    [Fact] public async Task CorruptRecoveredAndFutureNotesAreNeverReplaced()
    {
        await Seed(); await store.SaveNoteAsync(profile.Id, session.Id, "", "Original");
        await store.SaveNoteAsync(profile.Id, session.Id, "", "Newer");
        File.WriteAllText(NotePath, "broken");
        Assert.Equal(DocumentState.Recovered, (await store.LoadNotesAsync(profile.Id)).Sessions[0].Note.State);
        await Assert.ThrowsAsync<IOException>(() => store.SaveNoteAsync(profile.Id, session.Id, "", "Replace"));
        Assert.Equal("broken", File.ReadAllText(NotePath));
        File.WriteAllText(NotePath, "{\"schemaVersion\":99}");
        Assert.Equal(DocumentState.Unsupported, (await store.LoadNotesAsync(profile.Id)).Sessions[0].Note.State);
        await Assert.ThrowsAsync<IOException>(() => store.SaveNoteAsync(profile.Id, session.Id, "", "Replace"));
    }
    [Fact] public async Task FailedCommitKeepsPreviousNoteAndEmptyEditsAreRejected()
    {
        await Seed(); await store.SaveNoteAsync(profile.Id, session.Id, "", "Original");
        var failing = new AppDataStore(ApplicationPaths.FromRoot(root), p => { if (p == NotePath) throw new IOException("fixture failure"); });
        await Assert.ThrowsAsync<IOException>(() => failing.SaveNoteAsync(profile.Id, session.Id, "", "Lost"));
        Assert.Equal("Original", (await store.LoadNotesAsync(profile.Id)).Sessions[0].Note.Value!.Body);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveNoteAsync(profile.Id, session.Id, "", " \n "));
    }
    [Fact] public async Task ActiveJournalAndInconsistentStatisticsCannotBeMistakenForFinishedSession()
    {
        await Seed();
        var journal = new SessionJournal(1, profile.Id, session.Id, session.StartedAt, session.EndedAt!.Value, 3029 * TimeSpan.TicksPerSecond,
            SessionState.Active, 123, DateTime.UtcNow.Ticks, profile.ExecutablePath, null, 0);
        await store.SaveSessionJournalAsync(journal);
        await store.SaveStatisticsAsync(new(1, profile.Id, [session], []));
        Assert.False((await store.LoadNotesAsync(profile.Id)).Sessions[0].CanEdit);
        await Assert.ThrowsAsync<IOException>(() => store.SaveNoteAsync(profile.Id, session.Id, "", "Still running"));
        await store.SaveSessionJournalAsync(journal with { State = SessionState.Interrupted, RecoveryCount = 1 });
        Assert.True((await store.LoadNotesAsync(profile.Id)).Sessions[0].CanEdit);
        await store.SaveStatisticsAsync(new(1, profile.Id, [session with { DurationSeconds = 3030 }], []));
        Assert.False((await store.LoadNotesAsync(profile.Id)).Sessions[0].CanEdit);
    }
    [Fact] public async Task BulkCatalogIncludesArchivedReviewsAndReportsExcludedUnreadableNotes()
    {
        await Seed(); await store.SaveNoteAsync(profile.Id, session.Id, "Session", "Journal");
        await store.SaveProfileAsync(profile with { Status = GameStatus.Completed });
        await store.SaveNoteAsync(profile.Id, null, "Review", "Finished");
        await store.SaveProfileAsync(profile with { IsArchived = true });
        var catalog = await GameVault.Infrastructure.Notes.NoteExports.LoadAsync(store, null);
        Assert.Equal(2, catalog.Entries.Length); Assert.Equal(session.Id, catalog.Entries[0].Note.SessionId); Assert.Null(catalog.Entries[1].Note.SessionId);
        File.WriteAllText(NotePath, "{\"schemaVersion\":99}");
        catalog = await GameVault.Infrastructure.Notes.NoteExports.LoadAsync(store, profile.Id);
        Assert.Single(catalog.Entries); Assert.NotEmpty(catalog.Warnings);
        Assert.Null(catalog.Entries[0].Note.SessionId);
    }    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}