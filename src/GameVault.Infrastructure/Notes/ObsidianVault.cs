using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Notes;

public enum ObsidianVaultState { NotLinked, Ready, Missing }
public sealed record ObsidianVaultStatus(ObsidianVaultState State, string? Path, string? Message = null);
public enum ObsidianConflictChoice { Cancel, KeepTheirs, Replace }
/// <summary>A note that was changed outside GameVault since the last export.</summary>
public sealed record ObsidianConflict(string GameName, string Title, string File);
public sealed record ObsidianExportResult(int Created, int Updated, int Unchanged, int Kept, bool Cancelled, string[] Failures);

/// <summary>
/// Writes session notes and reviews as plain Markdown into an Obsidian vault (ticket #80). Obsidian itself is not needed. GameVault only adds folders and
/// notes it can recognise again (a marker per vault and game folder, ids in each note), never deletes anything, and never overwrites a note edited elsewhere
/// without being told to.
/// </summary>
public sealed class ObsidianVault(ApplicationPaths paths, AppDataStore store)
{
    private const string VaultMarker = ".gamevault-vault.json", GameMarker = ".gamevault-game";
    private const int MaxNoteBytes = 2 * 1024 * 1024, MaxFiles = 20_000, MaxDepth = 6;
    private static readonly UTF8Encoding Utf8 = new(false);

    public string DefaultPath => System.IO.Path.Combine(paths.Root, ObsidianSettings.DefaultFolder);

    // ---------- linking ----------
    public async Task<ObsidianVaultStatus> StatusAsync()
    {
        var stored = await store.LoadObsidianSettingsAsync().ConfigureAwait(false);
        if (stored.IsReadOnly) return new(ObsidianVaultState.Missing, null, stored.Message);
        if (stored.Value?.VaultPath is not { } saved) return new(ObsidianVaultState.NotLinked, null);
        var path = System.IO.Path.GetFullPath(saved, paths.Root);
        return Directory.Exists(System.IO.Path.Combine(path, ".obsidian"))
            ? new(ObsidianVaultState.Ready, path)
            : new(ObsidianVaultState.Missing, path, "The linked vault was not found. Choose where it is now, or create a new one.");
    }

    /// <summary>Creates a vault at the folder, or links the one that is already there. Returns its path.</summary>
    public async Task<string> CreateAsync(string folder)
    {
        var path = Checked(folder);
        Directory.CreateDirectory(path);
        CheckLinks(path);
        Directory.CreateDirectory(System.IO.Path.Combine(path, ".obsidian"));
        await LinkAsync(path).ConfigureAwait(false);
        return path;
    }

    public async Task UseExistingAsync(string folder)
    {
        var path = Checked(folder);
        if (!Directory.Exists(System.IO.Path.Combine(path, ".obsidian")))
            throw new IOException("That folder is not an Obsidian vault (it has no .obsidian folder). Choose a vault, or create a new one.");
        await LinkAsync(path).ConfigureAwait(false);
    }

    /// <summary>Forgets the link. Nothing in the vault is deleted.</summary>
    public Task UnlinkAsync() => store.SaveObsidianSettingsAsync(new(1, null, Guid.Empty));

