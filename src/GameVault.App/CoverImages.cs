using System.IO;
using System.Windows.Media.Imaging;

namespace GameVault.App;

internal static class CoverImages
{
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
