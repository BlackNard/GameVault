using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Steam hours (ticket #62), all against a synthetic Steam folder: nothing here reads the real Steam installation or the network.</summary>
public sealed class SteamHoursTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault steam hours", Guid.NewGuid().ToString("N"));
    private readonly string steamRoot, installedExe, looseExe;
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService library;
    private SteamAppSummary? storeAnswer;
    private int storeRequests;

    public SteamHoursTests()
    {
        steamRoot = Path.Combine(root, "Steam");
        installedExe = Path.Combine(steamRoot, "steamapps", "common", "Alpha", "alpha.exe");
        looseExe = Path.Combine(root, "loose", "alpha.exe");
        Fixture(installedExe, "fixture only; never executed");
        Fixture(looseExe, "fixture only; never executed");
        Fixture(Path.Combine(steamRoot, "steamapps", "appmanifest_100.acf"),
            "\"AppState\"\n{\n \"appid\" \"100\"\n \"name\" \"Alpha: Deluxe™\"\n \"installdir\" \"Alpha\"\n \"StateFlags\" \"4\"\n}\n");
        Account("111", "Player One", ("100", 90), ("200", 600));
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        store = new(paths);
        library = new(paths, store);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private static void Fixture(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private void Account(string id, string persona, params (string App, int Minutes)[] apps)
    {
        var entries = string.Concat(apps.Select(a => $"\"{a.App}\"\n{{\n\"LastPlayed\" \"1700000000\"\n\"Playtime\" \"{a.Minutes}\"\n\"cloud\"\n{{\n\"Playtime\" \"999999\"\n}}\n}}\n"));
        Fixture(Path.Combine(steamRoot, "userdata", id, "config", "localconfig.vdf"),
            "\"UserLocalConfigStore\"\n{\n\"friends\"\n{\n\"PersonaName\" \"" + persona + "\"\n}\n\"Software\"\n{\n\"Valve\"\n{\n\"Steam\"\n{\n\"apps\"\n{\n" + entries +
            "\"300\"\n{\n\"LastPlayed\" \"1700000000\"\n}\n}\n}\n}\n}\n}\n");
    }

    private SteamHoursService Service() => new(store, new SteamLocalData(steamRoot), new StoreGameLauncher(Path.Combine(root, "no-epic")),
        (_, _) => { storeRequests++; return Task.FromResult(storeAnswer); });

    private async Task<GameProfile> AddAsync(string name, string exe) =>
        (await library.SaveAsync(new(null, name, exe, Path.GetDirectoryName(exe)!, []))).Profile;

    private async Task LookedUpAsync(Guid id, string appId) =>
        await store.SaveMetadataAsync(GameMetadataDocument.Empty(id) with { SteamAppId = appId });

    // ---------- reading Steam ----------
    [Fact]
    public void PlaytimeIsReadPerAccountFromTheAppsListOnly()
    {
        var steam = new SteamLocalData(steamRoot);
        var account = Assert.Single(steam.Accounts());
        Assert.Equal(("111", "Player One"), (account.Id, account.Name));
        Assert.Equal(90, steam.Minutes("111", "100"));
        Assert.Null(steam.Minutes("111", "300"));            // listed, but never given a playtime
        Assert.Null(steam.Minutes("111", "404"));
        Assert.Null(steam.Minutes("999", "100"));
        Assert.Null(steam.Minutes("..", "100"));
    }

    [Fact]
    public void MissingOrDamagedSteamFilesGiveNoAccountsInsteadOfFailing()
    {
        Assert.Empty(new SteamLocalData(null).Accounts());
        Assert.Empty(new SteamLocalData(Path.Combine(root, "nothing")).Accounts());
        File.WriteAllText(Path.Combine(steamRoot, "userdata", "111", "config", "localconfig.vdf"), "\"UserLocalConfigStore\" {");
        Assert.Empty(new SteamLocalData(steamRoot).Accounts());
    }

    [Fact]
    public async Task TheStoreSummaryShowsNameDevelopersAndYear()
    {
        const string json = "{\"200\":{\"success\":true,\"data\":{\"name\":\"Beta Game\",\"developers\":[\"Studio\"],\"release_date\":{\"coming_soon\":false,\"date\":\"5 Mar, 2020\"}}}}";
        using var provider = new SteamStoreProvider(new Answer(json), minimumGap: TimeSpan.Zero);
        var summary = (await provider.SummaryAsync("200", CancellationToken.None))!;
        Assert.Equal(("200", "Beta Game", 2020), (summary.AppId, summary.Name, summary.Year!.Value));
        Assert.Equal(["Studio"], summary.Developers);
        Assert.EndsWith("/apps/200/header.jpg", summary.ImageUrl);
        Assert.Null(await provider.SummaryAsync("../1", CancellationToken.None));
    }

    private sealed class Answer(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    // ---------- guards ----------
    [Fact]
    public async Task AProgramInsideTheSteamInstallIsOfferedDirectlyWithoutAnyWebRequest()
    {
        var game = await AddAsync("My own name", installedExe);
        var offer = (await Service().FindOfferAsync(game.Id)).Offer!;
        Assert.Equal(("100", "Alpha: Deluxe™", true), (offer.AppId, offer.SteamName, offer.Proven));
        Assert.Equal(90, Assert.Single(offer.Accounts).Minutes);
        Assert.Equal(0, storeRequests);
    }

    [Fact]
    public async Task ACopyElsewhereNeedsTheLookedUpSteamGameAndAnExactTitle()
    {
        var game = await AddAsync("Beta Game", looseExe);
        var nothing = await Service().FindOfferAsync(game.Id);
        Assert.Null(nothing.Offer);                          // no looked-up Steam id: nothing can be linked
        Assert.NotEmpty(nothing.Reason!);

        await LookedUpAsync(game.Id, "200");
        storeAnswer = new("200", "Beta Game 2", ["Studio"], 2020, null);
        Assert.Null((await Service().FindOfferAsync(game.Id)).Offer);

        storeAnswer = new("200", "BETA: Game®", ["Studio"], 2020, null);
        var offer = (await Service().FindOfferAsync(game.Id)).Offer!;
        Assert.False(offer.Proven);
        Assert.Equal(2020, offer.Summary!.Year);
        Assert.Equal(600, Assert.Single(offer.Accounts).Minutes);

        storeAnswer = null;                                  // Steam cannot be asked: no link
        Assert.Null((await Service().FindOfferAsync(game.Id)).Offer);
    }

    [Fact]
    public async Task OneSteamGameCanBeLinkedToOneProfileOnly()
    {
        var first = await AddAsync("Beta Game", looseExe);
        var other = Path.Combine(root, "loose2", "beta.exe");
        Fixture(other, "fixture");
        var second = await AddAsync("Beta game", other);
        await LookedUpAsync(first.Id, "200");
        await LookedUpAsync(second.Id, "200");
        storeAnswer = new("200", "Beta Game", [], null, null);
        var service = Service();
        var early = (await service.FindOfferAsync(second.Id)).Offer!;
        await service.LinkAsync(first.Id, (await service.FindOfferAsync(first.Id)).Offer!, "111");
        var refused = await service.FindOfferAsync(second.Id);
        Assert.Null(refused.Offer);
        Assert.Contains("Beta Game", refused.Reason);
        await Assert.ThrowsAsync<IOException>(() => service.LinkAsync(second.Id, early, "111"));
        await library.SetArchivedAsync(first.Id, true);      // a removed game no longer holds the link
        Assert.NotNull((await service.FindOfferAsync(second.Id)).Offer);
    }

    [Fact]
    public async Task OnlyAnOfferForThisGameAndAListedAccountCanBeLinked()
    {
        var game = await AddAsync("Alpha", installedExe);
        var service = Service();
        var offer = (await service.FindOfferAsync(game.Id)).Offer!;
        await Assert.ThrowsAsync<IOException>(() => service.LinkAsync(game.Id, offer, "999"));
        await Assert.ThrowsAsync<IOException>(() => service.LinkAsync(game.Id, offer with { AppId = "200" }, "111"));
        Assert.Null((await store.LoadSteamLinkAsync(game.Id)).Value);
    }

    [Fact]
    public async Task WithSeveralAccountsOnlyThoseThatPlayedTheGameAreOffered()
    {
        Account("222", "Player Two", ("100", 5));
        Account("333", "Player Three", ("200", 5));
        var game = await AddAsync("Alpha", installedExe);
        var offer = (await Service().FindOfferAsync(game.Id)).Offer!;
        Assert.Equal(["111", "222"], offer.Accounts.Select(a => a.Account.Id).Order());
    }

    [Fact]
    public async Task ChangingTheNameOrProgramRechecksTheLink()
    {
        var game = await AddAsync("Beta Game", looseExe);
        await LookedUpAsync(game.Id, "200");
        storeAnswer = new("200", "Beta Game", [], null, null);
        var service = Service();
        await service.LinkAsync(game.Id, (await service.FindOfferAsync(game.Id)).Offer!, "111");
        Assert.True((await service.GetAsync(game.Id)).Active);

        await library.SaveAsync(new(game.Id, "Something else", looseExe, game.WorkingDirectory, []));
        var renamed = await service.GetAsync(game.Id);
        Assert.False(renamed.Active);
        Assert.Null(renamed.Totals);

        await library.SaveAsync(new(game.Id, "Beta game", looseExe, game.WorkingDirectory, []));
        Assert.True((await service.GetAsync(game.Id)).Active);

        await library.SaveAsync(new(game.Id, "Beta game", installedExe, Path.GetDirectoryName(installedExe)!, []));
        Assert.False((await service.GetAsync(game.Id)).Active);   // the program now belongs to another Steam game
    }

    [Fact]
    public async Task AProvenLinkSurvivesRenamingAndUninstalling()
    {
        var game = await AddAsync("Alpha", installedExe);
        var service = Service();
        await service.LinkAsync(game.Id, (await service.FindOfferAsync(game.Id)).Offer!, "111");
        await library.SaveAsync(new(game.Id, "My favourite", installedExe, game.WorkingDirectory, []));
        Assert.True((await service.GetAsync(game.Id)).Active);
        File.Delete(Path.Combine(steamRoot, "steamapps", "appmanifest_100.acf"));
        Assert.True((await service.GetAsync(game.Id)).Active);
    }

    // ---------- counting ----------
    private static GameStatistics Played(Guid game, long adjustment, params (Guid Id, long Seconds)[] sessions) => new(1, game,
        [.. sessions.Select(s => new PlaySession(s.Id, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, s.Seconds))],
        adjustment == 0 ? [] : [new PlaytimeAdjustment(Guid.NewGuid(), adjustment, "added")]);

    private static SteamLink Link(Guid game, bool confirmed, long minutes) =>
        new(1, game, "100", "111", "Alpha", confirmed, @"C:\games\alpha.exe", minutes, DateTimeOffset.UnixEpoch);

    [Fact]
    public void TheTotalAddsSteamHoursToGameVaultHours()
    {
        // The owner's choice (ticket #62): both figures are added, for any kind of link.
        var game = Guid.NewGuid();
        var statistics = Played(game, 600, (Guid.NewGuid(), 3600), (Guid.NewGuid(), 1800));
        Assert.Equal(new PlaytimeTotals(6000, 6000, 12000), SteamHours.Totals(Link(game, false, 100), statistics));
        Assert.Equal(new PlaytimeTotals(6000, 6000, 12000), SteamHours.Totals(Link(game, true, 100), statistics));
        var negative = Played(game, -4000, (Guid.NewGuid(), 3600));
        Assert.Equal(new PlaytimeTotals(0, 6000, 6000), SteamHours.Totals(Link(game, false, 100), negative));
    }

    // ---------- keeping the figure up to date, unlinking, backups ----------
    [Fact]
    public async Task RefreshReadsTheNewFigureAndNeverChangesRecordedSessions()
    {
        var game = await AddAsync("Alpha", installedExe);
        var service = Service();
        await service.LinkAsync(game.Id, (await service.FindOfferAsync(game.Id)).Offer!, "111");
        var statisticsFile = Path.Combine(ProfileFolders.Resolve(paths, game.Id), "statistics.json");
        var before = File.ReadAllText(statisticsFile);
        Account("111", "Player One", ("100", 150));
        Assert.Equal(1, await service.RefreshAsync());
        Assert.Equal(150 * 60, (await service.GetAsync(game.Id)).Totals!.SteamSeconds);
        Directory.Delete(Path.Combine(steamRoot, "userdata"), true);   // Steam gone: the last figure is kept
        Assert.Equal(0, await service.RefreshAsync());
        Assert.Equal(150 * 60, (await service.GetAsync(game.Id)).Totals!.SteamSeconds);
        Assert.Equal(before, File.ReadAllText(statisticsFile));
    }

    [Fact]
    public async Task UnlinkRemovesTheSteamHoursCompletely()
    {
        var game = await AddAsync("Alpha", installedExe);
        var service = Service();
        await service.LinkAsync(game.Id, (await service.FindOfferAsync(game.Id)).Offer!, "111");
        Account("111", "Player One", ("100", 150));
        await service.RefreshAsync();                          // creates the previous copy too
        await service.UnlinkAsync(game.Id);
        Assert.Equal(DocumentState.Missing, (await store.LoadSteamLinkAsync(game.Id)).State);
        Assert.Empty(Directory.GetFiles(ProfileFolders.Resolve(paths, game.Id), "steam-link*"));
        Assert.Null((await service.GetAsync(game.Id)).Link);
    }

    [Fact]
    public async Task DataBackupsIncludeTheLinkAndANewerFileIsLeftAlone()
    {
        var game = await AddAsync("Alpha", installedExe);
        var service = Service();
        await service.LinkAsync(game.Id, (await service.FindOfferAsync(game.Id)).Offer!, "111");
        Assert.Contains(DataArchive.Enumerate(paths, false), f => f.EndsWith("/steam-link.json", StringComparison.Ordinal));
        var file = Path.Combine(ProfileFolders.Resolve(paths, game.Id), "steam-link.json");
        var newer = "{\"schemaVersion\":9}";
        File.WriteAllText(file, newer);
        Assert.False((await service.GetAsync(game.Id)).Active);
        await Assert.ThrowsAnyAsync<IOException>(() => service.UnlinkAsync(game.Id));
        Assert.Equal(newer, File.ReadAllText(file));
    }
}
