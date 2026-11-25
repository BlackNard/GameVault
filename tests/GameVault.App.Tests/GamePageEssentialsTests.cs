using System.IO;

namespace GameVault.App.Tests;

public sealed class GamePageEssentialsTests
{
    private static string Source(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", name));
    }

    [Fact]
    public void The_overview_shows_the_essentials_and_keeps_the_rest_in_closed_sections()
    {
        var xaml = Source("MainWindow.xaml");
        var overview = xaml[xaml.IndexOf("x:Name=\"OverviewSection\"", StringComparison.Ordinal)..xaml.IndexOf("x:Name=\"SavesSection\"", StringComparison.Ordinal)];
        foreach (var name in new[] { "AttentionCard", "AboutText", "ReadMoreButton", "AboutFacts", "TimeToBeatCards", "TimeProgressBar", "TimeToBeatLink", "TagsChips", "EditTagsButton", "AboutDetails", "ManageInfo", "RefreshInfoButton", "EditDescriptionButton", "WrongGameButton" })
            Assert.Contains("x:Name=\"" + name + "\"", overview);
        Assert.DoesNotContain("Your game, at a glance", overview);
        Assert.DoesNotContain("Save backup status", overview);
        var details = Source("MainWindow.AboutDetails.cs");
        foreach (var section in new[] { "More details", "Store tags, features and languages", "Full description", "System requirements", "Links" }) Assert.Contains(section, details);
        Assert.Contains("section.IsExpanded = openSections.Contains(key);", details);        // closed until the player opens it
        var facts = Source("MainWindow.Metadata.cs");
        Assert.Contains("RenderAboutDetails(document, more);", facts);
        foreach (var label in new[] { "Publisher", "Install size", "Rated", "Platforms", "Metacritic", "Player reviews", "Achievements", "DLC", "Installed" })
            Assert.Contains("Extra(\"" + label + "\"", facts);                              // nothing that was shown before is lost
        Assert.Contains("BackupPhase.Blocked or BackupPhase.Failed", Source("MainWindow.xaml.cs"));
    }
}
