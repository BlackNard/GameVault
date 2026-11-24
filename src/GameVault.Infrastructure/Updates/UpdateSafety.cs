using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Updates;

/// <summary>The safety net made right before an update's installer starts.</summary>
public sealed record SafetyCopy(string ArchivePath, string FingerprintFile, LibraryFingerprint Fingerprint);

public enum AfterUpdateStatus { NotApplicable, Intact, Different }

/// <summary>What the new version found when it compared the library with the fingerprint taken before the update.</summary>
public sealed record AfterUpdateCheck(AfterUpdateStatus Status, FingerprintDifference? Difference = null, string? SafetyArchive = null, int FileCount = 0);

/// <summary>
/// "Updates must never damage the player's data" (issue #44). Right before the installer starts, a verified safety copy of the library's data is written (the existing
/// library-backup format without the bulky Backups and Media folders) and a fingerprint of the whole library is saved. The new version compares the library with that
/// fingerprint before it opens it, so any change an update could cause is noticed immediately and the safety copy is at hand. No safety copy means no update.
/// </summary>
public static class UpdateSafety
{
    public const string FolderName = "Safety";
    public const int KeepCopies = 2;
    public const string FingerprintPrefix = "fingerprint-";

    public static string SafetyFolder(string updatesFolder) => Path.Combine(updatesFolder, FolderName);

    /// <summary>
    /// A cheap yes/no before the player is even asked to restart: the safety folder must be writable and the free space must cover the archive several times over.
    /// </summary>
    public static bool CanPrepare(ApplicationPaths paths, string updatesFolder, out string reason)
    {
        reason = "";
        try
        {
            var folder = SafetyFolder(updatesFolder);
            Directory.CreateDirectory(folder);
            var estimate = DataArchive.Estimate(paths, includeBulky: false);
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
            var needed = Math.Max(50L * 1024 * 1024, estimate.Bytes * 3);
            if (free < needed) { reason = "There is not enough free disk space for the safety copy of your data."; return false; }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            reason = "The folder for the safety copy of your data cannot be used. " + error.Message;
            return false;
        }
    }

    /// <summary>Writes and verifies the safety copy and the fingerprint. Throws <see cref="IOException"/> when either cannot be made, and then the update must not start.</summary>
    public static async Task<SafetyCopy> PrepareAsync(ApplicationPaths paths, string updatesFolder, string fromVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            var folder = SafetyFolder(updatesFolder);
            Directory.CreateDirectory(folder);
            var archive = await DataArchive.CreateAsync(paths, folder, includeBulky: false, fromVersion, cancellationToken).ConfigureAwait(false);
            await DataArchive.ValidateAsync(archive, cancellationToken).ConfigureAwait(false);
            var fingerprint = LibraryFingerprint.Compute(paths, cancellationToken);
            var fingerprintFile = FingerprintPrefix + Path.GetFileNameWithoutExtension(archive) + ".txt";
            await File.WriteAllTextAsync(Path.Combine(folder, fingerprintFile), fingerprint.ToText(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            Prune(folder, KeepCopies, keepArchive: Path.GetFileName(archive));
            return new SafetyCopy(archive, fingerprintFile, fingerprint);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException)
        {
            throw new IOException("The safety copy of your data could not be made. " + error.Message, error);
        }
    }

    /// <summary>Keeps the newest <paramref name="keep"/> archives (and their fingerprints); never throws.</summary>
    public static void Prune(string folder, int keep, string? keepArchive = null)
    {
        try
        {
            var archives = new DirectoryInfo(folder).EnumerateFiles("GameVault-data-*.zip").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToArray();
            foreach (var old in archives.Skip(keep).Where(f => f.Name != keepArchive))
            {
                old.Delete();
                var print = Path.Combine(folder, FingerprintPrefix + Path.GetFileNameWithoutExtension(old.Name) + ".txt");
                if (File.Exists(print)) File.Delete(print);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Run by the new version before it opens the library. When a waiting update has now been installed (the running version is its version or newer) and its
    /// fingerprint is on file, the library must still match it exactly.
    /// </summary>
    public static AfterUpdateCheck VerifyAfterUpdate(ApplicationPaths paths, string updatesFolder, ReleaseVersion running, CancellationToken cancellationToken = default)
    {
        try
        {
            var store = new PendingUpdateStore(updatesFolder);
            var pending = store.Read();
            if (pending is null || pending.FingerprintFile is null || !AutoUpdateRules.IsInstalledAlready(pending, running)) return new(AfterUpdateStatus.NotApplicable);
            var folder = SafetyFolder(updatesFolder);
            var before = LibraryFingerprint.Parse(ReadSmall(Path.Combine(folder, pending.FingerprintFile)));
            if (before is null) return new(AfterUpdateStatus.NotApplicable);
            var archive = pending.SafetyArchive is null ? null : Path.Combine(folder, pending.SafetyArchive);
            var difference = before.Compare(LibraryFingerprint.Compute(paths, cancellationToken));
            return difference.IsEmpty ? new(AfterUpdateStatus.Intact, null, archive, before.FileCount) : new(AfterUpdateStatus.Different, difference, archive, before.FileCount);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(AfterUpdateStatus.NotApplicable); }
    }

    private static string? ReadSmall(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length <= 64L * 1024 * 1024 ? File.ReadAllText(path, Encoding.UTF8) : null;
    }
}
