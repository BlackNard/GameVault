using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Downloads a game's wide background image from a store or game-asset network (see <see cref="HeroImages.IsAllowedUrl"/>). Only JPEG or PNG
/// data of at most 8 MB is accepted; a missing image is simply "no image".
/// </summary>
public sealed class HeroImageDownloader : IDisposable
{
    public const int MaxBytes = 8 * 1024 * 1024;
    private readonly PoliteHttp http;

    public HeroImageDownloader(HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null) =>
        http = new("The image server", handler, timeout ?? TimeSpan.FromSeconds(30), minimumGap ?? TimeSpan.FromMilliseconds(300));

    public async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!HeroImages.IsAllowedUrl(url)) throw new ArgumentException("This image address is not allowed.", nameof(url));
        byte[] bytes;
        try { bytes = await http.GetBytesAsync(url, cancellationToken, MaxBytes, "image/jpeg,image/png").ConfigureAwait(false); }
        catch (FileNotFoundException) { return null; }
        var jpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var png = bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (!jpeg && !png) throw new IOException("The server did not send a JPEG or PNG image.");
        return bytes;
    }

    public void Dispose() => http.Dispose();
}
