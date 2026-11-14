using System.IO;
using System.Windows.Media.Imaging;
namespace GameVault.App.Artwork;
internal static class ArtworkImageNormalizer
{
    public static BitmapSource Preview(byte[] bytes)
    {
        ValidateHeader(bytes);
        using var stream=new MemoryStream(bytes,false);
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        if(decoder is not (PngBitmapDecoder or JpegBitmapDecoder)||decoder.Frames.Count!=1)throw new InvalidOperationException("Choose a static PNG or JPEG image.");
        var frame=decoder.Frames[0];CheckDimensions(frame.PixelWidth,frame.PixelHeight);frame.Freeze();return frame;
    }
    public static byte[] Normalize(byte[] bytes)
    {
        var source=Preview(bytes);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var output=new MemoryStream();encoder.Save(output);
        if(output.Length>10*1024*1024)throw new InvalidOperationException("The normalized cover is too large. Choose another image.");return output.ToArray();
    }
    private static void CheckDimensions(long width,long height)
    {if(width<=0||height<=0||width>8192||height>8192||width*height>16_000_000)throw new InvalidOperationException("Artwork exceeds the 16 megapixel limit. Choose another image.");}
    private static void ValidateHeader(byte[] bytes)
    {
        if(bytes.Length is < 24 or > 12*1024*1024)throw new InvalidOperationException("Artwork exceeds the safe size limit.");
        if(bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))
        {CheckDimensions(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16,4)),System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20,4)));return;}
        if(bytes[0]!=255||bytes[1]!=216)throw new InvalidOperationException("Choose PNG or JPEG artwork.");
        var position=2;
        while(position+4<=bytes.Length)
        {
            if(bytes[position++]!=255)throw new InvalidOperationException("The JPEG header is invalid.");
            while(position<bytes.Length&&bytes[position]==255)position++;if(position>=bytes.Length)break;var marker=bytes[position++];
            if(marker is 0xD9 or 0xDA)break;if(marker is 0x01 or >=0xD0 and <=0xD7)continue;
            if(position+2>bytes.Length)break;var length=(bytes[position]<<8)|bytes[position+1];if(length<2||position+length>bytes.Length)break;
            if(marker is >=0xC0 and <=0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {if(length<8)break;CheckDimensions((bytes[position+5]<<8)|bytes[position+6],(bytes[position+3]<<8)|bytes[position+4]);return;}
            position+=length;
        }
        throw new InvalidOperationException("The JPEG dimensions could not be verified.");
    }
}
