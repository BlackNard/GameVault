using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Backlog, Abandoned and Retired (ticket #94): kept beside the four stored statuses, so older versions still read every game.</summary>
public sealed class PlayStatusTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault statuses", Guid.NewGuid().ToString("N"));
    private readonly string exe, folder;
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService library;

    public PlayStatusTests()
    {
        folder = Path.Combine(root, "game");
        Directory.CreateDirectory(folder);
        exe = Path.Combine(folder, "game.exe");
        File.WriteAllText(exe, "fixture only; never executed");
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        store = new(paths);
        library = new(paths, store);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task<Guid> AddAsync(PlayStatus status = PlayStatus.NotStarted) =>
        (await library.SaveAsync(new(null, "Game", exe, folder, [], PlayStatuses.Stored(status), Detailed: status))).Profile.Id;

    private async Task<PlayStatus> ShownAsync()
    {
        var game = Assert.Single((await library.ListAsync()).Games);
        return PlayStatuses.From(game.Profile!.Status, game.Detail);
    }

    private async Task EditAsync(Guid id, PlayStatus status) =>
        await library.SaveAsync(new(id, "Game", exe, folder, [], PlayStatuses.Stored(status), Detailed: status));

    private string DetailFile(Guid id) => Path.Combine(ProfileFolders.Resolve(paths, id), "status.json");

    [Fact]
    public void EveryStatusIsStoredAsOneOfTheOldFourAndComesBack()
    {
        Assert.Equal(7, PlayStatuses.All.Count);
        Assert.Equal((GameStatus.NotStarted, GameStatus.OnHold, GameStatus.OnHold),
            (PlayStatuses.Stored(PlayStatus.Backlog), PlayStatuses.Stored(PlayStatus.Abandoned), PlayStatuses.Stored(PlayStatus.Retired)));
        Assert.All(PlayStatuses.All, status => Assert.Equal(status, PlayStatuses.From(PlayStatuses.Stored(status), PlayStatuses.Detail(status))));
        // A detail that no longer fits what an older version saved is ignored.
        Assert.Equal(PlayStatus.Playing, PlayStatuses.From(GameStatus.Playing, GameStatusDetail.Abandoned));
        Assert.Equal(["Not started", "Backlog", "Playing", "On hold", "Completed", "Abandoned", "Retired"], PlayStatuses.All.Select(PlayStatuses.Label));
    }

    [Fact]
    public async Task ANewStatusIsKeptBesideTheProfileWhichOlderVersionsStillRead()
    {
        var id = await AddAsync(PlayStatus.Abandoned);
        Assert.Equal(PlayStatus.Abandoned, await ShownAsync());
        var profile = (await store.LoadProfileAsync(id)).Profile;
        Assert.Equal((DocumentState.Ready, GameStatus.OnHold), (profile.State, profile.Value!.Status));
        Assert.DoesNotContain("Abandoned", File.ReadAllText(Path.Combine(ProfileFolders.Resolve(paths, id), "profile.json")));
        await EditAsync(id, PlayStatus.Retired);
        Assert.Equal(PlayStatus.Retired, await ShownAsync());
        await EditAsync(id, PlayStatus.OnHold);
        Assert.Equal(PlayStatus.OnHold, await ShownAsync());
        Assert.False(File.Exists(DetailFile(id)));
    }

    [Fact]
    public async Task ADetailDoesNotComeBackAfterTheStatusMovedOn()
    {
        var id = await AddAsync(PlayStatus.Abandoned);
        await library.SaveAsync(new(id, "Game", exe, folder, [], GameStatus.Playing));        // a caller that only knows the four
        await library.SaveAsync(new(id, "Game", exe, folder, [], GameStatus.OnHold));
        Assert.Equal(PlayStatus.OnHold, await ShownAsync());
        await store.SetPlayStatusAsync(id, PlayStatus.Retired);
        await library.SaveAsync(new(id, "Renamed", exe, folder, [], GameStatus.OnHold));      // unchanged base keeps the detail
        Assert.Equal(PlayStatus.Retired, await ShownAsync());
    }

    [Fact]
    public async Task StartingABacklogGameMakesItPlaying()
    {
        var id = await AddAsync(PlayStatus.Backlog);
        Assert.Equal(PlayStatus.Backlog, await ShownAsync());
        await library.MarkPlayingAsync(id);
        Assert.Equal(PlayStatus.Playing, await ShownAsync());
        Assert.False(File.Exists(DetailFile(id)));
    }

    [Fact]
    public async Task TheStatusCanBeSetFromSessionsAndNotesAndCompletionStillWorks()
    {
        var id = await AddAsync();
        await store.SetPlayStatusAsync(id, PlayStatus.Completed);
        Assert.NotNull((await store.LoadCompletionAsync(id)).Value);
        await store.SetPlayStatusAsync(id, PlayStatus.Retired);                             // retiring is not completing
        Assert.Null((await store.LoadCompletionAsync(id)).Value);
        Assert.Equal(PlayStatus.Retired, await ShownAsync());
        Assert.True((await store.LoadNotesAsync(id)).CanReview);
        await store.SetPlayStatusAsync(id, PlayStatus.Abandoned);
        Assert.False((await store.LoadNotesAsync(id)).CanReview);
    }

    [Fact]
    public async Task ANewerStatusFileIsLeftAlone()
    {
        var id = await AddAsync(PlayStatus.Abandoned);
        var newer = "{\"schemaVersion\":9}";
        File.WriteAllText(DetailFile(id), newer);
        Assert.Equal(PlayStatus.OnHold, await ShownAsync());
        await Assert.ThrowsAnyAsync<IOException>(() => store.SetPlayStatusAsync(id, PlayStatus.Retired));
        Assert.Equal(newer, File.ReadAllText(DetailFile(id)));
    }

    [Fact]
    public async Task StatisticsUseTheNewStatuses()
    {
        var id = await AddAsync(PlayStatus.Abandoned);
        await store.SaveStatisticsAsync(new(1, id, [new(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddYears(50), DateTimeOffset.UnixEpoch.AddYears(50).AddHours(1), 3600)], []));
        var steam = new SteamHoursService(store, new SteamLocalData(null), new GameVault.Infrastructure.Launching.StoreGameLauncher(Path.Combine(root, "none")),
            (_, _) => Task.FromResult<SteamAppSummary?>(null));
        var game = Assert.Single((await new StatisticsService(store, steam).LoadAsync(TimeZoneInfo.Utc)).Games);
        Assert.Equal("Abandoned", Assert.Single(PlayStatistics.StatusShares([game], null)).Name);
    }
}
