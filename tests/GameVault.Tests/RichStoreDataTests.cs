using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class RichStoreDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-rich-" + Guid.NewGuid().ToString("N"));
    private readonly Guid id = Guid.NewGuid();

    public RichStoreDataTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Urls.Add(request.RequestUri!.ToString()); return Task.FromResult(respond(request)); }
    }

    // Shape recorded from the live Steam store answer for a real game (trimmed).
    private const string SteamRich = """
        {"1778820":{"success":true,"data":{"name":"TEKKEN 8","short_description":"The latest <b>TEKKEN</b>.","about_the_game":"<h2>About</h2><p>Long <i>text</i> &amp; more.</p>",
        "supported_languages":"English<strong>*</strong>, French, Japanese, Simplified Chinese<br><strong>*</strong>languages with full audio support",
        "pc_requirements":{"minimum":"<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>OS:</strong> Windows 10<br></li><li><strong>Memory:</strong> 8 GB RAM<br></li></ul>","recommended":"<strong>Recommended:</strong><br><ul class=\"bb_ul\"><li><strong>OS:</strong> Windows 11<br></li></ul>"},
        "developers":["Bandai Namco Studios Inc."],"publishers":["Bandai Namco Entertainment"],"website":"https://tk8.tekken-official.jp/","support_info":{"url":"https://bnfaq.channel.or.jp/ce","email":""},
        "platforms":{"windows":true,"mac":false,"linux":false},"metacritic":{"score":91,"url":"https://www.metacritic.com/game/pc/tekken-8?ftag=MCD-06-10aaa1f"},
        "categories":[{"id":2,"description":"Single-player"},{"id":1,"description":"Multi-player"}],"genres":[{"id":"1","description":"Action"}],
        "screenshots":[{"id":0,"path_thumbnail":"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1778820/ss_a.600x338.jpg?t=1","path_full":"https://shared.akamai.steamstatic.com/x.jpg"},{"id":1,"path_thumbnail":"https://evil.example/ss_b.jpg"}],
        "achievements":{"total":47},"dlc":[1,2,3],"required_age":0,"release_date":{"coming_soon":false,"date":"25 Jan, 2024"},
        "ratings":{"esrb":{"rating":"t"},"pegi":{"rating":"16"},"usk":{"rating":"16"}}}}}
        """;

    [Fact]
    public void Steam_answers_give_the_rich_fields_cleaned_and_limited()
    {
        var data = SteamStoreProvider.Parse(Encoding.UTF8.GetBytes(SteamRich), "1778820")!;
        Assert.Equal("The latest TEKKEN.", data.Description);
        Assert.Equal("About\nLong text & more.", data.About);
        Assert.Equal(new[] { "Single-player", "Multi-player" }, data.Features);
        Assert.Equal(new[] { "Windows" }, data.Platforms);
        Assert.Equal(new[] { "English", "French", "Japanese", "Simplified Chinese" }, data.Languages);              // footnotes and stars are gone
        Assert.Equal("PEGI 16 · ESRB T", data.AgeRating);
        Assert.Equal((91, "https://www.metacritic.com/game/pc/tekken-8?ftag=MCD-06-10aaa1f"), (data.CriticScore, data.CriticUrl));
        Assert.Equal("Minimum:\nOS: Windows 10\nMemory: 8 GB RAM\n\nRecommended:\nOS: Windows 11", data.Requirements);
        Assert.Equal(new[] { "Website|https://tk8.tekken-official.jp/", "Support|https://bnfaq.channel.or.jp/ce" }, data.Links);
        Assert.Equal((47, 3), (data.AchievementCount, data.DlcCount));
        Assert.Equal(new[] { "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1778820/ss_a.600x338.jpg?t=1" }, data.Screenshots);   // the foreign host is dropped
    }

    [Fact]
    public void Steam_answers_without_the_optional_parts_still_work_and_unsafe_links_are_dropped()
    {
        const string bare = "{\"7\":{\"success\":true,\"data\":{\"short_description\":\"x\",\"website\":\"http://insecure.example\",\"required_age\":\"18\"}}}";
        var data = SteamStoreProvider.Parse(Encoding.UTF8.GetBytes(bare), "7")!;
        Assert.Equal("Age 18+", data.AgeRating);
        Assert.Empty(data.Links!);
        Assert.Null(data.CriticScore);
        Assert.Null(data.Requirements);
        Assert.Null(data.Screenshots);
    }

    private const string Hover = """
        <div class="hover_body"><div class="hover_review_summary"><div class="title">English Reviews:</div>
        <span class="game_review_summary mixed">Mixed</span>
        (27,925 reviews)
        <span class="review_anomaly_icon">*</span></div></div>
        <div class="hover_body"><span class="tags_title">User tags:</span><div class="hover_tag_row"><div class="app_tag">Fighting</div><div class="app_tag">3D Fighter</div><div class="app_tag">Anime &amp; Manga</div></div></div>
        """;

    [Fact]
    public void The_hover_card_gives_user_tags_and_the_review_summary()
    {
        var (tags, summary) = SteamStoreProvider.ParseHover(Hover);
        Assert.Equal(new[] { "Fighting", "3D Fighter", "Anime & Manga" }, tags);
        Assert.Equal("Mixed (27,925 reviews)", summary);
        Assert.Equal((null, null), SteamStoreProvider.ParseHover("<html>nothing here</html>"));
        Assert.Equal((null, null), SteamStoreProvider.ParseHover(""));
        Assert.Equal((null, null), SteamStoreProvider.ParseHover(new string('x', 300_000)));
    }

    [Fact]
    public async Task A_failing_hover_card_never_loses_the_rest_of_the_steam_data()
    {
        var handler = new Handler(r => r.RequestUri!.AbsolutePath.Contains("apphoverpublic") ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Ok(SteamRich));
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(new(id, "TEKKEN 8", "x.exe", null, "1778820", null, null, true, false), CancellationToken.None))!;
        Assert.Equal(91, data.CriticScore);
        Assert.Null(data.Tags);
        Assert.Contains("Steam store page|https://store.steampowered.com/app/1778820/", data.Links!);
        var good = new Handler(r => r.RequestUri!.AbsolutePath.Contains("apphoverpublic") ? Ok(Hover) : Ok(SteamRich));
        using var both = new SteamStoreProvider(good, minimumGap: TimeSpan.Zero);
        var full = (await both.GetAsync(new(id, "TEKKEN 8", "x.exe", null, "1778820", null, null, true, false), CancellationToken.None))!;
        Assert.Equal("Mixed (27,925 reviews)", full.CommunitySummary);
        Assert.Equal(3, full.Tags!.Length);
    }

    // ---------- GOG ----------
    private const string GogCatalog = """
        {"products":[{"id":"1300381715","title":"Tokyo 42","releaseDate":"2017.05.31","developers":["SMAC Games"],"publishers":["Mode 7"],"genres":[{"name":"Shooter"},{"name":"Action"}],
        "tags":[{"name":"Indie"},{"name":"Sci-fi"}],"features":[{"name":"Cloud saves"},{"name":"Single-player"}],"operatingSystems":["windows","osx"],"ratings":[{"name":"pegiRating","ageRating":"16"}],
        "storeLink":"https://www.gog.com/en/game/tokyo_42","galaxyBackgroundImage":"https://images.gog-statics.com/bg.jpg"}]}
        """;
    private const string GogV2 = """
        {"overview":"<p>Short.</p>","description":"<p>The long text.</p>","_links":{"store":{"href":"https://www.gog.com/en/game/tokyo_42"},"support":{"href":"https://www.gog.com/support/tokyo_42_game"},"forum":{"href":"https://www.gog.com/forum/tokyo_42"}},
        "_embedded":{"developers":[{"name":"SMAC Games"}],"publisher":{"name":"Mode 7"},"features":[{"name":"Single-player"}],
        "localizations":[{"_embedded":{"language":{"name":"English"}}},{"_embedded":{"language":{"name":"English"}}},{"_embedded":{"language":{"name":"German"}}}],
        "supportedOperatingSystems":[{"operatingSystem":{"name":"windows"}}],
        "screenshots":[{"_links":{"self":{"href":"https://images.gog-statics.com/abc_{formatter}.jpg"}}},{"_links":{"self":{"href":"https://evil.example/{formatter}.jpg"}}}]}}
        """;

    [Fact]
    public async Task Gog_gives_tags_features_platforms_languages_age_links_and_screenshots()
    {
        var handler = new Handler(r => Ok(r.RequestUri!.Host == "catalog.gog.com" ? GogCatalog : GogV2));
        using var provider = new GogStoreProvider(handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(new(id, "Tokyo 42", "x.exe", null, null, null, null, true, false), CancellationToken.None))!;
        Assert.Equal(new[] { "Indie", "Sci-fi" }, data.Tags);
        Assert.Equal(new[] { "Cloud saves", "Single-player" }, data.Features);
        Assert.Equal(new[] { "Windows", "macOS" }, data.Platforms);
        Assert.Equal(new[] { "English", "German" }, data.Languages);
        Assert.Equal("PEGI 16", data.AgeRating);
        Assert.Equal("The long text.", data.About);
        Assert.Equal("Short.", data.Description);
        Assert.Equal(new[] { "GOG store page|https://www.gog.com/en/game/tokyo_42", "Support|https://www.gog.com/support/tokyo_42_game", "Forum|https://www.gog.com/forum/tokyo_42" }, data.Links);
        Assert.Equal(new[] { "https://images.gog-statics.com/abc_product_card_screenshot_748.jpg" }, data.Screenshots);
        Assert.Equal("https://images.gog-statics.com/bg.jpg", data.HeroUrl);
    }

    // ---------- Epic ----------
    [Fact]
    public void Epic_requirements_and_the_us_rating_become_readable_text()
    {
        const string page = """
            {"namespace":"n1","productRatings":{"ratings":[{"countryCodes":"DE","title":"USK ab 18"},{"countryCodes":"US,CA","title":"MATURE 17+"}]},"pages":[{"data":{
            "about":{"shortDescription":"S","description":"# Big\n\nStory"},"meta":{"platform":["Windows"]},
            "requirements":{"systems":[{"systemType":"Mac","details":[{"title":"OS","minimum":"macOS"}]},{"systemType":"Windows","details":[{"title":"OS ","minimum":"Windows 10","recommended":"Windows 11"},{"title":"Memory","minimum":"8 GB"}]}]}}}]}
            """;
        var data = EpicStoreProvider.Parse(Encoding.UTF8.GetBytes(page), "n1", "Any", "any-slug")!;
        Assert.Equal("ESRB MATURE 17+", data.AgeRating);
        Assert.Equal("Minimum\nOS: Windows 10\nMemory: 8 GB\n\nRecommended\nOS: Windows 11", data.Requirements);
        Assert.Equal("Big\nStory", data.About);
        Assert.Equal(new[] { "Windows" }, data.Platforms);
        Assert.Equal(new[] { "Epic Games Store page|https://store.epicgames.com/p/any-slug" }, data.Links);
    }

    // ---------- what the launchers recorded locally ----------
    [Fact]
    public void Installed_build_update_language_and_epic_version_are_read_from_the_launcher_files()
    {
        var steamapps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(steamapps);
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_620.acf"),
            "\"AppState\"\n{\n\"appid\" \"620\"\n\"buildid\" \"12345678\"\n\"LastUpdated\" \"1735689600\"\n\"UserConfig\"\n{\n\"language\" \"english\"\n\"BetaKey\" \"beta_test\"\n}\n}\n");
        var request = new MetadataRequest(id, "x", Path.Combine(root, "x.exe"), null, "620", steamapps, null, true, false);
        var lines = LocalInstallProvider.InstalledInfo(request)!;
        Assert.Equal("Build 12345678", lines[0]);
        Assert.StartsWith("Updated ", lines[1]);
        Assert.Contains("2025", lines[1]);
        Assert.Equal(new[] { "Language: english", "Branch: beta_test" }, lines.Skip(2));
        Assert.Null(LocalInstallProvider.InstalledInfo(request with { SteamAppId = "999" }));                          // no manifest, no Epic install: nothing
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_5.acf"), "{ broken");
        Assert.Null(LocalInstallProvider.InstalledInfo(request with { SteamAppId = "5" }));
    }

    // ---------- the stored document and the resolver ----------
    [Fact]
    public void The_stored_document_accepts_only_safe_rich_values()
    {
        GameMetadataDocument Doc(string field, MetadataValue value) => GameMetadataDocument.Empty(id) with { Fields = new() { [field] = value } };
        MetadataValue V(string[]? items = null, string? text = null, int? number = null, string? url = null) => new("p", false, DateTimeOffset.UtcNow, text, items, Number: number, Url: url);
        Assert.True(GameMetadataDocument.IsValid(Doc("Links", V(["Website|https://example.org/"])), id));
        Assert.False(GameMetadataDocument.IsValid(Doc("Links", V(["Website|http://example.org/"])), id));
        Assert.False(GameMetadataDocument.IsValid(Doc("Links", V(["Website|javascript:alert(1)"])), id));
        Assert.False(GameMetadataDocument.IsValid(Doc("Links", V(["no separator https://example.org/"])), id));
        Assert.True(GameMetadataDocument.IsValid(Doc("Screenshots", V(["https://images.gog-statics.com/a.jpg"])), id));
        Assert.False(GameMetadataDocument.IsValid(Doc("Screenshots", V(["https://evil.example/a.jpg"])), id));
        Assert.True(GameMetadataDocument.IsValid(Doc("CriticScore", V(number: 91, url: "https://www.metacritic.com/x")), id));
        Assert.False(GameMetadataDocument.IsValid(Doc("CriticScore", V(number: 101)), id));
        Assert.False(GameMetadataDocument.IsValid(Doc("CriticScore", V(number: 50, url: "file:///c:/x")), id));
        Assert.True(GameMetadataDocument.IsValid(Doc("DlcCount", V(number: 25)), id));
        Assert.False(GameMetadataDocument.IsValid(Doc("DlcCount", V(number: -1)), id));
        Assert.True(GameMetadataDocument.IsValid(Doc("About", V(text: new string('a', GameMetadataDocument.MaxAboutLength))), id));
        Assert.False(GameMetadataDocument.IsValid(Doc("About", V(text: new string('a', GameMetadataDocument.MaxAboutLength + 1))), id));
        Assert.True(GameMetadataDocument.IsValid(Doc("Tags", V(["Fighting", "Anime"])), id));
        Assert.True(GameMetadataDocument.IsValid(GameMetadataDocument.Empty(id) with { LookupVersion = 2 }, id));
        Assert.False(GameMetadataDocument.IsValid(GameMetadataDocument.Empty(id) with { LookupVersion = 5000 }, id));
    }

    [Fact]
    public async Task The_resolver_stores_every_rich_field_with_its_provider_and_filters_unsafe_values()
    {
        var provider = new Fake("steam", new ProviderData(
            About: "Long", Tags: ["A", "B"], Features: ["Co-op"], Platforms: ["Windows"], Languages: ["English"], AgeRating: "PEGI 16", CriticScore: 91, CriticUrl: "http://insecure.example",
            CommunitySummary: "Mixed (5 reviews)", Requirements: "Minimum\nOS", Links: ["Website|https://example.org/", "Bad|http://example.org/"], AchievementCount: 47, DlcCount: 3,
            Screenshots: ["https://images.gog-statics.com/a.jpg", "https://evil.example/b.jpg"], Installed: ["Build 1"]));
        var result = await MetadataResolver.ResolveAsync(new(id, "G", "x.exe", null, null, null, null, true, false), [provider], MetadataSettings.Default, GameMetadataDocument.Empty(id), CancellationToken.None);
        var d = result.Document;
        Assert.Equal("Long", d.Get(MetadataField.About)!.Text);
        Assert.Equal(new[] { "A", "B" }, d.Get(MetadataField.Tags)!.Items);
        Assert.Equal(91, d.Get(MetadataField.CriticScore)!.Number);
        Assert.Null(d.Get(MetadataField.CriticScore)!.Url);                                                           // an insecure page address is not kept
        Assert.Equal(new[] { "Website|https://example.org/" }, d.Get(MetadataField.Links)!.Items);
        Assert.Equal(new[] { "https://images.gog-statics.com/a.jpg" }, d.Get(MetadataField.Screenshots)!.Items);
        Assert.Equal((47, 3), (d.Get(MetadataField.AchievementCount)!.Number, d.Get(MetadataField.DlcCount)!.Number));
        Assert.Equal(new[] { "Build 1" }, d.Get(MetadataField.Installed)!.Items);
        Assert.True(GameMetadataDocument.IsValid(d, id));
    }

    private sealed class Fake(string providerId, ProviderData data) : IGameMetadataProvider
    {
        public string Id => providerId;
        public string DisplayName => providerId;
        public IReadOnlySet<MetadataField> AvailableFields { get; } = Enum.GetValues<MetadataField>().ToHashSet();
        public Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken) => Task.FromResult<ProviderData?>(data);
    }
}
