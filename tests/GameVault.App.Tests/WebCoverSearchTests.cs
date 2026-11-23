using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using GameVault.App.Artwork;

namespace GameVault.App.Tests;

public sealed class WebCoverSearchTests
{
    [Theory]
    [InlineData("")][InlineData("D:\\Games\\Game.exe")][InlineData("/home/game")]
    [InlineData("https://example.com/game")][InlineData("Game\nTitle")]
    public void SearchRejectsPathsAndControlCharacters(string value) => Assert.Throws<ArgumentException>(() => WebCoverPolicy.SearchUri(value));

    [Fact]
    public void SearchSendsOnlyEscapedTitle()
    {
        var uri = WebCoverPolicy.SearchUri(" Game & Edition #2 ");
        Assert.Equal("https://www.google.com/search?tbm=isch&q=Game%20%26%20Edition%20%232%20game%20cover&imgsz=l&imgar=t%7Cxt&as_filetype=jpg", uri.AbsoluteUri);
        Assert.True(WebCoverPolicy.AllowsNavigation(uri));
        Assert.Throws<ArgumentException>(() => WebCoverPolicy.SearchUri(new string('x', 241)));
    }

    [Theory]
    [InlineData("Resident Evil 2", "Resident Evil 2 game cover")]
    [InlineData(" Game COVER ", "Game COVER")]
    [InlineData("Game covers", "Game covers")]
    [InlineData("Game boxart", "Game boxart")]
    [InlineData("Game box art", "Game box art")]
    [InlineData("Game box-art", "Game box-art")]
    [InlineData("Discover", "Discover game cover")]
    public void SearchFocusesOnCoversWithoutDuplicatingExistingKeywords(string title, string expected)
    {
        var uri = WebCoverPolicy.SearchUri(title);
        Assert.Contains("q=" + Uri.EscapeDataString(expected) + "&", uri.AbsoluteUri);
        Assert.Contains("imgsz=l&imgar=t%7Cxt&as_filetype=jpg", uri.AbsoluteUri);
    }

