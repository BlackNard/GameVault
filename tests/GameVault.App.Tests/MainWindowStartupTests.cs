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

    [Fact]
    public void Library_has_status_chips_a_layout_switch_and_card_and_row_templates_with_a_context_menu()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var window = Root();
        XElement Named(string name) => window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        var chips = window.Descendants(ns + "RadioButton").Where(e => (string?)e.Attribute("GroupName") == "StatusChips").Select(e => e.Attribute("Tag")!.Value).ToArray();
        Assert.Equal(new[] { "All", "Playing", "Completed", "OnHold", "NotStarted" }, chips);
        // the chips drive the original filter controls, which stay in the tree for the filtering code
        Assert.Equal(new[] { "All", "NotStarted", "Playing", "Completed", "OnHold" }.Order(), Named("StatusFilter").Elements().Select(e => e.Attribute("Tag")!.Value).Order());
        Assert.NotNull(Named("RemovedFilter")); Assert.NotNull(Named("ChipRemoved"));
        Assert.NotNull(Named("GridModeButton")); Assert.NotNull(Named("ListModeButton"));
        var templates = window.Descendants(ns + "DataTemplate").Select(e => (string?)e.Attribute(x + "Key")).Where(k => k is not null).ToArray();
        Assert.Contains("CardTemplate", templates); Assert.Contains("RowTemplate", templates);
        var menu = window.Descendants(ns + "ContextMenu").Single(e => (string?)e.Attribute(x + "Key") == "GameContextMenu");
        Assert.Equal(new[] { "Play", "Back up now", "Open install folder", "Edit game…", "Sessions & notes", "Remove from library" }, menu.Elements(ns + "MenuItem").Select(e => e.Attribute("Header")!.Value).ToArray());
    }

    [Fact]
    public void Overhaul_part_two_has_continue_row_views_settings_search_and_global_pages()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var window = Root();
        var names = window.Descendants().Select(e => (string?)e.Attribute(x + "Name")).Where(n => n is not null).ToHashSet();
        foreach (var name in new[] { "ContinueRow", "ContinueList", "ViewsButton", "SettingsSearchBox", "BackupsNav", "CapturesNav", "OverviewPanel", "OverviewList", "CaptureGroups", "OverviewEmpty" })
            Assert.Contains(name, names);
        Assert.Equal("Collapsed", (string?)window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "OverviewPanel").Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ContinueRow").Attribute("Visibility"));
    }

    [Fact]
    public void Every_settings_menu_entry_scrolls_to_a_real_section_and_editors_open_in_a_side_drawer()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var window = Root();
        var names = window.Descendants().Select(e => (string?)e.Attribute(x + "Name")).Where(n => n is not null).ToHashSet();
        var settings = window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "SettingsPanel");
        var tags = settings.Descendants(ns + "Button").Where(e => (string?)e.Attribute("Click") == "SettingsSectionClicked").Select(e => e.Attribute("Tag")!.Value).ToArray();
        Assert.Equal(5, tags.Length);
        Assert.All(tags, tag => Assert.Contains(tag, names));
        var drawer = window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "DrawerLayer");
        Assert.Equal("Collapsed", (string?)drawer.Attribute("Visibility"));
        Assert.Contains(drawer.Descendants(), e => (string?)e.Attribute(x + "Name") == "EditorHost");
        Assert.NotNull(window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ContentRoot"));
        foreach (var name in new[] { "RecentList", "LibraryNav", "SettingsNav", "QuitButton", "HeroBackupButton", "OpenFolderButton" }) Assert.Contains(name, names);
    }

    [Fact]
    public void The_drawer_is_700_wide_and_the_cover_search_can_take_the_whole_window()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var panel = Root().Descendants().Single(e => (string?)e.Attribute(x + "Name") == "DrawerPanel");
        Assert.Equal("700", (string?)panel.Attribute("Width"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "GameVault.App"))) root = root.Parent;
        var editor = File.ReadAllText(Path.Combine(root!.FullName, "src", "GameVault.App", "GameEditor.xaml.cs"));
        Assert.Contains("FullWidthChanged?.Invoke(this, true)", editor);   // artwork search opened
        Assert.Contains("FullWidthChanged?.Invoke(this, false)", editor);  // and closed
        var window = File.ReadAllText(Path.Combine(root.FullName, "src", "GameVault.App", "MainWindow.Overhaul.cs"));
        Assert.Contains("double.NaN", window);                              // full width = no fixed width
    }
}