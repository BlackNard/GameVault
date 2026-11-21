using System.IO.Compression;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class DataArchiveTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-archive-" + Guid.NewGuid().ToString("N"));
    private readonly string output = Path.Combine(Path.GetTempPath(), "gv-archive-out-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;

    public DataArchiveTests()
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(output);
        paths = ApplicationPaths.FromRoot(root);
        Write("Data/settings.json", "{\"schemaVersion\":1}");
        Write("Profiles/Game A/profile.json", "{\"schemaVersion\":1,\"name\":\"A\"}");
        Write("Profiles/Game A/Notes/Sessions/n.md", "note ü ✓");
        Write("Profiles/Game A/Backups/b1/file.bin", new string('x', 5000));
        Write("Profiles/Game A/Media/s1/clip.mp4", new string('y', 7000));
        Write("Profiles/Game A/profile.json.tmp", "leftover");
    }

    private void Write(string relative, string text)
    {
        var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(f => !Path.GetRelativePath(root, f).StartsWith("Replaced-") && !f.EndsWith(".tmp"))
        .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));

    public void Dispose()
    {
        foreach (var folder in new[] { root, output }) if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    [Fact]
    public void Bulky_files_are_optional_and_temporary_files_never_included()
    {
        var light = DataArchive.Enumerate(paths, false);
        Assert.DoesNotContain(light, f => f.Contains("/Backups/") || f.Contains("/Media/") || f.EndsWith(".tmp"));
        Assert.Equal(3, light.Count);
        Assert.Equal(5, DataArchive.Enumerate(paths, true).Count);
        Assert.True(DataArchive.Estimate(paths, true).Bytes > DataArchive.Estimate(paths, false).Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backup_wipe_restore_is_byte_identical(bool bulky)
    {
        var zip = await DataArchive.CreateAsync(paths, output, bulky, "test");
        var expected = Snapshot().Where(p => bulky || !DataArchive.IsBulky(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        Directory.Delete(Path.Combine(root, "Data"), true);
        Directory.Delete(Path.Combine(root, "Profiles"), true);
        Write("Data/settings.json", "{\"schemaVersion\":1,\"junk\":true}");
        var result = await DataArchive.RestoreAsync(paths, zip);
        Assert.Equal(expected.Count, result.FileCount);
        Assert.Equal(expected, Snapshot());
        Assert.Contains("junk", File.ReadAllText(Path.Combine(result.ReplacedFolder, "Data", "settings.json")));
        Assert.False(Directory.EnumerateDirectories(root, ".restore-staging-*").Any());
    }

    [Fact]
    public async Task A_tampered_archive_is_rejected_without_touching_the_library()
    {
        var zip = await DataArchive.CreateAsync(paths, output, false, "test");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("Profiles/Game A/profile.json")!;
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("Profiles/Game A/profile.json").Open());
            writer.Write("{\"schemaVersion\":1,\"name\":\"EVIL\"}");
        }
        var before = Snapshot();
        await Assert.ThrowsAsync<IOException>(() => DataArchive.RestoreAsync(paths, zip));
        Assert.Equal(before, Snapshot());
        Assert.False(Directory.EnumerateDirectories(root, "Replaced-*").Any());
    }

    [Fact]
    public async Task Archives_with_unsafe_names_or_no_manifest_are_rejected()
    {
        var noManifest = Path.Combine(output, "plain.zip");
        using (var archive = ZipFile.Open(noManifest, ZipArchiveMode.Create)) archive.CreateEntry("Data/a.json");
        await Assert.ThrowsAsync<IOException>(() => DataArchive.ValidateAsync(noManifest));
        var notZip = Path.Combine(output, "bad.zip");
        File.WriteAllText(notZip, "not a zip");
        await Assert.ThrowsAsync<IOException>(() => DataArchive.ValidateAsync(notZip));

        var good = await DataArchive.CreateAsync(paths, output, false, "test");
        var evil = Path.Combine(output, "evil.zip");
        File.Copy(good, evil);
        using (var archive = ZipFile.Open(evil, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open());
            writer.Write("x");
        }
        await Assert.ThrowsAsync<IOException>(() => DataArchive.ValidateAsync(evil));
    }

    [Fact]
    public async Task The_backup_cannot_be_written_inside_the_data_it_backs_up()
    {
        await Assert.ThrowsAsync<IOException>(() => DataArchive.CreateAsync(paths, Path.Combine(root, "Data"), false, "test"));
        await Assert.ThrowsAsync<IOException>(() => DataArchive.CreateAsync(paths, Path.Combine(output, "missing"), false, "test"));
    }

    [Fact]
    public async Task An_unsupported_manifest_version_is_refused()
    {
        var zip = await DataArchive.CreateAsync(paths, output, false, "test");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            string text;
            using (var reader = new StreamReader(archive.GetEntry(DataArchive.ManifestName)!.Open())) text = reader.ReadToEnd().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");
            archive.GetEntry(DataArchive.ManifestName)!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(DataArchive.ManifestName).Open());
            writer.Write(text);
        }
        var error = await Assert.ThrowsAsync<IOException>(() => DataArchive.ValidateAsync(zip));
        Assert.Contains("unsupported", error.Message);
    }
}
