using System.Globalization;
using System.Text.Json;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Release date, developers, publishers, genres and a one-line description from Wikidata (CC0, no key), and the ids that link a game to the other
/// services (Steam, GOG, HowLongToBeat). It is the bridge for games that are not store installs: found by a known id, or by an exact title that
/// is a video game and unique. Two games with the same title (Dead Space 2008 and 2023) are never guessed; the owner picks one.
/// Only the normal Wikidata API is used (not the query service, which is heavily rate limited).
/// </summary>
public sealed class WikidataProvider : IMatchSearchProvider, IDisposable
{
    public const string ProviderId = "wikidata";
    private const string Api = "https://www.wikidata.org/w/api.php";
    private const int EntityLimit = 2 * 1024 * 1024;
    private readonly PoliteHttp http;
    private readonly object cacheGate = new();
    private readonly Dictionary<string, (DateTime At, ProviderData? Data)> cache = [];

    public WikidataProvider(HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null) =>
        http = new("Wikidata", handler, timeout ?? TimeSpan.FromSeconds(20), minimumGap ?? TimeSpan.FromMilliseconds(800));

    public string Id => ProviderId;
    public string DisplayName => "Wikidata";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField>
        { MetadataField.Description, MetadataField.ReleaseDate, MetadataField.Developers, MetadataField.Publishers, MetadataField.Genres };

