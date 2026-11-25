using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GameVault.App.Artwork;

// Only a single explicit click supplies a candidate. Page metadata never validates image bytes.
internal sealed class WebCoverSelection
{
    internal const int MaximumPayloadBytes = 16 * 1024;
    private long generation;
    internal Uri? Source { get; private set; }
    /// <summary>The original picture's size when the results page told us, otherwise null.</summary>
    internal (int Width, int Height)? Size { get; private set; }
    internal long Begin() { Source = null; Size = null; return ++generation; }
    internal void Reset() => Begin();
    internal void Select(Uri source) { ++generation; Source = source; Size = null; }
    internal bool IsCurrent(long operation) => operation == generation;
    internal bool Complete(long operation, string payload)
    {
        if (!IsCurrent(operation)) return false;
        Source = Parse(payload);
        Size = Source is null ? null : ParseSize(payload);
        return true;
    }

    internal static Uri? Parse(string? payload)
    {
        if (payload is null || payload.Length > MaximumPayloadBytes || Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes) return null;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.String) return null;
            var address = source.GetString();
            if (address is null || address.Length > 8192) return null;
            if (root.TryGetProperty("link", out var boundedLink) && boundedLink.ValueKind == JsonValueKind.String && boundedLink.GetString()?.Length > 8192) return null;
            if (root.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(link.GetString(), UriKind.Absolute, out var anchor) && anchor.Scheme == "https" &&
                anchor.IsDefaultPort && string.IsNullOrEmpty(anchor.UserInfo) &&
                (anchor.IdnHost is "google.com" or "www.google.com") && anchor.AbsolutePath == "/imgres")
            {
                // Only Google's explicit imgurl identifies an original; never choose another result.
                foreach (var part in anchor.Query.TrimStart('?').Split('&'))
                {
                    var pair = part.Split('=', 2);
                    if (Uri.UnescapeDataString(pair[0]) != "imgurl") continue;
                    address = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace("+", " ")) : null;
                    break;
                }
            }
            return address is { Length: <= 8192 } && Uri.TryCreate(address, UriKind.Absolute, out var uri) ? WebCoverPolicy.ImageUri(uri) : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or UriFormatException) { return null; }
    }

    internal static (int Width, int Height)? ParseSize(string? payload)
    {
        if (payload is null || payload.Length > MaximumPayloadBytes) return null;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("width", out var w) && w.TryGetInt32(out var width) &&
                   root.TryGetProperty("height", out var h) && h.TryGetInt32(out var height) && width is > 0 and <= 100_000 && height is > 0 and <= 100_000
                ? (width, height) : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    internal static (double X, double Y) CssPoint(double x, double y, double zoom)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || !double.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        return (x / zoom, y / zoom);
    }

    internal static string HitTestScript(double x, double y, double zoom)
    {
        var point = CssPoint(x, y, zoom);
        var coordinates = point.X.ToString("R", CultureInfo.InvariantCulture) + "," + point.Y.ToString("R", CultureInfo.InvariantCulture);
        // A result tile: the page's own data names the original picture and its size.
        return "(() => { " + WebCoverResults.LookupScript + "const hit = document.elementFromPoint(" + coordinates + "); " +
            "const tile = hit?.closest('div[data-docid][data-lpage]'); const found = tile ? original(tile.getAttribute('data-docid')) : null; if (found) return found; " +
            "const image = hit?.closest('img'); if (!image) return null; " +
            "const anchor = image.closest('a'); const rawSource = image.currentSrc || image.src || ''; const source = rawSource.length <= 8192 ? rawSource : ''; const link = anchor?.href || ''; " +
            "if (link.length > 8192) return null; const result = {source,link}; " +
            "return new TextEncoder().encode(JSON.stringify(result)).length <= 16384 ? result : null; })()";
    }
}
