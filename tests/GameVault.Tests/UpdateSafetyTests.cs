using GameVault.Core;
using GameVault.Infrastructure.Updates;

namespace GameVault.Tests;

public sealed class UpdateSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-safety-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly string updates;

    public UpdateSafetyTests()
    {
        Directory.CreateDirectory(root);
        paths = ApplicationPaths.FromRoot(root);
        updates = Path.Combine(root, "updates");
        Write("Data/settings.json", "{}");
        Write("Profiles/Game One/profile.json", "{\"a\":1}");
        Write("Profiles/Game One/Backups/b1/save.dat", "save-bytes");
        Write("Profiles/Game One/Media/shot.png", "png");
        Directory.CreateDirectory(Path.Combine(root, "Profiles", "Empty Game"));
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private static ReleaseVersion Version(string text) => ReleaseVersion.TryParse(text, out var v) ? v : throw new InvalidOperationException(text);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void FingerprintOfAnUntouchedLibraryMatches()
    {
        var before = LibraryFingerprint.Compute(paths);
        var after = LibraryFingerprint.Compute(paths);
        Assert.True(before.Compare(after).IsEmpty);
        Assert.Equal(before.Digest, after.Digest);
        Assert.Equal(4, before.FileCount);
    }

    [Fact]
    public void FingerprintFindsMissingChangedAndAddedFiles()
    {
        var before = LibraryFingerprint.Compute(paths);
        File.Delete(Path.Combine(root, "Data", "settings.json"));
        Write("Profiles/Game One/profile.json", "{\"a\":2}");
        Write("Profiles/Game One/new.txt", "x");
        var diff = before.Compare(LibraryFingerprint.Compute(paths));
        Assert.Equal(["Data/settings.json"], diff.Missing);
        Assert.Equal(["Profiles/Game One/profile.json"], diff.Changed);
        Assert.Equal(["Profiles/Game One/new.txt"], diff.Added);
        Assert.Contains("1 missing", diff.Summary());
    }

    [Fact]
    public void FingerprintNoticesAMissingEmptyFolder()
    {
        var before = LibraryFingerprint.Compute(paths);
        Directory.Delete(Path.Combine(root, "Profiles", "Empty Game"));
        Assert.Single(before.Compare(LibraryFingerprint.Compute(paths)).Missing);
    }

    [Fact]
    public void BulkyFoldersAreComparedBySizeOnly()
    {
        var before = LibraryFingerprint.Compute(paths);
        Write("Profiles/Game One/Media/shot.png", "PNG");          // same size, different bytes: not hashed
        Assert.True(before.Compare(LibraryFingerprint.Compute(paths)).IsEmpty);
        Write("Profiles/Game One/Media/shot.png", "longer");
        Assert.Single(before.Compare(LibraryFingerprint.Compute(paths)).Changed);
    }

    [Fact]
    public void FingerprintTextRoundTripsAndRejectsGarbage()
    {
        var print = LibraryFingerprint.Compute(paths);
        var parsed = LibraryFingerprint.Parse(print.ToText());
        Assert.NotNull(parsed);
        Assert.Equal(print.Digest, parsed!.Digest);
        Assert.Null(LibraryFingerprint.Parse("nonsense"));
        Assert.Null(LibraryFingerprint.Parse(""));
    }

    [Fact]
    public async Task PrepareMakesAVerifiedCopyAndKeepsOnlyTheNewestTwo()
    {
        string? last = null;
        for (var i = 0; i < 4; i++)
        {
            var copy = await UpdateSafety.PrepareAsync(paths, updates, "0.1.0");
            Assert.True(File.Exists(copy.ArchivePath));
            Assert.True(File.Exists(Path.Combine(UpdateSafety.SafetyFolder(updates), copy.FingerprintFile)));
            last = copy.ArchivePath;
            await Task.Delay(1100);                                // archive names carry the second
        }
        var folder = UpdateSafety.SafetyFolder(updates);
        Assert.Equal(2, Directory.GetFiles(folder, "GameVault-data-*.zip").Length);
        Assert.Equal(2, Directory.GetFiles(folder, "fingerprint-*.txt").Length);
        Assert.True(File.Exists(last));
    }

    [Fact]
    public async Task AfterTheUpdateTheLibraryIsComparedWithTheFingerprint()
    {
        var copy = await UpdateSafety.PrepareAsync(paths, updates, "0.1.0");
        new PendingUpdateStore(updates).Write(new PendingUpdate("v0.1.0-preview.3", "0.1.0-preview.3", "GameVault-Setup-0.1.0-win-x64.exe", new string('a', 64), 10, DateTimeOffset.UtcNow,
            SafetyArchive: Path.GetFileName(copy.ArchivePath), FingerprintFile: copy.FingerprintFile));

        // Still the old version: the update has not been installed, nothing to compare.
        Assert.Equal(AfterUpdateStatus.NotApplicable, UpdateSafety.VerifyAfterUpdate(paths, updates, Version("0.1.0-preview.2")).Status);

        var running = Version("0.1.0-preview.3");
        Assert.Equal(AfterUpdateStatus.Intact, UpdateSafety.VerifyAfterUpdate(paths, updates, running).Status);

        File.Delete(Path.Combine(root, "Profiles", "Game One", "profile.json"));
        var result = UpdateSafety.VerifyAfterUpdate(paths, updates, running);
        Assert.Equal(AfterUpdateStatus.Different, result.Status);
        Assert.Equal(["Profiles/Game One/profile.json"], result.Difference!.Missing);
        Assert.True(File.Exists(result.SafetyArchive));
    }

    [Fact]
    public void PendingRecordKeepsSafetyFieldsAndRejectsPathsInThem()
    {
        var store = new PendingUpdateStore(updates);
        var pending = new PendingUpdate("v1", "0.1.0-preview.3", "GameVault-Setup-0.1.0-win-x64.exe", new string('b', 64), 5, DateTimeOffset.UtcNow, SafetyArchive: "GameVault-data-1.zip", FingerprintFile: "fingerprint-1.txt");
        store.Write(pending);
        Assert.Equal("GameVault-data-1.zip", store.Read()!.SafetyArchive);
        store.Write(pending with { SafetyArchive = @"..\..\evil.zip" });
        Assert.Null(store.Read()!.SafetyArchive);
    }
}
