using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.App.Artwork;
namespace GameVault.App.Tests;
public sealed class PortraitCoverCropTests
{
    [Theory][InlineData(1600,900,600,900)][InlineData(900,900,600,900)][InlineData(600,1000,600,900)][InlineData(2400,2400,1200,1800)]
    public void LargeSourcesCropToPortraitWithoutUpscaling(int width,int height,int expectedWidth,int expectedHeight)
    {
        var png=PortraitCoverImages.Normalize(PortraitCoverImageTests.Png(width,height));
        var image=ArtworkImageNormalizer.Preview(png);
        Assert.Equal(expectedWidth,image.PixelWidth); Assert.Equal(expectedHeight,image.PixelHeight);
    }
    [Fact] public void CropUsesCenterPixelsWithoutStretching()
    {
        const int width=1600,height=900;
        var pixels=new byte[width*height*4];
        for(var y=0;y<height;y++) for(var x=0;x<width;x++)
        { var i=(y*width+x)*4; pixels[i]=x<500 ? (byte)255:(byte)0; pixels[i+1]=x>=500&&x<1100 ? (byte)255:(byte)0; pixels[i+2]=x>=1100 ? (byte)255:(byte)0; pixels[i+3]=255; }
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));
        using var stream=new MemoryStream(); encoder.Save(stream);
        var image=ArtworkImageNormalizer.Preview(PortraitCoverImages.Normalize(stream.ToArray()));
        var result=new byte[600*900*4]; image.CopyPixels(result,600*4,0);
        Assert.All(Enumerable.Range(0,600*900),i=>{ Assert.Equal(0,result[i*4]); Assert.Equal(255,result[i*4+1]); Assert.Equal(0,result[i*4+2]); });
    }
}
