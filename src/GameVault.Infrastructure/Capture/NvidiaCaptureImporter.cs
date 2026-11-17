using System.Security.Cryptography;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Capture;

/// <summary>
/// Copies clips and screenshots that NVIDIA App saved during a game's recorded play sessions into the game's profile.
/// The originals are only read, never moved, changed or deleted. A file is copied once (matched by a fingerprint of its
/// path, size and time), never overwrites anything, and a file that is still being written is left for a later run.
/// </summary>
public sealed class NvidiaCaptureImporter(ApplicationPaths paths, CaptureMediaStore store)
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EarlyWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LateWindow = TimeSpan.FromMinutes(10);
    private const int MaxFiles = 5000, MaxDepth = 4;
    private const long FreeSpaceMargin = 64L * 1024 * 1024;

    public async Task<NvidiaImportResult> ImportAsync(Guid profileId, string captureFolder, DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(captureFolder) || !Path.IsPathFullyQualified(captureFolder) || !Directory.Exists(captureFolder)) return NvidiaImportResult.None;
        var clock = now ?? DateTimeOffset.UtcNow;
        var game = await new AppDataStore(paths).LoadProfileAsync(profileId);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value!.IsArchived)
            throw new IOException("A writable library profile is required to import captures.");
        var sessions = (game.Statistics.Value?.Sessions ?? []).Where(s => s.EndedAt is not null).OrderByDescending(s => s.StartedAt).ToArray();
        if (sessions.Length == 0) return NvidiaImportResult.None;

        var known = (await store.ListAsync(profileId)).Where(m => m.Status == CaptureMediaStatus.Complete && m.SourceFingerprint is not null)
            .Select(m => m.SourceFingerprint!).ToHashSet(StringComparer.Ordinal);
        int imported = 0, already = 0, waiting = 0, unsupported = 0, failed = 0; string? firstError = null;

        foreach (var path in Files(captureFolder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo info;
            try { info = new FileInfo(path); if (!info.Exists) continue; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            var written = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            var session = sessions.FirstOrDefault(s => written >= s.StartedAt - EarlyWindow && written <= s.EndedAt!.Value + LateWindow);
            if (session is null) continue;

            var kind = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp4" => (CaptureMediaKind?)CaptureMediaKind.Video,
                ".png" => CaptureMediaKind.Screenshot,
                _ => null
            };
            if (kind is null) { unsupported++; continue; }
            var fingerprint = Fingerprint(info, written);
            if (known.Contains(fingerprint)) { already++; continue; }
            if (clock - written < Settle) { waiting++; continue; }

            FileStream input;
            try { input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan); }
            catch (IOException) { waiting++; continue; }          // another program still has it open
            catch (UnauthorizedAccessException) { waiting++; continue; }

            using (input)
            {
                CaptureMedia? media = null;
                try
                {
                    var length = input.Length;
                    var root = Path.GetPathRoot(paths.Profiles);
                    if (!string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace < length + FreeSpaceMargin)
                        throw new IOException("Not enough free space in the GameVault folder to copy " + Path.GetFileName(path) + ".");
                    media = await store.BeginImportAsync(profileId, session.Id, kind.Value, written, fingerprint);
                    using (var output = new FileStream(media.FilePath, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        await input.CopyToAsync(output, 1 << 20, cancellationToken);
                        output.Flush(true);
                        if (output.Length != length) throw new IOException("The copy of " + Path.GetFileName(path) + " is incomplete (the file changed while copying).");
                    }
                    store.Complete(media);
                    known.Add(fingerprint);
                    imported++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
                {
                    if (media is not null) try { store.Discard(media); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                    if (error is OperationCanceledException) throw;
                    failed++; firstError ??= error.Message;
                }
            }
        }
        return new NvidiaImportResult(imported, already, waiting, unsupported, failed, firstError);
    }

    /// <summary>Stable identity of an original file: where it is, how big it is and when it was last written.</summary>
    public static string Fingerprint(FileInfo file, DateTimeOffset written)
    {
        var text = file.FullName.ToLowerInvariant() + "|" + file.Length + "|" + written.UtcTicks;
        return "nvidia:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..40].ToLowerInvariant();
    }

    private static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<(string Folder, int Depth)>();
        pending.Push((root, 0));
        var count = 0;
        while (pending.Count > 0)
        {
            var (folder, depth) = pending.Pop();
            string[] files, folders;
            try
            {
                files = Directory.GetFiles(folder);
                folders = depth < MaxDepth ? Directory.GetDirectories(folder) : [];
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
            {
                if (++count > MaxFiles) yield break;
                yield return file;
            }
            foreach (var child in folders)
            {
                try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
                pending.Push((child, depth + 1));
            }
        }
    }
}
