using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

/// <summary>Journal entries. A game's entries are in its profile; <see cref="Guid.Empty"/> stands for "no game" (Data/journal.json).</summary>
public sealed partial class AppDataStore
{
    private string JournalFile(Guid id) => Path.Combine(Folder(id), "journal.json");
    private string GeneralJournalFile => Path.Combine(paths.Data, "journal.json");

    public Task<StoredDocument<GameJournal>> LoadJournalAsync(Guid id) => Run(() =>
        documents.Read<GameJournal>(JournalFile(id), value => GameJournal.IsValid(value, id)));

    public Task<StoredDocument<GeneralJournal>> LoadGeneralJournalAsync() => Run(() =>
        documents.Read<GeneralJournal>(GeneralJournalFile, GeneralJournal.IsValid));

    public Task SaveJournalEntryAsync(Guid id, JournalEntry entry) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(entry);
        var tidy = entry with { Tags = JournalRules.Tags(entry.Tags), Note = entry.Note?.Trim() ?? "", Title = entry.Title?.Trim() is { Length: > 0 } title ? title : null };
        if (id == Guid.Empty) tidy = tidy with { PlaythroughId = null };
        if (JournalRules.Problem(tidy) is { } problem) throw new ArgumentException(problem, nameof(entry));
        ChangeJournal(id, entries => JournalRules.Upsert(entries, tidy));
    });

    public Task DeleteJournalEntryAsync(Guid id, Guid entryId) => Run(() => ChangeJournal(id, entries => [.. entries.Where(e => e.Id != entryId)]));

    private void ChangeJournal(Guid id, Func<JournalEntry[], JournalEntry[]> change)
    {
        if (id == Guid.Empty)
        {
            EnsureRoots();
            var general = documents.Read<GeneralJournal>(GeneralJournalFile, GeneralJournal.IsValid);
            JsonDocuments.RequireWritable(general);
            documents.Write(GeneralJournalFile, new GeneralJournal(1, change(general.Value?.Entries ?? [])), GeneralJournal.IsValid);
            return;
        }
        RequireLibraryGame(id);
        var file = JournalFile(id);
        var stored = documents.Read<GameJournal>(file, value => GameJournal.IsValid(value, id));
        JsonDocuments.RequireWritable(stored);
        documents.Write(file, new GameJournal(1, id, change(stored.Value?.Entries ?? [])), value => GameJournal.IsValid(value, id));
    }
}
