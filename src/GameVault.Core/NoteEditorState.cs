namespace GameVault.Core;
public sealed class NoteEditorState
{
    public NoteDocument? Saved { get; private set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public bool IsReadMode { get; set; }
    public bool IsDirty => Title != (Saved?.Title ?? "") || Body != (Saved?.Body ?? "");
    public NoteEditorState(NoteDocument? note) { Saved = note; Title = note?.Title ?? ""; Body = note?.Body ?? ""; IsReadMode = note is not null; }
    public void AcceptSaved(NoteDocument note) { Saved = note; Title = note.Title; Body = note.Body; IsReadMode = true; }
}