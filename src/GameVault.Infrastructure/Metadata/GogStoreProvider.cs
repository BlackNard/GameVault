using System.Text.Json;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Description, release date, developers, publishers and genres for GOG games from GOG's public catalog and product API (no key, no account).
/// A game installed through GOG Galaxy is read by its GOG id; any other game is found by an exact title (one match only).
/// </summary>
public sealed class GogStoreProvider : IMatchSearchProvider, IDisposable
{
    public const string ProviderId = "gog";
    private readonly PoliteHttp http;

    public GogStoreProvider(HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null) =>
        http = new("GOG", handler, timeout, minimumGap);

    public string Id => ProviderId;
    public string DisplayName => "GOG";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField>
        { MetadataField.Description, MetadataField.ReleaseDate, MetadataField.Developers, MetadataField.Publishers, MetadataField.Genres };

    private sealed record CatalogItem(string Id, string Title, string? Year, PartialDate? Date, string[] Developers, string[] Publishers, string[] Genres, string? Cover);

    public async Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        var identity = request.Identity ?? GameIdentity.Empty;
        var gogId = identity.GogId;
        CatalogItem? item = null;
        if (gogId is null)
        {
            if (identity.NoMatch || !PoliteHttp.SafeTitle(request.Name)) return null;
            var exact = (await CatalogAsync(request.Name, cancellationToken).ConfigureAwait(false)).Where(c => PoliteHttp.NormalizeTitle(c.Title) == PoliteHttp.NormalizeTitle(request.Name)).ToArray();
            if (exact.Length != 1) return null;
            item = exact[0];
            gogId = item.Id;
        }
        if (gogId.Length > 16 || !gogId.All(char.IsAsciiDigit)) return null;
        string? description = null;
        string[]? developers = item?.Developers, publishers = item?.Publishers;
        PartialDate? date = item?.Date;
        try
        {
            var json = await http.GetBytesAsync($"https://api.gog.com/v2/games/{gogId}?locale=en-US", cancellationToken).ConfigureAwait(false);
            var details = ParseProduct(json);
            description = details.Description;
            if (developers is not { Length: > 0 }) developers = details.Developers;
            if (publishers is not { Length: > 0 }) publishers = details.Publishers;
            date ??= details.Date;
        }
        catch (FileNotFoundException) when (item is not null) { }
        return new ProviderData(description, date, Some(developers), Some(publishers), Some(item?.Genres), Identity: new GameIdentity(GogId: gogId));
    }

    private static string[]? Some(string[]? values) => values is { Length: > 0 } ? values : null;

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

    private static IReadOnlyList<CatalogItem> ParseCatalog(byte[] json)
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
                result.Add(new(id, title, date?.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), date, Names(product, "developers"), Names(product, "publishers"), Names(product, "genres"), Text(product, "coverVertical")));
            }
        }
        catch (JsonException) { throw new IOException("GOG sent an answer GameVault could not read."); }
        return result;
    }

    internal sealed record Product(string? Description, string[] Developers, string[] Publishers, PartialDate? Date);

    internal static Product ParseProduct(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 20 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(null, [], [], null);
            var description = PoliteHttp.PlainText(Text(root, "overview") ?? Text(root, "description"));
            string[] developers = [], publishers = [];
            PartialDate? date = null;
            if (root.TryGetProperty("_embedded", out var embedded) && embedded.ValueKind == JsonValueKind.Object)
            {
                developers = Names(embedded, "developers");
                publishers = Names(embedded, "publishers");
                if (publishers.Length == 0 && embedded.TryGetProperty("publisher", out var publisher) && Text(publisher, "name") is { } single) publishers = [single];
                if (embedded.TryGetProperty("product", out var product) && Text(product, "gogReleaseDate") is { Length: >= 10 } gogDate) date = PartialDate.Parse(gogDate[..10]);
            }
            return new(description, developers, publishers, date);
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
