using GameVault.Core;
namespace GameVault.Tests;
public sealed class NoteEditorTests
{
    [Fact] public async Task LeavingNotesRequiresSuccessfulSaveOrExplicitDiscardAndCancellationKeepsDraft()
    {
        var state = new NoteEditorState(null);
        Assert.True(await state.TryLeaveAsync(() => throw new InvalidOperationException("No prompt for clean page"), () => throw new InvalidOperationException("No save")));
        state.Title = "Draft"; state.Body = "Keep this when returning to library is cancelled";
        Assert.False(await state.TryLeaveAsync(() => NoteLeaveChoice.Cancel, () => throw new InvalidOperationException("No save")));
        Assert.False(await state.TryLeaveAsync(() => NoteLeaveChoice.Save, () => Task.FromResult(false)));
        Assert.True(state.IsDirty); Assert.Contains("Keep this", state.Body); Assert.Null(state.Saved);
        Assert.True(await state.TryLeaveAsync(() => NoteLeaveChoice.Discard, () => throw new InvalidOperationException("No save")));
    }    [Fact] public void ReadPreviewRetainsDirtyDraftAndOriginalSavedVersionUntilSaveSucceeds()
    {
        var stamp = DateTimeOffset.UtcNow; var saved = new NoteDocument(1, Guid.NewGuid(), null, "Title", "Saved", stamp, stamp);
        var state = new NoteEditorState(saved); Assert.True(state.IsReadMode); Assert.False(state.IsDirty);
        state.IsReadMode = false; state.Body = "Draft"; state.IsReadMode = true;
        Assert.True(state.IsDirty); Assert.Equal("Draft", state.Body); Assert.Equal("Saved", state.Saved!.Body);
        state.AcceptSaved(saved with { Body = "Draft" }); Assert.False(state.IsDirty); Assert.True(state.IsReadMode);
        Assert.Equal("Draft", state.Saved!.Body);
        var blank = new NoteEditorState(null); Assert.False(blank.IsReadMode); Assert.False(blank.IsDirty);
    }
}