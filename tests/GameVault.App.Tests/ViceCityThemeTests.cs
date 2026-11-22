using System.IO;
using System.Windows;
using System.Windows.Media;
using GameVault.App;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.App.Tests;

public sealed class ViceCityThemeTests
{
    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var x = Luminance(a) + 0.05; var y = Luminance(b) + 0.05;
        return Math.Max(x, y) / Math.Min(x, y);
    }

    [Fact]
    public void Vice_City_defines_every_theme_colour_and_keeps_text_readable_on_each_surface() => InSta(() =>
    {
        var element = new FrameworkElement();
        ThemePalette.Apply(element, "Vice City");
        foreach (var key in new[] { "AppChrome", "AppBackground", "AppText", "AppMuted", "AppSurface", "AppRaised", "AppLine", "AppAccent", "AppPrimary", "AppPrimaryText", "AppWarning" })
            Assert.IsAssignableFrom<Brush>(element.Resources[key]);
        var background = Assert.IsType<LinearGradientBrush>(element.Resources["AppBackground"]);
        Assert.True(background.IsFrozen);
        var text = ((SolidColorBrush)element.Resources["AppText"]).Color;
        var muted = ((SolidColorBrush)element.Resources["AppMuted"]).Color;
        foreach (var stop in background.GradientStops) Assert.True(Contrast(text, stop.Color) >= 7, "text on the gradient at " + stop.Offset);
        foreach (var key in new[] { "AppSurface", "AppRaised", "AppChrome" })
        {
            var surface = ((SolidColorBrush)element.Resources[key]).Color;
            Assert.True(Contrast(text, surface) >= 7, key);
            Assert.True(Contrast(muted, surface) >= 4.5, key + " muted");
        }
        var primary = ((SolidColorBrush)element.Resources["AppPrimary"]).Color;
        Assert.True(Contrast(((SolidColorBrush)element.Resources["AppPrimaryText"]).Color, primary) >= 4.5, "button text");
    });

    [Fact]
    public void The_other_themes_are_unchanged_and_the_picker_order_matches_the_saved_names()
    {
        Assert.Equal(0, ThemePalette.IndexOf("Midnight"));
        Assert.Equal(1, ThemePalette.IndexOf("Crimson"));
        Assert.Equal(2, ThemePalette.IndexOf("Vice City"));
        Assert.Equal(0, ThemePalette.IndexOf("unknown"));
        InSta(() =>
        {
            var element = new FrameworkElement();
            ThemePalette.Apply(element, "Crimson");
            Assert.IsType<SolidColorBrush>(element.Resources["AppBackground"]);
            Assert.Equal(Color.FromRgb(0x12, 0x0e, 0x11), ((SolidColorBrush)element.Resources["AppBackground"]).Color);
        });
    }

    [Fact]
    public async Task The_chosen_theme_is_saved_and_restored_after_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "GameVault theme", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = ApplicationPaths.FromRoot(root);
            await new AppDataStore(paths).InitializeAsync();
            await new AppDataStore(paths).SaveSettingsAsync(AppSettings.Default with { Theme = "Vice City" });
            var reopened = await new AppDataStore(paths).InitializeAsync();
            Assert.Equal(DocumentState.Ready, reopened.Settings.State);
            Assert.Equal("Vice City", reopened.Settings.Value!.Theme);
            Assert.Empty(reopened.Warnings);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
