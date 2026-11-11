using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault library", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService service;
    private readonly string exe;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");

    public LibraryTests()
    {
        Directory.CreateDirectory(root);
        paths = ApplicationPaths.FromRoot(root);
        store = new(paths); service = new(paths, store);
        var install = Path.Combine(root, "Game with spaces");
        Directory.CreateDirectory(install);
        exe = Path.Combine(install, "game.exe");
        File.WriteAllText(exe, "fixture only; never executed");
    }
    private GameDraft Draft(Guid? id = null) => new(id, "My game", exe, "", []);

    [Fact]
    public async Task AddInheritsDefaultsAndSurvivesRestart()
    {
        await store.InitializeAsync();
        await store.SaveSettingsAsync(AppSettings.Default with { DefaultBackupLimit = 33, KeepAllBackups = true });
        var saved = await service.SaveAsync(Draft());
        var item = Assert.Single((await new LibraryService(paths, new AppDataStore(paths)).ListAsync()).Games);
        Assert.Equal(saved.Profile.Id, item.Id);
        Assert.Equal(GameStatus.NotStarted, item.Profile!.Status);
        Assert.Equal(Path.GetDirectoryName(exe), item.Profile.WorkingDirectory);
        Assert.Equal(33, item.Profile.BackupLimit);
        Assert.True(item.Profile.KeepAllBackups);
        Assert.False(item.ExecutableMissing);
        Assert.Equal(0, item.PlayedSeconds);
    }

    [Fact]
    public async Task CanonicalExecutableAliasesCannotCreateDuplicateProfiles()
    {
        var saved = await service.SaveAsync(Draft());
        var alias = Path.Combine(Path.GetDirectoryName(exe)!, "..", "Game with spaces", "game.exe");
        var error = await Assert.ThrowsAsync<DuplicateGameException>(() => service.SaveAsync(Draft() with { ExecutablePath = alias }));
        Assert.Equal(saved.Profile.Id, error.ExistingId);
        Assert.Single((await service.ListAsync()).Games);
    }

    [Fact]
    public async Task ConcurrentAddsAcrossServicesCannotDuplicateAnExecutable()
    {
        var other = new LibraryService(paths, new AppDataStore(paths));
        async Task<bool> Add(LibraryService library)
        {
            try { await library.SaveAsync(Draft()); return true; }
            catch (DuplicateGameException) { return false; }
        }
        var results = await Task.WhenAll(Add(service), Add(other));
        Assert.Single(results, r => r);
        Assert.Single((await service.ListAsync()).Games);
    }

    [Fact]
    public async Task ArchiveAndRestoreRetainIdentityHistoryAndBackupFiles()
    {
        var saved = await service.SaveAsync(Draft());
        var statistics = new GameStatistics(1, saved.Profile.Id,
            [new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, 600)], []);
        await store.SaveStatisticsAsync(statistics);
        var backup = Path.Combine(paths.Profiles, saved.Profile.Id.ToString("N"), "Backups", "retained.sav");
        await File.WriteAllTextAsync(backup, "keep me");
        await service.SetArchivedAsync(saved.Profile.Id, true);
        var archived = Assert.Single((await service.ListAsync()).Games);
        Assert.True(archived.Profile!.IsArchived);
        await Assert.ThrowsAsync<DuplicateGameException>(() => service.SaveAsync(Draft()));
        await service.SetArchivedAsync(saved.Profile.Id, false);
        var restored = Assert.Single((await service.ListAsync()).Games);
        Assert.False(restored.Profile!.IsArchived);
        Assert.Equal(600, restored.PlayedSeconds);
        Assert.Equal("keep me", await File.ReadAllTextAsync(backup));
    }

    [Fact]
    public async Task RenameAndStatusChangePreserveIdentityAndBackupPreferences()
    {
        var saved = await service.SaveAsync(Draft());
        await store.SaveProfileAsync(saved.Profile with { BackupLimit = 9, KeepAllBackups = true });
        var result = await service.SaveAsync(Draft(saved.Profile.Id) with { Name = "Renamed", Status = GameStatus.Completed });
        Assert.Equal(saved.Profile.Id, result.Profile.Id);
        Assert.Equal("Renamed", result.Profile.Name);
        Assert.Equal(GameStatus.Completed, result.Profile.Status);
        Assert.Equal(9, result.Profile.BackupLimit);
        Assert.True(result.Profile.KeepAllBackups);
    }

    [Fact]
    public async Task MissingUnchangedInstallationCanBeEditedWithoutDiscardingRecords()
    {
        var saved = await service.SaveAsync(Draft()); File.Delete(exe);
        Assert.True(Assert.Single((await service.ListAsync()).Games).ExecutableMissing);
        var result = await service.SaveAsync(Draft(saved.Profile.Id) with { Name = "Still retained" });
        Assert.Equal("Still retained", result.Profile.Name);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Draft() with { ExecutablePath = Path.Combine(root, "new-missing.exe") }));
    }

    [Theory]
    [InlineData("relative.exe")]
    [InlineData("missing.exe")]
    public async Task InvalidExecutableDoesNotCreateAGame(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Draft() with { ExecutablePath = path }));
        Assert.Empty((await service.ListAsync()).Games);
    }

    [Fact]
    public async Task ExistingNonExecutableAndMissingWorkingFolderAreRejected()
    {
        var text = Path.Combine(root, "file.txt"); File.WriteAllText(text, "not executable");
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Draft() with { ExecutablePath = text }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Draft() with { WorkingDirectory = Path.Combine(root, "absent") }));
    }

    [Fact]
    public async Task CoverIsCopiedAndRetainedAfterRemovingTheGame()
    {
        var saved = await service.SaveAsync(Draft(), Png);
        var item = Assert.Single((await service.ListAsync()).Games);
        Assert.NotNull(item.CoverFile);
        Assert.Equal(Png, await File.ReadAllBytesAsync(item.CoverFile!));
        await service.SetArchivedAsync(saved.Profile.Id, true);
        Assert.Equal(Png, await File.ReadAllBytesAsync(item.CoverFile!));
    }

    [Fact]
    public async Task InvalidCoverCannotCreateAProfile()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Draft(), [1, 2, 3]));
        Assert.Empty((await service.ListAsync()).Games);
    }

    [Fact]
    public async Task ReadOnlyProfileCannotBeOverwrittenByAnEdit()
    {
        var saved = await service.SaveAsync(Draft());
        var file = Path.Combine(paths.Profiles, saved.Profile.Id.ToString("N"), "profile.json");
        await File.WriteAllTextAsync(file, "{\"schemaVersion\":99}");
        Assert.True(Assert.Single((await service.ListAsync()).Games).IsReadOnly);
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(Draft(saved.Profile.Id)));
        await Assert.ThrowsAsync<IOException>(() => service.SetArchivedAsync(saved.Profile.Id, true));
        Assert.Equal("{\"schemaVersion\":99}", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task PlaytimeUsesTheSameAdjustmentOrderAsStorageValidation()
    {
        var saved = await service.SaveAsync(Draft());
        await store.SaveStatisticsAsync(new(1, saved.Profile.Id,
            [new(Guid.NewGuid(), DateTimeOffset.UtcNow, null, long.MaxValue)],
            [new(Guid.NewGuid(), -long.MaxValue, "Correction"), new(Guid.NewGuid(), -long.MaxValue, "Correction"), new(Guid.NewGuid(), long.MaxValue, "Correction")]));
        Assert.Equal(0, Assert.Single((await service.ListAsync()).Games).PlayedSeconds);
    }

    [Fact]
    public async Task CoverFailureReportsThatProfileChangesWereSaved()
    {
        var saved = await service.SaveAsync(Draft());
        Directory.CreateDirectory(Path.Combine(paths.Profiles, saved.Profile.Id.ToString("N"), "cover.png"));
        var result = await service.SaveAsync(Draft(saved.Profile.Id) with { Name = "Saved despite artwork failure" }, Png);
        Assert.NotNull(result.Warning);
        Assert.Equal("Saved despite artwork failure", Assert.Single((await service.ListAsync()).Games).Profile!.Name);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
