using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string JournalFile(Guid id) => Path.Combine(Folder(id), "journal.json");

    public Task<StoredDocument<GameJournal>> LoadJournalAsync(Guid id) => Run(() =>
        documents.Read<GameJournal>(JournalFile(id), value => GameJournal.IsValid(value, id)));

    public Task SaveJournalEntryAsync(Guid id, JournalEntry entry) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(entry);
        var tidy = entry with { Tags = JournalRules.Tags(entry.Tags), Note = entry.Note?.Trim() ?? "", Title = entry.Title?.Trim() is { Length: > 0 } title ? title : null };
        if (JournalRules.Problem(tidy) is { } problem) throw new ArgumentException(problem, nameof(entry));
        ChangeJournal(id, journal => journal with { Entries = JournalRules.Upsert(journal.Entries, tidy) });
    });

    public Task DeleteJournalEntryAsync(Guid id, Guid entryId) => Run(() => ChangeJournal(id, journal => journal with { Entries = [.. journal.Entries.Where(e => e.Id != entryId)] }));

    /// <summary>The tags and playthrough of a recorded session; with neither, nothing is kept for it.</summary>
    public Task SaveSessionDetailAsync(Guid id, Guid session, string[] tags, Guid? playthrough) => Run(() =>
    {
        var tidy = new SessionDetail(session, JournalRules.Tags(tags), playthrough);
        if (!JournalRules.ValidTags(tidy.Tags)) throw new ArgumentException($"Use up to {JournalRules.MaxTags} tags of {JournalRules.MaxTagLength} characters.");
        ChangeJournal(id, journal =>
        {
            var others = (journal.Sessions ?? []).Where(s => s.SessionId != session);
            return journal with { Sessions = [.. tidy.Tags.Length == 0 && tidy.PlaythroughId is null ? others : others.Append(tidy)] };
        });
    });

    /// <summary>The saved session notes of a game, read from their own files only (the session journals are not needed).</summary>
    public Task<NoteDocument[]> LoadSessionNoteDocumentsAsync(Guid id) => Run(() =>
    {
        var folder = Path.Combine(Folder(id), "Notes", "Sessions");
        managed.Check(folder);
        if (!Directory.Exists(folder)) return Array.Empty<NoteDocument>();
        var result = new List<NoteDocument>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var session) &&
                documents.Read<NoteDocument>(file, n => ValidNote(n, id, session)).Value is { } note) result.Add(note);
        return result.ToArray();
    });

    private void ChangeJournal(Guid id, Func<GameJournal, GameJournal> change)
    {
        RequireLibraryGame(id);
        var file = JournalFile(id);
        var stored = documents.Read<GameJournal>(file, value => GameJournal.IsValid(value, id));
        JsonDocuments.RequireWritable(stored);
        documents.Write(file, change(stored.Value ?? new GameJournal(1, id, [])), value => GameJournal.IsValid(value, id));
    }
}
