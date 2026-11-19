using System.IO;
using System.Xml.Linq;

namespace GameVault.App.Tests;

public sealed class MainWindowStartupTests
{
    private static XElement Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "GameVault.App"))) root = root.Parent;
        return XDocument.Load(Path.Combine(root!.FullName, "src", "GameVault.App", "MainWindow.xaml")).Root!;
    }

    [Fact]
    public void Main_window_opens_maximized_and_keeps_its_minimum_size()
    {
        var window = Root();
        Assert.Equal("Maximized", (string?)window.Attribute("WindowState"));
        Assert.Equal("900", (string?)window.Attribute("MinWidth"));
        Assert.Equal("620", (string?)window.Attribute("MinHeight"));
    }

    [Fact]
    public void Restoring_from_minimized_returns_to_the_previous_state_instead_of_forcing_normal()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "GameVault.App"))) root = root.Parent;
        var tray = File.ReadAllText(Path.Combine(root!.FullName, "src", "GameVault.App", "MainWindow.Tray.cs"));
        Assert.Contains("WindowState=restoreState", tray);
        Assert.DoesNotContain("WindowState=WindowState.Normal", tray);
    }

    [Fact]
    public void Title_bar_has_history_buttons_a_breadcrumb_the_palette_and_a_now_playing_chip()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var window = Root();
        XElement Named(string name) => window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        Assert.Equal("NavBackClicked", (string?)Named("NavBackButton").Attribute("Click"));
        Assert.Equal("NavForwardClicked", (string?)Named("NavForwardButton").Attribute("Click"));
        Assert.Equal("OpenPaletteClicked", (string?)Named("PaletteButton").Attribute("Click"));
        Assert.Equal("NowPlayingClicked", (string?)Named("NowPlayingButton").Attribute("Click"));
        Assert.Equal("Collapsed", (string?)Named("NowPlayingButton").Attribute("Visibility"));
        Assert.NotNull(Named("BreadcrumbText"));
        // Every interactive title bar control must stay clickable inside the custom window chrome.
        foreach (var name in new[] { "NavBackButton", "NavForwardButton", "PaletteButton", "NowPlayingButton" })
            Assert.Equal("True", (string?)Named(name).Attribute(XName.Get("IsHitTestVisibleInChrome", "clr-namespace:System.Windows.Shell;assembly=PresentationFramework")) ??
                (string?)Named(name).Attributes().FirstOrDefault(a => a.Name.LocalName.EndsWith("IsHitTestVisibleInChrome"))?.Value);
    }

    [Fact]
    public void Messages_go_to_toasts_not_to_a_visible_status_line_and_the_palette_overlay_starts_closed()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var window = Root();
        XElement Named(string name) => window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        Assert.Equal("Collapsed", (string?)Named("OperationNotice").Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)Named("PaletteOverlay").Attribute("Visibility"));
        Assert.Equal("Polite", (string?)Named("ToastHost").Attribute(XName.Get("LiveSetting", "clr-namespace:System.Windows.Automation;assembly=UIAutomationClient")) ??
            (string?)Named("ToastHost").Attributes().FirstOrDefault(a => a.Name.LocalName.EndsWith("LiveSetting"))?.Value);
        Assert.NotNull(Named("PaletteInput")); Assert.NotNull(Named("PaletteList"));
    }
}