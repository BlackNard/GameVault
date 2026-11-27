using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace GameVault.App.Tests;

/// <summary>The status can be changed where the game is: its page and a card's menu (issue #94).</summary>
public sealed class StatusMenuTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Source(string file, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GameVault.App", file));

    [Fact]
    public void TheGamePageAndTheCardMenuOfferTheStatus()
    {
        var window = XDocument.Parse(Source("MainWindow.xaml"));
        var button = window.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "HeroStatusButton");
        Assert.Equal("HeroStatusClicked", (string?)button.Attribute("Click"));
        var menu = window.Descendants().Single(e => (string?)e.Attribute(X + "Key") == "GameContextMenu");
        Assert.Contains(menu.Elements(), e => (string?)e.Attribute("Header") == "Status");
        var code = Source("MainWindow.Status.cs");
        Assert.Contains("PlayStatuses.All", code);
        Assert.Contains("store.SetPlayStatusAsync(", code);
        // The open game page must not jump to another game when the new status leaves the current filter.
        Assert.Contains("StatusFilter.SelectedIndex = 0", code);
        Assert.Contains("FillStatusMenu(", Source("MainWindow.Overhaul.cs"));
    }
}
