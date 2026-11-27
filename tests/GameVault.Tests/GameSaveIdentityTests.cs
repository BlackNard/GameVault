using GameVault.Core;
using GameVault.Infrastructure.Saves;

namespace GameVault.Tests;

/// <summary>What a game's own folder says about where it may save (issue #111).</summary>
public sealed class GameSaveIdentityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault identity", Guid.NewGuid().ToString("N"));
    private string Install => Path.Combine(root, "Frostpunk");

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private void Put(string relative, string content)
    {
        var path = Path.Combine(Install, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private GameSaveIdentity Read(string exe = "Frostpunk.exe", string name = "Frostpunk")
    {
        Put(exe, "fixture only; never executed");
        var path = Path.Combine(Install, exe);
        return GameSaveIdentity.Read(new(1, Guid.NewGuid(), name, path, Path.GetDirectoryName(path)!, [], GameStatus.Playing, 20, false, false));
    }

    [Fact]
    public void ReadsTheSteamNumberFile() { Put("steam_appid.txt", "323190\r\n"); Assert.Equal("323190", Read().SteamAppId); }

    [Theory]
    [InlineData("hlm.ini")]
    [InlineData("steam_emu.ini")]
    [InlineData("cream_api.ini")]
    public void ReadsTheSteamNumberFromAReplacementSettingsFile(string file)
    {
        Put(file, "[GameSettings]\r\nUserName=someone\r\nAppId = 323190\r\n");
        Assert.Equal("323190", Read().SteamAppId);
    }

    [Fact]
    public void ReadsTheSteamNumberOneFolderDown() { Put(Path.Combine("steam_settings", "steam_appid.txt"), "323190"); Assert.Equal("323190", Read().SteamAppId); }

    [Fact]
    public void LooksAtTheInstallFolderAboveAnUnrealStyleProgramFolder()
    {
        Put("steam_appid.txt", "323190");
        var identity = Read(Path.Combine("Binaries", "Win64", "Game-Win64-Shipping.exe"));
        Assert.Equal("323190", identity.SteamAppId);
        Assert.Contains("frostpunk", identity.Tokens);
    }

    [Fact]
    public void ReadsGogAndEpicIds()
    {
        Put("goggame-1648559910.info", "{}");
        Put(Path.Combine(".egstore", "A1.mancpn"), "{ \"FormatVersion\": 0, \"AppName\": \"Fennec\" }");
        var identity = Read();
        Assert.Equal("1648559910", identity.GogId);
        Assert.Equal("Fennec", identity.EpicApp);
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData("12345678901234567890")]
    public void IgnoresAnUnusableNumber(string content) { Put("steam_appid.txt", content); Assert.Null(Read().SteamAppId); }

    [Fact]
    public void SkipsOversizedAndBrokenFiles()
    {
        Put("steam_appid.txt", "323190" + new string(' ', 2 * 1024 * 1024));
        Put(Path.Combine(".egstore", "A1.mancpn"), "{ broken");
        var identity = Read();
        Assert.Null(identity.SteamAppId);
        Assert.Null(identity.EpicApp);
    }

    [Fact]
    public void TokensCarryTheNamesAndTheNumber()
    {
        Put("steam_appid.txt", "323190");
        var identity = Read(name: "Frostpunk Complete");
        Assert.Contains("frostpunk", identity.Tokens);
        Assert.Contains("frostpunkcomplete", identity.Tokens);
        Assert.Contains("323190", identity.Tokens);
    }

    [Fact]
    public void ReadsPublisherAndProductFromTheProgram()
    {
        var program = typeof(GameSaveIdentityTests).Assembly.Location;
        var identity = GameSaveIdentity.Read(new(1, Guid.NewGuid(), "Anything", program, Path.GetDirectoryName(program)!, [], GameStatus.Playing, 20, false, false));
        Assert.Equal("GameVault.Tests", identity.Product);
        Assert.Equal("GameVault.Tests", identity.Company);
    }

    [Fact]
    public void AMissingProgramGivesTheNamesOnly()
    {
        var identity = GameSaveIdentity.Read(new(1, Guid.NewGuid(), "Gone Game", Path.Combine(root, "none", "gone.exe"), Path.Combine(root, "none"), [], GameStatus.Playing, 20, false, false));
        Assert.Null(identity.SteamAppId);
        Assert.Contains("gonegame", identity.Tokens);
    }
}
