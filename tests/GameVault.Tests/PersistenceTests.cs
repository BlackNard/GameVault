using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault persistence", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;

    public PersistenceTests()
    {
        Directory.CreateDirectory(root);
        paths = ApplicationPaths.FromRoot(root);
        store = new AppDataStore(paths);
    }

    private static GameProfile Profile(Guid? id = null) => new(1, id ?? Guid.NewGuid(), "Resident Evil 2",
        @"D:\Games\RE2\re2.exe", @"D:\Games\RE2", [@"C:\Users\Player\Saved Games\RE2"], GameStatus.Playing, 20, false, false);
    private string Folder(Guid id) => Path.Combine(ProfileFolders.Resolve(paths, id));
    private string SettingsFile => Path.Combine(paths.Data, "settings.json");

    [Fact] public async Task LegacySettingsGetTrayDefaultsAndPreferencesPersistWithTheme()
    {
        Directory.CreateDirectory(paths.Data);File.WriteAllText(SettingsFile,"{\"schemaVersion\":1,\"theme\":\"Crimson\",\"defaultBackupLimit\":20,\"keepAllBackups\":false,\"reduceMotion\":false}");
        var legacy=await store.InitializeAsync();Assert.True(legacy.Settings.Value!.CloseToTray);Assert.True(legacy.Settings.Value.NotifyBackupFailures);
        await store.SaveSettingsAsync(legacy.Settings.Value! with{CloseToTray=false,NotifyBackupFailures=false});var reopened=await new AppDataStore(paths).InitializeAsync();Assert.False(reopened.Settings.Value!.CloseToTray);Assert.False(reopened.Settings.Value.NotifyBackupFailures);Assert.Equal("Crimson",reopened.Settings.Value!.Theme);
    }
    [Fact]
    public async Task FirstRunCreatesLocalDirectoriesAndMidnightDefaults()
    {
        var snapshot = await store.InitializeAsync();
        Assert.Equal(DocumentState.Ready, snapshot.Settings.State);
        Assert.Equal(AppSettings.Default, snapshot.Settings.Value);
        Assert.True(File.Exists(SettingsFile));
        Assert.True(Directory.Exists(paths.Profiles));
        Assert.Empty(snapshot.Games);
    }

    [Fact]
    public async Task ThemeAndBackupPreferencesSurviveANewStoreInstance()
    {
        await store.InitializeAsync();
        var settings = new AppSettings(1, "Crimson", 37, true, true);
        await store.SaveSettingsAsync(settings);
        Assert.Equal(settings, (await new AppDataStore(paths).InitializeAsync()).Settings.Value);
    }

    [Fact]
    public async Task ProfileRoundTripRetainsPathsStatusAndStableIdentity()
    {
        await store.InitializeAsync();
        var profile = Profile() with { Status = GameStatus.OnHold, BackupLimit = 31, KeepAllBackups = true, IsArchived = true };
        await store.SaveProfileAsync(profile);
        var loaded = await new AppDataStore(paths).LoadProfileAsync(profile.Id);
        Assert.Equal(profile.Id, loaded.Profile.Value!.Id);
        Assert.Equal(profile.Name, loaded.Profile.Value.Name);
        Assert.Equal(profile.ExecutablePath, loaded.Profile.Value.ExecutablePath);
        Assert.Equal(profile.WorkingDirectory, loaded.Profile.Value.WorkingDirectory);
        Assert.Equal(profile.SaveSources, loaded.Profile.Value.SaveSources);
        Assert.Equal(profile.Status, loaded.Profile.Value.Status);
        Assert.True(loaded.Profile.Value.IsArchived);
        Assert.Equal(31, loaded.Profile.Value.BackupLimit);
        Assert.True(loaded.Profile.Value.KeepAllBackups);
        Assert.True(Directory.Exists(Path.Combine(Folder(profile.Id), "Backups")));
        Assert.Equal(DocumentState.Ready, loaded.Statistics.State);
    }

    [Fact]
    public async Task RenamePreservesStatisticsAndExistingBackupFiles()
    {
        await store.InitializeAsync();
        var profile = Profile();
        await store.SaveProfileAsync(profile);
        var stats = new GameStatistics(1, profile.Id,
            [new PlaySession(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 3600)],
            [new PlaytimeAdjustment(Guid.NewGuid(), -120, "Correction")]);
        await store.SaveStatisticsAsync(stats);
        var oldFolder = Folder(profile.Id);
        await File.WriteAllTextAsync(Path.Combine(oldFolder, "Backups", "fixture.sav"), "retained");
        var statBytes = await File.ReadAllBytesAsync(Path.Combine(oldFolder, "statistics.json"));
        await store.SaveProfileAsync(profile with { Name = "Renamed game" });
        var loaded = await store.LoadProfileAsync(profile.Id);
        Assert.Equal("Renamed game", loaded.Profile.Value!.Name);
        // The folder follows the game's name; everything inside moves with it.
        var renamed = Folder(profile.Id);
        Assert.Equal("Renamed game", Path.GetFileName(renamed));
        Assert.False(Directory.Exists(oldFolder));
        Assert.Equal(statBytes, await File.ReadAllBytesAsync(Path.Combine(renamed, "statistics.json")));
        Assert.Equal(3600, loaded.Statistics.Value!.Sessions[0].DurationSeconds);
        Assert.Equal(-120, loaded.Statistics.Value.Adjustments[0].Seconds);
        Assert.Equal("retained", await File.ReadAllTextAsync(Path.Combine(renamed, "Backups", "fixture.sav")));
    }

    [Fact]
    public async Task InventoryComesFromIndependentProfileFoldersWithoutAnIndex()
    {
        await store.InitializeAsync();
        var first = Profile(); var second = Profile() with { Name = "Another game" };
        await store.SaveProfileAsync(first); await store.SaveProfileAsync(second);
        var snapshot = await new AppDataStore(paths).InitializeAsync();
        Assert.Equal(2, snapshot.Games.Length);
        Assert.Contains(snapshot.Games, g => g.Id == first.Id);
        Assert.Contains(snapshot.Games, g => g.Id == second.Id);
        Assert.False(File.Exists(Path.Combine(paths.Data, "library.json")));
    }

    [Fact]
    public async Task AtomicUpdateRetainsThePreviousValidDocument()
    {
        await store.InitializeAsync();
        await store.SaveSettingsAsync(AppSettings.Default with { Theme = "Crimson" });
        using var backup = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsFile + ".bak"));
        Assert.Equal("Midnight", backup.RootElement.GetProperty("theme").GetString());
        Assert.Empty(Directory.GetFiles(paths.Data, "*.tmp"));
    }

    [Theory]
    [InlineData("{truncated", DocumentState.Corrupt)]
    [InlineData("{}", DocumentState.Corrupt)]
    [InlineData("{\"schemaVersion\":\"1\"}", DocumentState.Corrupt)]
    [InlineData("{\"schemaVersion\":null}", DocumentState.Corrupt)]
    [InlineData("{\"schemaVersion\":2}", DocumentState.Unsupported)]
    [InlineData("{\"schemaVersion\":1,\"theme\":\"Midnight\"}", DocumentState.Corrupt)]
    public async Task ExistingInvalidSettingsAreNeverResetOrOverwritten(string original, DocumentState expected)
    {
        Directory.CreateDirectory(paths.Data);
        await File.WriteAllTextAsync(SettingsFile, original);
        var snapshot = await store.InitializeAsync();
        Assert.Equal(expected, snapshot.Settings.State);
        Assert.True(snapshot.Settings.IsReadOnly);
        await Assert.ThrowsAsync<IOException>(() => store.SaveSettingsAsync(AppSettings.Default));
        Assert.Equal(original, await File.ReadAllTextAsync(SettingsFile));
    }

    [Fact]
    public async Task ValidBackupIsExposedReadOnlyWithoutReplacingCorruptPrimary()
    {
        await store.InitializeAsync();
        await store.SaveSettingsAsync(AppSettings.Default with { Theme = "Crimson" });
        await File.WriteAllTextAsync(SettingsFile, "broken");
        var snapshot = await store.InitializeAsync();
        Assert.Equal(DocumentState.Recovered, snapshot.Settings.State);
        Assert.Equal("Midnight", snapshot.Settings.Value!.Theme);
        Assert.True(snapshot.Settings.IsReadOnly);
        await Assert.ThrowsAsync<IOException>(() => store.SaveSettingsAsync(AppSettings.Default));
        Assert.Equal("broken", await File.ReadAllTextAsync(SettingsFile));
    }

    [Fact]
    public async Task FutureSchemaDoesNotFallBackToAnOlderBackup()
    {
        await store.InitializeAsync();
        await store.SaveSettingsAsync(AppSettings.Default with { Theme = "Crimson" });
        await File.WriteAllTextAsync(SettingsFile, "{\"schemaVersion\":99}");
        Assert.Equal(DocumentState.Unsupported, (await store.InitializeAsync()).Settings.State);
    }

    [Fact]
    public async Task ConcurrentRepositoryInstancesProduceWholeValidDocuments()
    {
        await store.InitializeAsync();
        await Task.WhenAll(Enumerable.Range(1, 20).Select(i => new AppDataStore(paths)
            .SaveSettingsAsync(AppSettings.Default with { DefaultBackupLimit = i })));
        var snapshot = await store.InitializeAsync();
        Assert.Equal(DocumentState.Ready, snapshot.Settings.State);
        Assert.InRange(snapshot.Settings.Value!.DefaultBackupLimit, 1, 20);
        Assert.Empty(Directory.GetFiles(paths.Data, "*.tmp"));
    }

    [Fact]
    public async Task InvalidProfileCannotCreateFoldersOrEscapeTheRoot()
    {
        await store.InitializeAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveProfileAsync(Profile(Guid.Empty)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveProfileAsync(Profile() with { ExecutablePath = "../escape.exe" }));
        Assert.Empty(Directory.GetDirectories(paths.Profiles));
    }

    [Fact]
    public async Task MismatchedProfileIdentityIsReadOnly()
    {
        await store.InitializeAsync();
        var profile = Profile(); await store.SaveProfileAsync(profile);
        var file = Path.Combine(Folder(profile.Id), "profile.json");
        var text = await File.ReadAllTextAsync(file);
        await File.WriteAllTextAsync(file, text.Replace(profile.Id.ToString(), Guid.NewGuid().ToString()));
        Assert.True((await store.LoadProfileAsync(profile.Id)).Profile.IsReadOnly);
        await Assert.ThrowsAsync<IOException>(() => store.SaveProfileAsync(profile));
    }

    [Fact]
    public async Task ALockedSettingsFileDoesNotGetReplaced()
    {
        await store.InitializeAsync();
        using (var locked = new FileStream(SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => store.SaveSettingsAsync(AppSettings.Default with { Theme = "Crimson" }));
        }
        Assert.Equal("Midnight", (await store.InitializeAsync()).Settings.Value!.Theme);
        Assert.Empty(Directory.GetFiles(paths.Data, "*.tmp"));
    }

    [Fact]
    public async Task InterruptedStagingKeepsTheLastCommittedSettingsAndRemovesTemporaryFiles()
    {
        await store.InitializeAsync();
        var original = await File.ReadAllBytesAsync(SettingsFile);
        var interrupted = new AppDataStore(paths, _ => throw new IOException("Simulated interruption after flush"));
        await Assert.ThrowsAsync<IOException>(() => interrupted.SaveSettingsAsync(AppSettings.Default with { Theme = "Crimson" }));
        Assert.Equal(original, await File.ReadAllBytesAsync(SettingsFile));
        Assert.Empty(Directory.GetFiles(paths.Data, "*.tmp"));
    }

    [Fact]
    public async Task InterruptedProfileCreationIsNotListedAndRetryPreservesItsStatistics()
    {
        await store.InitializeAsync();
        var profile = Profile();
        var interrupted = new AppDataStore(paths, file =>
        {
            if (Path.GetFileName(file) == "profile.json") throw new IOException("Simulated profile commit failure");
        });
        await Assert.ThrowsAsync<IOException>(() => interrupted.SaveProfileAsync(profile));
        Assert.Empty((await store.InitializeAsync()).Games);
        var statisticsFile = Path.Combine(Folder(profile.Id), "statistics.json");
        var statistics = new GameStatistics(1, profile.Id,
            [new PlaySession(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, 300)], []);
        await File.WriteAllTextAsync(statisticsFile, JsonSerializer.Serialize(statistics, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var original = await File.ReadAllBytesAsync(statisticsFile);
        await store.SaveProfileAsync(profile);
        Assert.Equal(original, await File.ReadAllBytesAsync(statisticsFile));
        Assert.Equal(300, (await store.LoadProfileAsync(profile.Id)).Statistics.Value!.Sessions[0].DurationSeconds);
    }

    [Fact]
    public async Task MissingPrimaryWithAnExistingBackupDoesNotResetToDefaults()
    {
        await store.InitializeAsync();
        await store.SaveSettingsAsync(AppSettings.Default with { Theme = "Crimson" });
        await store.SaveSettingsAsync(AppSettings.Default);
        File.Delete(SettingsFile);
        var loaded = await store.InitializeAsync();
        Assert.Equal(DocumentState.Recovered, loaded.Settings.State);
        Assert.Equal("Crimson", loaded.Settings.Value!.Theme);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public async Task LinkedManagedDirectoryCannotWriteIntoItsDestination()
    {
        var destination = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(destination);
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/d", "/c", "mklink", "/J", paths.Data, destination }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
        }
        else Directory.CreateSymbolicLink(paths.Data, destination);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => store.InitializeAsync());
            Assert.Empty(Directory.GetFileSystemEntries(destination));
        }
        finally { Directory.Delete(paths.Data); }
    }

    [Fact]
    public async Task InvalidStatisticsDoNotReplaceValidHistory()
    {
        await store.InitializeAsync();
        var profile = Profile(); await store.SaveProfileAsync(profile);
        var file = Path.Combine(Folder(profile.Id), "statistics.json");
        var original = await File.ReadAllBytesAsync(file);
        var bad = GameStatistics.Empty(profile.Id) with { Adjustments = [new(Guid.NewGuid(), -1, "Invalid negative total")] };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveStatisticsAsync(bad));
        Assert.Equal(original, await File.ReadAllBytesAsync(file));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
