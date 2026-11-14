using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.App.Artwork;
namespace GameVault.App.Tests;
public sealed class CoverImportTests
{
    [Fact] public void BrowserQueryEncodesOnlyExplicitTitle()
    {
        var uri = CoverImport.BrowserSearch("  Baldur's Gate 3 & friends  ");
        Assert.Equal("https",uri.Scheme);
        Assert.Equal("www.bing.com",uri.Host);
        Assert.Equal("?q=Baldur%27s%20Gate%203%20%26%20friends%20game%20cover",uri.Query);
    }
    [Theory]
    [InlineData(@"C:\Games\Private\game.exe")]
    [InlineData(@"\\server\private\game.exe")]
    [InlineData("/home/private/game")]
    [InlineData("https://private.example/game")]
    [InlineData("")]
    [InlineData("Game\nprivate")]
    public void BrowserQueryRejectsPathsAndControlCharacters(string title) => Assert.Throws<ArgumentException>(()=>CoverImport.BrowserSearch(title));
    [Fact] public async Task ImportRejectsMultipleDroppedFiles() => await Assert.ThrowsAsync<ArgumentException>(()=>CoverImport.ReadAsync(["one.png","two.png"]));
    [Fact] public async Task ImportRejectsUnsupportedTypeBeforeOpeningFile() => await Assert.ThrowsAsync<ArgumentException>(()=>CoverImport.ReadAsync(["missing.svg"]));
    [Fact] public async Task ImportChecksSizeBeforeReading()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        try { using(var stream=File.Create(path))stream.SetLength(12*1024*1024+1); await Assert.ThrowsAsync<InvalidOperationException>(()=>CoverImport.ReadAsync([path])); }
        finally { File.Delete(path); }
    }
    [Fact] public async Task ImportProducesIndependentNormalizedPng()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        try {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8)));
            using(var stream=File.Create(path))encoder.Save(stream);
            var png=await CoverImport.ReadAsync([path]);File.Delete(path);
            var preview=ArtworkImageNormalizer.Preview(png);Assert.True(preview.IsFrozen);Assert.Equal(2,preview.PixelWidth);
        } finally { File.Delete(path); }
    }
}
