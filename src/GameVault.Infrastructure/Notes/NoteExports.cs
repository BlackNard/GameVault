using System.Globalization;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Notes;

/// <param name="Added">A journal entry added by hand: its note carries the entry's own id in place of a session's.</param>
public sealed record NoteExportEntry(string GameName, NoteDocument Note, PlaySession? Session, string Context, int Rating = 0, bool Added = false)
{
    public static NoteExportEntry FromJournal(string game, Guid gameId, JournalEntry entry)
    {
        var day = new DateTimeOffset(entry.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var facts = new List<string>();
        if (entry.Minutes is { } minutes) facts.Add($"Played: {minutes} minutes");
        if (entry.Tags.Length > 0) facts.Add("Tags: " + string.Join(", ", entry.Tags));
        return new(game, new(1, gameId, entry.Id, entry.Title ?? "", entry.Note, day, day), null, string.Join(" · ", facts), 0, true);
    }
}
public sealed record NoteExportCatalog(NoteExportEntry[] Entries, string[] Warnings);
public sealed record NoteExportResult(string[] Paths, string[] Failures);
public delegate void NoteWriter(Stream output, IReadOnlyList<NoteExportEntry> entries);

public static class NoteExports
{
    public static async Task<NoteExportCatalog> LoadAsync(AppDataStore store, Guid? gameId)
    {
        var entries = new List<NoteExportEntry>(); var warnings = new List<string>();
        var snapshot = await store.InitializeAsync();
        if (gameId is null) warnings.AddRange(snapshot.Warnings);
        foreach (var game in snapshot.Games.Where(g => gameId is null || g.Id == gameId))
        {
            var notes = await store.LoadNotesAsync(game.Id);
            var name = notes.Profile.Value?.Name ?? $"Unavailable game {game.Id:N}";
            warnings.AddRange(notes.Warnings.Select(w => name + ": " + w));
            if (notes.Profile.Value is null) { warnings.Add(name + ": profile unreadable; entries excluded."); continue; }
            foreach (var row in notes.Sessions)
            {
                if (row.Note.Value is NoteDocument note && row.Note.State == DocumentState.Ready)
                    entries.Add(new(name, note, row.Session, row.Context));
            }
            var journal = await store.LoadJournalAsync(game.Id);
            if (journal.Message is { } problem) warnings.Add(name + ": " + problem);
            entries.AddRange((journal.Value?.Entries ?? []).Select(e => NoteExportEntry.FromJournal(name, game.Id, e)));
            if (notes.Review is { State: DocumentState.Ready, Value: NoteDocument review })
                entries.Add(new(name, review, null, "Game review", (await store.LoadRatingAsync(game.Id)).Value?.HalfStars ?? 0));
        }
        return new(Sort(entries).ToArray(), warnings.Distinct().ToArray());
    }

    public static IEnumerable<NoteExportEntry> Sort(IEnumerable<NoteExportEntry> entries) => entries
        .OrderBy(e => e.GameName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Note.GameId)
        .ThenBy(e => e.Note.SessionId is null ? 1 : 0).ThenBy(e => e.Session?.StartedAt ?? e.Note.CreatedAt).ThenBy(e => e.Note.SessionId);

