using GameVault.Core;
namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    public Task<GameNotes> LoadNotesAsync(Guid gameId) => Run(() => LoadNotes(gameId));

    public Task<NoteDocument> SaveNoteAsync(Guid gameId, Guid? sessionId, string title, string body) => Run(() =>
    {
        if (title is null || body is null || title.Length > 2000 || body.Length > 1_000_000 || string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Write a note before saving (title up to 2,000 characters; body up to 1,000,000).");
        var catalog = LoadNotes(gameId);
        if (catalog.Profile.State != DocumentState.Ready) throw new IOException("The profile needs attention before notes can be saved.");
        StoredDocument<NoteDocument> current;
        if (sessionId is Guid id)
        {
            var row = catalog.Sessions.SingleOrDefault(s => s.Id == id);
            if (row is null || !row.CanEdit) throw new IOException("Only a verified finished or interrupted recorded session can receive a note. Refresh after play ends or resolve the session warning.");
            current = row.Note;
        }
        else
        {
            if (!catalog.CanReview) throw new IOException("Mark this game Completed or Retired to write its first review.");
            current = catalog.Review;
        }
        JsonDocuments.RequireWritable(current);
        var now = DateTimeOffset.UtcNow;
        if (current.Value is NoteDocument old && now < old.UpdatedAt) now = old.UpdatedAt;
        var note = new NoteDocument(1, gameId, sessionId, title, body, current.Value?.CreatedAt ?? now, now);
        documents.Write(NoteFile(gameId, sessionId), note, n => ValidNote(n, gameId, sessionId));
        return note;
    });

    private GameNotes LoadNotes(Guid gameId)
    {
        var game = LoadProfile(gameId);
        var warnings = new List<string>();
        if (game.Profile.Message is string profileMessage) warnings.Add(profileMessage);
        if (game.Statistics.Message is string statsMessage) warnings.Add(statsMessage);
        var review = documents.Read<NoteDocument>(NoteFile(gameId, null), n => ValidNote(n, gameId, null));
        if (review.Message is string reviewMessage) warnings.Add("Game review: " + reviewMessage);
        var sessions = game.Statistics.Value?.Sessions ?? [];
        var ids = sessions.Select(s => s.Id).ToHashSet();
        var folder = Path.Combine(Folder(gameId), "Notes", "Sessions");
        try
        {
            managed.Check(folder);
            if (Directory.Exists(folder))
                foreach (var path in Directory.GetFiles(folder))
                {
                    var name = Path.GetFileName(path);
                    if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                    if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out var id) && id != Guid.Empty) ids.Add(id);
                    else warnings.Add("An unrecognized session note was preserved; inspect the profile's Notes folder.");
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add("Session notes cannot be enumerated safely; check access and linked paths."); }
        var rows = ids.Select(id =>
        {
            var session = sessions.SingleOrDefault(s => s.Id == id);
            var note = documents.Read<NoteDocument>(NoteFile(gameId, id), n => ValidNote(n, gameId, id));
            if (note.Message is string message) warnings.Add($"Session {id:N}: {message}");
            var journal = documents.Read<SessionJournal>(Path.Combine(Folder(gameId), "Sessions", id.ToString("N") + ".json"), n => StorageValidation.Session(n, gameId, id));
            var consistent = journal.Value is SessionJournal j && session is not null && j.StartedAt == session.StartedAt &&
                j.DurationTicks / TimeSpan.TicksPerSecond == session.DurationSeconds && j.State != SessionState.Active && j.ObservedAt == session.EndedAt;
            var eligible = game.Statistics.State == DocumentState.Ready && session?.EndedAt is not null &&
                (journal.State == DocumentState.Missing || (journal.State == DocumentState.Ready && consistent));
            var context = session is null ? "Session statistics unavailable; existing note preserved." :
                journal.State == DocumentState.Missing ? (session.EndedAt is null ? "Active session" : "Finished session · recovery metadata unavailable") :
                journal.State != DocumentState.Ready || (!consistent && journal.Value?.State != SessionState.Active) ? "Session metadata needs attention; refresh after tracking ends or check recovery files." :
                journal.Value!.State == SessionState.Active ? "Active session · write a note after tracking ends" :
                journal.Value.State == SessionState.Interrupted ? "Interrupted · recorded through last checkpoint" + (journal.Value.RecoveryCount > 0 ? " · recovered" : "") :
                "Finished session" + (journal.Value.RecoveryCount > 0 ? " · recovered" : "");
            if (journal.Message is string journalMessage) warnings.Add($"Session {id:N}: {journalMessage}");
            return new SessionNote(id, session, eligible && game.Profile.State == DocumentState.Ready && !note.IsReadOnly, context, note);
        }).OrderByDescending(s => s.Session?.StartedAt).ThenBy(s => s.Id).ToArray();
        return new(game.Profile, rows, review, game.Profile.State == DocumentState.Ready && !review.IsReadOnly &&
            (review.Value is not null || game.Profile.Value?.Status == GameStatus.Completed || Retired(gameId, game.Profile.Value?.Status)), warnings.Distinct().ToArray());
    }

    /// <summary>Retired means the player is done with it, so it may be reviewed too.</summary>
    private bool Retired(Guid id, GameStatus? stored) => stored is { } status &&
        PlayStatuses.From(status, documents.Read<StatusDetail>(StatusFile(id), value => StatusDetail.IsValid(value, id)).Value?.Detail) == PlayStatus.Retired;

    private string NoteFile(Guid gameId, Guid? sessionId) => sessionId is Guid id && id != Guid.Empty
        ? Path.Combine(Folder(gameId), "Notes", "Sessions", id.ToString("N") + ".json")
        : sessionId is null ? Path.Combine(Folder(gameId), "Notes", "review.json") : throw new ArgumentException("Invalid session identifier.");

    private static bool ValidNote(NoteDocument note, Guid gameId, Guid? sessionId) => note.SchemaVersion == 1 &&
        note.GameId == gameId && note.SessionId == sessionId && note.Title is not null && note.Title.Length <= 2000 &&
        note.Body is not null && !string.IsNullOrWhiteSpace(note.Body) && note.Body.Length <= 1_000_000 &&
        note.CreatedAt != default && note.UpdatedAt >= note.CreatedAt;
}