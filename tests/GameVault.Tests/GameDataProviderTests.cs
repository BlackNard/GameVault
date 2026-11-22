using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class GameDataProviderTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-gamedata-" + Guid.NewGuid().ToString("N"));
    private ApplicationPaths paths = null!;
    private AppDataStore store = null!;
    private GameProfile profile = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        paths = ApplicationPaths.FromRoot(root);
        store = new(paths);
        await store.InitializeAsync();
        var exe = Path.Combine(root, "game", "game.exe");
        File.WriteAllBytes(exe, new byte[3000]);
        profile = (await new LibraryService(paths, store).SaveAsync(new(null, "Dead Space", exe, Path.Combine(root, "game"), []))).Profile;
    }

    public Task DisposeAsync() { Directory.Delete(root, true); return Task.CompletedTask; }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string text, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private MetadataRequest Request(string name = "Tekken 8", GameIdentity? identity = null, string? steam = null) => new(profile.Id, name, profile.ExecutablePath, null, steam, null, null, true, false, identity);

    // ---------- Wikidata ----------
    private const string Entity = """
        {"entities":{"Q105486599":{"descriptions":{"en":{"language":"en","value":"2024 fighting game developed by Bandai Namco Studios"}},
        "claims":{"P577":[{"mainsnak":{"datavalue":{"value":{"time":"+2024-01-26T00:00:00Z","precision":11}}}}],
        "P178":[{"mainsnak":{"datavalue":{"value":{"id":"Q11328408"}}}},{"mainsnak":{"datavalue":{"value":{"id":"Q1194689"}}}}],
        "P123":[{"mainsnak":{"datavalue":{"value":{"id":"Q1194689"}}}}],"P136":[{"mainsnak":{"datavalue":{"value":{"id":"Q130270628"}}}}],
        "P1733":[{"mainsnak":{"datavalue":{"value":"1778820"}}}],"P2816":[{"mainsnak":{"datavalue":{"value":"113569"}}}]}}}}
        """;
    private const string Labels = """
        {"entities":{"Q11328408":{"labels":{"en":{"value":"Bandai Namco Studios"}}},"Q1194689":{"labels":{"en":{"value":"Bandai Namco Entertainment"}}},"Q130270628":{"labels":{"en":{"value":"3D fighting game"}}}}}
        """;

    private static HttpResponseMessage WikidataAnswer(HttpRequestMessage r, string searchHits = "[]", string searchTitle = "[]")
    {
        var url = r.RequestUri!.ToString();
        if (url.Contains("list=search")) return Json("{\"query\":{\"search\":" + searchHits + "}}");
        if (url.Contains("wbsearchentities")) return Json("{\"search\":" + searchTitle + "}");
        if (url.Contains("props=labels")) return Json(Labels);
        return Json(Entity);
    }

    [Fact]
    public async Task Wikidata_finds_a_game_by_its_steam_id_and_returns_facts_and_cross_ids()
    {
        var handler = new Handler(r => WikidataAnswer(r, "[{\"title\":\"Q105486599\"}]"));
        using var provider = new WikidataProvider(handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(Request(steam: "1778820"), CancellationToken.None))!;
        Assert.Equal("2024-01-26", data.ReleaseDate!.ToString());
        Assert.Equal(new[] { "Bandai Namco Studios", "Bandai Namco Entertainment" }, data.Developers);
        Assert.Equal(new[] { "Bandai Namco Entertainment" }, data.Publishers);
        Assert.Equal(new[] { "3D fighting game" }, data.Genres);
        Assert.Equal("113569", data.Identity!.HltbId);
        Assert.Equal("Q105486599", data.Identity.WikidataId);
        Assert.Equal("1778820", data.SteamAppId);
        Assert.All(handler.Urls, u => Assert.StartsWith("https://www.wikidata.org/w/api.php?", u));
        Assert.Contains(handler.Urls, u => u.Contains("haswbstatement:P1733=1778820") || u.Contains("haswbstatement%3AP1733%3D1778820"));
    }

    [Fact]
    public async Task Wikidata_uses_the_exact_title_only_when_exactly_one_video_game_has_it()
    {
        const string unique = "[{\"id\":\"Q105486599\",\"label\":\"Tekken 8\",\"description\":\"2024 fighting game developed by Bandai Namco Studios\"},{\"id\":\"Q1\",\"label\":\"Tekken 8 Soundtrack\",\"description\":\"album\"}]";
        using var ok = new WikidataProvider(new Handler(r => WikidataAnswer(r, searchTitle: unique)), minimumGap: TimeSpan.Zero);
        Assert.Equal("Q105486599", (await ok.GetAsync(Request("Tekken 8"), CancellationToken.None))!.Identity!.WikidataId);

        const string deadSpace = "[{\"id\":\"Q1180862\",\"label\":\"Dead Space\",\"description\":\"video game series\"},{\"id\":\"Q107616212\",\"label\":\"Dead Space\",\"description\":\"2023 survival horror video game remake developed by Motive Studios\"},{\"id\":\"Q729074\",\"label\":\"Dead Space\",\"description\":\"2008 video game\"},{\"id\":\"Q4424401\",\"label\":\"Dead Space\",\"description\":\"1991 film by Fred Gallo\"}]";
        var handler = new Handler(r => WikidataAnswer(r, searchTitle: deadSpace));
        using var ambiguous = new WikidataProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Null(await ambiguous.GetAsync(Request("Dead Space"), CancellationToken.None));                   // two real games: nothing is guessed
        Assert.DoesNotContain(handler.Urls, u => u.Contains("props=claims"));
        var candidates = await ambiguous.SearchAsync("Dead Space", CancellationToken.None);
        Assert.Equal(new[] { "Q107616212", "Q729074" }, candidates.Select(c => c.Id));                           // the series and the film are not offered
        Assert.Contains("2023", candidates[0].Detail);
    }

    [Fact]
    public async Task A_title_search_is_skipped_when_the_owner_said_no_listed_game_matches_and_bad_titles_are_not_sent()
    {
        var handler = new Handler(r => WikidataAnswer(r));
        using var provider = new WikidataProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Null(await provider.GetAsync(Request("Tekken 8", new GameIdentity(NoMatch: true, Source: "picked")), CancellationToken.None));
        Assert.Null(await provider.GetAsync(Request("C:\\Games\\x"), CancellationToken.None));
        Assert.Empty(await provider.SearchAsync("a/b", CancellationToken.None));
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public void Wikidata_dates_keep_the_precision_wikidata_recorded()
    {
        string Claim(string time, int precision) => "{\"entities\":{\"Q5\":{\"claims\":{\"P577\":[{\"mainsnak\":{\"datavalue\":{\"value\":{\"time\":\"" + time + "\",\"precision\":" + precision + "}}}}]}}}}";
        Assert.Equal("2008", WikidataProvider.ParseEntity(Encoding.UTF8.GetBytes(Claim("+2008-01-01T00:00:00Z", 9)), "Q5")!.Date!.ToString());
        Assert.Equal("2008-10", WikidataProvider.ParseEntity(Encoding.UTF8.GetBytes(Claim("+2008-10-01T00:00:00Z", 10)), "Q5")!.Date!.ToString());
        Assert.Equal("2008-10-14", WikidataProvider.ParseEntity(Encoding.UTF8.GetBytes(Claim("+2008-10-14T00:00:00Z", 11)), "Q5")!.Date!.ToString());
        Assert.Throws<IOException>(() => WikidataProvider.ParseEntity("not json"u8.ToArray(), "Q5"));
        Assert.Null(WikidataProvider.ParseEntity("{\"entities\":{}}"u8.ToArray(), "Q5"));
        Assert.True(WikidataProvider.LooksLikeGame("2023 survival horror video game remake"));
        Assert.False(WikidataProvider.LooksLikeGame("video game series"));
        Assert.False(WikidataProvider.LooksLikeGame("1991 film"));
    }

    // ---------- GOG ----------
    private const string GogCatalog = """
        {"products":[{"id":"1423049311","title":"Cyberpunk 2077","releaseDate":"2020.12.10","developers":["CD PROJEKT RED"],"publishers":["CD PROJEKT RED"],"genres":[{"name":"RPG"},{"name":"Action"}],"coverVertical":"https://images.gog-statics.com/c.jpg"},
        {"id":"1256837418","title":"Cyberpunk 2077: Phantom Liberty","releaseDate":"2023.09.26","developers":["CD PROJEKT RED"],"publishers":["CD PROJEKT RED"],"genres":[]}]}
        """;
    private const string GogProduct = """
        {"overview":"<p>Open-world <b>RPG</b> &amp; more.</p><p>Second paragraph.</p>","_embedded":{"developers":[{"name":"CD PROJEKT RED"}],"publisher":{"name":"CD PROJEKT RED"},"product":{"gogReleaseDate":"2020-12-07T11:55:59+01:00"}}}
        """;

    [Fact]
    public async Task Gog_finds_one_exact_title_and_adds_the_description_from_the_product_api()
    {
        var handler = new Handler(r => r.RequestUri!.Host == "catalog.gog.com" ? Json(GogCatalog) : Json(GogProduct));
        using var provider = new GogStoreProvider(handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(Request("Cyberpunk 2077"), CancellationToken.None))!;
        Assert.Equal("2020-12-10", data.ReleaseDate!.ToString());
        Assert.Equal(new[] { "RPG", "Action" }, data.Genres);
        Assert.Equal("Open-world RPG & more.\nSecond paragraph.", data.Description);
        Assert.Equal("1423049311", data.Identity!.GogId);
        Assert.Equal(2, handler.Urls.Count);
        Assert.StartsWith("https://catalog.gog.com/v1/catalog?", handler.Urls[0]);
        Assert.Contains("Cyberpunk", handler.Urls[0]);
        Assert.StartsWith("https://api.gog.com/v2/games/1423049311", handler.Urls[1]);
    }

    [Fact]
    public async Task Gog_uses_the_local_gog_id_directly_and_never_searches_for_ambiguous_or_refused_titles()
    {
        var byId = new Handler(_ => Json(GogProduct));
        using var known = new GogStoreProvider(byId, minimumGap: TimeSpan.Zero);
        var data = (await known.GetAsync(Request("Anything", new GameIdentity(GogId: "1423049311")), CancellationToken.None))!;
        Assert.Equal(new[] { "CD PROJEKT RED" }, data.Developers);
        Assert.Equal("2020-12-07", data.ReleaseDate!.ToString());
        Assert.Single(byId.Urls);

        var twice = new Handler(_ => Json("{\"products\":[{\"id\":\"1\",\"title\":\"Dead Space\"},{\"id\":\"2\",\"title\":\"Dead Space!\"}]}"));
        using var ambiguous = new GogStoreProvider(twice, minimumGap: TimeSpan.Zero);
        Assert.Null(await ambiguous.GetAsync(Request("Dead Space"), CancellationToken.None));
        Assert.Single(twice.Urls);                                                                              // searched, found two, stopped
        var refused = new Handler(_ => Json("{}"));
        using var none = new GogStoreProvider(refused, minimumGap: TimeSpan.Zero);
        Assert.Null(await none.GetAsync(Request("Dead Space", new GameIdentity(NoMatch: true, Source: "picked")), CancellationToken.None));
        Assert.Empty(refused.Urls);
        var candidates = await new GogStoreProvider(new Handler(_ => Json(GogCatalog)), minimumGap: TimeSpan.Zero).SearchAsync("Cyberpunk", CancellationToken.None);
        Assert.Equal("GOG · 2020", candidates[0].Detail);
    }

    [Fact]
    public async Task Gog_keeps_the_catalog_facts_when_the_product_page_is_missing_and_reports_other_failures()
    {
        var handler = new Handler(r => r.RequestUri!.Host == "catalog.gog.com" ? Json(GogCatalog) : Json("{}", HttpStatusCode.NotFound));
        using var provider = new GogStoreProvider(handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(Request("Cyberpunk 2077"), CancellationToken.None))!;
        Assert.Null(data.Description);
        Assert.Equal(new[] { "CD PROJEKT RED" }, data.Developers);
        using var broken = new GogStoreProvider(new Handler(_ => Json("not json")), minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => broken.GetAsync(Request("Cyberpunk 2077"), CancellationToken.None));
    }

    // ---------- Epic ----------
    private const string EpicPage = """
        {"namespace":"abc123","productName":"Grand Theft Auto V","pages":[{"data":{"about":{"developerAttribution":"Rockstar North","publisherAttribution":"Rockstar Games","shortDescription":"Short","description":"# Welcome to **Los Santos**\n\nA [big](https://x.test) city."},"meta":{"releaseDate":"2013-09-17T13:00:00.000Z"}}}]}
        """;

    [Fact]
    public async Task Epic_is_only_asked_for_epic_installs_and_the_page_namespace_must_match()
    {
        var none = new Handler(_ => Json(EpicPage));
        using var idle = new EpicStoreProvider(none, minimumGap: TimeSpan.Zero);
        Assert.Null(await idle.GetAsync(Request("Grand Theft Auto V"), CancellationToken.None));
        Assert.Empty(none.Urls);

        var handler = new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/grand-theft-auto-v-legacy") ? Json("{}", HttpStatusCode.NotFound) : Json(EpicPage));
        using var provider = new EpicStoreProvider(handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(Request("Grand Theft Auto V Legacy", new GameIdentity(EpicNamespace: "abc123")), CancellationToken.None))!;
        Assert.Equal("Rockstar North", data.Developers![0]);
        Assert.Equal("Rockstar Games", data.Publishers![0]);
        Assert.Equal("2013-09-17", data.ReleaseDate!.ToString());
        Assert.Equal("Welcome to Los Santos\nA big city.", data.Description);
        Assert.Equal("grand-theft-auto-v", data.Identity!.EpicSlug);
        Assert.Equal(2, handler.Urls.Count);                                                                      // the full title 404s, the shorter slug is verified by namespace

        using var wrong = new EpicStoreProvider(new Handler(_ => Json(EpicPage)), minimumGap: TimeSpan.Zero);
        Assert.Null(await wrong.GetAsync(Request("Grand Theft Auto V", new GameIdentity(EpicNamespace: "someoneelse")), CancellationToken.None));
    }

    [Fact]
    public void Epic_store_page_names_are_guessed_from_the_title_in_a_bounded_way()
    {
        Assert.Equal(new[] { "grand-theft-auto-v-legacy", "grand-theft-auto-v", "grand-theft-auto" }, EpicStoreProvider.Slugs("Grand Theft Auto V Legacy"));
        Assert.Equal(new[] { "dead-space" }, EpicStoreProvider.Slugs("Dead Space™"));
        Assert.Equal(new[] { "tom-and-jerry", "tom-and" }, EpicStoreProvider.Slugs("Tom & Jerry"));
        Assert.Empty(EpicStoreProvider.Slugs("###"));
    }

    // ---------- local launcher files ----------
    [Fact]
    public void Local_epic_gog_and_file_information_is_read_safely()
    {
        var manifests = Path.Combine(root, "epic");
        Directory.CreateDirectory(manifests);
        var install = Path.Combine(root, "Epic Games", "GTAV");
        Directory.CreateDirectory(install);
        string Item(string app, string main, string ns) => "{\"AppName\":\"" + app + "\",\"MainGameAppName\":\"" + main + "\",\"CatalogNamespace\":\"" + ns + "\",\"CatalogItemId\":\"item1\",\"DisplayName\":\"GTA V\",\"AppVersionString\":\"1.2\",\"InstallLocation\":\"" + install.Replace("\\", "\\\\") + "\"}";
        File.WriteAllText(Path.Combine(manifests, "dlc.item"), Item("Dlc", "Main", "dlcns"));
        File.WriteAllText(Path.Combine(manifests, "main.item"), Item("Main", "Main", "abc123"));
        File.WriteAllText(Path.Combine(manifests, "bad.item"), "{ nope");
        var info = EpicLocalInfo.Find(Path.Combine(install, "bin", "gta.exe"), manifests)!;
        Assert.Equal(("abc123", "item1", "1.2"), (info.Namespace, info.ItemId, info.Version));
        Assert.Null(EpicLocalInfo.Find(Path.Combine(root, "elsewhere", "x.exe"), manifests));
        Assert.Null(EpicLocalInfo.Find(Path.Combine(install, "x.exe"), Path.Combine(root, "nomanifests")));

        var gog = Path.Combine(root, "GOG Games", "Witcher");
        Directory.CreateDirectory(Path.Combine(gog, "bin"));
        File.WriteAllText(Path.Combine(gog, "goggame-1207658924.info"), "{\"gameId\":\"1207658924\",\"name\":\"The Witcher\"}");
        Assert.Equal("1207658924", GogLocalInfo.Find(Path.Combine(gog, "bin", "w.exe"))!.GameId);
        File.WriteAllText(Path.Combine(gog, "goggame-bad.info"), "{\"gameId\":\"../x\"}");
        Assert.Null(GogLocalInfo.Find(Path.Combine(root, "game", "game.exe")));

        Assert.True(LocalFileProvider.Plausible("Bandai Namco Studios Inc."));
        Assert.False(LocalFileProvider.Plausible("Epic Games, Inc."));
        Assert.False(LocalFileProvider.Plausible("Unity Technologies ApS"));
        Assert.False(LocalFileProvider.Plausible("  "));
        Assert.False(LocalFileProvider.Plausible("1234"));
    }

    [Fact]
    public async Task The_game_file_provider_never_fails_on_missing_or_unversioned_files()
    {
        var provider = new LocalFileProvider();
        Assert.Null(await provider.GetAsync(Request() with { ExecutablePath = Path.Combine(root, "missing.exe") }, CancellationToken.None));
        Assert.Null(await provider.GetAsync(Request(), CancellationToken.None));                                  // a plain fixture file has no version resource
    }

    // ---------- identity, picker and the service ----------
    private sealed class Recording(string id, ProviderData? data, params MetadataField[] fields) : IMatchSearchProvider
    {
        public readonly List<MetadataRequest> Requests = [];
        public List<MatchCandidate> Found = [];
        public Exception? SearchFails;
        public string Id => id;
        public string DisplayName => "Fake " + id;
        public IReadOnlySet<MetadataField> AvailableFields { get; } = fields.ToHashSet();
        public Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken) { Requests.Add(request); return Task.FromResult(data); }
        public Task<IReadOnlyList<MatchCandidate>> SearchAsync(string title, CancellationToken cancellationToken) =>
            SearchFails is null ? Task.FromResult<IReadOnlyList<MatchCandidate>>(Found) : throw SearchFails;
    }

    private sealed class NoStore : IStoreGameLauncher
    {
        public StoreLaunchPlan? Resolve(string selectedExecutable, string? targetOverride) => null;
        public void RequestLaunch(StoreLaunchPlan plan) { }
    }

    [Fact]
    public async Task What_one_provider_learns_flows_to_the_providers_asked_after_it()
    {
        var wiki = new Recording("wiki", new ProviderData(Genres: ["Action"], SteamAppId: "883710", Identity: new GameIdentity(GogId: "55", WikidataId: "Q9")), MetadataField.Description, MetadataField.Genres);
        var store = new Recording("later", new ProviderData(Description: "Text"), MetadataField.Description, MetadataField.Genres);
        var result = await MetadataResolver.ResolveAsync(Request("Resident Evil 2"), [wiki, store], MetadataSettings.Default, GameMetadataDocument.Empty(profile.Id), CancellationToken.None);
        var seen = Assert.Single(store.Requests);
        Assert.Equal("883710", seen.SteamAppId);
        Assert.Equal(("55", "Q9"), (seen.Identity!.GogId, seen.Identity.WikidataId));
        Assert.Equal(("883710", "Q9"), (result.Document.SteamAppId, result.Document.Identity!.WikidataId));
    }

    [Fact]
    public async Task A_picked_match_is_remembered_drops_wrong_facts_keeps_the_owners_text_and_is_not_overridden_by_the_install()
    {
        var gog = new Recording("gog", new ProviderData(Genres: ["Horror"], Identity: new GameIdentity(GogId: "7")), MetadataField.Genres);
        var service = new GameMetadataService(this.store, [gog], new NoStore());
        await service.SaveDescriptionAsync(profile.Id, "my own words");
        var wrong = (await service.LoadAsync(profile.Id)).Value!;
        await this.store.SaveMetadataAsync(wrong with { Fields = new(wrong.Fields) { ["Genres"] = new("old", false, DateTimeOffset.UtcNow, Items: ["Wrong genre"]) }, SteamAppId = "1693980" });

        var result = await service.ApplyMatchAsync(profile.Id, new MatchCandidate("gog", "7", "Dead Space (2008)", "GOG · 2008"), CancellationToken.None);
        var stored = (await service.LoadAsync(profile.Id)).Value!;
        Assert.Equal("my own words", stored.Get(MetadataField.Description)!.Text);
        Assert.Equal(new[] { "Horror" }, stored.Get(MetadataField.Genres)!.Items);                                   // replaced by the picked game, the wrong one is gone
        Assert.True(stored.Identity!.IsPicked);
        Assert.Equal("7", stored.Identity.GogId);
        Assert.Null(stored.SteamAppId);
        Assert.Equal(1, result.FieldsFilled);

        var sent = gog.Requests[^1];
        Assert.Equal("7", sent.Identity!.GogId);
        Assert.Null(sent.SteamAppId);                                                                                  // the old Steam id of another edition is gone
        await service.RefreshAsync(profile.Id, true, CancellationToken.None);
        Assert.Equal("7", (await service.LoadAsync(profile.Id)).Value!.Identity!.GogId);                               // a refresh keeps the choice
    }

    [Fact]
    public async Task Saying_none_match_stops_automatic_title_lookups_and_keeps_owner_text()
    {
        var provider = new Recording("p", new ProviderData(Genres: ["X"]), MetadataField.Genres);
        var service = new GameMetadataService(this.store, [provider], new NoStore());
        await service.SaveDescriptionAsync(profile.Id, "mine");
        await service.ApplyMatchAsync(profile.Id, null, CancellationToken.None);
        var request = provider.Requests[^1];
        Assert.True(request.Identity!.NoMatch);
        var stored = (await service.LoadAsync(profile.Id)).Value!;
        Assert.True(stored.Identity!.NoMatch);
        Assert.Equal("mine", stored.Get(MetadataField.Description)!.Text);
    }

    [Fact]
    public async Task Match_search_collects_candidates_from_every_source_and_reports_the_ones_that_fail()
    {
        var a = new Recording("a", null) { Found = [new("steam", "1", "Dead Space", "Steam · app 1")] };
        var b = new Recording("b", null) { SearchFails = new IOException("offline") };
        var c = new Recording("c", null) { Found = [new("wikidata", "Q1", "Dead Space", "Wikidata · 2008 video game")] };
        var search = await new GameMetadataService(this.store, [a, b, c], new NoStore()).SearchMatchesAsync("Dead Space", CancellationToken.None);
        Assert.Equal(new[] { "1", "Q1" }, search.Candidates.Select(x => x.Id));
        Assert.Single(search.Problems);
        Assert.Contains("offline", search.Problems[0]);
    }

    [Fact]
    public async Task Detected_launcher_ids_are_used_for_installed_games_and_stored()
    {
        var epicFolder = Path.Combine(root, "epicm");
        Directory.CreateDirectory(epicFolder);
        var game = Path.Combine(root, "game");
        File.WriteAllText(Path.Combine(epicFolder, "a.item"), "{\"AppName\":\"A\",\"CatalogNamespace\":\"nsx\",\"CatalogItemId\":\"i\",\"DisplayName\":\"D\",\"InstallLocation\":\"" + game.Replace("\\", "\\\\") + "\"}");
        File.WriteAllText(Path.Combine(game, "goggame-42.info"), "{\"gameId\":\"42\"}");
        var provider = new Recording("p", new ProviderData(Genres: ["X"]), MetadataField.Genres);
        var service = new GameMetadataService(this.store, [provider], new NoStore(), epicFolder);
        await service.RefreshAsync(profile.Id, false, CancellationToken.None);
        var identity = provider.Requests[0].Identity!;
        Assert.Equal(("nsx", "42"), (identity.EpicNamespace, identity.GogId));
        Assert.Equal(("nsx", "42"), (identity.EpicNamespace, (await service.LoadAsync(profile.Id)).Value!.Identity!.GogId));
    }

    // ---------- Steam: ambiguous titles ----------
    [Fact]
    public async Task Steam_picks_the_edition_that_fits_the_game_file_build_year_and_uses_the_plain_title_without_a_hint()
    {
        const string search = "{\"items\":[{\"type\":\"app\",\"name\":\"Dead Space\",\"id\":1693980,\"tiny_image\":\"https://cdn.test/a.jpg\"},{\"type\":\"app\",\"name\":\"Dead Space (2008)\",\"id\":17470},{\"type\":\"dlc\",\"name\":\"Dead Space DLC\",\"id\":5}]}";
        const string remake = "{\"1693980\":{\"success\":true,\"data\":{\"release_date\":{\"coming_soon\":false,\"date\":\"27 Jan, 2023\"}}}}";
        var handler = new Handler(r => r.RequestUri!.AbsolutePath.Contains("storesearch") ? Json(search) : Json(remake));
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Equal("1693980", await provider.FindAppIdAsync("Dead Space", CancellationToken.None));                    // no hint: the plain title
        Assert.Equal("17470", await provider.FindAppIdAsync("Dead Space", CancellationToken.None, 2008));                // built in 2008: the 2008 game
        Assert.Equal("17470", await provider.FindAppIdAsync("Dead Space", CancellationToken.None, 2009));
        Assert.Equal("1693980", await provider.FindAppIdAsync("Dead Space", CancellationToken.None, 2024));              // built in 2024: the 2023 remake
        var candidates = await provider.SearchAsync("Dead Space", CancellationToken.None);
        Assert.Equal(new[] { "1693980", "17470" }, candidates.Select(c => c.Id));
        Assert.Equal("https://cdn.test/a.jpg", candidates[0].ImageUrl);
        const string unique = "{\"items\":[{\"type\":\"app\",\"name\":\"TEKKEN\\u2122 8\",\"id\":1778820},{\"type\":\"app\",\"name\":\"TEKKEN 8 - Bob\",\"id\":4536140}]}";
        using var clear = new SteamStoreProvider(new Handler(_ => Json(unique)), minimumGap: TimeSpan.Zero);
        Assert.Equal("1778820", await clear.FindAppIdAsync("Tekken 8", CancellationToken.None, 2024));
        const string twins = "{\"items\":[{\"type\":\"app\",\"name\":\"Twin\",\"id\":1},{\"type\":\"app\",\"name\":\"Twin\",\"id\":2}]}";
        using var twin = new SteamStoreProvider(new Handler(_ => Json(twins)), minimumGap: TimeSpan.Zero);
        Assert.Null(await twin.FindAppIdAsync("Twin", CancellationToken.None));                                              // identical titles are never guessed
    }

    [Fact]
    public void The_build_year_comes_from_the_pe_header_and_is_ignored_when_it_is_not_believable()
    {
        Assert.Null(SteamStoreProvider.BuildYear(profile.ExecutablePath));                                                   // a plain file is not a program
        Assert.Null(SteamStoreProvider.BuildYear(Path.Combine(root, "missing.exe")));
        Assert.Null(SteamStoreProvider.BuildYear(null));
        var self = typeof(SteamStoreProvider).Assembly.Location;
        var year = SteamStoreProvider.BuildYear(self);                                                                       // a .NET assembly: deterministic builds may have no believable date
        Assert.True(year is null || year >= 1995);
    }

    [Fact]
    public void The_automatic_pass_retries_unmatched_games_after_a_week_but_never_picked_or_described_ones()
    {
        var now = DateTimeOffset.UtcNow;
        GameMetadataDocument Doc(DateTimeOffset? at, GameIdentity? identity = null, bool description = false) =>
            new(1, profile.Id, description ? new() { ["Description"] = new("steam", false, now, Text: "x") } : [], LastRefresh: at, Identity: identity);
        Assert.True(GameMetadataService.NeedsAutomaticLookup(null));
        Assert.True(GameMetadataService.NeedsAutomaticLookup(Doc(null)));
        Assert.False(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-1))));                                        // tried yesterday
        Assert.True(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-8))));                                         // unmatched, tried over a week ago
        Assert.False(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-30), description: true)));
        Assert.False(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-30), new GameIdentity(NoMatch: true, Source: "picked"))));
    }

    [Fact]
    public void Identity_documents_validate_and_older_files_still_load()
    {
        Assert.True(GameIdentity.IsValid(new GameIdentity(EpicSlug: "grand-theft-auto-v", GogId: "123", WikidataId: "Q9", HltbId: "113569", Source: "picked")));
        Assert.False(GameIdentity.IsValid(new GameIdentity(GogId: "../x")));
        Assert.False(GameIdentity.IsValid(new GameIdentity(Source: "mystery")));
        var picked = new GameIdentity(GogId: "1", WikidataId: "Q1", Source: "picked");
        Assert.Equal("1", picked.FillFrom(new GameIdentity(GogId: "2", HltbId: "9")).GogId);                           // never overwrites
        Assert.Equal("9", picked.FillFrom(new GameIdentity(GogId: "2", HltbId: "9")).HltbId);
        Assert.True(GameMetadataDocument.IsValid(GameMetadataDocument.Empty(profile.Id), profile.Id));
    }
}
