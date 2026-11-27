using System.Text.Json;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Facts for GOG games from GOG's public catalog and product API (no key, no account): description, release date, developers, publishers, genres,
/// tags, features, platforms, languages, age rating, links, screenshots and the background image. A game installed through GOG Galaxy is read by its
/// GOG id; any other game is found by an exact title (one match only).
/// </summary>
public sealed class GogStoreProvider : IMatchSearchProvider, IDisposable
{
    public const string ProviderId = "gog";
    private const string ScreenshotFormat = "product_card_screenshot_748";
    private readonly PoliteHttp http;

    public GogStoreProvider(HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null) =>
        http = new("GOG", handler, timeout, minimumGap);

    public string Id => ProviderId;
    public string DisplayName => "GOG";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField>
    {
        MetadataField.Description, MetadataField.ReleaseDate, MetadataField.Developers, MetadataField.Publishers, MetadataField.Genres, MetadataField.Hero,
        MetadataField.About, MetadataField.Tags, MetadataField.Features, MetadataField.Platforms, MetadataField.Languages, MetadataField.AgeRating, MetadataField.Links, MetadataField.Screenshots
    };

    internal sealed record CatalogItem(string Id, string Title, string? Year, PartialDate? Date, string[] Developers, string[] Publishers, string[] Genres, string? Cover, string? Background,
        string[] Tags, string[] Features, string[] Platforms, string? Age, string? StoreLink);

