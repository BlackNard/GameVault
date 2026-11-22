using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Description, release date, developers, publishers and genres for Steam games from the public Steam store API (no key, no account).
/// A Steam install is looked up by its numeric app id; any other game by an exact-title search of the Steam store. Requests have a timeout, a 1 MB response limit and a minimum gap between them.
/// </summary>
public sealed class SteamStoreProvider : IMatchSearchProvider, IDisposable
{
    public const string ProviderId = "steam";
    private const int MaxResponseBytes = 1024 * 1024;
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly TimeSpan minimumGap;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime lastRequest = DateTime.MinValue;

    public SteamStoreProvider(HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null)
    {
        ownsClient = true;
        client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true }) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameVault", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        this.minimumGap = minimumGap ?? TimeSpan.FromMilliseconds(1500);
    }

    public string Id => ProviderId;
    public string DisplayName => "Steam store";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField>
        { MetadataField.Description, MetadataField.ReleaseDate, MetadataField.Developers, MetadataField.Publishers, MetadataField.Genres, MetadataField.Hero };

    public async Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        var appId = request.SteamAppId;
        if (!ValidId(appId))
        {
            // Not known to be a Steam install: look the game up by its exact title, like a name-based provider in Playnite (the title is sent to the Steam store).
            appId = await FindAppIdAsync(request.Name, cancellationToken, BuildYear(request.ExecutablePath)).ConfigureAwait(false);
            if (appId is null) return null;
        }
        var json = await GetBytesAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", cancellationToken).ConfigureAwait(false);
        var data = Parse(json, appId!);
        // Steam's wide library banner for this app (a fixed address built from the id).
        return data is null ? null : data with { SteamAppId = appId, HeroUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg" };
    }

    private static bool ValidId(string? id) => id is { Length: > 0 and <= 10 } && id.All(char.IsAsciiDigit);

    /// <summary>Normalised title for matching: no trademark signs, case, spaces or punctuation.</summary>
    public static string NormalizeTitle(string title) => new(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Searches the Steam store and accepts only an app whose title equals the game title (ignoring case, punctuation and trademark signs).
    /// When Steam also lists the same title with a year ("Resident Evil 2" and "Resident Evil 2 (1998)"), the build year of the game file
    /// (<paramref name="exeYear"/>) picks the edition: the newest one released no later than that year. Without a usable year the plain title is used.
    /// Two apps with exactly the same title are never guessed.
    /// </summary>
    public async Task<string?> FindAppIdAsync(string name, CancellationToken cancellationToken, int? exeYear = null)
    {
        var title = (name ?? "").Trim();
        if (title.Length is < 2 or > 120 || title.Any(char.IsControl) || title.Contains('\\') || title.Contains('/') || title.Contains("://")) return null;
        var wanted = NormalizeTitle(title);
        if (wanted.Length < 2) return null;
        var json = await GetBytesAsync("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(title) + "&l=english&cc=US", cancellationToken).ConfigureAwait(false);
        var exact = new List<string>();
        var siblings = new List<(string Id, int Year)>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in items.EnumerateArray().Take(50))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("type", out var type) && !(type.ValueKind == JsonValueKind.String && type.GetString() == "app")) continue;
                if (!item.TryGetProperty("name", out var itemName) || itemName.ValueKind != JsonValueKind.String) continue;
                if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number <= 0 || number > 9_999_999_999) continue;
                var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var normalized = NormalizeTitle(itemName.GetString() ?? "");
                if (normalized == wanted) exact.Add(text);
                else if (normalized.Length == wanted.Length + 4 && normalized.StartsWith(wanted, StringComparison.Ordinal) && int.TryParse(normalized[wanted.Length..], out var year) && year is >= 1970 and <= 2100)
                    siblings.Add((text, year));
            }
        }
        catch (JsonException) { throw new IOException("Steam sent an answer GameVault could not read."); }
        if (exact.Count != 1) return null;
        if (siblings.Count == 0 || exeYear is not { } built) return exact[0];
        // The plain title has no year in its name, so ask Steam when it was released.
        var plainYear = await ReleaseYearAsync(exact[0], cancellationToken).ConfigureAwait(false);
        var choices = siblings.Select(x => (x.Id, Year: (int?)x.Year)).Append((Id: exact[0], Year: plainYear)).Where(x => x.Year is not null).ToList();
        if (choices.Count == 0) return exact[0];
        var fit = choices.Where(x => x.Year <= built + 1).OrderByDescending(x => x.Year).FirstOrDefault();
        return fit.Id ?? exact[0];
    }

    private async Task<int?> ReleaseYearAsync(string appId, CancellationToken token)
    {
        try { return Parse(await GetBytesAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", token).ConfigureAwait(false), appId)?.ReleaseDate?.Year; }
        catch (IOException) { return null; }
    }

    /// <summary>The year the game program was built (from its PE header), or null when the header has no believable date.</summary>
    public static int? BuildYear(string? executablePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return null;
            using var stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            var stamp = pe.PEHeaders.CoffHeader.TimeDateStamp;
            if (stamp == 0) return null;
            var year = DateTimeOffset.FromUnixTimeSeconds(stamp).Year;
            return year >= 1995 && year <= DateTime.UtcNow.Year + 1 ? year : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException) { return null; }
    }

    /// <summary>The Steam apps whose store search matches the title, for the "Not the right game?" picker.</summary>
    public async Task<IReadOnlyList<MatchCandidate>> SearchAsync(string title, CancellationToken cancellationToken)
    {
        title = (title ?? "").Trim();
        if (!PoliteHttp.SafeTitle(title)) return [];
        var json = await GetBytesAsync("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(title) + "&l=english&cc=US", cancellationToken).ConfigureAwait(false);
        var result = new List<MatchCandidate>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in items.EnumerateArray().Take(30))
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "app") continue;
                if (!item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number <= 0 || number > 9_999_999_999) continue;
                var image = item.TryGetProperty("tiny_image", out var img) && img.ValueKind == JsonValueKind.String && img.GetString() is { } u && u.StartsWith("https://", StringComparison.Ordinal) ? u : null;
                result.Add(new("steam", number.ToString(System.Globalization.CultureInfo.InvariantCulture), (name.GetString() ?? "").Trim(), "Steam · app " + number, image));
                if (result.Count == 8) break;
            }
        }
        catch (JsonException) { throw new IOException("Steam sent an answer GameVault could not read."); }
        return result;
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = lastRequest + minimumGap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            lastRequest = DateTime.UtcNow;
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new IOException("Steam asked GameVault to slow down. Try again in a few minutes.");
            if (!response.IsSuccessStatusCode) throw new IOException($"Steam answered with status {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new IOException("Steam sent an unexpectedly large answer.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes) throw new IOException("Steam sent an unexpectedly large answer.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (HttpRequestException error) { throw new IOException("Steam could not be reached. " + error.Message, error); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new IOException("Steam did not answer in time."); }
        finally { gate.Release(); }
    }

    public static ProviderData? Parse(byte[] json, string appId)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(appId, out var entry) || entry.ValueKind != JsonValueKind.Object) return null;
            if (!entry.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) return null;
            if (!entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
            string? Text(string name) => data.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            string[] List(string name) => data.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
                ? [.. p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(s => !string.IsNullOrWhiteSpace(s)).Take(GameMetadataDocument.MaxItems)] : [];
            var description = Clean(Text("short_description") ?? Text("about_the_game"));
            PartialDate? release = null;
            if (data.TryGetProperty("release_date", out var date) && date.ValueKind == JsonValueKind.Object &&
                !(date.TryGetProperty("coming_soon", out var soon) && soon.ValueKind == JsonValueKind.True) &&
                date.TryGetProperty("date", out var text) && text.ValueKind == JsonValueKind.String) release = PartialDate.Parse(text.GetString());
            string[] genres = data.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array
                ? [.. g.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String).Select(e => e.GetProperty("description").GetString()!).Take(GameMetadataDocument.MaxItems)] : [];
            return new ProviderData(description, release, List("developers"), List("publishers"), genres);
        }
        catch (JsonException) { throw new IOException("Steam sent an answer GameVault could not read."); }
    }

    private static string? Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = WebUtility.HtmlDecode(Tags.Replace(html, " "));
        text = string.Join(' ', text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? null : text;
    }

    public void Dispose() { if (ownsClient) client.Dispose(); gate.Dispose(); }
}
