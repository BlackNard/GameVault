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
        var tidy = entry with { Tags = JournalRules.Tags(entry.Tags), Note = entry.Note?.Trim() ?? "" };
        if (JournalRules.Problem(tidy) is { } problem) throw new ArgumentException(problem, nameof(entry));
        ChangeJournal(id, entries => JournalRules.Upsert(entries, tidy));
    });

    public Task DeleteJournalEntryAsync(Guid id, Guid entryId) => Run(() => ChangeJournal(id, entries => [.. entries.Where(e => e.Id != entryId)]));

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

    private void ChangeJournal(Guid id, Func<JournalEntry[], JournalEntry[]> change)
    {
        RequireLibraryGame(id);
        var file = JournalFile(id);
        var stored = documents.Read<GameJournal>(file, value => GameJournal.IsValid(value, id));
        JsonDocuments.RequireWritable(stored);
        documents.Write(file, new GameJournal(1, id, change(stored.Value?.Entries ?? [])), value => GameJournal.IsValid(value, id));
    }
}
