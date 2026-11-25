using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Library;

public enum ImportStatus { Ready, AlreadyInLibrary, CannotImport }

/// <summary>One profile folder found in an old GameVault folder.</summary>
public sealed record ImportCandidate(Guid? Id, string Folder, string Name, ImportStatus Status, string? Reason,
    string? ExecutablePath, bool ExecutableExists, long PlayedSeconds, int Sessions, int Backups, long Bytes);

public sealed record ImportScan(string ProfilesFolder, ImportCandidate[] Candidates);

public sealed record ImportResult(Guid[] Imported, string[] Failures);

/// <summary>
/// Brings game profiles from an old GameVault folder into this library (issue #53). It only ever adds: the old folder is never written to, an existing profile is
/// never overwritten or merged, and each game is copied to a temporary folder, checked, and only then moved into place. Settings are not imported.
/// </summary>
public sealed class ProfileImporter(ApplicationPaths paths, AppDataStore store)
{
    private const string StagingPrefix = ".import-staging-";
    private const long SpareBytes = 50L * 1024 * 1024;

    /// <summary>Reads the old folder (a GameVault folder or its Profiles folder) and says what can be imported. Writes nothing there.</summary>
    public async Task<ImportScan> ScanAsync(string folder, CancellationToken cancel = default)
    {
        var source = Source(folder);
        var current = (await store.InitializeAsync().ConfigureAwait(false)).Games;
        return await Task.Run(() =>
        {
            var documents = new JsonDocuments(new ManagedPaths(source));
            var candidates = ProfileFolders.Enumerate(source).Select(entry => Inspect(documents, entry.Id, entry.Folder, current, cancel)).ToArray();
            return new ImportScan(source.Profiles, [.. candidates.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)]);
        }, cancel).ConfigureAwait(false);
    }

    /// <summary>Copies the chosen games. A game that cannot be copied is reported and leaves nothing behind; the others still import.</summary>
    public async Task<ImportResult> ImportAsync(string folder, IReadOnlyCollection<Guid> ids, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var scan = await ScanAsync(folder, cancel).ConfigureAwait(false);
        var imported = new List<Guid>();
        var failures = new List<string>();
        foreach (var id in ids.Distinct())
        {
            cancel.ThrowIfCancellationRequested();
            // Looked at again right before copying: the library may have changed (for example an earlier game of this import used the same program).
            var current = (await store.InitializeAsync().ConfigureAwait(false)).Games;
            var source = Source(folder);
            var candidate = scan.Candidates.FirstOrDefault(c => c.Id == id);
            if (candidate is null) { failures.Add("A chosen game is no longer in that folder."); continue; }
            var fresh = await Task.Run(() => Inspect(new JsonDocuments(new ManagedPaths(source)), id, candidate.Folder, current, cancel), cancel).ConfigureAwait(false);
            if (fresh.Status != ImportStatus.Ready) { failures.Add($"{fresh.Name}: {fresh.Reason}"); continue; }
            try
            {
                await Task.Run(() => Copy(fresh, cancel), cancel).ConfigureAwait(false);
                imported.Add(id);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
            { failures.Add($"{fresh.Name}: {error.Message}"); }
        }
        return new([.. imported], [.. failures]);
    }

    private ApplicationPaths Source(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) throw new ArgumentException("Choose a folder.");
        var chosen = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (string.Equals(Path.GetFileName(chosen), "Profiles", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(chosen) is { } parent) chosen = parent;
        var source = ApplicationPaths.FromRoot(chosen);
        if (Overlaps(source.Profiles, paths.Profiles) || string.Equals(source.Root, paths.Root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("That is the library GameVault is using now. Choose the folder of the old copy.");
        if (!Directory.Exists(source.Profiles))
            throw new ArgumentException("No game profiles were found there. Choose the old GameVault folder (the one with a Profiles folder inside).");
        if (IsLink(source.Root) || IsLink(source.Profiles)) throw new ArgumentException("Linked folders are not supported. Choose the real folder.");
        return source;
    }

    private static bool Overlaps(string first, string second)
    {
        static string Normal(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        var a = Normal(first);
        var b = Normal(second);
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static ImportCandidate Inspect(JsonDocuments documents, Guid? folderId, string folder, StoredGame[] current, CancellationToken cancel)
    {
        var name = Path.GetFileName(folder);
        ImportCandidate Refuse(string reason, ImportStatus status = ImportStatus.CannotImport) => new(folderId, folder, name, status, reason, null, false, 0, 0, 0, 0);
        try
        {
            if (folderId is not Guid id) return Refuse("This folder cannot be identified as a game profile, or repeats another one.");
            if (IsLink(folder)) return Refuse("Linked folders are not supported.");
            var profile = documents.Read<GameProfile>(Path.Combine(folder, "profile.json"), p => StorageValidation.Profile(p, id));
            if (profile.State != DocumentState.Ready || profile.Value is not { } game)
                return Refuse(profile.Message ?? "Its profile is missing, damaged or from a newer version of GameVault.");
            name = game.Name;
            var statistics = documents.Read<GameStatistics>(Path.Combine(folder, "statistics.json"), s => StorageValidation.Statistics(s, id));
            if (statistics.State != DocumentState.Ready || statistics.Value is not { } played)
                return Refuse(statistics.Message ?? "Its playtime file is missing, damaged or from a newer version of GameVault.");
            var (files, bytes) = Walk(folder, cancel);
            if (files is null) return Refuse("It contains linked files or folders, which are not supported.");
            var seconds = played.Adjustments.Aggregate(played.Sessions.Aggregate(0L, (total, s) => total + s.DurationSeconds), (total, a) => total + a.Seconds);
            var backupFolder = Path.Combine(folder, "Backups");
            var backups = Directory.Exists(backupFolder) ? Directory.GetDirectories(backupFolder).Length : 0;
            var existing = current.FirstOrDefault(g => g.Id == id) ??
                current.FirstOrDefault(g => g.Profile.Value is { } other && SamePath(other.ExecutablePath, game.ExecutablePath));
            var result = new ImportCandidate(id, folder, name, ImportStatus.Ready, null, game.ExecutablePath, File.Exists(game.ExecutablePath), seconds, played.Sessions.Length, backups, bytes);
            if (existing is null) return result;
            return result with
            {
                Status = ImportStatus.AlreadyInLibrary,
                Reason = existing.Profile.Value is { IsArchived: true } ? "It is in your removed games. Restore it from there instead." : "Already in your library. It is not overwritten."
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException)
        { return Refuse("It could not be read. " + error.Message); }
    }

    private static bool SamePath(string first, string second)
    {
        try { return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>All files of a profile folder (temporary files left out), or null when anything in it is a link.</summary>
    private static (List<string>? Files, long Bytes) Walk(string folder, CancellationToken cancel)
    {
        var files = new List<string>();
        long bytes = 0;
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return (null, 0);
                if (entry is DirectoryInfo) { pending.Push(entry.FullName); continue; }
                if (entry.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add(entry.FullName);
                bytes += ((FileInfo)entry).Length;
            }
        }
        return (files, bytes);
    }

    private void Copy(ImportCandidate candidate, CancellationToken cancel)
    {
        var id = candidate.Id!.Value;
        var managed = new ManagedPaths(paths);
        managed.EnsureDirectory(paths.Profiles);
        var free = new DriveInfo(Path.GetPathRoot(paths.Root)!).AvailableFreeSpace;
        if (free < candidate.Bytes + SpareBytes) throw new IOException("There is not enough free disk space to copy this game.");
        // Staged outside Profiles, so a half-copied game is never picked up as a profile.
        var staging = Path.Combine(paths.Root, StagingPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            var staged = Path.Combine(staging, "Profiles", Path.GetFileName(candidate.Folder));
            Directory.CreateDirectory(staged);
            foreach (var directory in Directory.EnumerateDirectories(candidate.Folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                Directory.CreateDirectory(Path.Combine(staged, Path.GetRelativePath(candidate.Folder, directory)));
            var (files, _) = Walk(candidate.Folder, cancel);
            if (files is null) throw new IOException("It contains linked files or folders, which are not supported.");
            foreach (var file in files)
            {
                cancel.ThrowIfCancellationRequested();
                var target = Path.Combine(staged, Path.GetRelativePath(candidate.Folder, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
                if (new FileInfo(target).Length != new FileInfo(file).Length) throw new IOException("A file changed while it was being copied.");
            }
            var documents = new JsonDocuments(new ManagedPaths(ApplicationPaths.FromRoot(staging)));
            var profile = documents.Read<GameProfile>(Path.Combine(staged, "profile.json"), p => StorageValidation.Profile(p, id));
            var statistics = documents.Read<GameStatistics>(Path.Combine(staged, "statistics.json"), s => StorageValidation.Statistics(s, id));
            if (profile.State != DocumentState.Ready || statistics.State != DocumentState.Ready) throw new InvalidDataException("The copy did not pass its check.");
            var destination = ProfileFolders.ForNew(paths, profile.Value!.Name);
            managed.Check(destination);
            Directory.Move(staged, destination);
            ProfileFolders.Register(paths, id, destination);
            ProfileFolders.EnsureMarker(destination, id);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
