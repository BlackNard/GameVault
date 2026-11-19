using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App.Tests;

public sealed class RailIconTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-rail-" + Guid.NewGuid().ToString("N"));
    public RailIconTests() => Directory.CreateDirectory(root);

    private string Png(string name, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        var path = Path.Combine(root, name);
        using var stream = File.Create(path); encoder.Save(stream);
        return path;
    }

    private static LibraryItemView Row(string? cover) => new(new LibraryGame(Guid.NewGuid(), null, true, false, cover, 0, null));

    [Fact]
    public async Task The_rail_icon_is_a_small_decode_that_keeps_the_whole_cover()
    {
        var row = Row(Png("cover.png", 400, 600));
        Assert.Null(row.RailIcon);
        await row.LoadRailIconAsync();
        Assert.NotNull(row.RailIcon);
        Assert.True(row.RailIcon!.PixelWidth <= 64);
        Assert.Equal(400 / 600.0, row.RailIcon.PixelWidth / (double)row.RailIcon.PixelHeight, 1);
    }

    [Fact]
    public async Task Changing_the_game_drops_the_old_icon_so_a_new_cover_shows()
    {
        var first = Png("a.png", 400, 600); var second = Png("b.png", 300, 600);
        var id = Guid.NewGuid();
        var row = new LibraryItemView(new LibraryGame(id, null, true, false, first, 0, null));
        await row.LoadRailIconAsync();
        var seen = new List<string?>(); row.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
        row.RefreshGame(new LibraryGame(id, null, true, false, second, 0, null));
        Assert.Null(row.RailIcon); Assert.Contains("RailIcon", seen);
        await row.LoadRailIconAsync();
        Assert.Equal(0.5, row.RailIcon!.PixelWidth / (double)row.RailIcon.PixelHeight, 1);
    }

    [Fact]
    public async Task A_missing_or_unreadable_cover_leaves_the_letter_tile_without_an_error()
    {
        var none = Row(null); await none.LoadRailIconAsync(); Assert.Null(none.RailIcon);
        var missing = Row(Path.Combine(root, "nope.png")); await missing.LoadRailIconAsync(); Assert.Null(missing.RailIcon);
        var bad = Path.Combine(root, "bad.png"); File.WriteAllText(bad, "not an image");
        var corrupt = Row(bad); await corrupt.LoadRailIconAsync(); Assert.Null(corrupt.RailIcon);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