    public async Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        var identity = request.Identity ?? GameIdentity.Empty;
        var gogId = identity.GogId;
        CatalogItem? item = null;
        if (gogId is null)
        {
            if (identity.NoMatch || !PoliteHttp.SafeTitle(request.Name)) return null;
            foreach (var term in GameTitles.Fallbacks(request.Name).Prepend(request.Name).Take(3))
            {
                var (match, _, ambiguous) = GameTitles.Best(request.Name, await CatalogAsync(term, cancellationToken).ConfigureAwait(false), c => c.Title);
                if (ambiguous) return null;
                if (match is null) continue;
                item = match;
                break;
            }
            if (item is null) return null;
            gogId = item.Id;
        }
        if (gogId.Length > 16 || !gogId.All(char.IsAsciiDigit)) return null;
        Product? product = null;
        try { product = ParseProduct(await http.GetBytesAsync($"https://api.gog.com/v2/games/{gogId}?locale=en-US", cancellationToken).ConfigureAwait(false)); }
        catch (FileNotFoundException) when (item is not null) { }
        var links = new List<string>();
        var store = item?.StoreLink ?? product?.StoreLink;
        if (GameMetadataDocument.SafeLink(store)) links.Add("GOG store page|" + store);
        foreach (var link in product?.Links ?? []) if (!links.Contains(link, StringComparer.OrdinalIgnoreCase)) links.Add(link);
        return new ProviderData(product?.Description, item?.Date ?? product?.Date, Some(Prefer(item?.Developers, product?.Developers)), Some(Prefer(item?.Publishers, product?.Publishers)), Some(item?.Genres),
            Identity: new GameIdentity(GogId: gogId), HeroUrl: item?.Background,
            About: product?.About, Tags: Some(item?.Tags), Features: Some(Prefer(item?.Features, product?.Features)), Platforms: Some(Prefer(item?.Platforms, product?.Platforms)),
            Languages: Some(product?.Languages), AgeRating: item?.Age, Links: links.Count > 0 ? [.. links] : null, Screenshots: Some(product?.Screenshots));
    }

    private static string[]? Some(string[]? values) => values is { Length: > 0 } ? values : null;
    private static string[]? Prefer(string[]? first, string[]? second) => first is { Length: > 0 } ? first : second;

    public async Task<IReadOnlyList<MatchCandidate>> SearchAsync(string title, CancellationToken cancellationToken)
    {
        if (!PoliteHttp.SafeTitle(title)) return [];
        return [.. (await CatalogAsync(title, cancellationToken).ConfigureAwait(false)).Take(8)
            .Select(c => new MatchCandidate(ProviderId, c.Id, c.Title, "GOG" + (c.Year is null ? "" : " · " + c.Year), c.Cover))];
    }

    private async Task<IReadOnlyList<CatalogItem>> CatalogAsync(string title, CancellationToken token)
    {
        var url = "https://catalog.gog.com/v1/catalog?limit=20&productType=in%3Agame%2Cpack&query=like%3A" + Uri.EscapeDataString(title.Trim());
        return ParseCatalog(await http.GetBytesAsync(url, token).ConfigureAwait(false));
    }

    internal static IReadOnlyList<CatalogItem> ParseCatalog(byte[] json)
    {
        var result = new List<CatalogItem>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array) return result;
            foreach (var product in products.EnumerateArray().Take(40))
            {
                var id = Text(product, "id");
                var title = Text(product, "title");
                if (id is null || title is null || id.Length > 16 || !id.All(char.IsAsciiDigit)) continue;
                var release = Text(product, "releaseDate")?.Replace('.', '-');
                var date = PartialDate.Parse(release);
                string? age = null;
                if (product.TryGetProperty("ratings", out var ratings) && ratings.ValueKind == JsonValueKind.Array)
                    foreach (var rating in ratings.EnumerateArray().Take(6))
                        if (Text(rating, "name") is { } name && name.Contains("pegi", StringComparison.OrdinalIgnoreCase) && Text(rating, "ageRating") is { Length: > 0 and <= 4 } years && years.All(char.IsAsciiDigit)) { age = "PEGI " + years; break; }
                var systems = product.TryGetProperty("operatingSystems", out var os) && os.ValueKind == JsonValueKind.Array
                    ? os.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => PlatformName(e.GetString())).OfType<string>().Distinct().ToArray() : [];
                result.Add(new(id, title, date?.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), date, Names(product, "developers"), Names(product, "publishers"), Names(product, "genres"),
                    Text(product, "coverVertical"), Text(product, "galaxyBackgroundImage"), Names(product, "tags"), Names(product, "features"), systems, age, Text(product, "storeLink")));
            }
        }
        catch (JsonException) { throw new IOException("GOG sent an answer GameVault could not read."); }
        return result;
    }

    private static string? PlatformName(string? name) => name?.ToLowerInvariant() switch { "windows" => "Windows", "osx" or "mac" => "macOS", "linux" => "Linux", _ => null };

    internal sealed record Product(string? Description, string? About, string[] Developers, string[] Publishers, PartialDate? Date, string[] Languages, string[] Features, string[] Platforms, string[] Screenshots, string[] Links, string? StoreLink);

    internal static Product ParseProduct(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(null, null, [], [], null, [], [], [], [], [], null);
            var overview = PoliteHttp.PlainText(Text(root, "overview"));
            var full = PoliteHttp.PlainText(Text(root, "description"), GameMetadataDocument.MaxAboutLength);
            string[] developers = [], publishers = [], languages = [], features = [], platforms = [], screenshots = [], links = [];
            string? storeLink = null;
            PartialDate? date = null;
            if (root.TryGetProperty("_embedded", out var embedded) && embedded.ValueKind == JsonValueKind.Object)
            {
                developers = Names(embedded, "developers");
                publishers = Names(embedded, "publishers");
                if (publishers.Length == 0 && embedded.TryGetProperty("publisher", out var publisher) && Text(publisher, "name") is { } single) publishers = [single];
                if (embedded.TryGetProperty("product", out var product) && Text(product, "gogReleaseDate") is { Length: >= 10 } gogDate) date = PartialDate.Parse(gogDate[..10]);
                features = Names(embedded, "features");
                if (embedded.TryGetProperty("localizations", out var localizations) && localizations.ValueKind == JsonValueKind.Array)
                    languages = [.. localizations.EnumerateArray().Take(80).Select(l => l.TryGetProperty("_embedded", out var e) && e.TryGetProperty("language", out var lang) ? Text(lang, "name") : null).OfType<string>().Distinct().Take(GameMetadataDocument.MaxItems)];
                if (embedded.TryGetProperty("supportedOperatingSystems", out var systems) && systems.ValueKind == JsonValueKind.Array)
                    platforms = [.. systems.EnumerateArray().Take(6).Select(s => s.TryGetProperty("operatingSystem", out var o) ? PlatformName(Text(o, "name")) : null).OfType<string>().Distinct()];
                if (embedded.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
                    screenshots = [.. shots.EnumerateArray().Take(GameMetadataDocument.MaxScreenshots).Select(s => s.TryGetProperty("_links", out var l) && l.TryGetProperty("self", out var self) ? Text(self, "href") : null)
                        .OfType<string>().Select(h => h.Replace("{formatter}", ScreenshotFormat)).Where(HeroImages.IsAllowedUrl)];
            }
            if (root.TryGetProperty("_links", out var linkSet) && linkSet.ValueKind == JsonValueKind.Object)
            {
                var found = new List<string>();
                foreach (var (key, label) in new[] { ("store", "GOG store page"), ("support", "Support"), ("forum", "Forum") })
                    if (linkSet.TryGetProperty(key, out var link) && Text(link, "href") is { } href && GameMetadataDocument.SafeLink(href)) found.Add($"{label}|{href}");
                links = [.. found];
                storeLink = found.FirstOrDefault(l => l.StartsWith("GOG store page|", StringComparison.Ordinal))?[15..];
            }
            return new(overview, full is not null && full != overview ? full : null, developers, publishers, date, languages, features, platforms, screenshots, links.Where(l => !l.StartsWith("GOG store page|", StringComparison.Ordinal)).ToArray(), storeLink);
        }
        catch (JsonException) { throw new IOException("GOG sent an answer GameVault could not read."); }
    }

    private static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string[] Names(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array) return [];
        return [.. list.EnumerateArray().Take(GameMetadataDocument.MaxItems).Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : Text(e, "name")).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())];
    }

    public void Dispose() => http.Dispose();
}
