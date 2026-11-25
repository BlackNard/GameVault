using System.IO;
using GameVault.App;
using GameVault.Core;

namespace GameVault.App.Tests;

public sealed class ArchivedProfileAddFlowTests
{
    private static readonly GameDraft Draft = new(null, "Unsaved title", @"C:\Games\game.exe", "", []);
    private static readonly GameProfile Saved = new(1, Guid.NewGuid(), "Saved title", @"C:\Games\game.exe", @"C:\Games", [@"C:\Saves"], GameStatus.Completed, 7, true, false);

    [Fact]
    public async Task ConfirmingArchiveReturnsRetainedProfileRatherThanDraftDefaults()
    {
        var result = await GameEditorSaveFlow.SaveAsync(Draft, [3, 2, 1],
            (_, _) => throw new DuplicateGameException(Saved.Id, true),
            id => Task.FromResult(new GameSaveResult(id == Saved.Id ? Saved : throw new InvalidOperationException(), null)),
            () => Task.FromResult(RemovedProfileChoice.Restore));
        Assert.True(result!.WasRestored);
        Assert.Equal(Saved, result.Saved.Profile);
        Assert.Equal("Saved title", result.Saved.Profile.Name);
        Assert.Equal(GameStatus.Completed, result.Saved.Profile.Status);
        Assert.Equal([@"C:\Saves"], result.Saved.Profile.SaveSources);
    }

    [Fact]
    public async Task CancellingArchiveOfferLeavesDraftAndArchiveIntact()
    {
        var archived = true;
        var draft = Draft with { SaveSources = [@"C:\DraftSaves"] };
        var result = await GameEditorSaveFlow.SaveAsync(draft, null,
            (_, _) => throw new DuplicateGameException(Saved.Id, true),
            _ => { archived = false; return Task.FromResult(new GameSaveResult(Saved, null)); },
            () => Task.FromResult(RemovedProfileChoice.Cancel));
        Assert.Null(result);
        Assert.True(archived);
        Assert.Equal("Unsaved title", draft.Name);
        Assert.Equal([@"C:\DraftSaves"], draft.SaveSources);
    }

    [Fact]
    public async Task RefusingTheRestoreAddsAFreshProfileFromTheForm()
    {
        var png = new byte[] { 4 };
        var attempts = new List<GameDraft>();
        var result = await GameEditorSaveFlow.SaveAsync(Draft, png,
            (draft, cover) =>
            {
                attempts.Add(draft);
                return draft.BesideRemoved
                    ? Task.FromResult(new GameSaveResult(Saved with { Id = Guid.NewGuid(), Name = draft.Name, BackupLimit = cover![0] }, null))
                    : throw new DuplicateGameException(Saved.Id, true);
            },
            _ => throw new Xunit.Sdk.XunitException("The old profile must stay removed."),
            () => Task.FromResult(RemovedProfileChoice.AddNew));
        Assert.False(result!.WasRestored);
        Assert.NotEqual(Saved.Id, result.Saved.Profile.Id);
        Assert.Equal("Unsaved title", result.Saved.Profile.Name);
        Assert.Equal([false, true], attempts.Select(a => a.BesideRemoved));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ActiveDuplicateAndEditCannotTriggerRestore(bool archived, bool editing)
    {
        var draft = Draft with { Id = editing ? Guid.NewGuid() : null };
        var error = await Assert.ThrowsAsync<DuplicateGameException>(() => GameEditorSaveFlow.SaveAsync(draft, null,
            (_, _) => throw new DuplicateGameException(Saved.Id, archived),
            _ => throw new Xunit.Sdk.XunitException("An active duplicate or edit cannot restore another profile."),
            () => throw new Xunit.Sdk.XunitException("An active duplicate or edit cannot offer a restore.")));
        Assert.Equal(Saved.Id, error.ExistingId);
    }

    [Fact]
    public async Task OrdinaryAddSavesDraftAndChosenArtwork()
    {
        var png = new byte[] { 7, 8, 9 };
        var result = await GameEditorSaveFlow.SaveAsync(Draft, png,
            (draft, cover) => Task.FromResult(new GameSaveResult(Saved with { Name = draft.Name, BackupLimit = cover![0] }, null)),
            _ => throw new Xunit.Sdk.XunitException("A new game cannot restore."),
            () => throw new Xunit.Sdk.XunitException("A new game cannot offer a restore."));
        Assert.False(result!.WasRestored);
        Assert.Equal("Unsaved title", result.Saved.Profile.Name);
        Assert.Equal(7, result.Saved.Profile.BackupLimit);
    }

    [Fact]
    public async Task RestoreFailureSurfacesWithoutRetryingAddOrDiscardingTheDraft()
    {
        await Assert.ThrowsAsync<IOException>(() => GameEditorSaveFlow.SaveAsync(Draft, null,
            (_, _) => throw new DuplicateGameException(Saved.Id, true),
            _ => throw new IOException("Profile became read-only"),
            () => Task.FromResult(RemovedProfileChoice.Restore)));
        Assert.Equal("Unsaved title", Draft.Name);
    }
}
