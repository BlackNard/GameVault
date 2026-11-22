using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using GameVault.App;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.App.Tests;

public sealed class ThemeTests
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

    public static IEnumerable<object[]> Themes() => ThemeCatalog.All.Select(t => new object[] { t.Id });

    [Fact]
    public void Theme_ids_are_unique_and_every_colour_is_a_valid_hex_value()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(ThemeCatalog.DefaultId, ThemeCatalog.All[0].Id);
        var hex = new System.Text.RegularExpressions.Regex("^#[0-9a-fA-F]{6}$");
        foreach (var t in ThemeCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Tagline), t.Id);
            foreach (var colour in new[] { t.Chrome, t.Background, t.Text, t.Muted, t.Surface, t.Raised, t.Line, t.Accent, t.Primary, t.PrimaryText, t.Warning }.Concat(t.BackgroundGradient ?? []))
                Assert.Matches(hex, colour);
            Assert.True(t.BackgroundGradient is null || t.BackgroundGradient.Count == 3, t.Id + " gradient");
        }
        Assert.True(ThemeCatalog.IsKnown("Vice City"));
        Assert.False(ThemeCatalog.IsKnown("Nope"));
        Assert.False(ThemeCatalog.IsKnown(null));
        Assert.Equal("Midnight", ThemeCatalog.Find("Nope").Id);
        Assert.Equal(1, ThemeCatalog.IndexOf("Crimson"));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_theme_keeps_text_readable_on_each_surface(string id) => InSta(() =>
    {
        var element = new FrameworkElement();
        ThemePalette.Apply(element, id);
        foreach (var key in new[] { "AppChrome", "AppBackground", "AppText", "AppMuted", "AppSurface", "AppRaised", "AppLine", "AppAccent", "AppPrimary", "AppPrimaryText", "AppWarning" })
            Assert.IsAssignableFrom<Brush>(element.Resources[key]);
        var text = ((SolidColorBrush)element.Resources["AppText"]).Color;
        var muted = ((SolidColorBrush)element.Resources["AppMuted"]).Color;
        var backdrops = element.Resources["AppBackground"] switch
        {
            LinearGradientBrush g => g.GradientStops.Select(s => s.Color).ToArray(),
            SolidColorBrush b => [b.Color],
            _ => throw new InvalidOperationException()
        };
        foreach (var color in backdrops) { Assert.True(Contrast(text, color) >= 7, id + " text on background"); Assert.True(Contrast(muted, color) >= 4.5, id + " muted on background"); }
        foreach (var key in new[] { "AppSurface", "AppRaised", "AppChrome" })
        {
            var surface = ((SolidColorBrush)element.Resources[key]).Color;
            Assert.True(Contrast(text, surface) >= 7, id + " " + key);
            Assert.True(Contrast(muted, surface) >= 4.5, id + " " + key + " muted");
        }
        var primary = ((SolidColorBrush)element.Resources["AppPrimary"]).Color;
        Assert.True(Contrast(((SolidColorBrush)element.Resources["AppPrimaryText"]).Color, primary) >= 4.5, id + " button text");
    });

    [Fact]
    public void Vice_City_uses_a_gradient_and_the_older_themes_are_unchanged() => InSta(() =>
    {
        var vice = new FrameworkElement();
        ThemePalette.Apply(vice, "Vice City");
        var gradient = Assert.IsType<LinearGradientBrush>(vice.Resources["AppBackground"]);
        Assert.True(gradient.IsFrozen);
        var crimson = new FrameworkElement();
        ThemePalette.Apply(crimson, "Crimson");
        Assert.Equal(Color.FromRgb(0x12, 0x0e, 0x11), ((SolidColorBrush)crimson.Resources["AppBackground"]).Color);
        var midnight = new FrameworkElement();
        ThemePalette.Apply(midnight, "something unknown");
        Assert.Equal(Color.FromRgb(0x13, 0x1b, 0x26), ((SolidColorBrush)midnight.Resources["AppBackground"]).Color);
    });

    [Fact]
    public void The_picker_shows_one_card_per_theme_and_applies_the_chosen_one() => InSta(() =>
    {
        var applied = new List<string>();
        var view = new ThemePickerView("Midnight", theme => { applied.Add(theme); return Task.FromResult<string?>(null); });
        Assert.Equal(ThemeCatalog.All.Count, view.CardCount);
        var buttons = Buttons(view);
        Assert.Contains(buttons, b => (string)b.Content == "Current theme" && !b.IsEnabled);
        var vice = buttons.Single(b => AutomationProperties.GetName(b) == "Use theme Vice City");
        vice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(() => view.CurrentTheme == "Vice City");
        Assert.Equal(["Vice City"], applied);
        Assert.Contains(Buttons(view), b => AutomationProperties.GetName(b) == "Current theme: Vice City");
        var closed = new List<bool>();
        view.Completed += (_, changed) => closed.Add(changed);
        Buttons(view).Single(b => (string)b.Content == "Close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([true], closed);
    });

    [Fact]
    public void A_theme_that_cannot_be_saved_keeps_the_old_one_and_says_why() => InSta(() =>
    {
        var view = new ThemePickerView("Midnight", _ => Task.FromResult<string?>("Settings are read-only."));
        Buttons(view).Single(b => AutomationProperties.GetName(b) == "Use theme Crimson").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(() => !view.IsBusy);
        Assert.Equal("Midnight", view.CurrentTheme);
    });

    [Fact]
    public void Every_inline_view_with_a_leave_guard_is_accepted_by_the_drawer_host()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", "MainWindow.Navigation.cs"));
        var views = typeof(ThemePickerView).Assembly.GetTypes().Where(t => typeof(UserControl).IsAssignableFrom(t) && t.GetMethod("CanLeaveAsync") is not null && t.GetEvent("Completed") is not null).ToArray();
        Assert.Contains(typeof(ThemePickerView), views);
        foreach (var view in views) Assert.Contains(view.Name + " editor=>editor.CanLeaveAsync", host);
    }

    private static List<Button> Buttons(DependencyObject root)
    {
        var found = new List<Button>();
        void Walk(DependencyObject node)
        {
            if (node is Button b) found.Add(b);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        Walk(root);
        return found;
    }

    private static void Pump(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(10);
        }
        Assert.True(done());
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