    private async Task LinkAsync(string path)
    {
        var marker = System.IO.Path.Combine(path, VaultMarker);
        CheckLinks(marker);
        var id = Guid.Empty;
        if (File.Exists(marker))
            try { id = JsonSerializer.Deserialize<Guid>(File.ReadAllText(marker)); }
            catch (JsonException) { }
        if (id == Guid.Empty)
        {
            id = Guid.NewGuid();
            Write(marker, JsonSerializer.Serialize(id));
        }
        var relative = System.IO.Path.GetRelativePath(paths.Root, path);
        var inside = !System.IO.Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);
        await store.SaveObsidianSettingsAsync(new(1, inside ? relative : path, id)).ConfigureAwait(false);
    }

    private string Checked(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !System.IO.Path.IsPathFullyQualified(folder)) throw new IOException("Choose a full folder path.");
        var path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
        if (System.IO.Path.GetPathRoot(path) is { } drive && path.Length <= drive.Length) throw new IOException("Choose a folder, not a whole drive.");
        if (Same(path, paths.Root) || Within(path, paths.Data) || Within(path, paths.Profiles) || Within(paths.Root, path))
            throw new IOException("Choose a folder of its own, outside GameVault's Data and Profiles folders.");
        CheckLinks(path);
        return path;
    }

    // ---------- exporting ----------
    /// <summary>Writes each note once. <paramref name="resolve"/> is asked before a note changed outside GameVault would be overwritten.</summary>
    public async Task<ObsidianExportResult> ExportAsync(IReadOnlyList<NoteExportEntry> entries, Func<ObsidianConflict, Task<ObsidianConflictChoice>> resolve)
    {
        if (entries.Count == 0) throw new ArgumentException("There are no saved notes to export.");
        var status = await StatusAsync().ConfigureAwait(false);
        if (status.State != ObsidianVaultState.Ready) throw new IOException(status.Message ?? "No Obsidian vault is linked.");
        var vault = status.Path!;
        CheckLinks(vault);
        int created = 0, updated = 0, unchanged = 0, kept = 0;
        var failures = new List<string>();
        foreach (var group in NoteExports.Sort(entries).GroupBy(e => e.Note.GameId))
        {
            var name = group.First().GameName;
            try
            {
                var stored = await store.LoadObsidianRecordAsync(group.Key).ConfigureAwait(false);
                JsonDocuments.RequireWritable(stored);
                var written = new Dictionary<string, string>(stored.Value?.Written ?? []);
                var folder = GameFolder(vault, group.Key, name);
                var existing = FindNotes(folder, group.Key);
                foreach (var entry in group)
                {
                    var key = entry.Note.SessionId?.ToString() ?? "review";
                    try
                    {
                        var text = Render(entry);
                        var hash = Hash(text);
                        var candidates = existing.GetValueOrDefault(key) ?? [];
                        // A copy the player made carries the same ids: the one still as GameVault wrote it comes first.
                        var file = candidates.OrderByDescending(f => Hash(Read(f)) == written.GetValueOrDefault(key)).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                        if (file is null)
                        {
                            file = FreeName(folder, FileName(entry));
                            Write(file, text);
                            existing[key] = [file];
                            created++;
                        }
                        else
                        {
                            var current = Hash(Read(file));
                            if (current == hash) unchanged++;
                            else if (current == written.GetValueOrDefault(key)) { Write(file, text); updated++; }
                            else
                            {
                                var choice = await resolve(new(name, System.IO.Path.GetFileNameWithoutExtension(file), file)).ConfigureAwait(false);
                                if (choice == ObsidianConflictChoice.Cancel)
                                {
                                    await SaveAsync(group.Key, written, failures, name).ConfigureAwait(false);
                                    return new(created, updated, unchanged, kept, true, [.. failures]);
                                }
                                if (choice == ObsidianConflictChoice.KeepTheirs) { kept++; continue; }
                                // The copy is not a note (no .md), so it is never mistaken for one.
                                var copy = $"{file}.replaced-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.bak";
                                CheckLinks(copy);
                                File.Copy(file, copy, false);
                                Write(file, text);
                                updated++;
                            }
                        }
                        written[key] = hash;
                    }
                    catch (Exception error) when (IsFileError(error)) { failures.Add($"{name}: {error.Message}"); }
                }
                await SaveAsync(group.Key, written, failures, name).ConfigureAwait(false);
            }
            catch (Exception error) when (IsFileError(error)) { failures.Add($"{name}: {error.Message}"); }
        }
        return new(created, updated, unchanged, kept, false, [.. failures]);
    }

    private async Task SaveAsync(Guid game, Dictionary<string, string> written, List<string> failures, string name)
    {
        try { await store.SaveObsidianRecordAsync(new(1, game, written)).ConfigureAwait(false); }
        catch (Exception error) when (IsFileError(error)) { failures.Add($"{name}: the export could not be remembered. {error.Message}"); }
    }

    /// <summary>The game's folder, found by its marker whatever it is now called; otherwise a new one that takes nobody else's name.</summary>
    private static string GameFolder(string vault, Guid game, string name)
    {
        var id = game.ToString();
        foreach (var folder in Folders(vault, 0))
        {
            var marker = System.IO.Path.Combine(folder, GameMarker);
            if (File.Exists(marker) && new FileInfo(marker).Length < 200 && File.ReadAllText(marker).Trim() == id) return folder;
        }
        var wanted = SafeName(name, "Game");
        for (var number = 1; ; number++)
        {
            var folder = System.IO.Path.Combine(vault, number == 1 ? wanted : $"{wanted} ({number})");
            if (Directory.Exists(folder) || File.Exists(folder)) continue;
            Directory.CreateDirectory(folder);
            Write(System.IO.Path.Combine(folder, GameMarker), id);
            return folder;
        }
    }

    private static IEnumerable<string> Folders(string root, int depth)
    {
        if (depth >= MaxDepth) yield break;
        foreach (var folder in Directory.EnumerateDirectories(root).Take(MaxFiles))
        {
            if (System.IO.Path.GetFileName(folder).StartsWith('.') || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
            yield return folder;
            foreach (var child in Folders(folder, depth + 1)) yield return child;
        }
    }

    private static Dictionary<string, List<string>> FindNotes(string folder, Guid game)
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var directory in Folders(folder, 0).Prepend(folder))
            foreach (var file in Directory.EnumerateFiles(directory, "*.md").Take(MaxFiles))
            {
                var info = new FileInfo(file);
                if (info.Length > MaxNoteBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                string? owner = null, key = null;
                var lines = Normalize(Read(file)).Split('\n');
                if (lines.Length == 0 || lines[0] != "---") continue;
                foreach (var line in lines.Skip(1).TakeWhile(l => l != "---").Take(60))
                {
                    if (line.StartsWith("gamevault-game:", StringComparison.Ordinal)) owner = line["gamevault-game:".Length..].Trim().Trim('"', '\'');
                    if (line.StartsWith("gamevault-note:", StringComparison.Ordinal)) key = line["gamevault-note:".Length..].Trim().Trim('"', '\'');
                }
                if (!string.Equals(owner, game.ToString(), StringComparison.OrdinalIgnoreCase) || key is null) continue;
                if (!result.TryGetValue(key, out var list)) result[key] = list = [];
                list.Add(file);
            }
        return result;
    }

    /// <summary>The note exactly as GameVault writes it. The same saved note always gives the same text.</summary>
    public static string Render(NoteExportEntry entry)
    {
        var note = entry.Note;
        var text = new StringBuilder("---\n");
        void Property(string name, string value) => text.Append(name).Append(": ").Append(value).Append('\n');
        static string Quoted(string value) => "\"" + new string(value.Where(c => !char.IsControl(c)).ToArray()).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        Property("gamevault-game", note.GameId.ToString());
        Property("gamevault-note", note.SessionId?.ToString() ?? "review");
        Property("game", Quoted(entry.GameName));
        if (entry.Session is { } session)
        {
            Property("started", Stamp(session.StartedAt));
            if (session.EndedAt is { } ended) Property("ended", Stamp(ended));
            Property("duration-minutes", (session.DurationSeconds / 60).ToString(CultureInfo.InvariantCulture));
        }
        if (note.SessionId is null && entry.Rating > 0) Property("rating", (entry.Rating / 2.0).ToString("0.#", CultureInfo.InvariantCulture));
        Property("updated", Stamp(note.UpdatedAt));
        text.Append("---\n\n");
        var title = note.Title.ReplaceLineEndings(" ").Trim();
        if (title.Length > 0) text.Append("# ").Append(title).Append("\n\n");
        text.Append(Normalize(note.Body).TrimEnd('\n')).Append('\n');
        return text.ToString();
    }

    private static string FileName(NoteExportEntry entry)
    {
        if (entry.Note.SessionId is null) return "Review";
        var when = entry.Session?.StartedAt ?? entry.Note.CreatedAt;
        return when.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture) + " - " + SafeName(entry.Note.Title, "Session");
    }

    private static string FreeName(string folder, string name)
    {
        for (var number = 1; ; number++)
        {
            var file = System.IO.Path.Combine(folder, (number == 1 ? name : $"{name} ({number})") + ".md");
            if (!File.Exists(file) && !Directory.Exists(file)) return file;
        }
    }

    /// <summary>A file or folder name Windows and Obsidian both accept.</summary>
    private static string SafeName(string value, string otherwise)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var kept = new string([.. value.Where(c => !char.IsControl(c) && !invalid.Contains(c) && c is not ('[' or ']' or '#' or '^' or '|'))]);
        var name = string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (name.Length > 60) name = name[..60].Trim(' ', '.');
        var stem = name.Split('.')[0].Trim().ToUpperInvariant();
        var reserved = stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]));
        return name.Length == 0 || reserved ? otherwise : name;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(Normalize(text).TrimEnd('\n'))));

    private static string Read(string file)
    {
        CheckLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxNoteBytes) throw new IOException("A note in the vault is too large to compare.");
        using var reader = new StreamReader(stream, Utf8);
        return reader.ReadToEnd();
    }

    private static void Write(string file, string text)
    {
        CheckLinks(file);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8.GetBytes(text);
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, file, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void CheckLinks(string path)
    {
        for (string? current = path; current is not null; current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked folders or files are not supported for the vault.");
    }

    private static bool Same(string first, string second) => string.Equals(System.IO.Path.TrimEndingDirectorySeparator(first), System.IO.Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string parent) => Same(path, parent) || path.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(parent) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
