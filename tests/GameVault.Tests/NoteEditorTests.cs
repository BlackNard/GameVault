using GameVault.Core;
namespace GameVault.Tests;
public sealed class NoteEditorTests
{
    [Fact] public void ReadPreviewRetainsDirtyDraftAndOriginalSavedVersionUntilSaveSucceeds()
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