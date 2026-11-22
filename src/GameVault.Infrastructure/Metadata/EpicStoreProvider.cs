using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Description, developer, publisher and release date for Epic Games Store games from Epic's public store page data (no key, no account).
/// Epic offers no usable public search, so this runs only for games installed through Epic (their local manifest names the catalog namespace) and
/// guesses the store page name from the title. A guess is accepted only when the page's namespace equals the installed game's namespace.
/// </summary>
public sealed class EpicStoreProvider : IGameMetadataProvider, IDisposable
{
    public const string ProviderId = "epic";
    private readonly PoliteHttp http;

    public EpicStoreProvider(HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null) =>
        http = new("Epic Games Store", handler, timeout, minimumGap);

    public string Id => ProviderId;
    public string DisplayName => "Epic Games Store";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField>
        { MetadataField.Description, MetadataField.ReleaseDate, MetadataField.Developers, MetadataField.Publishers, MetadataField.Hero };

    public async Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        var identity = request.Identity ?? GameIdentity.Empty;
        if (identity.EpicNamespace is null && identity.EpicSlug is null) return null;
        foreach (var slug in identity.EpicSlug is { } known ? [known] : Slugs(request.Name))
        {
            byte[] json;
            try { json = await http.GetBytesAsync("https://store-content.ak.epicgames.com/api/en-US/content/products/" + slug, cancellationToken).ConfigureAwait(false); }
            catch (FileNotFoundException) { continue; }
            var data = Parse(json, identity.EpicNamespace, request.Name, slug);
            if (data is not null) return data;
        }
        return null;
    }

    /// <summary>Store page names to try: the kebab-case title, then the title without its last words (editions such as "Legacy" or "Enhanced").</summary>
    public static IEnumerable<string> Slugs(string title)
    {
        var words = Regex.Matches((title ?? "").ToLowerInvariant().Replace("&", " and "), "[a-z0-9]+").Select(m => m.Value).ToArray();
        for (var count = words.Length; count >= Math.Min(words.Length, Math.Max(2, words.Length - 2)) && count >= 1; count--)
        {
            var slug = string.Join('-', words.Take(count));
            if (slug.Length is >= 2 and <= 100) yield return slug;
        }
    }

    public static ProviderData? Parse(byte[] json, string? expectedNamespace, string title, string slug)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var pageNamespace = Text(root, "namespace");
            if (expectedNamespace is not null ? !string.Equals(pageNamespace, expectedNamespace, StringComparison.OrdinalIgnoreCase) : !string.Equals(PoliteHttp.NormalizeTitle(Text(root, "productName") ?? ""), PoliteHttp.NormalizeTitle(title), StringComparison.Ordinal)) return null;
            if (!root.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array) return null;
            JsonElement page = default;
            foreach (var candidate in pages.EnumerateArray()) { page = candidate; break; }
            if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
            string? description = null;
            string[]? developers = null, publishers = null;
            string? hero = null;
            if (data.TryGetProperty("about", out var about) && about.ValueKind == JsonValueKind.Object)
            {
                if (about.TryGetProperty("image", out var image) && Text(image, "src") is { } source) hero = source;
                description = Markdown(Text(about, "description")) ?? Markdown(Text(about, "shortDescription"));
                if (Text(about, "developerAttribution") is { Length: > 0 } dev) developers = [dev.Trim()];
                if (Text(about, "publisherAttribution") is { Length: > 0 } pub) publishers = [pub.Trim()];
            }
            PartialDate? date = null;
            if (data.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object && Text(meta, "releaseDate") is { Length: >= 10 } release)
                date = PartialDate.Parse(release[..10]);
            if (description is null && developers is null && publishers is null && date is null && hero is null) return null;
            return new ProviderData(description, date, developers, publishers, Identity: new GameIdentity(EpicSlug: slug, EpicNamespace: pageNamespace is { Length: <= 64 } ns && ns.All(char.IsLetterOrDigit) ? ns : null), HeroUrl: hero);
        }
        catch (JsonException) { throw new IOException("Epic sent an answer GameVault could not read."); }
    }

    private static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static readonly Regex MarkdownMarks = new(@"(^|\n)\s{0,3}#{1,6}\s*|\*\*|__|\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);

    /// <summary>Plain text from the store's markdown: headings, bold and links lose their marks.</summary>
    internal static string? Markdown(string? text) => PoliteHttp.PlainText(string.IsNullOrWhiteSpace(text) ? null : MarkdownMarks.Replace(text, m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value));

    public void Dispose() => http.Dispose();
}
