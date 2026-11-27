using GameVault.Core;

namespace GameVault.Tests;

/// <summary>How GameVault decides which folder holds a game's saves (issue #111).</summary>
public sealed class SaveFindingTests
{
    private static readonly string R = Path.Combine(Path.GetTempPath(), "GameVault finding", "root");
    private static string Under(params string[] parts) => Path.Combine([R, .. parts]);
    private static SaveCandidateFacts Facts(SaveCandidateSource source, bool named = false, bool saves = false, bool settingsOnly = false, bool written = false, int sessions = 0, bool shared = false) =>
        new(Under("x"), source, named, saves, settingsOnly, written, sessions, shared);

    [Fact]
    public void FrostpunkStoreNumberFolderIsHigh() => Assert.Equal(SaveConfidence.High, SaveCandidateScore.Of(Facts(SaveCandidateSource.StoreNumber, named: true, saves: true)));

    [Fact]
    public void TekkenUnrealFolderIsHigh() => Assert.Equal(SaveConfidence.High, SaveCandidateScore.Of(Facts(SaveCandidateSource.GameNamed, named: true, saves: true)));

    [Fact]
    public void SettingsOnlyFolderIsLow() => Assert.Equal(SaveConfidence.Low, SaveCandidateScore.Of(Facts(SaveCandidateSource.PublisherFolder, named: true, settingsOnly: true)));

    [Fact]
    public void UnnamedActivityFolderIsMedium() => Assert.Equal(SaveConfidence.Medium, SaveCandidateScore.Of(Facts(SaveCandidateSource.Activity, saves: true, written: true, sessions: 1)));

    [Fact]
    public void NamedActivityFolderIsHigh() => Assert.Equal(SaveConfidence.High, SaveCandidateScore.Of(Facts(SaveCandidateSource.Activity, named: true, saves: true, written: true, sessions: 1)));

    [Fact]
    public void WrittenWhileOtherGameRanIsPenalised() => Assert.NotEqual(SaveConfidence.High, SaveCandidateScore.Of(Facts(SaveCandidateSource.Activity, named: true, saves: true, written: true, sessions: 1, shared: true)));

    [Fact]
    public void NoSaveFilesIsNeverHigh() => Assert.NotEqual(SaveConfidence.High, SaveCandidateScore.Of(Facts(SaveCandidateSource.KnownRule, named: true, written: true, sessions: 3)));

    [Theory]
    [InlineData("config.bin")]
    [InlineData("GfxConfig.ini")]
    [InlineData("keybindings.settings")]
    [InlineData("settings.json")]
    [InlineData("pc_settings.bin")]
    public void SettingsNames(string name) { Assert.True(SaveFileNames.IsSettings(name)); Assert.False(SaveFileNames.IsSave(Under(name))); }

    [Theory]
    [InlineData("PLAYTHROUGH1.save")]
    [InlineData("autosave.save")]
    [InlineData("ProfileData.bin")]
    [InlineData("SGTA400")]
    [InlineData("slot1.sav")]
    public void SaveNames(string name) { Assert.False(SaveFileNames.IsSettings(name)); Assert.True(SaveFileNames.IsSave(Under(name))); }

    [Theory]
    [InlineData("iPhoneProfiles")]
    [InlineData("readme.txt")]
    public void OtherFilesAreNotSaves(string name) => Assert.False(SaveFileNames.IsSave(Under(name)));

    [Fact]
    public void LiftsToCommonFolder() => Assert.Equal([Under("HOODLUM", "323190", "remote")],
        SaveRootFinder.Lift([Under("HOODLUM", "323190", "remote", "saves", "a.save"), Under("HOODLUM", "323190", "remote", "ProfileData.bin")], [R]));

    [Fact]
    public void LiftSplitsPerTopFolder() => Assert.Equal([Under("A", "x"), Under("B", "y")],
        SaveRootFinder.Lift([Under("B", "y", "2.sav"), Under("A", "x", "1.sav")], [R]));

    [Fact]
    public void LiftNeverReturnsProtectedRoot()
    {
        Assert.Empty(SaveRootFinder.Lift([Under("loose.sav")], [R]));
        Assert.Empty(SaveRootFinder.Lift([Path.Combine(Path.GetTempPath(), "elsewhere", "a.sav")], [R]));
    }

    [Fact]
    public void LiftUsesTheDeepestRoot() => Assert.Equal([Under("Documents", "Studio")],
        SaveRootFinder.Lift([Under("Documents", "Studio", "a.sav")], [R, Under("Documents")]));

    [Fact]
    public void LiftPrefersSaveFilesOverOtherWritesInTheSameBranch() => Assert.Equal([Under("Game", "Saved", "SaveGames")],
        SaveRootFinder.Lift([Under("Game", "Saved", "SaveGames", "slot.sav"), Under("Game", "Saved", "Config", "GameUserSettings.ini")], [R]));

    [Theory]
    [InlineData("Temp/a.sav")]
    [InlineData("Microsoft/x/a.dat")]
    [InlineData("discord/Crashpad/settings.dat")]
    [InlineData("Google/Chrome/a.dat")]
    [InlineData("NVIDIA/DXCache/a.bin")]
    [InlineData("D3DSCache/a.bin")]
    [InlineData("Steam/htmlcache/a.bin")]
    [InlineData("Packages/SomeOther.App_x/LocalState/a.sav")]
    [InlineData("Studio/x.log")]
    [InlineData("Studio/x.tmp")]
    [InlineData("Studio/x.dmp")]
    [InlineData("Studio/x.etl")]
    [InlineData("Studio/db-journal")]
    public void IgnoresNoise(string relative) => Assert.True(SaveActivityFilter.Ignore(Under(relative.Split('/')), [R]));

    [Theory]
    [InlineData("HOODLUM/323190/remote/saves/a.save")]
    [InlineData("Steam/CODEX/883710/remote/a.bin")]
    [InlineData("Packages/11bitstudios.Frostpunk_x/LocalState/saves/a.save")]
    public void KeepsGameWrites(string relative) => Assert.False(SaveActivityFilter.Ignore(Under(relative.Split('/')), [R], GameNameTokens.For(["Frostpunk"])));

    [Fact]
    public void NoiseIsJudgedBelowTheWatchedFolderOnly()
    {
        var watched = Under("Temp", "watched");                   // the watched folder itself may sit under a folder with a noisy name
        Assert.False(SaveActivityFilter.Ignore(Path.Combine(watched, "Game", "a.sav"), [watched]));
        Assert.True(SaveActivityFilter.Ignore(Under("outside.sav"), [watched]));
    }

    [Fact]
    public void TokensMatch()
    {
        var tokens = GameNameTokens.For(["TEKKEN 8", "Frostpunk", "323190", "bin", "x", null]);
        Assert.True(GameNameTokens.Matches("Tekken8", tokens));
        Assert.True(GameNameTokens.Matches("FrostPunk", tokens));
        Assert.True(GameNameTokens.Matches("323190", tokens));
        Assert.False(GameNameTokens.Matches("Saves", tokens));
        Assert.False(GameNameTokens.Matches("bin", tokens));
    }
}
