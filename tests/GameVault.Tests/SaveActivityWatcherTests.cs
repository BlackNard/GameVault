using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Which folders get save files written while a game runs (issue #111). Only synthetic temporary folders are watched.</summary>
public sealed class SaveActivityWatcherTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault watcher", Guid.NewGuid().ToString("N"));
    private readonly Signals signals = new();
    private readonly Scanner scanner = new();
    private readonly Guid first = Guid.NewGuid(), second = Guid.NewGuid();
    private bool enabled = true;
    private AppDataStore store = null!;
    private SaveActivityWatcher watcher = null!;
    private string Watched => Path.Combine(root, "r");
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        var paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(Watched);
        store = new(paths);
        await store.InitializeAsync();
        foreach (var (id, name) in new[] { (first, "Game"), (second, "Other") })
            await store.SaveProfileAsync(new(1, id, name, Path.Combine(root, name, name + ".exe"), Path.Combine(root, name), [], GameStatus.Playing, 20, false, false));
        watcher = new(signals, store, scanner, _ => [Watched], () => enabled, TimeSpan.FromMilliseconds(500));
    }

    public async Task DisposeAsync()
    {
        await watcher.DisposeAsync();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private async Task StartAsync(Guid id) { signals.State(id, LaunchPhase.Running); await watcher.FlushAsync(id).WaitAsync(Limit); }
    private async Task EndAsync(Guid id) { signals.End(id, true); await watcher.FlushAsync(id).WaitAsync(Limit); }
    private async Task<SaveFind[]> FindsAsync(Guid id) => (await store.LoadSaveFindsAsync(id)).Value?.Finds ?? [];

    private string Write(params string[] parts)
    {
        var file = Path.Combine([Watched, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "synthetic");
        return Path.GetDirectoryName(file)!;
    }

    [Fact]
    public async Task RecordsWritesAndStoresFind()
    {
        await StartAsync(first);
        var saves = Write("Studio", "Game", "saves", "1.sav");
        await EndAsync(first);
        var find = Assert.Single(await FindsAsync(first));
        Assert.Equal(saves, find.Path);
        Assert.Equal(1, find.Sessions);
        Assert.False(find.Shared);
        Assert.Equal([first], scanner.Calls);
    }

    [Fact]
    public async Task TheGamesOwnFolderIsWatchedToo()
    {
        var saves = Path.Combine(root, "Game", "saves");
        Directory.CreateDirectory(saves);
        await StartAsync(first);
        File.WriteAllText(Path.Combine(saves, "1.sav"), "synthetic");
        await EndAsync(first);
        Assert.Equal(saves, Assert.Single(await FindsAsync(first)).Path);
    }

    [Fact]
    public void TheWatchedFoldersAreTheUsersAndSteamAccountData()
    {
        var env = new SaveDiscoveryEnvironment(Path.Combine(root, "user"), Path.Combine(root, "docs"), Path.Combine(root, "saved"), Path.Combine(root, "roaming"),
            Path.Combine(root, "local"), Path.Combine(root, "public"), [Path.Combine(root, "Steam")]);
        Assert.Equal([env.Roaming, env.Local, env.LocalLow, env.Documents, Path.Combine(env.Documents, "My Games"), env.SavedGames, env.PublicDocuments, Path.Combine(root, "Steam", "userdata")],
            env.WatchedRoots);
        Assert.DoesNotContain(env.Home, env.WatchedRoots);
    }

    [Fact]
    public async Task NoWatchersWhenNoGameRuns()
    {
        Assert.Equal(0, watcher.ActiveWatcherCount);
        await StartAsync(first);
        Assert.Equal(1, watcher.ActiveWatcherCount);                       // the game's folder does not exist here
        await EndAsync(first);
        Assert.Equal(0, watcher.ActiveWatcherCount);
    }

    [Fact]
    public async Task DisabledDoesNothing()
    {
        enabled = false;
        await StartAsync(first);
        Assert.Equal(0, watcher.ActiveWatcherCount);
        Write("Studio", "Game", "saves", "1.sav");
        await EndAsync(first);
        Assert.Equal(DocumentState.Missing, (await store.LoadSaveFindsAsync(first)).State);
        Assert.Empty(scanner.Calls);
    }

    [Fact]
    public async Task NoiseIgnored()
    {
        await StartAsync(first);
        Write("Temp", "x.sav");
        Write("Studio", "Game", "run.log");
        await EndAsync(first);
        Assert.Empty(await FindsAsync(first));
    }

    [Fact]
    public async Task TwoGamesMarkShared()
    {
        await StartAsync(first);
        await StartAsync(second);
        Write("Studio", "Game", "saves", "1.sav");
        await EndAsync(first);
        await EndAsync(second);
        Assert.True(Assert.Single(await FindsAsync(first)).Shared);
        Assert.True(Assert.Single(await FindsAsync(second)).Shared);
    }

    [Fact]
    public async Task AGameStartedWhileTheLastOneIsStillSettlingCountsAsShared()
    {
        await StartAsync(first);
        signals.End(first, true);
        var ending = watcher.FlushAsync(first);
        await StartAsync(second);
        Write("Studio", "Game", "saves", "1.sav");
        await ending.WaitAsync(Limit);
        await EndAsync(second);
        Assert.True(Assert.Single(await FindsAsync(first)).Shared);
        Assert.True(Assert.Single(await FindsAsync(second)).Shared);
    }

    [Fact]
    public async Task SecondSessionCountsUp()
    {
        for (var session = 0; session < 2; session++)
        {
            await StartAsync(first);
            Write("Studio", "Game", "saves", "1.sav");
            await EndAsync(first);
        }
        Assert.Equal(2, Assert.Single(await FindsAsync(first)).Sessions);
    }

    [Fact]
    public async Task ARejectedFolderIsNotRecorded()
    {
        var saves = Write("Studio", "Game", "saves", "0.sav");
        await store.UpdateSaveFindsAsync(first, d => d with { Rejected = [saves] });
        await StartAsync(first);
        Write("Studio", "Game", "saves", "1.sav");
        await EndAsync(first);
        Assert.Empty(await FindsAsync(first));
    }

    [Fact]
    public async Task BurstStaysBounded()
    {
        await StartAsync(first);
        for (var i = 0; i < 10_000; i++) Write("Studio", "Game", "saves", i + ".sav");
        await EndAsync(first);
        var find = Assert.Single(await FindsAsync(first));
        Assert.InRange(find.Files, 1, SaveActivityWatcher.MaxRecorded);
    }

    [Fact]
    public async Task QuittingKeepsWhatWasSeen()
    {
        await StartAsync(first);
        Write("Studio", "Game", "saves", "1.sav");
        await Task.Delay(500);
        await watcher.DisposeAsync();
        Assert.Single(await FindsAsync(first));
        Assert.Equal(0, watcher.ActiveWatcherCount);
    }

    private sealed class Signals : IGameSessionSignals
    {
        public event EventHandler<LaunchState>? StateChanged;
        public event EventHandler<GameSessionEnded>? SessionEnded;
        public void State(Guid id, LaunchPhase phase) => StateChanged?.Invoke(this, new(id, phase, "Fixture"));
        public void End(Guid id, bool confirmed) => SessionEnded?.Invoke(this, new(id, confirmed));
    }

    private sealed class Scanner : ISaveDiscoveryScanner
    {
        public readonly List<Guid> Calls = [];
        public Task<SaveDiscoveryResult> ScanAsync(Guid id, CancellationToken token = default)
        {
            lock (Calls) Calls.Add(id);
            return Task.FromResult(new SaveDiscoveryResult(new(null, DocumentState.Missing), SaveDiscoveryPhase.Unresolved, "Fixture"));
        }
    }
}