    public static string Metadata(NoteExportEntry entry)
    {
        var note = entry.Note;
        var lines = new List<string> { entry.Added ? "Journal entry" : note.SessionId is null ? "Game review" : "Session note" };
        if (entry.Added)
        {
            lines.Add("Date: " + note.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (entry.Context.Length > 0) lines.Add(entry.Context);
            return string.Join("\n", lines);
        }
        if (entry.Session is PlaySession session)
        {
            lines.Add("Started: " + session.StartedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            lines.Add("Ended: " + (session.EndedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? "Unavailable"));
            lines.Add($"Recorded duration: {session.DurationSeconds} seconds ({session.DurationSeconds / 3600}h {session.DurationSeconds / 60 % 60}m {session.DurationSeconds % 60}s)");
        }
        lines.Add(entry.Context);
        if (note.SessionId is null && entry.Rating > 0) lines.Add("Rating: " + StarRating.Describe(entry.Rating));
        lines.Add("Created: " + note.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        lines.Add("Updated: " + note.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        return string.Join("\n", lines);
    }

    public static string Markdown(IReadOnlyList<NoteExportEntry> entries)
    {
        if (entries.Count == 0) throw new ArgumentException("There are no saved notes to export.");
        var text = new StringBuilder("# GameVault notes\n\n");
        foreach (var entry in Sort(entries))
        {
            text.Append("## ").Append(Escape(entry.GameName)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(entry.Note.Title)) text.Append("### ").Append(Escape(entry.Note.Title)).Append("\n\n");
            text.Append(Escape(Metadata(entry)).Replace("\\-", "-", StringComparison.Ordinal).Replace("\n", "  \n", StringComparison.Ordinal)).Append("\n\n");
            text.Append(Escape(entry.Note.Body)).Append("\n\n---\n\n");
        }
        return text.ToString();
    }
    private static string Escape(string value)
    {
        var text = new StringBuilder();
        foreach (var ch in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            if ("\\`*_{}[]()#+-.!|>".Contains(ch)) text.Append('\\');
            text.Append(ch switch { '<' => "&lt;", '>' => "&gt;", '&' => "&amp;", _ => ch.ToString() });
        }
        return text.ToString();
    }
    public static void WriteMarkdown(Stream stream, IReadOnlyList<NoteExportEntry> entries) => stream.Write(Encoding.UTF8.GetBytes(Markdown(entries)));

    public static Task<NoteExportResult> WriteAsync(ApplicationPaths source, IReadOnlyList<NoteExportEntry> entries,
        string destination, string extension, bool separate, bool overwrite, NoteWriter writer) => Task.Run(() =>
    {
        if (entries.Count == 0) throw new ArgumentException("There are no saved notes to export.");
        if (extension is not (".md" or ".pdf")) throw new ArgumentException("Choose Markdown or PDF.");
        var target = Path.GetFullPath(destination);
        var sourceRoot = Path.TrimEndingDirectorySeparator(source.Root);
        if (target.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase) || target.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an export destination outside the GameVault app/profile folder to protect your original data.");
        if (!separate && !target.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The destination extension does not match the chosen format.");
        var paths = new List<string>(); var failures = new List<string>();
        var batches = separate ? Sort(entries).Select(e => new[] { e }).ToArray() : [Sort(entries).ToArray()];
        foreach (var batch in batches)
        {
            string? temporary = null;
            try
            {
                var folder = separate ? target : Path.GetDirectoryName(target)!;
                CheckLinks(folder); Directory.CreateDirectory(folder); CheckLinks(folder);
                temporary = Path.Combine(folder, ".gamevault-export-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                { writer(output, batch); output.Flush(true); }
                var file = target;
                if (separate)
                {
                    var note = batch[0].Note;
                    var name = SafeName(batch[0].GameName) + "-" + note.GameId.ToString("N") + "-" + (note.SessionId?.ToString("N") ?? "review");
                    for (var suffix = 0; ; suffix++)
                    {
                        file = Path.Combine(folder, name + (suffix == 0 ? "" : "-" + suffix) + extension);
                        if (File.Exists(file) || Directory.Exists(file)) continue;
                        try { CheckLinks(file); File.Move(temporary, file, false); break; }
                        catch (IOException) when (File.Exists(file) || Directory.Exists(file)) { }
                    }
                }
                else { CheckLinks(file); File.Move(temporary, file, overwrite); }
                paths.Add(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            { failures.Add($"{batch[0].GameName}: {e.Message}"); }
            finally { if (temporary is not null && File.Exists(temporary)) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
        }
        return new NoteExportResult(paths.ToArray(), failures.ToArray());
    });
    private static string SafeName(string value)
    {
        var name = new string(value.Take(45).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray()).Trim('_');
        return name.Length == 0 ? "Game" : "Game-" + name;
    }
    private static void CheckLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Choose a destination without linked folders or files.");
    }
}