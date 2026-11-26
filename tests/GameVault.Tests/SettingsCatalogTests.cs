using GameVault.Core;

namespace GameVault.Tests;

public sealed class SettingsCatalogTests
{
    [Fact]
    public void EveryPageHasAUniqueKeyAndAtLeastOneOption()
    {
        Assert.Equal(SettingsCatalog.Categories.Count, SettingsCatalog.Categories.Select(c => c.Key).Distinct().Count());
        Assert.All(SettingsCatalog.Categories, c => Assert.Contains(SettingsCatalog.Entries, e => e.Category == c.Key));
        Assert.All(SettingsCatalog.Entries, e => Assert.True(SettingsCatalog.IsKnown(e.Category)));
    }

    [Theory]
    [InlineData("tray", "Keep running in the tray")]
    [InlineData("THEME", "Color theme")]
    [InlineData("shortcut", "Capture shortcuts")]
    [InlineData("zip library", "Back up GameVault data")]
    [InlineData("bug report", "Diagnostics package")]
    [InlineData("auto update", "Install updates automatically")]
    public void SearchFindsOptionsByTitleOrKeyword(string query, string expected) => Assert.Equal(expected, SettingsCatalog.Search(query)[0].Title);

    // The explanations moved to hover help (issue #77); the search must still find an option by their wording.
    [Theory]
    [InlineData("silent microphone", "Recorder, video and audio")]
    [InlineData("stutters", "Recorder, video and audio")]
    [InlineData("xbox overlay", "Capture shortcuts")]
    [InlineData("runtime administrator", "Capture troubleshooting")]
    [InlineData("cover automatically", "Game information sources")]
    [InlineData("replaced", "Restore from a backup")]
    [InlineData("replace the whole library", "Restore from a backup")]
    [InlineData("redacted", "Diagnostics package")]
    public void SearchStillFindsOptionsByTheHiddenExplanations(string query, string expected) =>
        Assert.Contains(SettingsCatalog.Search(query), entry => entry.Title == expected);

    [Fact]
    public void SearchNeedsEveryWordAndNothingTypedFindsNothing()
    {
        Assert.Empty(SettingsCatalog.Search(""));
        Assert.Empty(SettingsCatalog.Search("   "));
        Assert.Empty(SettingsCatalog.Search("tray elephant"));
        Assert.Contains("Updates", SettingsCatalog.Search("check at start")[0].ToString());
    }

    [Fact]
    public void ASettingsPageIsAPlaceInTheHistory()
    {
        Assert.Equal("Settings", NavBreadcrumb.Text(NavRoute.Settings, null));
        Assert.Equal("Settings › Updates", NavBreadcrumb.Text(NavRoute.SettingsPage(SettingsCatalog.Updates), null));
        var history = new NavigationHistory();
        history.Record(NavRoute.SettingsPage(SettingsCatalog.Appearance));
        history.Record(NavRoute.SettingsPage(SettingsCatalog.Backups));
        Assert.Equal(NavRoute.SettingsPage(SettingsCatalog.Appearance), history.Back(_ => true));
    }
}
