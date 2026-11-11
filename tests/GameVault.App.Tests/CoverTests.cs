using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.App;
using GameVault.Core;

namespace GameVault.App.Tests;

// These tests exercise image decoding without creating a window or opening native dialogs.
public sealed class CoverTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "GameVault-cover-" + Guid.NewGuid().ToString("N") + ".png");

    public CoverTests() => File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82]);

    [Fact]
    public void DamagedCoverProducesValidationError() => Assert.Throws<ArgumentException>(() => CoverImages.Normalize(path));

    [Fact]
    public async Task DamagedStoredCoverKeepsLibraryFallback()
    {
        var row = new LibraryItemView(new LibraryGame(Guid.NewGuid(), null, true, false, path, null, null));
        await row.LoadCoverAsync();
        Assert.Null(row.Cover);
    }

    [Fact]
    public void ValidCoverIsNormalizedAndDoesNotLockItsSource()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4)));
        using (var stream = File.Create(path)) encoder.Save(stream);
        var png = CoverImages.Normalize(path);
        Assert.True(CoverImages.Load(path, 240).IsFrozen);
        File.Delete(path);
        Assert.False(File.Exists(path));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
    }

    public void Dispose() => File.Delete(path);
}
