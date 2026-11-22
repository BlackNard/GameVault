using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Average time to beat a game (main story, with extras, completionist) from the game's HowLongToBeat page. HowLongToBeat has no public API and its
/// search is closed to programs, so the page is read by id only; the id comes from Wikidata (or an earlier lookup). The page is unofficial data and can
/// change, so every problem here is a quiet "no times" and never affects anything else. One request per game, with a gap, a timeout and a size cap.
/// </summary>
public sealed class HowLongToBeatProvider : IGameMetadataProvider, IDisposable
{
    public const string ProviderId = "hltb";
    private static readonly Regex NextData = new("<script id=\"__NEXT_DATA__\"[^>]*>(.*?)</script>", RegexOptions.Compiled | RegexOptions.Singleline);
    private readonly PoliteHttp http;
    private readonly WikidataProvider wikidata;

    public HowLongToBeatProvider(WikidataProvider wikidata, HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null)
    {
        this.wikidata = wikidata;
        http = new("HowLongToBeat", handler, timeout ?? TimeSpan.FromSeconds(20), minimumGap ?? TimeSpan.FromMilliseconds(1500));
    }

    public string Id => ProviderId;
    public string DisplayName => "HowLongToBeat";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField> { MetadataField.TimeToBeat };

    public async Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        var id = request.Identity?.HltbId;
        if (id is null) id = (await wikidata.GetAsync(request, cancellationToken).ConfigureAwait(false))?.Identity?.HltbId;
        if (id is not { Length: > 0 and <= 9 } || !id.All(char.IsAsciiDigit)) return null;
        var page = await http.GetBytesAsync($"https://howlongtobeat.com/game/{id}", cancellationToken, 2 * 1024 * 1024, "text/html").ConfigureAwait(false);
        var times = ParsePage(page, id);
        return times is null ? null : new ProviderData(Times: times, Identity: new GameIdentity(HltbId: id));
    }

    /// <summary>Reads the times from the page's embedded data. Anything unexpected gives null.</summary>
    public static BeatTimes? ParsePage(byte[] page, string id)
    {
        try
        {
            if (page.Length == 0 || page.Length > 2 * 1024 * 1024) return null;
            var match = NextData.Match(System.Text.Encoding.UTF8.GetString(page));
            if (!match.Success || match.Groups[1].Length > 1_500_000) return null;
            using var document = JsonDocument.Parse(match.Groups[1].Value, new JsonDocumentOptions { MaxDepth = 32 });
            if (!document.RootElement.TryGetProperty("props", out var props) || !props.TryGetProperty("pageProps", out var pageProps) ||
                !pageProps.TryGetProperty("game", out var game) || !game.TryGetProperty("data", out var data) || !data.TryGetProperty("game", out var games) ||
                games.ValueKind != JsonValueKind.Array || games.GetArrayLength() == 0) return null;
            var entry = games[0];
            int? Seconds(string name) => entry.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n is > 0 and <= BeatTimes.MaxSeconds ? n : null;
            var times = new BeatTimes(Seconds("comp_main"), Seconds("comp_plus"), Seconds("comp_100"), Seconds("comp_all"), $"https://howlongtobeat.com/game/{id}");
            return times.HasAny ? times : null;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    public void Dispose() => http.Dispose();
}
