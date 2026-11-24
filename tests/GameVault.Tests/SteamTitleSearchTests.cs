using System.Net;
using System.Text;
using GameVault.Infrastructure.Metadata;

namespace GameVault.Tests;

/// <summary>
/// Regression for "Grand Theft Auto IV - The Complete Edition" finding no game information or cover: Steam's store search returns nothing when the
/// query contains " - " (it works with a space or a colon), while Steam's own title is "Grand Theft Auto IV: The Complete Edition".
/// </summary>
public sealed class SteamTitleSearchTests
{
    private const string GtaName = "Grand Theft Auto IV - The Complete Edition";

    private sealed class Handler(Func<string, string> items) : HttpMessageHandler
    {
        public readonly List<string> Terms = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query;
            var term = Uri.UnescapeDataString(query[(query.IndexOf("term=", StringComparison.Ordinal) + 5)..].Split('&')[0]);
            Terms.Add(term);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(items(term), Encoding.UTF8, "application/json") });
        }
    }

    private static string Result(params (int Id, string Name)[] apps) =>
        "{\"total\":" + apps.Length + ",\"items\":[" + string.Join(",", apps.Select(a => $"{{\"type\":\"app\",\"name\":\"{a.Name}\",\"id\":{a.Id},\"tiny_image\":\"https://shared.akamai.steamstatic.com/x/capsule.jpg\"}}")) + "]}";

    /// <summary>Behaves like the real store search: nothing for a query that has " - " in it.</summary>
    private static string Steam(string term) => term.Contains(" - ", StringComparison.Ordinal) ? Result() :
        term.StartsWith("Grand Theft Auto IV", StringComparison.OrdinalIgnoreCase) ? Result((12210, "Grand Theft Auto IV: The Complete Edition")) : Result();

    [Fact]
    public async Task A_title_with_a_dash_subtitle_finds_steams_colon_title_and_the_first_query_has_no_dash()
    {
        var handler = new Handler(Steam);
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Equal("12210", await provider.FindAppIdAsync(GtaName, CancellationToken.None));
        Assert.Equal(["Grand Theft Auto IV The Complete Edition"], handler.Terms);              // one request is enough
    }

    [Fact]
    public async Task The_same_title_written_with_a_colon_or_an_en_dash_or_extra_spaces_is_found_too()
    {
        foreach (var name in new[] { "Grand Theft Auto IV: The Complete Edition", "Grand Theft Auto IV – The Complete Edition", "  Grand Theft Auto IV   -   The Complete Edition ", "grand theft auto iv the complete edition" })
        {
            var handler = new Handler(Steam);
            using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
            Assert.Equal("12210", await provider.FindAppIdAsync(name, CancellationToken.None));
        }
    }

    [Fact]
    public async Task When_the_full_title_finds_nothing_the_part_before_the_subtitle_is_tried_and_the_match_must_still_equal_the_full_title()
    {
        var handler = new Handler(term => term == "Grand Theft Auto IV" ? Result((12210, "Grand Theft Auto IV: The Complete Edition"), (12220, "Grand Theft Auto: Episodes from Liberty City")) : Result());
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Equal("12210", await provider.FindAppIdAsync(GtaName, CancellationToken.None));
        Assert.Equal(["Grand Theft Auto IV The Complete Edition", GtaName, "Grand Theft Auto IV"], handler.Terms);

        var near = new Handler(term => term == "Grand Theft Auto IV" ? Result((12210, "Grand Theft Auto IV: The Complete Edition")) : Result());
        using var strict = new SteamStoreProvider(near, minimumGap: TimeSpan.Zero);
        Assert.Null(await strict.FindAppIdAsync("Grand Theft Auto IV - Episodes", CancellationToken.None));     // a different edition is never taken
    }

    [Fact]
    public async Task Nothing_found_stops_after_three_requests_and_two_apps_with_the_same_title_are_never_guessed()
    {
        var empty = new Handler(_ => Result());
        using var provider = new SteamStoreProvider(empty, minimumGap: TimeSpan.Zero);
        Assert.Null(await provider.FindAppIdAsync("Some Game - Subtitle", CancellationToken.None));
        Assert.Equal(3, empty.Terms.Count);

        var twins = new Handler(_ => Result((1, "Twin Game"), (2, "Twin Game")));
        using var twin = new SteamStoreProvider(twins, minimumGap: TimeSpan.Zero);
        Assert.Null(await twin.FindAppIdAsync("Twin Game", CancellationToken.None));
        Assert.Single(twins.Terms);                                                                  // ambiguity ends the search at once

        var single = new Handler(_ => Result());
        using var plain = new SteamStoreProvider(single, minimumGap: TimeSpan.Zero);
        Assert.Null(await plain.FindAppIdAsync("Tekken 8", CancellationToken.None));
        Assert.Single(single.Terms);                                                                 // no dash or subtitle: still one request, as before
    }

    [Fact]
    public async Task The_not_the_right_game_picker_also_finds_the_game_for_a_dash_title_and_stops_at_the_first_query_with_results()
    {
        var handler = new Handler(Steam);
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        var found = await provider.SearchAsync(GtaName, CancellationToken.None);
        var candidate = Assert.Single(found);
        Assert.Equal("12210", candidate.Id);
        Assert.Equal("Grand Theft Auto IV: The Complete Edition", candidate.Title);
        Assert.Single(handler.Terms);

        var fallback = new Handler(term => term == "Grand Theft Auto IV" ? Result((12210, "Grand Theft Auto IV: The Complete Edition")) : Result());
        using var second = new SteamStoreProvider(fallback, minimumGap: TimeSpan.Zero);
        Assert.Single(await second.SearchAsync(GtaName, CancellationToken.None));
        Assert.Equal(3, fallback.Terms.Count);
        Assert.Empty(await new SteamStoreProvider(new Handler(_ => Result()), minimumGap: TimeSpan.Zero).SearchAsync("Nothing Here - At All", CancellationToken.None));
    }

    [Theory]
    [InlineData("Grand Theft Auto IV - The Complete Edition", "Grand Theft Auto IV The Complete Edition|Grand Theft Auto IV - The Complete Edition|Grand Theft Auto IV")]
    [InlineData("Tekken 8", "Tekken 8")]
    [InlineData("Hades: Battle Out of Hell", "Hades: Battle Out of Hell|Hades")]
    [InlineData("Dead Space", "Dead Space")]
    [InlineData("  Resident   Evil 2  ", "Resident Evil 2")]
    [InlineData("Half-Life 2", "Half-Life 2")]                      // a hyphen inside a word is not a separator
    [InlineData("A - B", "A B|A - B")]                              // too short to cut at the subtitle
    [InlineData("", "")]
    public void The_search_terms_are_the_dash_free_title_then_the_title_as_typed_then_the_main_title(string title, string expected) =>
        Assert.Equal(expected, string.Join("|", SteamStoreProvider.SearchTerms(title)));

    [Fact]
    public void At_most_three_terms_are_ever_produced()
    {
        Assert.InRange(SteamStoreProvider.SearchTerms("Alpha Beta - Gamma: Delta - Epsilon").Count, 1, 3);
        Assert.Empty(SteamStoreProvider.SearchTerms(null!));
    }
}
