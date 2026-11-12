namespace GameVault.Core;
public enum NoteLeaveChoice { Save, Discard, Cancel }
public sealed class NoteEditorState
{
    public NoteDocument? Saved { get; private set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public bool IsReadMode { get; set; }
    public bool IsDirty => Title != (Saved?.Title ?? "") || Body != (Saved?.Body ?? "");
    public NoteEditorState(NoteDocument? note) { Saved = note; Title = note?.Title ?? ""; Body = note?.Body ?? ""; IsReadMode = note is not null; }
    public async Task<bool> TryLeaveAsync(Func<NoteLeaveChoice> choose, Func<Task<bool>> save)
    {
        if (!IsDirty) return true;
        return choose() switch { NoteLeaveChoice.Discard => true, NoteLeaveChoice.Save => await save(), _ => false };
    }
    public void AcceptSaved(NoteDocument note) { Saved = note; Title = note.Title; Body = note.Body; IsReadMode = true; }
}