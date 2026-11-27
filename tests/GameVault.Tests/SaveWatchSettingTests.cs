using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>The switch for watching saves while playing, and the words of the Saves tab (issue #111).</summary>
public sealed class SaveWatchSettingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault watch setting", Guid.NewGuid().ToString("N"));
    private ApplicationPaths Paths => ApplicationPaths.FromRoot(root);

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    [Fact]
    public void IsOnUntilTurnedOffAndIsKeptInItsOwnMarker()
    {
        Directory.CreateDirectory(root);
        var marker = Path.Combine(Paths.Data, "save-watch-off.marker");
        Assert.True(SaveWatchSetting.IsOn(Paths));
        SaveWatchSetting.Set(Paths, false);
        Assert.False(SaveWatchSetting.IsOn(Paths));
        Assert.True(File.Exists(marker));
        SaveWatchSetting.Set(Paths, false);
        SaveWatchSetting.Set(Paths, true);
        Assert.True(SaveWatchSetting.IsOn(Paths));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void SettingsListTheSwitchOnTheBackupsPage()
    {
        var entry = Assert.Single(SettingsCatalog.Entries, e => e.Target == "WatchSavesSwitch");
        Assert.Equal("Watch for saves while playing", entry.Title);
        Assert.Equal(SettingsCatalog.Backups, entry.Category);
    }

    [Theory]
    [InlineData(SaveSourceOrigin.KnownRule, "Found automatically · Steam app 1 folder", null, "Found automatically")]
    [InlineData(SaveSourceOrigin.KnownRule, "RE2 (2019): game-specific public save directory", null, "Known game location")]
    [InlineData(SaveSourceOrigin.Suggestion, "Game-named folder", null, "Needs confirmation")]
    [InlineData(SaveSourceOrigin.Confirmed, "Confirmed by you · Game-named folder", null, "Confirmed by you")]
    [InlineData(SaveSourceOrigin.Manual, "Selected manually in Edit game", null, "Chosen by you")]
    [InlineData(SaveSourceOrigin.Suggestion, "Steam app 1 folder", "Not this game's saves", "Not this game's saves")]
    public void RowsSayHowAFolderWasFound(SaveSourceOrigin origin, string evidence, string? message, string words)
    {
        var mapping = new SaveSourceMapping(Guid.NewGuid(), "C:/x", SaveSourceKind.Folder, origin, false, SaveSourceAccess.Ready, evidence, message, origin == SaveSourceOrigin.Suggestion && message is null);
        Assert.Equal(words, SaveSourceWords.Origin(mapping));
    }

    [Fact]
    public void AListedFolderThatCannotBeConfirmedSaysSo() => Assert.Equal("Listed only",
        SaveSourceWords.Origin(new(Guid.NewGuid(), "C:/x", SaveSourceKind.Folder, SaveSourceOrigin.Suggestion, false, SaveSourceAccess.Ready, "Publisher's game folder", null)));
}
