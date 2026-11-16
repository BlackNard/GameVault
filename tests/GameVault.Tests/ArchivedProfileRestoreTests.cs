using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class ArchivedProfileRestoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault restore", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService service;
    private readonly string exe;
    public ArchivedProfileRestoreTests()
    {
        Directory.CreateDirectory(root);
        paths = ApplicationPaths.FromRoot(root);
        store = new(paths); service = new(paths, store);
        exe = Path.Combine(root, "installed.exe");
        File.WriteAllText(exe, "fixture; never run");
    }
    private async Task<GameProfile> Archive()
    {
        var saved = await service.SaveAsync(new(null, "Saved name", exe, "", [], GameStatus.Completed));
        var profile = saved.Profile with { BackupLimit = 7, KeepAllBackups = true, SaveSources = [Path.Combine(root, "custom-saves")] };
        await store.SaveProfileAsync(profile);
        await service.SetArchivedAsync(profile.Id, true);
        return profile with { IsArchived = true };
    }

    [Fact]
    public async Task RestoreKeepsSavedConfigurationStableIdentityAndEveryRetainedSidecar()
    {
        var archived = await Archive();
        var folder = Path.Combine(paths.Profiles, archived.Id.ToString("N"));
        await store.SaveStatisticsAsync(new(1, archived.Id,
            [new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow, 120)], []));
        foreach (var relative in new[] { "cover.png", "cover.png.bak", "notes.json", "notes.json.bak", "session.journal", "launch.json", "save-discovery.json", "Backups/retained.sav" })
            await File.WriteAllBytesAsync(Path.Combine(folder, relative), [1, 7, 35, 240]);
        var retained = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) is not ("profile.json" or "profile.json.bak"))
            .ToDictionary(file => file, File.ReadAllBytes);
        var result = await service.RestoreAsync(archived.Id);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(archived with { IsArchived = false }), System.Text.Json.JsonSerializer.Serialize(result.Profile));
        Assert.Null(result.Warning);
        foreach (var (file, bytes) in retained) Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
        Assert.Equal(retained.Count + 2, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length);
        var game = Assert.Single((await service.ListAsync()).Games);
        Assert.Equal(archived.Id, game.Id);
        Assert.Equal(120, game.PlayedSeconds);
        var duplicate = await Assert.ThrowsAsync<DuplicateGameException>(() => service.SaveAsync(new(null, "Blank defaults", exe, "", [])));
        Assert.False(duplicate.IsArchived);
    }

    [Fact]
    public async Task ArchivedDuplicateExposesRestorableIdWithoutChangingSavedProfile()
    {
        var archived = await Archive();
        var duplicate = await Assert.ThrowsAsync<DuplicateGameException>(() => service.SaveAsync(new(null, "New draft", exe, "", [])));
        Assert.True(duplicate.IsArchived);
        Assert.Equal(archived.Id, duplicate.ExistingId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(archived), System.Text.Json.JsonSerializer.Serialize(Assert.Single((await service.ListAsync()).Games).Profile));
    }

    [Fact]
    public async Task RestoreRefusesReadOnlyOrMissingProfilesWithoutOverwritingFiles()
    {
        var archived = await Archive();
        var file = Path.Combine(paths.Profiles, archived.Id.ToString("N"), "profile.json");
        const string unsupported = "{\"schemaVersion\":99}";
        await File.WriteAllTextAsync(file, unsupported);
        await Assert.ThrowsAsync<IOException>(() => service.RestoreAsync(archived.Id));
        await Assert.ThrowsAsync<IOException>(() => service.RestoreAsync(Guid.NewGuid()));
        Assert.Equal(unsupported, await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task RestoreRejectsConflictingActiveProfileWithoutChangingArchive()
    {
        var archived = await Archive();
        var conflicting = archived with { Id = Guid.NewGuid(), IsArchived = false };
        await store.SaveProfileAsync(conflicting);
        var error = await Assert.ThrowsAsync<DuplicateGameException>(() => service.RestoreAsync(archived.Id));
        Assert.Equal(conflicting.Id, error.ExistingId);
        Assert.False(error.IsArchived);
        Assert.True((await store.LoadProfileAsync(archived.Id)).Profile.Value!.IsArchived);
    }

    [Fact]
    public async Task ConcurrentRestoreAndAddCannotSplitTheSavedHistory()
    {
        var archived = await Archive();
        var other = new LibraryService(paths, new AppDataStore(paths));
        var restore = service.RestoreAsync(archived.Id);
        var add = Assert.ThrowsAsync<DuplicateGameException>(() => other.SaveAsync(new(null, "Duplicate", exe, "", [])));
        await Task.WhenAll(restore, add);
        Assert.Equal(archived.Id, (await restore).Profile.Id);
        Assert.Single((await service.ListAsync()).Games);
        Assert.False(Assert.Single((await service.ListAsync()).Games).Profile!.IsArchived);
    }

    [Fact]
    public async Task ConcurrentRestoresReturnSameSavedProfileWithoutCreatingAnotherHistory()
    {
        var archived = await Archive();
        var other = new LibraryService(paths, new AppDataStore(paths));
        var profileFile = Path.Combine(paths.Profiles, archived.Id.ToString("N"), "profile.json");
        var archivedBytes = await File.ReadAllBytesAsync(profileFile);
        var results = await Task.WhenAll(service.RestoreAsync(archived.Id), other.RestoreAsync(archived.Id));
        Assert.All(results, result => { Assert.Equal(archived.Id, result.Profile.Id); Assert.False(result.Profile.IsArchived); });
        Assert.Equal(archivedBytes, await File.ReadAllBytesAsync(profileFile + ".bak"));
        Assert.Single((await service.ListAsync()).Games);
    }
    public void Dispose() => Directory.Delete(root, true);
}
