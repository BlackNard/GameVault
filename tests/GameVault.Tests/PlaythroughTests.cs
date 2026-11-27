using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Playthroughs (ticket #95): each run of a game kept apart, ready for a later import.</summary>
public sealed class PlaythroughTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault runs", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService library;
    private readonly string exe;

    public PlaythroughTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        exe = Path.Combine(root, "game", "game.exe");
        File.WriteAllText(exe, "fixture only; never executed");
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        store = new(paths);
        library = new(paths, store);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task<Guid> AddAsync() => (await library.SaveAsync(new(null, "Runs", exe, Path.GetDirectoryName(exe)!, []))).Profile.Id;
    private static DateOnly D(int m, int d) => new(2026, m, d);
    private static Playthrough Run(string title = "Leon", DateOnly? started = null, DateOnly? finished = null, PlaythroughOutcome outcome = PlaythroughOutcome.Playing,
        int? minutes = null, string? source = null, string? external = null) =>
        new(Guid.NewGuid(), title, "PC", started, finished, outcome, minutes, 0, null, false, source, external);

    [Fact]
    public async Task RunsAreAddedChangedAndRemoved()
    {
        var id = await AddAsync();
        var run = Run();
        await store.SavePlaythroughAsync(id, run);
        await store.SavePlaythroughAsync(id, Run("Claire"));
        await store.SavePlaythroughAsync(id, run with { Title = "Leon, second run", Mastered = true });
        var runs = (await store.LoadPlaythroughsAsync(id)).Value!.Runs;
        Assert.Equal(["Leon, second run", "Claire"], runs.Select(r => r.Title));
        await store.DeletePlaythroughAsync(id, run.Id);
        Assert.Equal("Claire", Assert.Single((await store.LoadPlaythroughsAsync(id)).Value!.Runs).Title);
    }

    [Fact]
    public async Task AnImportedRunReplacesItselfInsteadOfAppearingTwice()
    {
        var id = await AddAsync();
        await store.SavePlaythroughAsync(id, Run("First", source: "backloggd", external: "77"));
        await store.SavePlaythroughAsync(id, Run("Updated", source: "backloggd", external: "77"));
        await store.SavePlaythroughAsync(id, Run("Typed by hand"));
        Assert.Equal(["Updated", "Typed by hand"], (await store.LoadPlaythroughsAsync(id)).Value!.Runs.Select(r => r.Title));
    }

    [Fact]
    public async Task ImpossibleRunsAreRefusedAndNewerFilesLeftAlone()
    {
        var id = await AddAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SavePlaythroughAsync(id, Run("")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SavePlaythroughAsync(id, Run(started: D(5, 2), finished: D(5, 1))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SavePlaythroughAsync(id, Run(minutes: -1)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SavePlaythroughAsync(id, Run() with { HalfStars = 11 }));
        var file = Path.Combine(ProfileFolders.Resolve(paths, id), "playthroughs.json");
        File.WriteAllText(file, "{\"schemaVersion\":9}");
        await Assert.ThrowsAnyAsync<IOException>(() => store.SavePlaythroughAsync(id, Run()));
        Assert.Equal("{\"schemaVersion\":9}", File.ReadAllText(file));
    }

    [Fact]
    public void HoursAreTypedOrCountedFromTheSessionsInTheRunsDates()
    {
        PlaySpan[] sessions = [new(new(2026, 4, 30, 20, 0, 0), 100), new(new(2026, 5, 1, 20, 0, 0), 200), new(new(2026, 5, 9, 20, 0, 0), 400), new(new(2026, 6, 1, 20, 0, 0), 800)];
        Assert.Equal(600, PlaythroughRules.Seconds(Run(started: D(5, 1), finished: D(5, 9)), sessions, D(7, 1)));
        Assert.Equal(1400, PlaythroughRules.Seconds(Run(started: D(5, 1)), sessions, D(7, 1)));          // still running: up to today
        Assert.Equal(5400, PlaythroughRules.Seconds(Run(started: D(5, 1), minutes: 90), sessions, D(7, 1)));
        Assert.Null(PlaythroughRules.Seconds(Run(), sessions, D(7, 1)));                                  // no dates: nothing is guessed
        Assert.Equal("38 of ~62 h", PlaythroughRules.AgainstAverage(38 * 3600, 62 * 3600));
        Assert.Null(PlaythroughRules.AgainstAverage(null, 100));
        Assert.Null(PlaythroughRules.AgainstAverage(100, null));
    }

    [Fact]
    public void FinishedRunsCountAsCompletionsInStatisticsOncePerGame()
    {
        var game = new GamePlay(Guid.NewGuid(), "A", false, [], [], 0, CompletedOn: D(5, 3), FinishedRuns: [D(5, 3), D(8, 1)]);
        Assert.Equal(["A"], PlayStatistics.Insight([game], (D(5, 1), D(5, 31))).Completed);
        Assert.Equal(["A"], PlayStatistics.Insight([game], (D(8, 1), D(8, 31))).Completed);
        Assert.Empty(PlayStatistics.Insight([game], (D(6, 1), D(6, 30))).Completed);
        Assert.Equal(["A"], PlayStatistics.Insight([game], null).Completed);
    }
}
