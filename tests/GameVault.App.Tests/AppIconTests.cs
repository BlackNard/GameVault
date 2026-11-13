using System.Reflection;
using System.Windows.Media.Imaging;
using GameVault.App;

namespace GameVault.App.Tests;

public sealed class AppIconTests
{
    [Fact]
    public void Embedded_icon_decodes_at_small_and_large_sizes_without_starting_the_app()
    {
        var uri = new Uri("/GameVault;component/Assets/GameVault.ico", UriKind.Relative);
        using var stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Contains(decoder.Frames, frame => frame.PixelWidth == 16);
        Assert.Contains(decoder.Frames, frame => frame.PixelWidth == 256);
        using var trayIcon = AppIcon.LoadTrayIcon();
        Assert.True(trayIcon.Width > 0);
    }
}
