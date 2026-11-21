using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Description, release date, developers, publishers and genres for Steam games from the public Steam store API (no key, no account).
/// Only the numeric app id leaves the PC. Requests have a timeout, a 1 MB response limit and a minimum gap between them.
/// </summary>
public sealed class SteamStoreProvider : IGameMetadataProvider, IDisposable
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
        { MetadataField.Description, MetadataField.ReleaseDate, MetadataField.Developers, MetadataField.Publishers, MetadataField.Genres };

    public async Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        var appId = request.SteamAppId;
        if (appId is null || appId.Length is 0 or > 10 || !appId.All(char.IsAsciiDigit)) return null;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = lastRequest + minimumGap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            lastRequest = DateTime.UtcNow;
            // The address is built from the digits only; no game name, path or user data is sent.
            using var message = new HttpRequestMessage(HttpMethod.Get, $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english");
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
            return Parse(buffer.ToArray(), appId);
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
