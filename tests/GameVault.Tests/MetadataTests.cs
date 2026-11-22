using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class MetadataTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-meta-" + Guid.NewGuid().ToString("N"));
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
        File.WriteAllBytes(exe, new byte[5000]);
        File.WriteAllBytes(Path.Combine(root, "game", "data.bin"), new byte[100]);
        profile = (await new LibraryService(paths, store).SaveAsync(new(null, "My Game", exe, Path.Combine(root, "game"), []))).Profile;
    }

    public Task DisposeAsync() { Directory.Delete(root, true); return Task.CompletedTask; }

    private sealed class Fake(string id, ProviderData? data, params MetadataField[] fields) : IGameMetadataProvider
    {
        public int Calls;
        public Exception? Fail;
        public string Id => id;
        public string DisplayName => "Fake " + id;
        public IReadOnlySet<MetadataField> AvailableFields { get; } = fields.ToHashSet();
        public Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail is not null) throw Fail;
            return Task.FromResult(data);
        }
    }

    private MetadataRequest Request() => new(profile.Id, "My Game", profile.ExecutablePath, null, null, null, null, true, false);
    private static readonly MetadataField[] Text = [MetadataField.Description, MetadataField.Genres];

    [Fact]
    public async Task The_first_provider_with_valid_data_wins_failures_and_gaps_are_skipped()
    {
        var failing = new Fake("a", null, Text) { Fail = new IOException("offline") };
        var empty = new Fake("b", new ProviderData(Description: "  "), Text);
        var good = new Fake("c", new ProviderData(Description: "Good one", Genres: ["RPG"]), Text);
        var late = new Fake("d", new ProviderData(Description: "Late", Genres: ["Racing"]), Text);
        var result = await MetadataResolver.ResolveAsync(Request(), [failing, empty, good, late], MetadataSettings.Default, GameMetadataDocument.Empty(profile.Id), CancellationToken.None);
        Assert.Equal("Good one", result.Document.Get(MetadataField.Description)!.Text);
        Assert.Equal("c", result.Document.Get(MetadataField.Description)!.Provider);
        Assert.Equal(new[] { "RPG" }, result.Document.Get(MetadataField.Genres)!.Items);
        Assert.Equal(2, result.FieldsFilled);
        Assert.Contains(result.Problems, p => p.Contains("offline"));
        Assert.Equal(0, late.Calls);                                   // never asked once everything was filled
        Assert.Equal(1, good.Calls);                                   // one request serves several fields
    }

    [Fact]
    public async Task Per_field_provider_order_decides_who_is_asked_first()
    {
        var a = new Fake("a", new ProviderData(Description: "from a"), MetadataField.Description);
        var b = new Fake("b", new ProviderData(Description: "from b"), MetadataField.Description);
        var settings = MetadataSettings.Default with { ProviderOrder = new() { ["Description"] = ["b", "a"] } };
        var result = await MetadataResolver.ResolveAsync(Request(), [a, b], settings, GameMetadataDocument.Empty(profile.Id), CancellationToken.None);
        Assert.Equal("from b", result.Document.Get(MetadataField.Description)!.Text);
        Assert.Equal(new[] { "b", "a" }, settings.OrderFor(MetadataField.Description, ["a", "b"]));
        Assert.Equal(new[] { "a", "b" }, MetadataSettings.Default.OrderFor(MetadataField.Description, ["a", "b"]));
        Assert.Equal(new[] { "b", "a", "c" }, settings.OrderFor(MetadataField.Description, ["a", "b", "c"]));
    }

    [Fact]
    public async Task Values_the_owner_typed_are_never_overwritten_and_existing_values_stay_unless_replacing()
    {
        var provider = new Fake("p", new ProviderData(Description: "provider text", Genres: ["Provider genre"]), Text);
        var typed = new MetadataValue("you", true, DateTimeOffset.UtcNow, Text: "my own words");
        var existing = new GameMetadataDocument(1, profile.Id, new() { ["Description"] = typed, ["Genres"] = new("old", false, DateTimeOffset.UtcNow, Items: ["Old genre"]) });
        var keep = await MetadataResolver.ResolveAsync(Request(), [provider], MetadataSettings.Default, existing, CancellationToken.None);
        Assert.Equal("my own words", keep.Document.Get(MetadataField.Description)!.Text);
        Assert.Equal(new[] { "Old genre" }, keep.Document.Get(MetadataField.Genres)!.Items);
        Assert.Equal(0, keep.FieldsFilled);
        Assert.Equal(0, provider.Calls);
        var replace = await MetadataResolver.ResolveAsync(Request(), [provider], MetadataSettings.Default with { OnlyFillEmpty = false }, existing, CancellationToken.None);
        Assert.Equal("my own words", replace.Document.Get(MetadataField.Description)!.Text);     // still the owner's
        Assert.Equal(new[] { "Provider genre" }, replace.Document.Get(MetadataField.Genres)!.Items);
    }

    [Fact]
    public async Task Disabled_fields_are_not_imported_and_cancellation_stops_the_run()
    {
        var provider = new Fake("p", new ProviderData(Description: "x", Genres: ["g"]), Text);
        var result = await MetadataResolver.ResolveAsync(Request(), [provider], MetadataSettings.Default with { DisabledFields = ["Description"] }, GameMetadataDocument.Empty(profile.Id), CancellationToken.None);
        Assert.Null(result.Document.Get(MetadataField.Description));
        Assert.NotNull(result.Document.Get(MetadataField.Genres));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MetadataResolver.ResolveAsync(Request(), [provider], MetadataSettings.Default, GameMetadataDocument.Empty(profile.Id), cancelled.Token));
    }

    [Theory]
    [InlineData("2013", "2013", "2013")]
    [InlineData("Mar 2013", "2013-03", "March 2013")]
    [InlineData("21 Mar, 2013", "2013-03-21", "21 March 2013")]
    [InlineData("Mar 21, 2013", "2013-03-21", "21 March 2013")]
    [InlineData("2013-03", "2013-03", "March 2013")]
    [InlineData("2013-03-21", "2013-03-21", "21 March 2013")]
    public void Release_dates_may_be_a_year_a_month_or_a_full_date(string input, string stored, string display)
    {
        var date = PartialDate.Parse(input)!;
        Assert.Equal(stored, date.ToString());
        Assert.Equal(display, date.Display());
        Assert.Equal(stored, PartialDate.Parse(stored)!.ToString());
    }

    [Theory]
    [InlineData("Coming soon")]
    [InlineData("")]
    [InlineData("31 Feb 2020")]
    [InlineData("1850")]
    [InlineData("2013-13")]
    public void Unusable_dates_are_rejected(string input) => Assert.Null(PartialDate.Parse(input));

    [Fact]
    public async Task Install_size_sums_nested_files_logically_or_by_cluster_skips_unchanged_folders_and_handles_missing_ones()
    {
        var folder = Path.Combine(root, "sizes");
        Directory.CreateDirectory(Path.Combine(folder, "a", "b"));
        File.WriteAllBytes(Path.Combine(folder, "one.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(folder, "a", "two.bin"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(folder, "a", "b", "three.bin"), new byte[1]);
        Assert.Equal(6001, LocalInstallProvider.Scan(folder, false, CancellationToken.None));
        Assert.Equal(4096 + 8192 + 4096, LocalInstallProvider.Scan(folder, true, CancellationToken.None));
        var provider = new LocalInstallProvider();
        MetadataRequest Req(string? install, DateTimeOffset? last, bool force) => new(profile.Id, "x", "x.exe", install, null, null, last, false, force);
        Assert.Equal(6001, (await provider.GetAsync(Req(folder, null, false), CancellationToken.None))!.InstallSizeBytes);
        Assert.Null(await provider.GetAsync(Req(folder, DateTimeOffset.UtcNow.AddMinutes(5), false), CancellationToken.None));          // not modified since the last scan
        Assert.NotNull(await provider.GetAsync(Req(folder, DateTimeOffset.UtcNow.AddMinutes(5), true), CancellationToken.None));       // unless forced
        Assert.Null(await provider.GetAsync(Req(Path.Combine(root, "missing"), null, false), CancellationToken.None));
        Assert.Null(await provider.GetAsync(Req(null, null, false), CancellationToken.None));
    }

    [Fact]
    public async Task Steam_games_use_the_size_from_their_own_manifest()
    {
        var steamapps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(steamapps);
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_620.acf"), "\"AppState\"\n{\n\"appid\" \"620\"\n\"SizeOnDisk\" \"123456789\"\n}\n");
        var data = await new LocalInstallProvider().GetAsync(new(profile.Id, "x", "x.exe", Path.Combine(root, "game"), "620", steamapps, null, true, false), CancellationToken.None);
        Assert.Equal(123456789, data!.InstallSizeBytes);
    }

    private const string SteamJson = "{\"620\":{\"success\":true,\"data\":{\"short_description\":\"Solve <b>puzzles</b> &amp; escape.\",\"release_date\":{\"coming_soon\":false,\"date\":\"18 Apr, 2011\"},\"developers\":[\"Valve\"],\"publishers\":[\"Valve\"],\"genres\":[{\"id\":\"1\",\"description\":\"Action\"},{\"id\":\"25\",\"description\":\"Adventure\"}]}}}";

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

    [Fact]
    public async Task The_steam_provider_reads_a_recorded_answer_and_sends_only_the_app_id()
    {
        var handler = new Handler(_ => Json(SteamJson));
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(new(profile.Id, "My Private Game Name", @"C:\Users\someone\game.exe", @"D:\secret", "620", null, null, true, false), CancellationToken.None))!;
        Assert.Equal("Solve puzzles & escape.", data.Description);
        Assert.Equal("2011-04-18", data.ReleaseDate!.ToString());
        Assert.Equal(new[] { "Valve" }, data.Developers);
        Assert.Equal(new[] { "Action", "Adventure" }, data.Genres);
        Assert.Equal(2, handler.Urls.Count);                                                                      // the details and the hover card, both addressed by the app id
        var url = handler.Urls[0];
        Assert.Equal("https://store.steampowered.com/apphoverpublic/620?l=english", handler.Urls[1]);
        Assert.Equal("https://store.steampowered.com/api/appdetails?appids=620&l=english", url);
        Assert.DoesNotContain("Private", url);
    }

    [Theory]
    [InlineData("{\"620\":{\"success\":false}}")]
    [InlineData("{\"999\":{\"success\":true,\"data\":{}}}")]
    [InlineData("[]")]
    public async Task Unsuccessful_or_unrelated_answers_give_no_data(string body)
    {
        using var provider = new SteamStoreProvider(new Handler(_ => Json(body)), minimumGap: TimeSpan.Zero);
        Assert.Null(await provider.GetAsync(new(profile.Id, "x", "x.exe", null, "620", null, null, true, false), CancellationToken.None));
    }

    [Fact]
    public async Task The_steam_provider_refuses_bad_ids_without_a_request_and_reports_errors_clearly()
    {
        var handler = new Handler(_ => Json("{}", HttpStatusCode.TooManyRequests));
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Null(await provider.GetAsync(new(profile.Id, "x", "x.exe", null, null, null, null, true, false), CancellationToken.None));
        Assert.Null(await provider.GetAsync(new(profile.Id, "x", "x.exe", null, "62a/../", null, null, true, false), CancellationToken.None));
        Assert.Empty(handler.Urls);
        var error = await Assert.ThrowsAsync<IOException>(() => provider.GetAsync(new(profile.Id, "x", "x.exe", null, "620", null, null, true, false), CancellationToken.None));
        Assert.Contains("slow down", error.Message);
        using var bad = new SteamStoreProvider(new Handler(_ => Json("not json")), minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => bad.GetAsync(new(profile.Id, "x", "x.exe", null, "620", null, null, true, false), CancellationToken.None));
    }

    [Fact]
    public async Task Oversized_answers_and_slow_servers_are_cut_off()
    {
        using var big = new SteamStoreProvider(new Handler(_ => Json(new string('x', 1100 * 1024))), minimumGap: TimeSpan.Zero);
        var error = await Assert.ThrowsAsync<IOException>(() => big.GetAsync(new(profile.Id, "x", "x.exe", null, "620", null, null, true, false), CancellationToken.None));
        Assert.Contains("large", error.Message);
        var slow = new SlowHandler();
        using var timeout = new SteamStoreProvider(slow, TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => timeout.GetAsync(new(profile.Id, "x", "x.exe", null, "620", null, null, true, false), CancellationToken.None));
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return Json("{}");
        }
    }

    [Fact]
    public async Task Requests_to_the_steam_store_keep_a_minimum_gap()
    {
        using var provider = new SteamStoreProvider(new Handler(_ => Json(SteamJson)), minimumGap: TimeSpan.FromMilliseconds(250));
        var request = new MetadataRequest(profile.Id, "x", "x.exe", null, "620", null, null, true, false);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await provider.GetAsync(request, CancellationToken.None);
        await provider.GetAsync(request, CancellationToken.None);
        Assert.True(clock.ElapsedMilliseconds >= 240, clock.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public async Task The_service_stores_facts_without_replacing_owner_text_and_the_documents_round_trip()
    {
        var steam = new SteamStoreProvider(new Handler(_ => Json(SteamJson)), minimumGap: TimeSpan.Zero);
        var service = new GameMetadataService(store, [new LocalInstallProvider(), steam], new NoStore());
        var first = await service.RefreshAsync(profile.Id, false, CancellationToken.None);
        Assert.NotNull(first.Document.Get(MetadataField.InstallSize));                // local size works without a store
        Assert.Null(first.Document.Get(MetadataField.Description));                   // not a Steam game: no app id, no request
        await service.SaveDescriptionAsync(profile.Id, "  my   own\tnotes ");
        var stored = (await service.LoadAsync(profile.Id)).Value!;
        Assert.Equal("my own notes", stored.Get(MetadataField.Description)!.Text);
        Assert.True(stored.Get(MetadataField.Description)!.UserEdited);
        var again = await service.RefreshAsync(profile.Id, true, CancellationToken.None);
        Assert.Equal("my own notes", again.Document.Get(MetadataField.Description)!.Text);
        await service.SaveDescriptionAsync(profile.Id, "");
        Assert.Null((await service.LoadAsync(profile.Id)).Value!.Get(MetadataField.Description));
        await service.SaveSettingsAsync(MetadataSettings.Default with { OnlyFillEmpty = false, DisabledFields = ["Genres"] });
        Assert.False((await service.LoadSettingsAsync()).OnlyFillEmpty);
        Assert.False((await service.LoadSettingsAsync()).IsEnabled(MetadataField.Genres));
    }

    [Fact]
    public async Task Bulk_fetch_reports_progress_and_stops_when_cancelled()
    {
        var service = new GameMetadataService(store, [new LocalInstallProvider()], new NoStore());
        var progress = new List<int>();
        var result = await service.FetchMissingAsync([(profile.Id, "My Game"), (Guid.NewGuid(), "Ghost")], new Progress<(int Done, int Total, string Name)>(p => { lock (progress) progress.Add(p.Done); }), CancellationToken.None);
        Assert.Equal((2, 1, false), (result.Games, result.GamesUpdated, result.Cancelled));
        Assert.Contains(result.Problems, p => p.StartsWith("Ghost"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.True((await service.FetchMissingAsync([(profile.Id, "My Game")], null, cancelled.Token)).Cancelled);
    }

    [Fact]
    public async Task Invalid_and_unsupported_documents_are_refused_or_read_only()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMetadataAsync(new(1, profile.Id, new() { ["Description"] = new("p", false, DateTimeOffset.UtcNow, Text: "") })));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMetadataAsync(new(1, profile.Id, new() { ["Bogus"] = new("p", false, DateTimeOffset.UtcNow, Text: "x") })));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMetadataAsync(new(1, profile.Id, new() { ["ReleaseDate"] = new("p", false, DateTimeOffset.UtcNow, Date: "soon") })));
        File.WriteAllText(Path.Combine(ProfileFolders.Resolve(paths, profile.Id), "metadata.json"), "{\"schemaVersion\":7,\"gameId\":\"" + profile.Id + "\",\"fields\":{}}");
        Assert.True((await store.LoadMetadataAsync(profile.Id)).IsReadOnly);
        await Assert.ThrowsAsync<IOException>(() => new GameMetadataService(store, []).SaveDescriptionAsync(profile.Id, "x"));
    }

    [Fact]
    public async Task A_failed_lookup_does_not_mark_the_game_as_looked_up_so_it_is_retried_and_a_finished_one_is_skipped()
    {
        var failing = new Fake("p", null, MetadataField.Genres) { Fail = new IOException("offline") };
        var service = new GameMetadataService(store, [failing], new NoStore());
        var failed = await service.RefreshAsync(profile.Id, false, CancellationToken.None);
        Assert.NotEmpty(failed.Problems);
        Assert.Equal(DocumentState.Missing, (await service.LoadAsync(profile.Id)).State);          // nothing saved: retried next time
        failing.Fail = null;
        var ok = new Fake("q", new ProviderData(Genres: ["RPG"]), MetadataField.Genres);
        var working = new GameMetadataService(store, [ok], new NoStore());
        var games = new[] { (profile.Id, "My Game") };
        Assert.Equal(1, (await working.FetchMissingAsync(games, null, CancellationToken.None, onlyNeverLookedUp: true)).GamesUpdated);
        Assert.Equal(1, ok.Calls);
        await working.FetchMissingAsync(games, null, CancellationToken.None, onlyNeverLookedUp: true);
        Assert.Equal(1, ok.Calls);                                                                   // already looked up: no second request
    }

    [Fact]
    public async Task A_game_nobody_knows_is_saved_as_looked_up_once_so_it_is_not_asked_again_every_start()
    {
        var none = new Fake("n", null, MetadataField.Genres);
        var service = new GameMetadataService(store, [none], new NoStore());
        await service.FetchMissingAsync([(profile.Id, "My Game")], null, CancellationToken.None, onlyNeverLookedUp: true);
        await service.FetchMissingAsync([(profile.Id, "My Game")], null, CancellationToken.None, onlyNeverLookedUp: true);
        Assert.Equal(1, none.Calls);
        Assert.NotNull((await service.LoadAsync(profile.Id)).Value!.LastRefresh);
    }

    [Fact]
    public async Task Automatic_lookup_is_on_by_default_and_older_settings_files_keep_it_on()
    {
        Assert.True(MetadataSettings.Default.AutoFetch);
        File.WriteAllText(Path.Combine(paths.Data, "metadata-settings.json"), "{\"schemaVersion\":1,\"onlyFillEmpty\":true,\"sizeOnDisk\":true,\"disabledFields\":[],\"providerOrder\":{}}");
        var loaded = await store.LoadMetadataSettingsAsync();
        Assert.Equal(DocumentState.Ready, loaded.State);
        Assert.True(loaded.Value!.AutoFetch);
    }

    [Fact]
    public async Task A_cover_is_only_written_when_the_game_has_none()
    {
        var library = new LibraryService(paths, store);
        byte[] Png(byte tail) => [137, 80, 78, 71, 13, 10, 26, 10, .. new byte[40], tail];
        var cover = Path.Combine(ProfileFolders.Resolve(paths, profile.Id), "cover.png");
        Assert.True(await library.SetCoverIfMissingAsync(profile.Id, Png(1)));
        Assert.Equal(1, File.ReadAllBytes(cover)[^1]);
        Assert.False(await library.SetCoverIfMissingAsync(profile.Id, Png(2)));                     // an existing cover is never replaced
        Assert.Equal(1, File.ReadAllBytes(cover)[^1]);
        await Assert.ThrowsAsync<ArgumentException>(() => library.SetCoverIfMissingAsync(profile.Id, new byte[100]));
        Assert.False(await library.SetCoverIfMissingAsync(Guid.NewGuid(), Png(3)));
    }

    private const string SearchJson = "{\"total\":3,\"items\":[{\"type\":\"app\",\"name\":\"TEKKEN 8 Ultimate Edition\",\"id\":111},{\"type\":\"dlc\",\"name\":\"TEKKEN 8\",\"id\":222},{\"type\":\"app\",\"name\":\"TEKKEN\\u2122 8\",\"id\":1778820}]}";

    [Fact]
    public async Task A_game_that_is_not_a_steam_install_is_found_by_its_exact_title_only()
    {
        var handler = new Handler(r => r.RequestUri!.AbsolutePath.Contains("storesearch") ? Json(SearchJson) : Json(SteamJson.Replace("\"620\"", "\"1778820\"")));
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Equal("1778820", await provider.FindAppIdAsync("Tekken 8", CancellationToken.None));               // case, trademark sign and edition-less title match
        Assert.Null(await provider.FindAppIdAsync("Tekken", CancellationToken.None));                              // a near match is never used
        var data = await provider.GetAsync(new(profile.Id, "TEKKEN 8", "x.exe", null, null, null, null, true, false), CancellationToken.None);
        Assert.Equal("1778820", data!.SteamAppId);
        Assert.Equal("Solve puzzles & escape.", data.Description);
        Assert.Contains(handler.Urls, u => u.Contains("storesearch") && (u.Contains("TEKKEN 8") || u.Contains("TEKKEN%208")));
        Assert.Contains(handler.Urls, u => u.EndsWith("appdetails?appids=1778820&l=english"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("C:\\Games\\x")]
    [InlineData("a/b")]
    [InlineData("http://evil")]
    public async Task Unsuitable_titles_are_never_searched(string title)
    {
        var handler = new Handler(_ => Json(SearchJson));
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Null(await provider.FindAppIdAsync(title, CancellationToken.None));
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public async Task The_steam_game_found_by_title_is_remembered_for_covers_and_later_lookups()
    {
        var calls = 0;
        var handler = new Handler(r => { calls++; return r.RequestUri!.AbsolutePath.Contains("storesearch") ? Json(SearchJson) : Json(SteamJson.Replace("\"620\"", "\"1778820\"")); });
        using var steam = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        var service = new GameMetadataService(store, [steam], new NoStore());
        await store.SaveProfileAsync(profile with { Name = "Tekken 8" });
        var first = await service.RefreshAsync(profile.Id, false, CancellationToken.None);
        Assert.Equal("1778820", first.Document.SteamAppId);
        Assert.Equal("1778820", await service.SteamAppIdAsync(profile.Id));
        var before = calls;
        await service.RefreshAsync(profile.Id, true, CancellationToken.None);
        Assert.Equal(before + 2, calls);                                                                         // one details request, no second search
    }

    private sealed class NoStore : IStoreGameLauncher
    {
        public StoreLaunchPlan? Resolve(string selectedExecutable, string? targetOverride) => null;
        public void RequestLaunch(StoreLaunchPlan plan) { }
    }
}
