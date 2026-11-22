using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// The shared way metadata providers talk to a public web service: one request at a time, a minimum gap between requests, a timeout, a size cap,
/// no cookies and no redirects, and clear messages for the usual failures. Every provider builds its own fixed URLs; nothing here sends user data.
/// </summary>
public sealed class PoliteHttp : IDisposable
{
    public const int DefaultMaxBytes = 1024 * 1024;
    private readonly HttpClient client;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeSpan minimumGap;
    private DateTime lastRequest = DateTime.MinValue;

    public PoliteHttp(string serviceName, HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null)
    {
        ServiceName = serviceName;
        client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true }) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameVault", "1"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(desktop game library)"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        this.minimumGap = minimumGap ?? TimeSpan.FromMilliseconds(1200);
    }

    public string ServiceName { get; }

    public async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken, int maxBytes = DefaultMaxBytes, string? accept = null)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = lastRequest + minimumGap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            lastRequest = DateTime.UtcNow;
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            if (accept is not null) { message.Headers.Accept.Clear(); message.Headers.Accept.ParseAdd(accept); }
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new IOException($"{ServiceName} asked GameVault to slow down. Try again later.");
            if (response.StatusCode == HttpStatusCode.NotFound) throw new FileNotFoundException($"{ServiceName} does not know this game.");
            if ((int)response.StatusCode is >= 300 and < 400) throw new IOException($"{ServiceName} redirected the request, so it was not followed.");
            if (!response.IsSuccessStatusCode) throw new IOException($"{ServiceName} answered with status {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > maxBytes) throw new IOException($"{ServiceName} sent an unexpectedly large answer.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes) throw new IOException($"{ServiceName} sent an unexpectedly large answer.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (HttpRequestException error) { throw new IOException($"{ServiceName} could not be reached. " + error.Message, error); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new IOException($"{ServiceName} did not answer in time."); }
        finally { gate.Release(); }
    }

    public void Dispose() { client.Dispose(); gate.Dispose(); }

    // ---------- text helpers shared by the providers ----------
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Breaks = new(@"<\s*/?\s*(br|p|li|ul|ol|div|tr|h[1-6])\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Plain text from a small piece of store HTML or markdown: tags removed, entities decoded, line breaks kept, spaces collapsed.</summary>
    public static string? PlainText(string? html, int max = GameVault.Core.GameMetadataDocument.MaxTextLength)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = WebUtility.HtmlDecode(Tags.Replace(Breaks.Replace(html, "\n"), ""));
        text = text.Replace("\r", "");
        var lines = text.Split('\n').Select(l => string.Join(' ', l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))).Where(l => l.Length > 0);
        var joined = string.Join("\n", lines).Trim();
        if (joined.Length == 0) return null;
        return joined.Length <= max ? joined : joined[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Title for matching: letters and digits only, lower case, so case, punctuation and trademark signs do not matter.</summary>
    public static string NormalizeTitle(string title) => new((title ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static bool SafeTitle(string? title) => title is { } t && t.Trim().Length is >= 2 and <= 120 && !t.Any(char.IsControl) && !t.Contains('\\') && !t.Contains('/') && !t.Contains("://") && NormalizeTitle(t).Length >= 2;
}
