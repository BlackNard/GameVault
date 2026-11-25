using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;
using GameVault.Infrastructure.Updates;

namespace GameVault.Tests;

public sealed class ProfileImporterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault import", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths old;
    private readonly ApplicationPaths now;
    private readonly LibraryService oldLibrary;
    private readonly LibraryService library;
    private readonly ProfileImporter importer;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");

    public ProfileImporterTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "old"));
        Directory.CreateDirectory(Path.Combine(root, "now"));
        old = ApplicationPaths.FromRoot(Path.Combine(root, "old"));
        now = ApplicationPaths.FromRoot(Path.Combine(root, "now"));
        oldLibrary = new(old, new AppDataStore(old));
        var store = new AppDataStore(now);
        library = new(now, store);
        importer = new(now, store);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private string Exe(string name)
    {
        var folder = Path.Combine(root, "games", name);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "game.exe");
        File.WriteAllText(file, "fixture only; never executed");
        return file;
    }

    private async Task<Guid> AddOldAsync(string name, string? exe = null)
    {
        var id = (await oldLibrary.SaveAsync(new(null, name, exe ?? Exe(name), "", []), Png)).Profile.Id;
        var backup = Path.Combine(ProfileFolders.Resolve(old, id), "Backups", "2026-01-01");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "save.dat"), "old save");
        return id;
    }

    private static string Fingerprint(ApplicationPaths paths) => LibraryFingerprint.Compute(paths).Digest;

    [Fact]
    public async Task ImportsTheChosenGameWithItsFilesAndNeverTouchesTheOldFolder()
    {
        var chosen = await AddOldAsync("Frostpunk");
        await AddOldAsync("Not chosen");
        var before = Fingerprint(old);
        var scan = await importer.ScanAsync(old.Root);
        Assert.Equal(2, scan.Candidates.Length);
        Assert.All(scan.Candidates, c => Assert.Equal(ImportStatus.Ready, c.Status));
        Assert.Equal(1, scan.Candidates.Single(c => c.Id == chosen).Backups);

        var result = await importer.ImportAsync(old.Root, [chosen]);

        Assert.Equal([chosen], result.Imported);
        Assert.Empty(result.Failures);
        var game = Assert.Single((await library.ListAsync()).Games);
        Assert.Equal("Frostpunk", game.Profile!.Name);
        Assert.NotNull(game.CoverFile);
        Assert.Equal("old save", File.ReadAllText(Path.Combine(ProfileFolders.Resolve(now, chosen), "Backups", "2026-01-01", "save.dat")));
        Assert.Equal(before, Fingerprint(old));
        Assert.Empty(Directory.GetDirectories(now.Root, ".import-staging-*"));
    }

    [Fact]
    public async Task TheProfilesFolderItselfCanBeChosen()
    {
        await AddOldAsync("Game");
        Assert.Single((await importer.ScanAsync(old.Profiles)).Candidates);
    }

    [Fact]
    public async Task AGameAlreadyInTheLibraryIsNeverOverwritten()
    {
        var exe = Exe("Shared");
        var oldId = await AddOldAsync("Shared", exe);
        var mine = (await library.SaveAsync(new(null, "My copy", exe, "", []))).Profile;
        var candidate = Assert.Single((await importer.ScanAsync(old.Root)).Candidates);
        Assert.Equal(ImportStatus.AlreadyInLibrary, candidate.Status);
        var result = await importer.ImportAsync(old.Root, [oldId]);
        Assert.Empty(result.Imported);
        Assert.Single(result.Failures);
        Assert.Equal("My copy", Assert.Single((await library.ListAsync()).Games).Profile!.Name);
        Assert.Equal(mine.Id, (await library.ListAsync()).Games[0].Id);
    }

    [Fact]
    public async Task ImportingTwiceAddsItOnce()
    {
        var id = await AddOldAsync("Once");
        await importer.ImportAsync(old.Root, [id]);
        var again = await importer.ImportAsync(old.Root, [id]);
        Assert.Empty(again.Imported);
        Assert.Single((await library.ListAsync()).Games);
    }

    [Fact]
    public async Task TheSameNameGetsItsOwnFolder()
    {
        var id = await AddOldAsync("Same name");
        await library.SaveAsync(new(null, "Same name", Exe("other program"), "", []));
        Assert.Equal([id], (await importer.ImportAsync(old.Root, [id])).Imported);
        Assert.Equal("Same name (2)", Path.GetFileName(ProfileFolders.Resolve(now, id)));
        Assert.Equal(2, (await library.ListAsync()).Games.Length);
    }

    [Fact]
    public async Task DamagedAndNewerProfilesAreRefusedWithAReasonAndOthersStillImport()
    {
        var good = await AddOldAsync("Good");
        var damaged = await AddOldAsync("Damaged");
        var newer = await AddOldAsync("Newer");
        var damagedFile = Path.Combine(ProfileFolders.Resolve(old, damaged), "profile.json");
        File.WriteAllText(damagedFile, "{ not json");
        File.Delete(damagedFile + ".bak");
        var newerFile = Path.Combine(ProfileFolders.Resolve(old, newer), "profile.json");
        File.WriteAllText(newerFile, File.ReadAllText(newerFile).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99").Replace("\"schemaVersion\":1", "\"schemaVersion\":99"));
        var scan = await importer.ScanAsync(old.Root);
        Assert.Equal(2, scan.Candidates.Count(c => c.Status == ImportStatus.CannotImport && !string.IsNullOrWhiteSpace(c.Reason)));
        var result = await importer.ImportAsync(old.Root, [good, damaged, newer]);
        Assert.Equal([good], result.Imported);
        Assert.Equal(2, result.Failures.Length);
        Assert.Single((await library.ListAsync()).Games);
    }

    [Fact]
    public async Task AFailedCopyLeavesNothingBehind()
    {
        var id = await AddOldAsync("Locked");
        var save = Path.Combine(ProfileFolders.Resolve(old, id), "Backups", "2026-01-01", "save.dat");
        using (new FileStream(save, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await importer.ImportAsync(old.Root, [id]);
            Assert.Empty(result.Imported);
            Assert.Single(result.Failures);
        }
        Assert.Empty((await library.ListAsync()).Games);
        Assert.Empty(Directory.GetDirectories(now.Profiles));
        Assert.Empty(Directory.GetDirectories(now.Root, ".import-staging-*"));
    }

    [Fact]
    public async Task TheCurrentLibraryAndFoldersWithoutProfilesAreRefused()
    {
        await library.SaveAsync(new(null, "Mine", Exe("mine"), "", []));
        await Assert.ThrowsAsync<ArgumentException>(() => importer.ScanAsync(now.Root));
        await Assert.ThrowsAsync<ArgumentException>(() => importer.ScanAsync(now.Profiles));
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        await Assert.ThrowsAsync<ArgumentException>(() => importer.ScanAsync(empty));
        await Assert.ThrowsAsync<ArgumentException>(() => importer.ScanAsync("relative"));
    }
}
