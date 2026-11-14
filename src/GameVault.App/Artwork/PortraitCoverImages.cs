using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace GameVault.App.Artwork;

/// <summary>Validates image pixels and prepares a centered portrait without stretching or upscaling.</summary>
internal static class PortraitCoverImages
{
    internal sealed record ThumbnailImage(BitmapSource Image, int Width, int Height, bool IsCropped);
    internal sealed record PreparedImage(byte[] Png, int Width, int Height, bool IsCropped, bool IsPortrait);
    public static bool CanCropToPortrait(int width, int height) =>
        width >= 600 && height >= 900 && width <= 8192 && height <= 8192 && (long)width * height <= 16_000_000;
    public static ThumbnailImage Thumbnail(byte[] bytes)
    {
        var source = DecodePortrait(bytes);
        return new(CropAndResize(source,60),source.PixelWidth,source.PixelHeight,NeedsCrop(source));
    }
    public static byte[] Normalize(byte[] bytes, Uri? sourcePage = null) => Prepare(bytes,sourcePage).Png;
    public static PreparedImage Prepare(byte[] bytes, Uri? sourcePage = null, bool allowSmallLocal = false)
    {
        var original = ArtworkImageNormalizer.Preview(bytes);
        var isPortrait = CanCropToPortrait(original.PixelWidth,original.PixelHeight);
        if(!isPortrait && !allowSmallLocal) throw TooSmall();
        var source = isPortrait ? CropAndResize(original,600) : Copy(original);
        var metadata = new BitmapMetadata("png");
        if (sourcePage is not null)
            metadata.SetQuery("/tEXt/{str=Description}",(sourcePage.Host.Equals("store.steampowered.com",StringComparison.OrdinalIgnoreCase) ? "Publisher artwork via Steam; rights remain with the publisher. Source: " : "Web image; rights remain with the original owner. Source: ") + sourcePage.AbsoluteUri);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source,null,metadata,null));
        using var output = new MemoryStream(); encoder.Save(output);
        if(output.Length>10*1024*1024) throw new InvalidOperationException("The normalized cover is too large. Choose another cover.");
        return new(output.ToArray(),original.PixelWidth,original.PixelHeight,isPortrait && NeedsCrop(original),isPortrait);
    }
    private static BitmapSource DecodePortrait(byte[] bytes)
    {
        var source=ArtworkImageNormalizer.Preview(bytes);
        if(!CanCropToPortrait(source.PixelWidth,source.PixelHeight)) throw TooSmall();
        return source;
    }
    private static InvalidOperationException TooSmall() => new("This image cannot provide a 600 × 900 portrait crop without upscaling. Choose a larger image.");
    private static bool NeedsCrop(BitmapSource source) => (long)source.PixelWidth*3 != (long)source.PixelHeight*2;
    private static BitmapSource CropAndResize(BitmapSource source,int maxUnits)
    {
        // Integer 2:3 dimensions keep the output exact and never invent source pixels.
        var units=Math.Min(source.PixelWidth/2,source.PixelHeight/3);
        var width=units*2; var height=units*3;
        BitmapSource cropped=new CroppedBitmap(source,new Int32Rect((source.PixelWidth-width)/2,(source.PixelHeight-height)/2,width,height));
        if(units>maxUnits)
            cropped=new TransformedBitmap(cropped,new ScaleTransform((double)maxUnits/units,(double)maxUnits/units));
        return Copy(cropped);
    }
    private static BitmapSource Copy(BitmapSource source)
    {
        // Copy pixels even without scaling so neither crops nor transforms retain the full source.
        var stride=(source.PixelWidth*source.Format.BitsPerPixel+7)/8;
        var pixels=new byte[checked(stride*source.PixelHeight)]; source.CopyPixels(pixels,stride,0);
        var independent=BitmapSource.Create(source.PixelWidth,source.PixelHeight,96,96,source.Format,source.Palette,pixels,stride);
        independent.Freeze(); return independent;
    }
}
