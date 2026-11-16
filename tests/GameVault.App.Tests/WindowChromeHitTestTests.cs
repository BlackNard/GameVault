using System.Windows;
using GameVault.App;

namespace GameVault.App.Tests;

public sealed class WindowChromeHitTestTests
{
    [Theory]
    [InlineData(-1800, -400)]
    [InlineData(1800, 400)]
    [InlineData(-1, -1)]
    [InlineData(32767, -32768)]
    public void Packed_native_coordinates_remain_signed(int x, int y)
    {
        var packed = unchecked((nint)((y & 0xffff) << 16 | (x & 0xffff)));
        Assert.Equal(new Point(x, y), WindowChromeBehavior.ScreenPoint(packed));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Maximize_region_uses_screen_pixels_and_excludes_the_top_resize_edge(double scale)
    {
        var window = new Rect(-1920 * scale, -200 * scale, 1280 * scale, 850 * scale);
        var button = new Rect(-732 * scale, -200 * scale, 46 * scale, 44 * scale);
        Assert.True(WindowChromeBehavior.IsMaximizeRegion(new Point(-710 * scale, -180 * scale), button, window, new Thickness(8 * scale), false, true));
        Assert.False(WindowChromeBehavior.IsMaximizeRegion(new Point(-710 * scale, -199 * scale), button, window, new Thickness(8 * scale), false, true));
        Assert.True(WindowChromeBehavior.IsMaximizeRegion(new Point(-710 * scale, -199 * scale), button, window, new Thickness(8 * scale), true, true));
        Assert.False(WindowChromeBehavior.IsMaximizeRegion(new Point(-680 * scale, -180 * scale), button, window, new Thickness(8 * scale), false, true));
        Assert.False(WindowChromeBehavior.IsMaximizeRegion(new Point(-710 * scale, -180 * scale), button, window, new Thickness(8 * scale), false, false));
    }

    [Fact]
    public void Right_and_bottom_resize_edges_keep_native_hit_testing()
    {
        var window = new Rect(0, 0, 100, 100);
        var button = new Rect(50, 0, 50, 44);
        Assert.False(WindowChromeBehavior.IsMaximizeRegion(new Point(99, 20), button, window, new Thickness(8), false, true));
        Assert.False(WindowChromeBehavior.IsMaximizeRegion(new Point(60, 44), button, window, new Thickness(8), false, true));
        Assert.False(WindowChromeBehavior.IsMaximizeRegion(new Point(49, 20), button, window, new Thickness(8), false, true));
    }
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Caption_resize_border_takes_precedence_over_client_buttons(double scale)
    {
        var window = new Rect(-1920 * scale, -200 * scale, 1280 * scale, 850 * scale);
        int Hit(double x, double y, bool maximized = false) => WindowChromeBehavior.ResizeHitTest(
            new Point(x * scale, y * scale), window, new Thickness(8 * scale), maximized);
        Assert.Equal(13, Hit(-710, -199)); // Top, even when a client-visible caption button is underneath.
        Assert.Equal(14, Hit(-1919, -199));
        Assert.Equal(15, Hit(-641, -199));
        Assert.Equal(11, Hit(-641, -180));
        Assert.Equal(10, Hit(-1919, -180));
        Assert.Equal(17, Hit(-1919, 649));
        Assert.Equal(18, Hit(-641, 649));
        Assert.Equal(16, Hit(-710, 649));
        Assert.Equal(0, Hit(-710, -180));
        Assert.Equal(0, Hit(-710, -199, true));
        Assert.Equal(0, Hit(-640, -199)); // Half-open screen bounds.
    }
}
