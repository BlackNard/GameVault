using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace GameVault.App.Artwork;

internal static class WebCoverPolicy
{
    internal static Uri SearchUri(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 240 || title.Any(char.IsControl) ||
            title.Contains('/') || title.Contains('\\') || title.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("Enter a game title, without a file path or web address.");
        var query = title.Trim();
        if (!Regex.IsMatch(query, @"\b(?:covers?|box[\s-]?art)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            query += " game cover";
        // Google's public advanced-search filters are hints; decoded pixel validation remains mandatory.
        return new Uri("https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(query) +
                       "&imgsz=l&imgar=t%7Cxt&as_filetype=jpg");
    }

    internal static bool AllowsNavigation(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https" &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        ((uri.IdnHost is "www.google.com" or "google.com" &&
          (uri.AbsolutePath is "/" or "/search" or "/imghp" or "/imgres" || uri.AbsolutePath.StartsWith("/images/", StringComparison.Ordinal))) ||
         (uri.IdnHost == "consent.google.com" && uri.AbsolutePath is "/m" or "/ml" or "/save"));

    internal static Uri ImageUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsoluteUri.Length > 8192)
            throw new InvalidOperationException("Choose a public HTTPS PNG or JPEG image.");
        var host = uri.IdnHost.TrimEnd('.');
        var numeric = IPAddress.TryParse(host.Trim('[', ']'), out var address);
        if ((!numeric && !host.Contains('.')) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            (numeric && !IsPublic(address!)))
            throw new InvalidOperationException("Local and private image addresses are unavailable.");
        return new UriBuilder(uri) { Fragment = "" }.Uri;
    }

    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublic(address.MapToIPv4());
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224 ||
                     (b[0] == 100 && b[1] is >= 64 and <= 127) ||
                     (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31) ||
                     (b[0] == 192 && (b[1] is 0 or 168 || (b[1] == 88 && b[2] == 99))) ||
                     (b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100))) ||
                     (b[0] == 203 && b[1] == 0 && b[2] == 113));
        // Only globally routed unicast IPv6; excludes mapped, local, multicast and transition prefixes.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && b[0] is >= 0x20 and <= 0x3f &&
               !(b[0] == 0x20 && b[1] == 0x01 && b[2] <= 1) &&
               !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) &&
               !(b[0] == 0x20 && b[1] == 0x02);
    }
}

internal enum WebCoverDownloadFailureKind
{
    Blocked,
    NotFound,
    UnsupportedFormat,
    TooLarge,
    Redirect,
    RateLimit,
    Unavailable
}

internal sealed class WebCoverDownloadException(WebCoverDownloadFailureKind kind) : InvalidOperationException(kind switch
{
    WebCoverDownloadFailureKind.Blocked => "This website blocked the image download. Choose another image or Browse a local image.",
    WebCoverDownloadFailureKind.NotFound => "This image is no longer available. Choose another image or Browse a local image.",
    WebCoverDownloadFailureKind.UnsupportedFormat => "Choose a direct PNG or JPEG image. SVG, animated and web page downloads are unsupported.",
    WebCoverDownloadFailureKind.TooLarge => "The image exceeds the 12 MB download limit. Choose another image.",
    WebCoverDownloadFailureKind.Redirect => "This image has an unavailable or excessive redirect. Choose another image.",
    WebCoverDownloadFailureKind.RateLimit => "This website is limiting image downloads. Wait before retrying or choose another image.",
    WebCoverDownloadFailureKind.Unavailable => "This image server is temporarily unavailable. Retry later or choose another image.",
    _ => "The image could not be downloaded. Choose another image or Browse a local image."
})
{
    internal WebCoverDownloadFailureKind Kind { get; } = kind;
}

internal sealed class WebCoverImageDownload : IDisposable
{
    internal const int MaximumBytes = 12 * 1024 * 1024;
    private readonly HttpClient client;
    public WebCoverImageDownload() : this(CreateHandler()) { }
    internal WebCoverImageDownload(HttpMessageHandler handler)
    { client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; }

    private static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false, Credentials = null,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectCallback = async (context, token) =>
        {
            if (context.DnsEndPoint.Port != 443) throw new HttpRequestException("Image address is unavailable.");
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
            if (addresses.Length == 0 || addresses.Any(address => !WebCoverPolicy.IsPublic(address)))
                throw new HttpRequestException("Local and private image addresses are unavailable.");
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    // Connect to the validated address, never resolve the host again (DNS rebinding).
                    await socket.ConnectAsync(new IPEndPoint(address, 443), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new HttpRequestException("The image server could not be reached.");
        }
    };

    internal async Task<byte[]> DownloadAsync(Uri source, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var current = WebCoverPolicy.ImageUri(source);
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            // Identify this app honestly. Rebuild headers for every manually validated redirect.
            request.Headers.UserAgent.ParseAdd("GameVault/0.1 (+https://github.com/BlackNard/GameVault)");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (redirects >= 3 || response.Headers.Location is not { } location)
                    throw new WebCoverDownloadException(WebCoverDownloadFailureKind.Redirect);
                try { current = WebCoverPolicy.ImageUri(new Uri(current, location)); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or UriFormatException)
                { throw new WebCoverDownloadException(WebCoverDownloadFailureKind.Redirect); }
                continue;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new WebCoverDownloadException(WebCoverDownloadFailureKind.Blocked);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                throw new WebCoverDownloadException(WebCoverDownloadFailureKind.NotFound);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new WebCoverDownloadException(WebCoverDownloadFailureKind.RateLimit);
            if (!response.IsSuccessStatusCode)
                throw new WebCoverDownloadException(WebCoverDownloadFailureKind.Unavailable);
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/png" or "image/jpeg"))
                throw new WebCoverDownloadException(WebCoverDownloadFailureKind.UnsupportedFormat);
            if (response.Content.Headers.ContentLength > MaximumBytes)
                throw new WebCoverDownloadException(WebCoverDownloadFailureKind.TooLarge);
            using var input = await response.Content.ReadAsStreamAsync(token);
            using var output = new MemoryStream();
            var buffer = new byte[32 * 1024];
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + count > MaximumBytes) throw new WebCoverDownloadException(WebCoverDownloadFailureKind.TooLarge);
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
            token.ThrowIfCancellationRequested();
            return output.ToArray();
        }
    }
    public void Dispose() => client.Dispose();
}
