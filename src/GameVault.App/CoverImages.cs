using System.IO;
using System.Windows.Media.Imaging;

namespace GameVault.App;

internal static class CoverImages
{
    private static readonly GameVault.Infrastructure.Library.BoundedAsyncCache<BitmapImage> Thumbnails =
        new(32L * 1024 * 1024, 2, image => checked((long)image.PixelWidth * image.PixelHeight * 4));
    internal static long CachedThumbnailBytes => Thumbnails.RetainedBytes;
    internal static Task<BitmapImage> LoadThumbnailAsync(string path, CancellationToken cancellationToken = default) =>
        Thumbnails.GetAsync(Path.GetFullPath(path), () => Task.FromResult(Load(path, 240)), cancellationToken);
    internal static void InvalidateThumbnail(string? path)
    {
        if (path is not null) Thumbnails.Invalidate(Path.GetFullPath(path));
    }
    public static byte[] Normalize(string path)
    {
        var image = Load(path, 480);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public static BitmapImage Load(string path, int width)
    {
        try { return LoadChecked(path, width); }
        catch (FileFormatException error)
        { throw new ArgumentException("This cover image could not be read. Choose a valid PNG or JPEG image.", error); }
    }

    private static BitmapImage LoadChecked(string path, int width)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 20 * 1024 * 1024) throw new ArgumentException("Choose a cover image smaller than 20 MB.");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 8192 || frame.PixelHeight > 8192 || (long)frame.PixelWidth * frame.PixelHeight > 20_000_000)
            throw new ArgumentException("Choose a cover image with at most 20 million pixels and sides under 8192 pixels.");
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = Math.Min(width, frame.PixelWidth);
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
