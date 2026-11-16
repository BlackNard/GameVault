using GameVault.Infrastructure.Launching;

namespace GameVault.Tests;

public sealed class StoreGameLauncherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault store tests", Guid.NewGuid().ToString("N"));
    private string FileAt(string relative, string text = "fixture") { var path = Path.GetFullPath(Path.Combine(root, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path; }
    private StoreGameLauncher Launcher() => new(Path.Combine(root, "EpicManifests"), _ => throw new InvalidOperationException("Tests must never activate a store"));

    [Fact] public void SteamMetadataResolvesDetachedUnrealGameAndNumericActivation()
    {
        var selected = FileAt("steamapps/common/TEKKEN 8/TEKKEN8.exe");
        var actual = FileAt("steamapps/common/TEKKEN 8/Polaris/Binaries/Win64/Polaris-Win64-Shipping.exe");
        FileAt("steamapps/appmanifest_1778820.acf", "\"AppState\" { \"appid\" \"1778820\" \"installdir\" \"TEKKEN 8\" }");
        var plan = Assert.IsType<StoreLaunchPlan>(Launcher().Resolve(selected, null));
        Assert.Equal("steam://run/1778820", plan.ActivationUri); Assert.Equal(actual, plan.TargetExecutablePath); Assert.Equal("Steam:1778820", plan.StoreKey);
    }
    [Fact] public void ExplicitTargetWinsAndMultipleUnrealTargetsRequireSelection()
    {
        var selected = FileAt("steamapps/common/Game/start.exe");
        var one = FileAt("steamapps/common/Game/One/Binaries/Win64/One-Win64-Shipping.exe");
        FileAt("steamapps/common/Game/Two/Binaries/Win64/Two-Win64-Shipping.exe");
        FileAt("steamapps/appmanifest_10.acf", "\"AppState\" { \"appid\" \"10\" \"installdir\" \"Game\" }");
        Assert.Throws<IOException>(() => Launcher().Resolve(selected, null));
        Assert.Equal(one, Launcher().Resolve(selected, one)!.TargetExecutablePath);
    }
    [Fact] public void EpicManifestUsesMatchedInstallPathAndLaunchExecutable()
    {
        var selected = FileAt("Epic Game/start.exe"); var actual = FileAt("Epic Game/bin/game.exe");
        FileAt("EpicManifests/game.item", System.Text.Json.JsonSerializer.Serialize(new { InstallLocation = Path.GetDirectoryName(selected), LaunchExecutable = "bin/game.exe" }));
        var plan = Assert.IsType<StoreLaunchPlan>(Launcher().Resolve(selected, null));
        Assert.Equal(actual, plan.TargetExecutablePath); Assert.Equal("com.epicgames.launcher://apps/" + Uri.EscapeDataString(Path.GetDirectoryName(selected)!) + "?action=launch&silent=true", plan.ActivationUri);
    }
    [Fact] public void MalformedOrAmbiguousMetadataNeverChoosesAStore()
    {
        var selected = FileAt("steamapps/common/Game/start.exe");
        FileAt("steamapps/appmanifest_10.acf", "\"AppState\" { \"appid\" \"10;evil\" \"installdir\" \"Game\" }");
        Assert.Null(Launcher().Resolve(selected, null));
        FileAt("steamapps/appmanifest_10.acf", "\"AppState\" { \"appid\" \"10\" \"installdir\" \"Game\" }");
        FileAt("steamapps/appmanifest_11.acf", "\"AppState\" { \"appid\" \"11\" \"installdir\" \"Game\" }");
        Assert.Throws<IOException>(() => Launcher().Resolve(selected, null));
    }
    [Fact] public void ActivationRejectsUnvalidatedUriWithoutInvokingShell()
    {
        var selected = FileAt("game.exe"); var invoked = false;
        var launcher = new StoreGameLauncher(Path.Combine(root, "EpicManifests"), _ => invoked = true);
        Assert.Throws<ArgumentException>(() => launcher.RequestLaunch(new(StoreProvider.Steam, "Steam:10", "https://evil.invalid", root, selected)));
        Assert.False(invoked);
    }
    [Theory]
    [InlineData("\"AppState\" { \"appid\" \"10\" \"appid\" \"11\" \"installdir\" \"Game\" }")]
    [InlineData("\"AppState\" { \"appid\" \"11\" \"installdir\" \"Game\" }")]
    [InlineData("\"AppState\" { \"appid\" \"10\" \"installdir\" \"../Game\" }")]
    [InlineData("\"AppState\" { \"appid\" \"10\" \"installdir\" \"Game\"")]
    public void InvalidSteamMetadataCannotActivateAStore(string metadata)
    {
        var selected = FileAt("steamapps/common/Game/start.exe"); FileAt("steamapps/appmanifest_10.acf", metadata);
        Assert.Null(Launcher().Resolve(selected, null));
    }
    [Theory]
    [InlineData("\"LauncherPath\" \"\"")]
    [InlineData("\"UserConfig\" { \"BetaKey\" \"\" \"arbitrary\" \"{\" \"other\" \"}\" }")]
    public void ValidSteamMetadataMayContainEmptyUnrelatedProperties(string properties)
    {
        var selected = FileAt("steamapps/common/Game/game.exe");
        FileAt("steamapps/appmanifest_10.acf", "\"AppState\" { \"appid\" \"10\" \"installdir\" \"Game\" " + properties + " }");
        Assert.Equal(selected, Launcher().Resolve(selected, null)!.TargetExecutablePath);
    }
    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("missing.exe")]
    public void EpicMetadataCannotSelectOutsideOrMissingExecutable(string launch)
    {
        var selected = FileAt("Epic Game/start.exe"); FileAt("outside.exe");
        FileAt("EpicManifests/game.item", System.Text.Json.JsonSerializer.Serialize(new { InstallLocation = Path.GetDirectoryName(selected), LaunchExecutable = launch }));
        Assert.Null(Launcher().Resolve(selected, null));
    }
    [Fact] public void OversizedAndDuplicateEpicMetadataAreRejected()
    {
        var selected = FileAt("Epic Game/start.exe");
        var location = System.Text.Json.JsonSerializer.Serialize(Path.GetDirectoryName(selected));
        FileAt("EpicManifests/game.item", "{\"InstallLocation\":" + location + ",\"InstallLocation\":" + location + ",\"LaunchExecutable\":\"start.exe\"}");
        Assert.Null(Launcher().Resolve(selected, null));
        FileAt("EpicManifests/game.item", new string(' ', 256 * 1024 + 1)); Assert.Null(Launcher().Resolve(selected, null));
    }
    [Fact] public void MetadataInspectionDoesNotScanBeyondCountLimit()
    {
        var selected = FileAt("steamapps/common/Game/start.exe");
        for (var i = 1; i <= 257; i++) FileAt($"steamapps/appmanifest_{i}.acf", "fixture");
        Assert.Throws<IOException>(() => Launcher().Resolve(selected, null));
    }
    [Fact] public void ValidatedProtocolActivationUsesOnlyGeneratedUri()
    {
        var selected = FileAt("steamapps/common/Game/start.exe"); FileAt("steamapps/appmanifest_10.acf", "\"AppState\" { \"appid\" \"10\" \"installdir\" \"Game\" }");
        string? requested = null; var launcher = new StoreGameLauncher(Path.Combine(root, "EpicManifests"), uri => requested = uri);
        launcher.RequestLaunch(launcher.Resolve(selected, null)!); Assert.Equal("steam://run/10", requested);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
