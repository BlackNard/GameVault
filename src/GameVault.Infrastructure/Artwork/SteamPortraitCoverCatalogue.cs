using System.Globalization;
using System.Net;
using System.Text.Json;
using GameVault.Core;

namespace GameVault.Infrastructure.Artwork;

/// <summary>Explicit anonymous title searches and fixed Steam library capsule URLs; never reads local profiles.</summary>
public sealed class SteamPortraitCoverCatalogue : IPortraitCoverCatalogue, IDisposable
{
    private readonly HttpClient catalogue;
    private readonly HttpClient images;
    public SteamPortraitCoverCatalogue() : this(CreateClient(), CreateClient()) { }
    public SteamPortraitCoverCatalogue(HttpClient catalogue, HttpClient images)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(images);
        if (ReferenceEquals(catalogue, images)) throw new ArgumentException("Catalogue and image clients must be separate.");
        if (HasCredentials(catalogue) || HasCredentials(images)) throw new ArgumentException("Cover clients must be anonymous.");
        this.catalogue = catalogue;
        this.images = images;
    }
    private static bool HasCredentials(HttpClient client) => client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Contains("Cookie");
    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private static Uri SourcePage(int appId) => new($"https://store.steampowered.com/app/{appId.ToString(CultureInfo.InvariantCulture)}/");

    public async Task<IReadOnlyList<PortraitCoverCandidate>> SearchAsync(string title, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (title is null || title.Any(char.IsControl)) throw Query();
        title = title.Trim();
        if (title.Length is < 2 or > 120 || title.Contains('\\') || title.Contains('/') || title.Contains("://") || (title.Length >= 2 && char.IsLetter(title[0]) && title[1] == ':')) throw Query();
        using var search = await JsonAsync(new Uri("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(title) + "&l=english&cc=US"), cancellationToken);
        if (search.RootElement.ValueKind != JsonValueKind.Object || !search.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) throw Invalid();
        var ids = new List<int>();
        foreach (var item in items.EnumerateArray().Take(100))
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int number) || number <= 0 || string.IsNullOrWhiteSpace(Text(item, "name")) || ids.Contains(number)) continue;
            if (item.TryGetProperty("type", out var kind) && (kind.ValueKind != JsonValueKind.String || kind.GetString() != "app")) continue;
            ids.Add(number);
            if (ids.Count == 20) break;
        }
        var result = new List<PortraitCoverCandidate>();
        // Batches bound both outstanding metadata work and total catalogue work, keeping search order stable.
        for (int offset = 0; offset < ids.Count && result.Count < 10; offset += 2)
        {
            var batch = await Task.WhenAll(ids.Skip(offset).Take(2).Select(id => ReadGameAsync(id, cancellationToken)));
            result.AddRange(batch.OfType<PortraitCoverCandidate>().Take(10 - result.Count));
        }
        return result;
    }

    private async Task<PortraitCoverCandidate?> ReadGameAsync(int appId, CancellationToken token)
    {
        using var doc = await JsonAsync(new Uri($"https://store.steampowered.com/api/appdetails?appids={appId.ToString(CultureInfo.InvariantCulture)}&l=english&cc=US"), token);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var entry) || entry.ValueKind != JsonValueKind.Object) throw Invalid();
        if (!entry.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) return null;
        if (!entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) throw Invalid();
        if (Text(data, "type") != "game") return null;
        string name = Text(data, "name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string date = data.TryGetProperty("release_date", out var release) ? Text(release, "date") : "";
        return new(appId, name, string.IsNullOrWhiteSpace(date) ? "Steam · Game" : "Steam · Game · " + date, SourcePage(appId));
    }

    public async Task<byte[]> DownloadAsync(PortraitCoverCandidate candidate, bool preview, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidate is null || candidate.AppId <= 0 || candidate.SourcePage != SourcePage(candidate.AppId)) throw Invalid();
        string prefix = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{candidate.AppId.ToString(CultureInfo.InvariantCulture)}/library_600x900";
        // Steam may serve 300x450 pixels from the base-named asset; use the larger asset before decoded validation.
        var bytes = await SendAsync(images, new Uri(prefix + "_2x.jpg"), 12 * 1024 * 1024, true, true, cancellationToken);
        return bytes ?? (await SendAsync(images, new Uri(prefix + ".jpg"), 12 * 1024 * 1024, true, false, cancellationToken))!;
    }

    private async Task<JsonDocument> JsonAsync(Uri uri, CancellationToken token)
    {
        byte[] bytes = (await SendAsync(catalogue, uri, 1_048_576, false, false, token))!;
        try { return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 }); }
        catch (JsonException) { throw Invalid(); }
    }
    private static string Text(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString() ?? "";
        return new string(text.Take(240).Where(c => !char.IsControl(c)).ToArray());
    }
    private static async Task<byte[]?> SendAsync(HttpClient client, Uri uri, int limit, bool image, bool allowMissing, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (HasCredentials(client)) throw Invalid();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.RequestMessage?.RequestUri is { } actual && actual != uri) throw Invalid();
            if ((int)response.StatusCode is >= 300 and < 400) throw Invalid();
            if ((int)response.StatusCode == 429) throw new ArtworkException(ArtworkFailure.RateLimit, "Steam is limiting cover requests. Wait before retrying, or use Browse.");
            if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw new ArtworkException(ArtworkFailure.Offline, "Steam could not provide this cover or catalogue. Retry later or use Browse.");
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (image ? mediaType is not ("image/jpeg" or "image/png") : mediaType is not "application/json") throw Invalid();
            if (response.Content.Headers.ContentLength > limit) throw Large();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > limit) throw Large();
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new ArtworkException(ArtworkFailure.Timeout, "Steam cover requests timed out. Retry or use Browse."); }
        catch (HttpRequestException) { throw Offline(); }
        catch (IOException) { throw Offline(); }
    }
    private static ArtworkException Query() => new(ArtworkFailure.Query, "Enter a game title (2–120 characters), never a file or folder path.");
    private static ArtworkException Invalid() => new(ArtworkFailure.InvalidResponse, "Steam returned unsupported cover data. Choose another game or use Browse.");
    private static ArtworkException Large() => new(ArtworkFailure.TooLarge, "Steam returned cover data exceeding the safe size limit. Choose another cover or use Browse.");
    private static ArtworkException Offline() => new(ArtworkFailure.Offline, "Steam cover requests are unavailable. Check your connection, retry or use Browse.");
    public void Dispose() { catalogue.Dispose(); images.Dispose(); }
}
