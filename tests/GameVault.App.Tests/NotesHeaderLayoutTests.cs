using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using GameVault.App;

namespace GameVault.App.Tests;

public sealed class NotesHeaderLayoutTests
{
    [Theory]
    [InlineData("Midnight", 720)]
    [InlineData("Crimson", 1200)]
    [InlineData("Vice City", 900)]
    public void Header_actions_share_normal_height_and_statistics_stay_together(string theme, double width) => InSta(() =>
    {
        var header = LoadHeader(theme);
        ((TextBlock)header.FindName("GameHeading")).Text = "A game already identified by the page heading";
        ((TextBlock)header.FindName("TotalTime")).Text = "128h 34m";
        ((TextBlock)header.FindName("SessionCount")).Text = "128";
        ((Button)header.FindName("LongestButton")).Content = "2h 35m";
        ((TextBlock)header.FindName("LastPlayed")).Text = "11/14/2026 7:30 PM";
        ((ComboBox)header.FindName("StatusChoice")).Items.Add("Completed");
        ((ComboBox)header.FindName("StatusChoice")).SelectedIndex = 0;
        header.Measure(new Size(width, 400));
        header.Arrange(new Rect(0, 0, width, header.DesiredSize.Height));
        header.UpdateLayout();

        var status = (ComboBox)header.FindName("StatusChoice");
        var export = (Button)header.FindName("ExportButton");
        Assert.Equal(40, status.ActualHeight);
        Assert.Equal(40, export.ActualHeight);
        Assert.Equal(status.TranslatePoint(new Point(), header).Y, export.TranslatePoint(new Point(), header).Y);
        Assert.Equal(Visibility.Collapsed, ((TextBlock)header.FindName("GameHeading")).Visibility);
        foreach (var name in new[] { "SessionsTab", "ActivityTab", "ReviewTab", "RefreshButton" })
            Assert.Equal(40, ((Button)header.FindName(name)).ActualHeight);
        var last = (TextBlock)header.FindName("LastPlayed");
        Assert.InRange(last.TranslatePoint(new Point(), header).X + last.ActualWidth, 1, width); // never clipped at the right edge
    });

    [Fact]
    public void Longest_session_is_a_borderless_keyboard_action_with_an_explicit_purpose() => InSta(() =>
    {
        var header = LoadHeader("Midnight");
        var longest = (Button)header.FindName("LongestButton");
        longest.ApplyTemplate();
        Assert.Equal(new Thickness(0), longest.BorderThickness);
        Assert.True(longest.Focusable);
        Assert.True(longest.IsTabStop);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(longest)));
        Assert.False(string.IsNullOrWhiteSpace(longest.ToolTip?.ToString()));
        var document = Source();
        Assert.Equal("SelectLongest", (string?)document.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "LongestButton").Attribute("Click"));
    });

    [Fact]
    public void Session_sort_selector_uses_the_same_normal_control_height() => InSta(() =>
    {
        var toolbar = LoadFragment(Source().Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TableToolbar"), "Midnight");
        toolbar.Measure(new Size(720, 100));
        toolbar.Arrange(new Rect(0, 0, 720, toolbar.DesiredSize.Height));
        Assert.Equal(40, ((ComboBox)toolbar.FindName("SortChoice")).ActualHeight);
    });

    [Fact]
    public void Sessions_page_has_one_control_row_a_roomy_session_list_and_a_side_note_pane()
    {
        var document = Source();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        // One tab level: the three views and the page actions share the first row.
        var tabs = new[] { "SessionsTab", "ActivityTab", "ReviewTab" }.Select(n => Named(n).Parent).Distinct().ToArray();
        Assert.Single(tabs);
        Assert.Same(Named("StatusChoice").Parent, Named("RefreshButton").Parent);
        Assert.Same(Named("StatusChoice").Parent, Named("ExportButton").Parent);
        // The list is a full-height pane beside the note, not a fixed 160px strip.
        Assert.DoesNotContain(document.Descendants(), e => (string?)e.Attribute(x + "Name") == "SessionsRow");
        Assert.Equal(2, Named("Fields").Elements().Single(e => e.Name.LocalName == "Grid.ColumnDefinitions").Elements().Count());
        Assert.Same(Named("SessionsPane"), Named("SessionList").Parent);
        Assert.DoesNotContain(document.Descendants(), e => (string?)e.Attribute("Height") == "160");
        // Compact list columns; per-session detail (offsets, measured time) lives with the selected note.
        var headers = Named("SessionList").Descendants().Where(e => e.Name.LocalName == "DataGridTextColumn").Select(e => (string?)e.Attribute("Header")).ToArray();
        Assert.Equal(new[] { "Date", "Time", "Played", "Note" }, headers);
        Assert.Equal("SortChoice", Named("TableToolbar").Descendants().First(e => e.Name.LocalName == "ComboBox").Attribute(x + "Name")!.Value);
    }

    private static FrameworkElement LoadHeader(string theme)
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = Source();
        var grid = document.Descendants(ns + "Grid").Single(e => e.Parent?.Name == ns + "ScrollViewer" && e.Attribute("MinHeight") is not null);
        var fragment = new XElement(ns + "Grid", new XAttribute(XNamespace.Xmlns + "x", x),
            new XElement(ns + "Grid.RowDefinitions", Enumerable.Range(0, 2).Select(_ => new XElement(ns + "RowDefinition", new XAttribute("Height", "Auto")))),
            grid.Elements().Where(e => e.Name != ns + "Grid.RowDefinitions").Take(2).Select(e => new XElement(e)));
        return LoadFragment(fragment, theme);
    }

    private static FrameworkElement LoadFragment(XElement source, string theme)
    {
        // Measure the production layout without constructing NotesView, stores, or a Window.
        var fragment = new XElement(source);
        foreach (var attribute in fragment.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "Click" or "SelectionChanged" or "SettingHelp.Text").ToArray()) attribute.Remove();
        var element = (FrameworkElement)XamlReader.Parse(fragment.ToString());
        System.Windows.Documents.TextElement.SetFontSize(element, 14);
        ThemePalette.Apply(element, theme);
        element.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/GameVault;component/Styles.xaml", UriKind.Relative)));
        return element;
    }

    private static XDocument Source()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "GameVault.App"))) root = root.Parent;
        return XDocument.Load(Path.Combine(root!.FullName, "src", "GameVault.App", "NotesView.xaml"));
    }

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Headless layout measurement timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
