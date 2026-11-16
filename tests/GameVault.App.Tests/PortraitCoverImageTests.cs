using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.App.Artwork;
namespace GameVault.App.Tests;
public sealed class PortraitCoverImageTests
{
    internal static byte[] Png(int width,int height)
    {
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,new byte[width*height*4],width*4)));
        using var stream=new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    [Theory][InlineData(599,900)][InlineData(900,600)][InlineData(600,899)][InlineData(8193,900)]
    public void UnsuitableDecodedImagesAreRejected(int width,int height)=>Assert.Throws<InvalidOperationException>(()=>PortraitCoverImages.Thumbnail(Png(width,height)));
    [Fact] public void GalleryKeepsOnlySmallFrozenImage()
    {
        var item=PortraitCoverImages.Thumbnail(Png(600,900)); Assert.Equal(600,item.Width); Assert.Equal(900,item.Height);
        Assert.True(item.Image.IsFrozen); Assert.Equal(120,item.Image.PixelWidth); Assert.Equal(180,item.Image.PixelHeight);
    }
    [Theory][InlineData(600,900,600,900)][InlineData(1200,1800,1200,1800)][InlineData(1600,2400,1200,1800)]
    public void SelectedImagesRespectMaximumWithoutUpscaling(int width,int height,int expectedWidth,int expectedHeight)
    {
        var bytes=PortraitCoverImages.Normalize(Png(width,height)); var image=ArtworkImageNormalizer.Preview(bytes);
        Assert.Equal(expectedWidth,image.PixelWidth); Assert.Equal(expectedHeight,image.PixelHeight); Assert.True(bytes.Length<=10*1024*1024);
    }
    [Fact] public void SourceSafetyLimitsStillApply()=>Assert.Throws<InvalidOperationException>(()=>PortraitCoverImages.Thumbnail(new byte[12*1024*1024+1]));
}