    public async Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        // The same game is asked about by several providers in one run (HowLongToBeat needs the id Wikidata knows), so recent answers are reused.
        var identity0 = request.Identity ?? GameIdentity.Empty;
        var key = $"{identity0.WikidataId}|{request.SteamAppId}|{identity0.GogId}|{identity0.NoMatch}|{PoliteHttp.NormalizeTitle(request.Name)}";
        lock (cacheGate) if (cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10)) return hit.Data;
        var data = await LookUpAsync(request, cancellationToken).ConfigureAwait(false);
        lock (cacheGate) cache[key] = (DateTime.UtcNow, data);
        return data;
    }

    private async Task<ProviderData?> LookUpAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        var identity = request.Identity ?? GameIdentity.Empty;
        var item = identity.WikidataId;
        if (item is null && request.SteamAppId is { Length: > 0 } steam && steam.All(char.IsAsciiDigit)) item = await ItemByStatementAsync("P1733", steam, cancellationToken).ConfigureAwait(false);
        if (item is null && identity.GogId is { Length: > 0 } gog && gog.All(char.IsAsciiDigit)) item = await ItemByStatementAsync("P2725", gog, cancellationToken).ConfigureAwait(false);
        if (item is null && !identity.NoMatch && PoliteHttp.SafeTitle(request.Name)) item = await ItemByExactTitleAsync(request.Name, cancellationToken).ConfigureAwait(false);
        if (!IsItemId(item)) return null;
        var json = await http.GetBytesAsync($"{Api}?action=wbgetentities&format=json&props=claims%7Cdescriptions&languages=en&ids={item}", cancellationToken, EntityLimit).ConfigureAwait(false);
        var entity = ParseEntity(json, item!);
        if (entity is null) return null;
        var ids = entity.Developers.Concat(entity.Publishers).Concat(entity.Genres).Distinct().Take(40).ToArray();
        var labels = new Dictionary<string, string>();
        if (ids.Length > 0)
        {
            var labelJson = await http.GetBytesAsync($"{Api}?action=wbgetentities&format=json&props=labels&languages=en&ids={string.Join("%7C", ids)}", cancellationToken, EntityLimit).ConfigureAwait(false);
            labels = ParseLabels(labelJson);
        }
        string[]? Names(string[] list) => list.Select(i => labels.GetValueOrDefault(i)).OfType<string>().Distinct().Take(GameMetadataDocument.MaxItems).ToArray() is { Length: > 0 } names ? names : null;
        return new ProviderData(PoliteHttp.PlainText(entity.Description, 300), entity.Date, Names(entity.Developers), Names(entity.Publishers), Names(entity.Genres),
            SteamAppId: entity.SteamId, Identity: new GameIdentity(GogId: entity.GogId, WikidataId: item, HltbId: entity.HltbId));
    }

    private static bool IsItemId(string? id) => id is { Length: > 1 and <= 14 } && id[0] == 'Q' && id[1..].All(char.IsAsciiDigit);

    /// <summary>The one item that states property = value (for example a Steam app id). Zero or several items give nothing.</summary>
    private async Task<string?> ItemByStatementAsync(string property, string value, CancellationToken token)
    {
        var json = await http.GetBytesAsync($"{Api}?action=query&list=search&format=json&srlimit=3&srsearch=haswbstatement%3A{property}%3D{Uri.EscapeDataString(value)}", token).ConfigureAwait(false);
        var items = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.TryGetProperty("query", out var query) && query.TryGetProperty("search", out var hits) && hits.ValueKind == JsonValueKind.Array)
                foreach (var hit in hits.EnumerateArray().Take(3)) if (Str(hit, "title") is { } title && IsItemId(title)) items.Add(title);
        }
        catch (JsonException) { throw new IOException("Wikidata sent an answer GameVault could not read."); }
        return items.Count == 1 ? items[0] : null;
    }

    private async Task<string?> ItemByExactTitleAsync(string title, CancellationToken token)
    {
        foreach (var term in GameTitles.Fallbacks(title).Prepend(title).Take(3))
        {
            var games = (await SearchItemsAsync(term, token).ConfigureAwait(false)).Where(c => LooksLikeGame(c.Detail));
            var (match, _, ambiguous) = GameTitles.Best(title, games, c => c.Title);
            if (ambiguous) return null;
            if (match is not null) return match.Id;
        }
        return null;
    }

    /// <summary>A Wikidata description such as "2023 survival horror video game remake" names a game; "video game series" or "film" do not.</summary>
    public static bool LooksLikeGame(string description)
    {
        var text = (description ?? "").ToLowerInvariant();
        if (!text.Contains("game")) return false;
        return !new[] { "series", "franchise", "film", "soundtrack", "expansion", "dlc", "board game", "card game", "tabletop", "television", "anime", "novel", "book", "comic" }.Any(text.Contains);
    }

    private async Task<List<MatchCandidate>> SearchItemsAsync(string title, CancellationToken token)
    {
        var json = await http.GetBytesAsync($"{Api}?action=wbsearchentities&language=en&uselang=en&type=item&limit=10&format=json&search={Uri.EscapeDataString(title.Trim())}", token).ConfigureAwait(false);
        var result = new List<MatchCandidate>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (!document.RootElement.TryGetProperty("search", out var items) || items.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in items.EnumerateArray().Take(10))
            {
                var id = Str(item, "id");
                var label = Str(item, "label");
                if (!IsItemId(id) || label is null) continue;
                result.Add(new(ProviderId, id!, label, Str(item, "description") ?? ""));
            }
        }
        catch (JsonException) { throw new IOException("Wikidata sent an answer GameVault could not read."); }
        return result;
    }

    public async Task<IReadOnlyList<MatchCandidate>> SearchAsync(string title, CancellationToken cancellationToken)
    {
        if (!PoliteHttp.SafeTitle(title)) return [];
        return [.. (await SearchItemsAsync(title, cancellationToken).ConfigureAwait(false)).Where(c => LooksLikeGame(c.Detail)).Take(8)
            .Select(c => new MatchCandidate(ProviderId, c.Id, c.Title, "Wikidata · " + c.Detail))];
    }

    internal sealed record Entity(string? Description, PartialDate? Date, string[] Developers, string[] Publishers, string[] Genres, string? SteamId, string? GogId, string? HltbId);

    internal static Entity? ParseEntity(byte[] json, string item)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (!document.RootElement.TryGetProperty("entities", out var entities) || !entities.TryGetProperty(item, out var entity) || entity.ValueKind != JsonValueKind.Object) return null;
            string? description = null;
            if (entity.TryGetProperty("descriptions", out var descriptions) && descriptions.TryGetProperty("en", out var en)) description = Str(en, "value");
            if (!entity.TryGetProperty("claims", out var claims) || claims.ValueKind != JsonValueKind.Object) return new(description, null, [], [], [], null, null, null);
            return new(description, DateOf(claims), EntityIds(claims, "P178"), EntityIds(claims, "P123"), EntityIds(claims, "P136"), Digits(StringValue(claims, "P1733")), Digits(StringValue(claims, "P2725")), Digits(StringValue(claims, "P2816")));
        }
        catch (JsonException) { throw new IOException("Wikidata sent an answer GameVault could not read."); }
    }

    internal static Dictionary<string, string> ParseLabels(byte[] json)
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (!document.RootElement.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Object) return result;
            foreach (var entity in entities.EnumerateObject().Take(60))
                if (entity.Value.TryGetProperty("labels", out var labels) && labels.TryGetProperty("en", out var en) && Str(en, "value") is { Length: > 0 and <= 80 } label) result[entity.Name] = label;
        }
        catch (JsonException) { throw new IOException("Wikidata sent an answer GameVault could not read."); }
        return result;
    }

    /// <summary>The earliest release date among the statements, with the precision Wikidata recorded (year, month or day).</summary>
    private static PartialDate? DateOf(JsonElement claims)
    {
        if (!claims.TryGetProperty("P577", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        PartialDate? best = null;
        foreach (var claim in list.EnumerateArray().Take(12))
        {
            if (!claim.TryGetProperty("mainsnak", out var snak) || !snak.TryGetProperty("datavalue", out var data) || !data.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;
            var time = Str(value, "time");
            var precision = value.TryGetProperty("precision", out var p) && p.TryGetInt32(out var pr) ? pr : 11;
            if (time is null || !DateTime.TryParse(time.TrimStart('+'), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) continue;
            var date = precision >= 11 ? new PartialDate(parsed.Year, parsed.Month, parsed.Day) : precision == 10 ? new PartialDate(parsed.Year, parsed.Month) : new PartialDate(parsed.Year);
            if (!PartialDate.IsValid(date)) continue;
            if (best is null || Sortable(date) < Sortable(best)) best = date;
        }
        return best;
        static int Sortable(PartialDate d) => d.Year * 10000 + (d.Month ?? 12) * 100 + (d.Day ?? 31);
    }

    private static string[] EntityIds(JsonElement claims, string property)
    {
        if (!claims.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array) return [];
        var ids = new List<string>();
        foreach (var claim in list.EnumerateArray().Take(12))
            if (claim.TryGetProperty("mainsnak", out var snak) && snak.TryGetProperty("datavalue", out var data) && data.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object && Str(value, "id") is { } id && IsItemId(id)) ids.Add(id);
        return [.. ids];
    }

    private static string? StringValue(JsonElement claims, string property)
    {
        if (!claims.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array) return null;
        foreach (var claim in list.EnumerateArray().Take(4))
            if (claim.TryGetProperty("mainsnak", out var snak) && snak.TryGetProperty("datavalue", out var data) && data.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
        return null;
    }

    private static string? Digits(string? value) => value is { Length: > 0 and <= 12 } && value.All(char.IsAsciiDigit) ? value : null;

    private static string? Str(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public void Dispose() => http.Dispose();
}
