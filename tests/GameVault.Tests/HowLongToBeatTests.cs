using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Metadata;

namespace GameVault.Tests;

public sealed class HowLongToBeatTests
{
    private readonly Guid id = Guid.NewGuid();

    private static byte[] Page(string game) => Encoding.UTF8.GetBytes(
        "<!DOCTYPE html><html><head><script id=\"__NEXT_DATA__\" type=\"application/json\">{\"props\":{\"pageProps\":{\"game\":{\"data\":{\"game\":[" + game + "]}}}}}</script></head><body></body></html>");

    private const string Tekken = "{\"game_id\":113569,\"game_name\":\"Tekken 8\",\"comp_main\":14970,\"comp_plus\":41088,\"comp_100\":63052,\"comp_all\":28095,\"profile_dev\":\"Bandai Namco Studios\"}";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Urls.Add(request.RequestUri!.ToString()); return Task.FromResult(respond(request)); }
    }

    private static HttpResponseMessage Html(byte[] body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new ByteArrayContent(body) };
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private MetadataRequest Request(GameIdentity? identity = null, string? steam = null) => new(id, "Tekken 8", "x.exe", null, steam, null, null, true, false, identity);

    [Fact]
    public void The_times_are_read_from_the_page_and_odd_pages_give_nothing()
    {
        var times = HowLongToBeatProvider.ParsePage(Page(Tekken), "113569")!;
        Assert.Equal((14970, 41088, 63052, 28095), (times.MainSeconds, times.PlusSeconds, times.CompleteSeconds, times.AllSeconds));
        Assert.Equal("https://howlongtobeat.com/game/113569", times.Url);
        var partial = HowLongToBeatProvider.ParsePage(Page("{\"comp_main\":3600,\"comp_plus\":0,\"comp_100\":-5,\"comp_all\":null}"), "5")!;
        Assert.Equal((3600, null, null, null), (partial.MainSeconds, partial.PlusSeconds, partial.CompleteSeconds, partial.AllSeconds));
        Assert.Null(HowLongToBeatProvider.ParsePage(Page("{\"comp_main\":0,\"comp_plus\":0}"), "5"));                       // a game nobody has timed
        Assert.Null(HowLongToBeatProvider.ParsePage(Page("{\"comp_main\":99999999}"), "5"));                                 // not believable
        Assert.Null(HowLongToBeatProvider.ParsePage(Page(""), "5"));
        Assert.Null(HowLongToBeatProvider.ParsePage("<html>no data</html>"u8.ToArray(), "5"));
        Assert.Null(HowLongToBeatProvider.ParsePage("<script id=\"__NEXT_DATA__\">{ broken</script>"u8.ToArray(), "5"));
        Assert.Null(HowLongToBeatProvider.ParsePage([], "5"));
    }

    [Fact]
    public async Task A_known_hltb_id_is_read_directly_and_only_that_page_is_requested()
    {
        var handler = new Handler(_ => Html(Page(Tekken)));
        using var wiki = new WikidataProvider(new Handler(_ => Json("{}")), minimumGap: TimeSpan.Zero);
        using var provider = new HowLongToBeatProvider(wiki, handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(Request(new GameIdentity(HltbId: "113569")), CancellationToken.None))!;
        Assert.Equal(14970, data.Times!.MainSeconds);
        Assert.Equal("113569", data.Identity!.HltbId);
        Assert.Equal(new[] { "https://howlongtobeat.com/game/113569" }, handler.Urls);
    }

    [Fact]
    public async Task Without_an_id_wikidata_supplies_it_and_games_without_one_get_nothing_and_no_request()
    {
        const string entity = "{\"entities\":{\"Q1\":{\"claims\":{\"P2816\":[{\"mainsnak\":{\"datavalue\":{\"value\":\"113569\"}}}]}}}}";
        var wikiHandler = new Handler(r => Json(r.RequestUri!.ToString().Contains("list=search") ? "{\"query\":{\"search\":[{\"title\":\"Q1\"}]}}" : entity));
        using var wiki = new WikidataProvider(wikiHandler, minimumGap: TimeSpan.Zero);
        var handler = new Handler(_ => Html(Page(Tekken)));
        using var provider = new HowLongToBeatProvider(wiki, handler, minimumGap: TimeSpan.Zero);
        var data = (await provider.GetAsync(Request(steam: "1778820"), CancellationToken.None))!;
        Assert.Equal(63052, data.Times!.CompleteSeconds);
        Assert.Single(handler.Urls);

        var empty = new Handler(_ => Json("{\"entities\":{\"Q1\":{\"claims\":{}}}}"));
        using var wiki2 = new WikidataProvider(new Handler(r => Json(r.RequestUri!.ToString().Contains("list=search") ? "{\"query\":{\"search\":[{\"title\":\"Q1\"}]}}" : "{\"entities\":{\"Q1\":{\"claims\":{}}}}")), minimumGap: TimeSpan.Zero);
        using var none = new HowLongToBeatProvider(wiki2, empty, minimumGap: TimeSpan.Zero);
        Assert.Null(await none.GetAsync(Request(steam: "1778820"), CancellationToken.None));
        Assert.Empty(empty.Urls);
        Assert.Null(await none.GetAsync(Request(new GameIdentity(HltbId: "../etc"), "1"), CancellationToken.None));            // a bad id is never put in an address
    }

    [Fact]
    public async Task A_blocked_or_broken_site_is_a_quiet_problem_and_never_a_crash()
    {
        using var wiki = new WikidataProvider(new Handler(_ => Json("{}")), minimumGap: TimeSpan.Zero);
        using var blocked = new HowLongToBeatProvider(wiki, new Handler(_ => Html([], HttpStatusCode.Forbidden)), minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => blocked.GetAsync(Request(new GameIdentity(HltbId: "5")), CancellationToken.None));
        using var gone = new HowLongToBeatProvider(wiki, new Handler(_ => Html([], HttpStatusCode.NotFound)), minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<FileNotFoundException>(() => gone.GetAsync(Request(new GameIdentity(HltbId: "5")), CancellationToken.None));
        using var redirect = new HowLongToBeatProvider(wiki, new Handler(_ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/") } }), minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => redirect.GetAsync(Request(new GameIdentity(HltbId: "5")), CancellationToken.None));
        // the resolver turns those into a reported problem and still stores what other providers found
        var result = await MetadataResolver.ResolveAsync(Request(new GameIdentity(HltbId: "5")), [blocked], MetadataSettings.Default, GameMetadataDocument.Empty(id), CancellationToken.None);
        Assert.Null(result.Document.Get(MetadataField.TimeToBeat));
        Assert.Contains(result.Problems, p => p.Contains("HowLongToBeat"));
    }

    [Fact]
    public async Task The_resolver_stores_the_times_and_the_document_only_accepts_believable_times_and_the_hltb_site()
    {
        var handler = new Handler(_ => Html(Page(Tekken)));
        using var wiki = new WikidataProvider(new Handler(_ => Json("{}")), minimumGap: TimeSpan.Zero);
        using var provider = new HowLongToBeatProvider(wiki, handler, minimumGap: TimeSpan.Zero);
        var result = await MetadataResolver.ResolveAsync(Request(new GameIdentity(HltbId: "113569")), [provider], MetadataSettings.Default, GameMetadataDocument.Empty(id), CancellationToken.None);
        Assert.Equal(41088, result.Document.Get(MetadataField.TimeToBeat)!.Times!.PlusSeconds);
        Assert.True(GameMetadataDocument.IsValid(result.Document, id));
        GameMetadataDocument Doc(BeatTimes times) => GameMetadataDocument.Empty(id) with { Fields = new() { ["TimeToBeat"] = new("hltb", false, DateTimeOffset.UtcNow, Times: times) } };
        Assert.False(GameMetadataDocument.IsValid(Doc(new(null, null, null, null, null)), id));
        Assert.False(GameMetadataDocument.IsValid(Doc(new(-1, null, null, null, null)), id));
        Assert.False(GameMetadataDocument.IsValid(Doc(new(BeatTimes.MaxSeconds + 1, null, null, null, null)), id));
        Assert.False(GameMetadataDocument.IsValid(Doc(new(100, null, null, null, "https://evil.example/game/1")), id));
        Assert.False(GameMetadataDocument.IsValid(Doc(new(100, null, null, null, "http://howlongtobeat.com/game/1")), id));
        Assert.True(GameMetadataDocument.IsValid(Doc(new(100, null, null, null, "https://howlongtobeat.com/game/1")), id));
    }

    [Fact]
    public void Games_whose_times_could_not_be_read_are_retried_after_a_week_and_older_documents_are_refilled_once()
    {
        var now = DateTimeOffset.UtcNow;
        GameMetadataDocument Doc(DateTimeOffset at, bool times, string? hltb = "113569") => new(1, id, new Dictionary<string, MetadataValue>
        {
            ["Description"] = new("steam", false, now, Text: "x"),
            ["TimeToBeat"] = new("hltb", false, now, Times: new(100, null, null, null, null))
        }.Where(p => times || p.Key != "TimeToBeat").ToDictionary(p => p.Key, p => p.Value), LastRefresh: at, Identity: hltb is null ? null : new GameIdentity(HltbId: hltb), LookupVersion: GameMetadataService.CurrentLookupVersion);
        Assert.False(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-1), false)));
        Assert.True(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-8), false)));                              // the id is known but the times are missing
        Assert.False(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-8), true)));
        Assert.False(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-30), false, null)));                      // not on HowLongToBeat at all
        Assert.True(GameMetadataService.NeedsAutomaticLookup(Doc(now.AddDays(-1), true) with { LookupVersion = 2 }));    // looked up before this fact existed
        Assert.Equal(3, GameMetadataService.CurrentLookupVersion);
    }
}
