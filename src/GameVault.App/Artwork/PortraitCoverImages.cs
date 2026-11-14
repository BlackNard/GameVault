using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.Core;
namespace GameVault.App.Artwork;

/// <summary>Validates actual image pixels before online artwork enters the gallery or editor.</summary>
internal static class PortraitCoverImages
{
    internal sealed record ThumbnailImage(BitmapSource Image, int Width, int Height);
    public static ThumbnailImage Thumbnail(byte[] bytes)
    {
        var source = DecodePortrait(bytes);
        return new(Resize(source,120,180),source.PixelWidth,source.PixelHeight);
    }
    public static byte[] Normalize(byte[] bytes, Uri? sourcePage = null)
    {
        var source = Resize(DecodePortrait(bytes),1200,1800);
        var metadata = new BitmapMetadata("png");
        if (sourcePage is not null)
        {
            metadata.SetQuery("/tEXt/{str=Description}","Publisher artwork via Steam; rights remain with the publisher. Source: " + sourcePage.AbsoluteUri);
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source,null,metadata,null));
        using var output = new MemoryStream(); encoder.Save(output);
        if(output.Length>10*1024*1024) throw new InvalidOperationException("The normalized cover is too large. Choose another cover.");
        return output.ToArray();
    }
    private static BitmapSource DecodePortrait(byte[] bytes)
    {
        var source=ArtworkImageNormalizer.Preview(bytes);
        if(!PortraitCoverPolicy.IsSuitable(source.PixelWidth,source.PixelHeight))
            throw new InvalidOperationException("Online covers must be at least 600 × 900 pixels and close to a 2:3 portrait shape.");
        return source;
    }
    private static BitmapSource Resize(BitmapSource source,int maxWidth,int maxHeight)
    {
        var scale=Math.Min(1d,Math.Min((double)maxWidth/source.PixelWidth,(double)maxHeight/source.PixelHeight));
        if(scale>=1) return source;
        var resized=new TransformedBitmap(source,new ScaleTransform(scale,scale)); resized.Freeze();
        // Copy pixels to release the full-size source retained by TransformedBitmap.
        var stride=(resized.PixelWidth*resized.Format.BitsPerPixel+7)/8;
        var pixels=new byte[checked(stride*resized.PixelHeight)]; resized.CopyPixels(pixels,stride,0);
        var independent=BitmapSource.Create(resized.PixelWidth,resized.PixelHeight,96,96,resized.Format,resized.Palette,pixels,stride);
        independent.Freeze(); return independent;
    }
}