    [Fact]
    public async Task EveryRedirectRequestIdentifiesGameVaultAndAcceptsSupportedImages()
    {
        var requests = 0;
        using var service = new WebCoverImageDownload(new Handler((request, _) =>
        {
            requests++;
            Assert.Equal("GameVault/0.1 (+https://github.com/BlackNard/GameVault)", request.Headers.UserAgent.ToString());
            Assert.Equal(["image/png", "image/jpeg"], request.Headers.Accept.Select(value => value.MediaType));
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Headers.Referrer);
            Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(requests < 3
                ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://images.example.org/next") } }
                : ImageResponse([1, 2, 3], "image/png"));
        }));
        Assert.Equal(new byte[] { 1, 2, 3 }, await service.DownloadAsync(new Uri("https://example.com/a"), default));
        Assert.Equal(3, requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Blocked")]
    [InlineData(HttpStatusCode.Forbidden, "Blocked")]
    [InlineData(HttpStatusCode.NotFound, "NotFound")]
    [InlineData(HttpStatusCode.Gone, "NotFound")]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimit")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unavailable")]
    public async Task HttpFailuresAreTypedAndDoNotExposeSourceOrResponseSecrets(HttpStatusCode status, string kind)
    {
        using var service = new WebCoverImageDownload(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(status) { ReasonPhrase = "SecretReason", Content = new StringContent("SecretBody") })));
        var error = await Assert.ThrowsAsync<WebCoverDownloadException>(() => service.DownloadAsync(new Uri("https://example.com/a?key=SecretQuery"), default));
        Assert.Equal(kind, error.Kind.ToString());
        Assert.DoesNotContain("Secret", error.Message);
        Assert.DoesNotContain("example.com", error.Message);
    }

    [Theory]
    [InlineData("https://www.google.com/search?q=game", true)]
    [InlineData("https://consent.google.com/m?continue=x", true)]
    [InlineData("https://accounts.google.com/", false)]
    [InlineData("https://www.google.com/url?q=https://example.com", false)]
    [InlineData("https://www.google.com.evil.example/search", false)]
    [InlineData("https://user@www.google.com/search", false)]
    [InlineData("http://www.google.com/search", false)]
    [InlineData("file:///C:/file.png", false)]
    public void NavigationStaysWithinGoogleImages(string address, bool expected) => Assert.Equal(expected, WebCoverPolicy.AllowsNavigation(new Uri(address)));

    [Theory]
    [InlineData("127.0.0.1")][InlineData("0.0.0.0")][InlineData("10.1.2.3")]
    [InlineData("172.31.1.2")][InlineData("192.168.1.1")][InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")][InlineData("224.1.2.3")][InlineData("198.18.0.1")]
    [InlineData("::1")][InlineData("::ffff:127.0.0.1")][InlineData("fe80::1")]
    [InlineData("fc00::1")][InlineData("fec0::1")][InlineData("2001:db8::1")][InlineData("2002:7f00:1::")]
    public void PrivateAndSpecialAddressesAreRejected(string address) => Assert.False(WebCoverPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory][InlineData("8.8.8.8")][InlineData("1.1.1.1")][InlineData("2606:4700:4700::1111")]
    public void PublicAddressesAreAccepted(string address) => Assert.True(WebCoverPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("file:///C:/image.png")][InlineData("data:image/png;base64,AA")]
    [InlineData("http://example.com/a.png")][InlineData("https://localhost/a.png")]
    [InlineData("https://host.local/a.png")][InlineData("https://127.1/a.png")]
    [InlineData("https://[::ffff:127.0.0.1]/a.png")][InlineData("https://example.com:8443/a.png")]
    [InlineData("https://user:password@example.com/a.png")]
    public void UnsafeSourcesAreRejectedBeforeAnyRequest(string value) => Assert.Throws<InvalidOperationException>(() => WebCoverPolicy.ImageUri(new Uri(value)));

    [Fact]
    public async Task RedirectIsValidatedWithoutForwardingCredentials()
    {
        var requests = 0;
        using var service = new WebCoverImageDownload(new Handler((request, _) =>
        {
            requests++;
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://127.0.0.1/image.png") } });
        }));
        var error = await Assert.ThrowsAsync<WebCoverDownloadException>(() => service.DownloadAsync(new Uri("https://example.com/a.png"), default));
        Assert.Equal(WebCoverDownloadFailureKind.Redirect, error.Kind);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task RedirectLimitIsBounded()
    {
        var requests = 0;
        using var service = new WebCoverImageDownload(new Handler((_, _) =>
        { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/next", UriKind.Relative) } }); }));
        var error = await Assert.ThrowsAsync<WebCoverDownloadException>(() => service.DownloadAsync(new Uri("https://example.com/a.png"), default));
        Assert.Equal(WebCoverDownloadFailureKind.Redirect, error.Kind);
        Assert.Equal(4, requests);
    }

    [Theory][InlineData("image/svg+xml")][InlineData("image/gif")][InlineData("text/html")]
    public async Task UnsupportedContentIsRejected(string type)
    {
        using var service = new WebCoverImageDownload(new Handler((_, _) => Task.FromResult(ImageResponse([1, 2, 3], type))));
        var error = await Assert.ThrowsAsync<WebCoverDownloadException>(() => service.DownloadAsync(new Uri("https://example.com/a"), default));
        Assert.Equal(WebCoverDownloadFailureKind.UnsupportedFormat, error.Kind);
    }

    [Fact]
    public async Task OversizedDeclaredAndStreamingImagesAreRejected()
    {
        using var declared = new WebCoverImageDownload(new Handler((_, _) =>
        {
            var response = ImageResponse([1], "image/png"); response.Content.Headers.ContentLength = WebCoverImageDownload.MaximumBytes + 1;
            return Task.FromResult(response);
        }));
        var declaredError = await Assert.ThrowsAsync<WebCoverDownloadException>(() => declared.DownloadAsync(new Uri("https://example.com/a"), default));
        Assert.Equal(WebCoverDownloadFailureKind.TooLarge, declaredError.Kind);
        using var streamed = new WebCoverImageDownload(new Handler((_, _) =>
        {
            var content = new StreamContent(new UnseekableStream(new byte[WebCoverImageDownload.MaximumBytes + 1]));
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        var streamedError = await Assert.ThrowsAsync<WebCoverDownloadException>(() => streamed.DownloadAsync(new Uri("https://example.com/a"), default));
        Assert.Equal(WebCoverDownloadFailureKind.TooLarge, streamedError.Kind);
    }

    [Fact]
    public async Task CancelStopsPendingRequest()
    {
        using var cancellation = new CancellationTokenSource();
        using var service = new WebCoverImageDownload(new Handler(async (_, token) => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, token); return ImageResponse([], "image/png"); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(new Uri("https://example.com/a"), cancellation.Token));
    }

    [Fact]
    public async Task DownloadReturnsOriginalBytesAndStripsFragment()
    {
        byte[] bytes = [1, 2, 3, 4];
        using var service = new WebCoverImageDownload(new Handler((request, _) =>
        { Assert.Equal("", request.RequestUri!.Fragment); return Task.FromResult(ImageResponse(bytes, "image/jpeg")); }));
        Assert.Equal(bytes, await service.DownloadAsync(new Uri("https://example.com/a#fragment"), default));
    }

    private static HttpResponseMessage ImageResponse(byte[] bytes, string type)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(type); return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }
}
