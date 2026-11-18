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
}
