using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace GameVault.App.Tests;

/// <summary>Explanations appear on hover or keyboard focus instead of being written on every screen (issue #102).</summary>
public sealed class HoverHelpTests
{
    private static string Source(string file, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GameVault.App", file));

    [Theory]
    [InlineData("BackupsView.xaml", "Revisit your save history")]
    [InlineData("BackupsView.xaml", "never removed by ordinary retention")]
    [InlineData("GameEditor.xaml", "Choose your installed game executable")]
    [InlineData("GameEditor.xaml", "Optional manual overrides")]
    [InlineData("GameEditor.xaml", "A copy is stored in this game")]
    [InlineData("LaunchSettingsWindow.xaml", "GameVault detects Steam and Epic")]
    [InlineData("LaunchSettingsWindow.xaml", "Other ways to start this game")]
    [InlineData("LaunchSettingsWindow.xaml", "Optional programs GameVault runs")]
    [InlineData("MainWindow.xaml", "not your own playtime")]
    [InlineData("MainWindow.xaml", "Up to 12 tags of 30 characters")]
    [InlineData("RestoreView.xaml", "Review each path")]
    [InlineData("SaveSourcesView.xaml", "Original files stay in place")]
    [InlineData("CoverSearchView.xaml", "Large images are center cropped")]
    [InlineData("WebCoverSearchView.xaml", "Artwork belongs to its owner")]
    public void AnExplanationIsHoverHelpOnly(string file, string words)
    {
        var holding = XDocument.Parse(Source(file)).Descendants().SelectMany(e => e.Attributes()).Where(a => a.Value.Contains(words, StringComparison.Ordinal)).ToArray();
        Assert.Equal("SettingHelp.Text", Assert.Single(holding).Name.LocalName);
    }

    /// <summary>What is sent, replaced or removed stays written on the page.</summary>
    [Theory]
    [InlineData("CoverSearchView.xaml", "Only the title you enter is sent to Steam.")]
    [InlineData("WebCoverSearchView.xaml", "Only the entered title is sent to Google")]
    [InlineData("RestoreView.xaml", "Your current saves will be preserved first")]
    [InlineData("RestoreView.xaml", "Select a file only if you want it removed")]
    [InlineData("SaveSourcesView.xaml", "Finding a source alone does not protect a save")]
    public void WarningsStayOnThePage(string file, string words) =>
        Assert.Contains(XDocument.Parse(Source(file)).Descendants().Attributes("Text"), a => a.Value.Contains(words, StringComparison.Ordinal));

    [Fact]
    public void FormFieldsHaveShortNamesWithTheirHelpOnHover()
    {
        var playthrough = Source("MainWindow.Playthroughs.cs");
        Assert.DoesNotContain("year-month-day, optional)", playthrough);
        Assert.Contains("form.Text(\"Hours\"", playthrough);
        Assert.Contains("help: \"Leave empty to count the recorded sessions between the dates.\"", playthrough);
        Assert.Contains("form.Text(\"Tags\"", Source("MainWindow.Journal.cs"));
        Assert.Contains("(looked up online)", Source("MainWindow.Lists.cs"));
        Assert.Contains("SettingHelp.SetText(caption, help); SettingHelp.SetText(control, help);", Source("FormView.cs"));
    }
}
