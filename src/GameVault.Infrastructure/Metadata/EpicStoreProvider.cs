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
        { MetadataField.Description, MetadataField.ReleaseDate, MetadataField.Developers, MetadataField.Publishers, MetadataField.Hero,
          MetadataField.About, MetadataField.Platforms, MetadataField.AgeRating, MetadataField.Requirements, MetadataField.Links };

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
            string? hero = null, aboutText = null, age = null, requirements = null;
            string[]? platforms = null;
            if (data.TryGetProperty("about", out var about) && about.ValueKind == JsonValueKind.Object)
            {
                if (about.TryGetProperty("image", out var image) && Text(image, "src") is { } source) hero = source;
                var full = Markdown(Text(about, "description"), GameMetadataDocument.MaxAboutLength);
                description = Markdown(Text(about, "shortDescription")) ?? (full is null ? null : full.Length <= GameMetadataDocument.MaxTextLength ? full : full[..(GameMetadataDocument.MaxTextLength - 1)].TrimEnd() + "…");
                aboutText = full;
                if (Text(about, "developerAttribution") is { Length: > 0 } dev) developers = [dev.Trim()];
                if (Text(about, "publisherAttribution") is { Length: > 0 } pub) publishers = [pub.Trim()];
            }
            PartialDate? date = null;
            if (data.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object && Text(meta, "releaseDate") is { Length: >= 10 } release)
                date = PartialDate.Parse(release[..10]);
            if (data.TryGetProperty("meta", out var meta2) && meta2.ValueKind == JsonValueKind.Object && meta2.TryGetProperty("platform", out var platformList) && platformList.ValueKind == JsonValueKind.Array)
                platforms = [.. platformList.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(e => e.Length is > 0 and <= 40).Distinct().Take(6)];
            requirements = RequirementsText(data);
            age = AgeText(root);
            if (description is null && developers is null && publishers is null && date is null && hero is null) return null;
            return new ProviderData(description, date, developers, publishers, Identity: new GameIdentity(EpicSlug: slug, EpicNamespace: pageNamespace is { Length: <= 64 } ns && ns.All(char.IsLetterOrDigit) ? ns : null), HeroUrl: hero,
                About: aboutText, Platforms: platforms is { Length: > 0 } ? platforms : null, AgeRating: age, Requirements: requirements, Links: [$"Epic Games Store page|https://store.epicgames.com/p/{slug}"]);
        }
        catch (JsonException) { throw new IOException("Epic sent an answer GameVault could not read."); }
    }

    private static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static readonly Regex MarkdownMarks = new(@"(^|\n)\s{0,3}#{1,6}\s*|\*\*|__|\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);

    /// <summary>Plain text from the store's markdown: headings, bold and links lose their marks.</summary>
    /// <summary>"Minimum" and "Recommended" lines for Windows from the store's requirements table.</summary>
    internal static string? RequirementsText(JsonElement data)
    {
        if (!data.TryGetProperty("requirements", out var requirements) || requirements.ValueKind != JsonValueKind.Object || !requirements.TryGetProperty("systems", out var systems) || systems.ValueKind != JsonValueKind.Array) return null;
        foreach (var system in systems.EnumerateArray().Take(4))
        {
            if (!string.Equals(Text(system, "systemType"), "Windows", StringComparison.OrdinalIgnoreCase) || !system.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array) continue;
            var minimum = new List<string>();
            var recommended = new List<string>();
            foreach (var detail in details.EnumerateArray().Take(20))
            {
                var title = Text(detail, "title")?.Trim();
                if (string.IsNullOrEmpty(title)) continue;
                if (PoliteHttp.PlainText(Text(detail, "minimum"), 200) is { } min) minimum.Add($"{title}: {min.Replace("\n", " ")}");
                if (PoliteHttp.PlainText(Text(detail, "recommended"), 200) is { } rec) recommended.Add($"{title}: {rec.Replace("\n", " ")}");
            }
            var parts = new List<string>();
            if (minimum.Count > 0) parts.Add("Minimum\n" + string.Join("\n", minimum));
            if (recommended.Count > 0) parts.Add("Recommended\n" + string.Join("\n", recommended));
            var text = string.Join("\n\n", parts);
            return text.Length == 0 ? null : text.Length <= GameMetadataDocument.MaxTextLength ? text : text[..(GameMetadataDocument.MaxTextLength - 1)] + "…";
        }
        return null;
    }

    /// <summary>The age rating for the US (ESRB) from the store's ratings, such as "ESRB MATURE 17+".</summary>
    internal static string? AgeText(JsonElement root)
    {
        if (!root.TryGetProperty("productRatings", out var block) || block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("ratings", out var ratings) || ratings.ValueKind != JsonValueKind.Array) return null;
        foreach (var rating in ratings.EnumerateArray().Take(12))
            if (Text(rating, "countryCodes") is { } countries && countries.Split(',').Contains("US") && Text(rating, "title") is { Length: > 0 and <= 60 } title) return "ESRB " + title.Trim();
        return null;
    }

    internal static string? Markdown(string? text, int max = GameMetadataDocument.MaxTextLength) => PoliteHttp.PlainText(string.IsNullOrWhiteSpace(text) ? null : MarkdownMarks.Replace(text, m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value), max);

    public void Dispose() => http.Dispose();
}
