using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.App.Artwork;
namespace GameVault.App.Tests;
public sealed class ArtworkImageTests
{
    [Fact] public void DownloadedPngNormalizesIndependentlyInMemory()
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8)));using var stream=new MemoryStream();encoder.Save(stream);
        var bytes=stream.ToArray();var png=ArtworkImageNormalizer.Normalize(bytes);Array.Clear(bytes);
        Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},png[..8]);var preview=ArtworkImageNormalizer.Preview(png);Assert.True(preview.IsFrozen);Assert.Equal(2,preview.PixelWidth);
    }
    [Fact] public void RejectsHugePngHeaderBeforeDecoderAllocation()
    {
        var bytes=new byte[24];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(bytes,0);System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16,4),100000);System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20,4),100000);
        var error=Assert.Throws<InvalidOperationException>(()=>ArtworkImageNormalizer.Normalize(bytes));Assert.Contains("megapixel",error.Message);
    }
    [Fact] public void RejectsSvgAndGifRegardlessOfFilename()
    {Assert.Throws<InvalidOperationException>(()=>ArtworkImageNormalizer.Normalize(System.Text.Encoding.UTF8.GetBytes("<svg width='100' height='100'></svg>")));}
    [Fact] public void RejectsOversizeImageBeforeDecoding()=>Assert.Throws<InvalidOperationException>(()=>ArtworkImageNormalizer.Normalize(new byte[12*1024*1024+1]));
}
