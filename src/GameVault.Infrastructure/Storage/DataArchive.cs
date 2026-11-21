using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed record DataArchiveFile(string Path, long Size, string Sha256);

public sealed record DataArchiveManifest(int SchemaVersion, DateTimeOffset CreatedAt, string AppVersion, bool IncludesBulky, DataArchiveFile[] Files);

public sealed record DataArchiveEstimate(int FileCount, long Bytes);

public sealed record DataArchiveInfo(DataArchiveManifest Manifest, string[] Warnings);

public sealed record DataRestoreResult(string ReplacedFolder, int FileCount);

/// <summary>
/// One-step backup of GameVault's own data (<c>Data</c> and <c>Profiles</c>) into a zip with a hashed manifest, and a restore that validates the zip
/// completely before it touches the library, keeps whatever it replaces, and never merges histories (ticket #89).
/// </summary>
public static class DataArchive
{
    public const string ManifestName = "manifest.json";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    /// <summary>Files that belong in a backup, as '/'-separated paths relative to the application folder.</summary>
    public static IReadOnlyList<string> Enumerate(ApplicationPaths paths, bool includeBulky)
    {
        var result = new List<string>();
        foreach (var top in new[] { paths.Data, paths.Profiles })
        {
            if (!Directory.Exists(top)) continue;
            foreach (var file in Directory.EnumerateFiles(top, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(paths.Root, file).Replace('\\', '/');
                var name = Path.GetFileName(file);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                if (!includeBulky && IsBulky(relative)) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                result.Add(relative);
            }
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>Backups and recorded media are large and can be recreated or kept elsewhere, so they are optional.</summary>
    public static bool IsBulky(string relative)
    {
        var parts = relative.Split('/');
        return parts.Length > 3 && parts[0] == "Profiles" && (parts[2] == "Backups" || parts[2] == "Media");
    }

    public static DataArchiveEstimate Estimate(ApplicationPaths paths, bool includeBulky)
    {
        var files = Enumerate(paths, includeBulky);
        long bytes = 0;
        foreach (var file in files)
        {
            try { bytes += new FileInfo(Path.Combine(paths.Root, file)).Length; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return new(files.Count, bytes);
    }

    public static async Task<string> CreateAsync(ApplicationPaths paths, string destinationFolder, bool includeBulky, string appVersion, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(destinationFolder) || !Path.IsPathFullyQualified(destinationFolder) || !Directory.Exists(destinationFolder))
            throw new IOException("Choose an existing folder for the backup.");
        var destination = Path.GetFullPath(destinationFolder);
        foreach (var inside in new[] { paths.Data, paths.Profiles })
            if (IsInside(destination, inside)) throw new IOException("Save the backup outside GameVault's own Data and Profiles folders.");

        var finalPath = Path.Combine(destination, $"GameVault-data-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var temporary = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var files = new List<DataArchiveFile>();
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (var relative in Enumerate(paths, includeBulky))
                {
                    cancel.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                    using var source = new FileStream(Path.Combine(paths.Root, relative), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var target = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                        total += read;
                    }
                    files.Add(new(relative, total, Convert.ToHexString(hash.GetHashAndReset())));
                }
                var manifest = new DataArchiveManifest(1, DateTimeOffset.UtcNow, appVersion, includeBulky, [.. files]);
                var manifestEntry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, Json, cancel);
            }
            File.Move(temporary, finalPath, overwrite: false);
            return finalPath;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Reads the whole archive and checks names, the manifest and every hash. Throws <see cref="IOException"/> for anything wrong.</summary>
    public static async Task<DataArchiveInfo> ValidateAsync(string zipPath, CancellationToken cancel = default)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var manifest = await ReadManifestAsync(zip, cancel);
            var entries = zip.Entries.Where(e => e.FullName != ManifestName && !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName, StringComparer.Ordinal);
            if (zip.Entries.Count(e => e.FullName == ManifestName) != 1) throw new IOException("The backup has no single manifest.");
            if (entries.Count != manifest.Files.Length || manifest.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Length)
                throw new IOException("The backup's file list does not match its manifest.");
            var warnings = new List<string>();
            foreach (var file in manifest.Files)
            {
                cancel.ThrowIfCancellationRequested();
                CheckName(file.Path);
                if (!entries.TryGetValue(file.Path, out var entry)) throw new IOException($"The backup is missing {file.Path}.");
                if (entry.Length != file.Size) throw new IOException($"{file.Path} has the wrong size.");
                using var stream = entry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
                {
                    total += read;
                    if (total > file.Size) throw new IOException($"{file.Path} is larger than its manifest says.");
                    hash.AppendData(buffer, 0, read);
                }
                if (total != file.Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"{file.Path} was changed or damaged.");
            }
            return new(manifest, [.. warnings]);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        {
            throw new IOException("This is not a valid GameVault data backup.", error);
        }
    }

    private static async Task<DataArchiveManifest> ReadManifestAsync(ZipArchive zip, CancellationToken cancel)
    {
        var entry = zip.GetEntry(ManifestName) ?? throw new IOException("This is not a GameVault data backup: it has no manifest.");
        if (entry.Length > 64 * 1024 * 1024) throw new IOException("The backup manifest is too large.");
        using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<DataArchiveManifest>(stream, Json, cancel) ?? throw new IOException("The backup manifest is empty.");
        if (manifest.SchemaVersion != 1) throw new IOException($"This backup uses an unsupported format version ({manifest.SchemaVersion}). Use a compatible GameVault version.");
        if (manifest.Files is null) throw new IOException("The backup manifest is incomplete.");
        return manifest;
    }

    private static void CheckName(string name)
    {
        var parts = name.Split('/');
        var ok = parts.Length >= 2 && (parts[0] == "Data" || parts[0] == "Profiles") && !name.Contains('\\') && !name.Contains(':') &&
                 parts.All(p => p.Length > 0 && p != "." && p != ".." && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        if (!ok) throw new IOException($"The backup contains an unsafe path: {name}");
    }

    /// <summary>
    /// Replaces the library with the archive. The zip is fully validated first; the current Data and Profiles are moved (not deleted)
    /// into a <c>Replaced-…</c> folder next to them, and moved back if anything fails.
    /// </summary>
    public static async Task<DataRestoreResult> RestoreAsync(ApplicationPaths paths, string zipPath, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var info = await ValidateAsync(zipPath, cancel);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var staging = Path.Combine(paths.Root, $".restore-staging-{Guid.NewGuid():N}");
        var replaced = Path.Combine(paths.Root, $"Replaced-{stamp}");
        var movedData = false;
        var movedProfiles = false;
        try
        {
            Directory.CreateDirectory(staging);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var file in info.Manifest.Files)
                {
                    cancel.ThrowIfCancellationRequested();
                    var target = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var source = zip.GetEntry(file.Path)!.Open();
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(output, cancel);
                }
            }
            Directory.CreateDirectory(Path.Combine(staging, "Data"));
            Directory.CreateDirectory(Path.Combine(staging, "Profiles"));
            Directory.CreateDirectory(replaced);
            if (Directory.Exists(paths.Data)) { Directory.Move(paths.Data, Path.Combine(replaced, "Data")); movedData = true; }
            if (Directory.Exists(paths.Profiles)) { Directory.Move(paths.Profiles, Path.Combine(replaced, "Profiles")); movedProfiles = true; }
            Directory.Move(Path.Combine(staging, "Data"), paths.Data);
            Directory.Move(Path.Combine(staging, "Profiles"), paths.Profiles);
            return new(replaced, info.Manifest.Files.Length);
        }
        catch
        {
            // Put the old library back exactly as it was.
            try
            {
                if (movedData && !Directory.Exists(paths.Data) || movedData && Directory.Exists(paths.Data) && !Directory.EnumerateFileSystemEntries(paths.Data).Any())
                {
                    if (Directory.Exists(paths.Data)) Directory.Delete(paths.Data);
                    Directory.Move(Path.Combine(replaced, "Data"), paths.Data);
                }
                if (movedProfiles && !Directory.Exists(paths.Profiles))
                    Directory.Move(Path.Combine(replaced, "Profiles"), paths.Profiles);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            try { if (Directory.Exists(replaced) && !Directory.EnumerateFileSystemEntries(replaced).Any()) Directory.Delete(replaced); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool IsInside(string folder, string parent)
    {
        var relative = Path.GetRelativePath(parent, folder);
        return relative == "." || !(Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar));
    }
}
