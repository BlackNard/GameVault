using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameVault.Core;
using GameVault.Infrastructure;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;

namespace GameVault.InstallerFixture;

public sealed record InstallerSummary(int Games, int Sessions, long Seconds, int Pinned, int Unpinned, int Notes);

// Synthetic data only. This source is linked into unit tests and the excluded fixture.
public static class InstallerLibrary
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() } };
    private static void Guard(string root, Guid token)
    {
        if (token == Guid.Empty || !Path.IsPathFullyQualified(root)) throw new IOException("A fixture token and absolute root are required.");
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected fixture roots are refused.");
        var marker = Path.Combine(root, ".installer-fixture-token");
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != token.ToString("N"))
            throw new IOException("Fixture ownership token is missing or incorrect.");
    }
    public static async Task<InstallerSummary> SeedAsync(string root, Guid token)
    {
        Guard(root, token);
        var paths = ApplicationPaths.FromRoot(root);
        using (ApplicationWorkspace.Open(paths))
        {
            foreach (var directory in new[] { paths.Data, paths.Profiles })
                if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                    throw new IOException("Seed refuses existing data.");
            var store = new AppDataStore(paths);
            await store.InitializeAsync();
            await store.SaveSettingsAsync(AppSettings.Default with { Theme = "Crimson", CloseToTray = false });
            var started = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
            for (var index = 0; index < 2; index++)
            {
                var id = Guid.NewGuid(); var session = Guid.NewGuid();
                await store.SaveProfileAsync(new(1, id, "Synthetic game " + index,
                    Path.Combine(root, "never-launched.exe"), root, [], GameStatus.Completed, 20, false, false));
                await store.SaveStatisticsAsync(new(1, id, [new(session, started, started.AddHours(1), 3600)], []));
                await store.SaveNoteAsync(id, session, "Synthetic session", "# Installation fixture\nPreserve this session note.");
                var profile = ProfileFolders.Resolve(paths, id);
                await File.WriteAllBytesAsync(Path.Combine(profile, "cover.png"), Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aBTkAAAAASUVORK5CYII="));
                for (var number = 0; number < 2; number++)
                {
                    var source = Guid.NewGuid(); var bytes = Encoding.UTF8.GetBytes("Synthetic saved progress " + number);
                    var file = new BackupFile(source, "slot.sav", source.ToString("N") + "/slot.sav", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
                    var manifest = new BackupManifest(1, id, Guid.NewGuid(), started.AddMinutes(number), new string('A', 64),
                        [new(source, Path.Combine(Path.GetTempPath(), "Synthetic saves " + token.ToString("N")), SaveSourceKind.Folder)], [file], []);
                    var folder = Path.Combine(profile, "Backups", manifest.CreatedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", System.Globalization.CultureInfo.InvariantCulture) + "-" + manifest.Id.ToString("N"));
                    var payload = Path.Combine(folder, "Files", file.StoragePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
                    await File.WriteAllBytesAsync(payload, bytes);
                    await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
                    if (number == 0) await store.SaveBackupPinsAsync(new(1, id, 1, [manifest.Id]), 0);
                }
                var retained = Path.Combine(profile, "Retained", "recovery-marker.bin");
                Directory.CreateDirectory(Path.GetDirectoryName(retained)!);
                await File.WriteAllTextAsync(retained, "Unknown user files must survive too.");
            }
        }
        return await InspectAsync(root, token);
    }
    public static async Task<InstallerSummary> InspectAsync(string root, Guid token)
    {
        Guard(root, token);
        var paths = ApplicationPaths.FromRoot(root);
        using var lease = ApplicationWorkspace.Open(paths);
        var store = new AppDataStore(paths);
        var library = await store.InitializeAsync();
        if (library.Settings.State != DocumentState.Ready || library.Settings.Value?.Theme != "Crimson" || library.Warnings.Length != 0 || library.Games.Length != 2)
            throw new IOException("Seeded library did not reopen correctly.");
        var sessions = 0; long seconds = 0; var pinned = 0; var unpinned = 0; var notes = 0;
        var history = new BackupHistoryService(paths, store, new(), _ => Task.FromResult(false));
        foreach (var game in library.Games)
        {
            if (game.Profile.State != DocumentState.Ready || game.Statistics.State != DocumentState.Ready) throw new IOException("Profile/statistics are not ready.");
            sessions += game.Statistics.Value!.Sessions.Length;
            seconds += game.Statistics.Value.Sessions.Sum(session => session.DurationSeconds);
            var catalog = await history.ListAsync(game.Id);
            if (catalog.Warnings.Length != 0 || catalog.Entries.Length != 2) throw new IOException("Backup history did not reopen correctly.");
            foreach (var backup in catalog.Entries)
            {
                if (backup.Pinned) pinned++; else unpinned++;
                foreach (var file in backup.Backup.Manifest.Files)
                {
                    var directory = Path.Combine(backup.Backup.Directory, "Files");
                    var payload = Path.GetFullPath(Path.Combine(directory, file.StoragePath));
                    if (!payload.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Backup path escaped fixture.");
                    using var input = File.OpenRead(payload);
                    if (input.Length != file.Size || Convert.ToHexString(SHA256.HashData(input)) != file.Sha256) throw new IOException("Backup integrity check failed.");
                }
            }
            var catalogNotes = await store.LoadNotesAsync(game.Id);
            if (catalogNotes.Warnings.Length != 0) throw new IOException("Session notes did not reopen correctly.");
            notes += catalogNotes.Sessions.Count(note => note.Note.State == DocumentState.Ready && note.Note.Value?.Body.Contains("Preserve this session note.", StringComparison.Ordinal) == true);
        }
        var summary = new InstallerSummary(library.Games.Length, sessions, seconds, pinned, unpinned, notes);
        if (summary != new InstallerSummary(2, 2, 7200, 2, 2, 2)) throw new IOException("Synthetic library contents changed.");
        return summary;
    }
    public static string Fingerprint(string root)
    {
        var lines = new List<string>();
        foreach (var name in new[] { "Data", "Profiles" })
        {
            var folder = Path.Combine(root, name);
            if (!Directory.Exists(folder)) throw new IOException("Missing personal data folder.");
            foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Prepend(folder))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked data refused.");
                var relative = Path.GetRelativePath(root, path);
                if ((attributes & FileAttributes.Directory) != 0) lines.Add("D|" + relative);
                else { using var file = File.OpenRead(path); lines.Add("F|" + relative + "|" + Convert.ToHexString(SHA256.HashData(file))); }
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines.Order(StringComparer.Ordinal)))));
    }
}
