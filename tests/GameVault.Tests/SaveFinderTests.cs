using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>The search before play: a game's saves are found by its store number, publisher and name, and confident finds are selected (issue #111).</summary>
public sealed class SaveFinderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault finder", Guid.NewGuid().ToString("N"));
    private readonly Guid id = Guid.NewGuid();
    private ApplicationPaths Paths => ApplicationPaths.FromRoot(Path.Combine(root, "app"));
    private AppDataStore Store => new(Paths);
    private SaveDiscoveryEnvironment Env => new(Path.Combine(root, "user"), Path.Combine(root, "user", "Documents"), Path.Combine(root, "user", "Saved Games"),
        Path.Combine(root, "user", "AppData", "Roaming"), Path.Combine(root, "user", "AppData", "Local"), Path.Combine(root, "Public", "Documents"), [Path.Combine(root, "Steam")]);
    private SaveDiscoveryService Service => new(Paths, Store, Env);
    private string Install => Path.Combine(root, "Games", "Frostpunk");
    private GameProfile Profile => new(1, id, "Frostpunk", Path.Combine(Install, "Frostpunk.exe"), Install, [], GameStatus.Playing, 20, false, false);
    private string Hoodlum => Path.Combine(Env.Roaming, "HOODLUM", "323190", "remote");

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private static string Put(string folder, string file = "slot.sav", string content = "synthetic")
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, file), content);
        return folder;
    }

    private async Task SeedAsync(GameProfile? profile = null, string? appId = "323190")
    {
        Directory.CreateDirectory(Paths.Root);
        profile ??= Profile;
        Put(Path.GetDirectoryName(profile.ExecutablePath)!, Path.GetFileName(profile.ExecutablePath), "fixture only; never executed");
        if (appId is not null) Put(Path.GetDirectoryName(profile.ExecutablePath)!, "steam_appid.txt", appId);
        await Store.InitializeAsync();
        await Store.SaveProfileAsync(profile);
    }

    private static SaveSourceMapping[] Selected(SaveDiscoveryResult result) => [.. result.Document.Value!.Sources.Where(s => s.Selected)];

    [Fact]
    public async Task FindsHoodlumFolderByAppNumber()
    {
        await SeedAsync();
        Put(Path.Combine(Hoodlum, "saves"), "PLAYTHROUGH1.save");
        Put(Hoodlum, "ProfileData.bin");
        var result = await Service.ScanAsync(id);
        var source = Assert.Single(Selected(result));
        Assert.Equal(Hoodlum, source.Path);
        Assert.Equal(SaveSourceOrigin.KnownRule, source.Origin);
        Assert.StartsWith("Found automatically", source.Evidence);
        Assert.Equal(SaveDiscoveryPhase.SourcesFound, result.Phase);
        Assert.Single(await Service.RequireVerifiedSourcesAsync(id));
    }

    [Theory]
    [InlineData("roaming", "Goldberg SteamEmu Saves/323190/remote")]
    [InlineData("roaming", "GSE Saves/323190/remote")]
    [InlineData("roaming", "HOODLUM/323190/remote")]
    [InlineData("roaming", "EMPRESS/323190/remote")]
    [InlineData("roaming", "FLT/323190")]
    [InlineData("roaming", "OnlineFix/323190")]
    [InlineData("public", "Steam/CODEX/323190/remote")]
    [InlineData("public", "Steam/RUNE/323190/remote")]
    [InlineData("public", "OnlineFix/323190")]
    [InlineData("public", "CPY_SAVES/323190")]
    [InlineData("programdata", "Steam/RLD!/323190")]
    [InlineData("steam", "userdata/1001/323190/remote")]
    public async Task FindsEveryStoreNumberLayout(string place, string relative)
    {
        await SeedAsync();
        var baseFolder = place switch { "roaming" => Env.Roaming, "public" => Env.PublicDocuments, "programdata" => Env.ProgramData, _ => Env.SteamRoots[0] };
        var folder = Put(Path.Combine([baseFolder, .. relative.Split('/')]));
        Assert.Equal(folder, Assert.Single(Selected(await Service.ScanAsync(id))).Path);
    }

    [Fact]
    public async Task FindsUnrealFolderAndSelectsIt()
    {
        var install = Path.Combine(root, "Games", "TEKKEN 8");
        await SeedAsync(Profile with { Name = "TEKKEN 8", ExecutablePath = Path.Combine(install, "TEKKEN 8.exe"), WorkingDirectory = install }, null);
        var saves = Put(Path.Combine(Env.Local, "TEKKEN 8", "Saved", "SaveGames"));
        Assert.Equal(saves, Assert.Single(Selected(await Service.ScanAsync(id))).Path);
    }

    [Fact]
    public async Task ASettingsOnlyFolderIsNeitherSelectedNorAskedAbout()
    {
        await SeedAsync();
        var settings = Put(Path.Combine(Env.Roaming, "11bitstudios", "Frostpunk"), "config.bin");
        Put(settings, "iPhoneProfiles");
        var result = await Service.ScanAsync(id);
        Assert.Empty(Selected(result));
        Assert.DoesNotContain(result.Document.Value!.Sources, s => s.CanConfirm);
    }

    [Fact]
    public async Task NameSearchFindsADeepFolderAndSelectsOnlyWhatIsCertain()
    {
        await SeedAsync(appId: null);
        var deep = Put(Path.Combine(Env.Documents, "My Games", "Studio", "Frostpunk"), "save1.sav");
        var result = await Service.ScanAsync(id);
        var source = Assert.Single(result.Document.Value!.Sources, s => s.Path == deep);
        Assert.False(source.Selected);                       // a name match alone is asked about
        Assert.True(source.CanConfirm);
        Assert.Equal(SaveDiscoveryPhase.NeedsConfirmation, result.Phase);
    }

    [Fact]
    public void NameSearchIsBounded()
    {
        for (var i = 0; i < 2100; i++) Directory.CreateDirectory(Path.Combine(root, "wide", "folder" + i));
        var (found, visited) = SaveFinder.SearchByName([Path.Combine(root, "wide")], GameNameTokens.For(["Frostpunk"]), TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Empty(found);
        Assert.InRange(visited, 1, 2000);
    }

    [Fact]
    public void NameSearchStopsAtDepthThree()
    {
        var near = Path.Combine(root, "tree", "a", "b", "Frostpunk");
        var far = Path.Combine(root, "tree", "a", "b", "c", "Frostpunk");
        Directory.CreateDirectory(near); Directory.CreateDirectory(far);
        var (found, _) = SaveFinder.SearchByName([Path.Combine(root, "tree")], GameNameTokens.For(["Frostpunk"]), TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal([near], found);
    }

    [Fact]
    public async Task AFolderWrittenDuringPlayIsSelected()
    {
        await SeedAsync(appId: null);
        var odd = Put(Path.Combine(Env.Roaming, "Studio", "Frostpunk Data"));
        Assert.Empty(Selected(await Service.ScanAsync(id)));
        await Store.UpdateSaveFindsAsync(id, d => d with { Finds = [new(odd, 1, 1, DateTimeOffset.UtcNow, false)] });
        var result = await Service.ScanAsync(id);
        var source = Assert.Single(result.Document.Value!.Sources, s => s.Path == odd);
        Assert.True(source.CanConfirm);                      // not named after the game: asked about
        var named = Put(Path.Combine(Env.Roaming, "Studio2", "x", "y", "Frostpunk"));
        await Store.UpdateSaveFindsAsync(id, d => d with { Finds = [new(named, 1, 1, DateTimeOffset.UtcNow, false)] });
        Assert.Equal(named, Assert.Single(Selected(await Service.ScanAsync(id))).Path);
    }

    [Fact]
    public async Task RejectedPathIsNeverSelected()
    {
        await SeedAsync();
        Put(Path.Combine(Hoodlum, "saves"), "a.save");
        var service = Service;
        var first = Assert.Single(Selected(await service.ScanAsync(id)));
        await Store.UpdateSaveFindsAsync(id, d => d with { Finds = [new(Path.Combine(Hoodlum, "saves"), 1, 3, DateTimeOffset.UtcNow, false)] });
        var rejected = await service.RejectAsync(id, first.Id);
        Assert.Empty(Selected(rejected));
        for (var i = 0; i < 2; i++) rejected = await service.ScanAsync(id);
        Assert.Empty(Selected(rejected));                                    // not by store number, play activity or the parent folder
        Assert.DoesNotContain(rejected.Document.Value!.Sources, s => s.CanConfirm);
        Assert.Contains(rejected.Document.Value.Sources, s => s.Path == Hoodlum && s.Message == "Not this game's saves");
        var restored = await service.RestoreAsync(id, Hoodlum);
        Assert.Equal(Hoodlum, Assert.Single(Selected(restored)).Path);
    }

    [Fact]
    public async Task ARejectedFolderTheyHadConfirmedStaysUnselected()
    {
        await SeedAsync(appId: null);
        var deep = Put(Path.Combine(Env.Documents, "My Games", "Studio", "Frostpunk"), "save1.sav");
        var service = Service;
        var asked = (await service.ScanAsync(id)).Document.Value!.Sources.Single(s => s.Path == deep);
        await service.ConfirmAsync(id, asked.Id);
        Assert.Empty(Selected(await service.RejectAsync(id, asked.Id)));
        Assert.Empty(Selected(await service.ScanAsync(id)));
    }

    [Fact]
    public async Task ManualPathWins()
    {
        var manual = Put(Path.Combine(root, "manual saves"));
        await SeedAsync(Profile with { SaveSources = [manual] });
        Put(Hoodlum);
        Assert.Equal(manual, Assert.Single(Selected(await Service.ScanAsync(id))).Path);
    }

    [Fact]
    public async Task SelectionChangesAreAnnouncedOnce()
    {
        await SeedAsync();
        Put(Hoodlum);
        var service = Service;
        var announced = new List<SaveSelectionChanged>();
        service.SelectionChanged += (_, change) => announced.Add(change);
        await service.ScanAsync(id);
        await service.ScanAsync(id);
        var change = Assert.Single(announced);
        Assert.Equal("Frostpunk", change.GameName);
        Assert.Equal(Hoodlum, Assert.Single(change.NewlySelected).Path);
        Assert.Empty(change.NewlyToConfirm);
    }

    [Fact]
    public async Task AnUnsureFindIsAnnouncedForConfirmationAndConfirmingIsNotAnnouncedAgain()
    {
        await SeedAsync(appId: null);
        var deep = Put(Path.Combine(Env.Documents, "My Games", "Studio", "Frostpunk"), "save1.sav");
        var service = Service;
        var announced = new List<SaveSelectionChanged>();
        service.SelectionChanged += (_, change) => announced.Add(change);
        await service.ScanAsync(id);
        var asked = Assert.Single(Assert.Single(announced).NewlyToConfirm);
        Assert.Equal(deep, asked.Path);
        await service.ConfirmAsync(id, asked.Id);
        await service.ScanAsync(id);
        Assert.Single(announced);
    }

    [Fact]
    public async Task AnAutomaticSelectionStaysWhenASharedWriteLowersItsScore()
    {
        await SeedAsync(appId: null);
        var saves = Put(Path.Combine(Env.Local, "Frostpunk", "Saved", "SaveGames"));
        var service = Service;
        Assert.Single(Selected(await service.ScanAsync(id)));
        await Store.UpdateSaveFindsAsync(id, d => d with { Finds = [new(saves, 1, 1, DateTimeOffset.UtcNow, true)] });
        Assert.Equal(saves, Assert.Single(Selected(await service.ScanAsync(id))).Path);
    }

    [Fact]
    public async Task AnAutomaticSelectionStaysWhenTheSearchNoLongerReachesIt()
    {
        await SeedAsync(appId: null);
        var deep = Put(Path.Combine(Env.Roaming, "Studio", "x", "y", "Frostpunk"));          // deeper than the name search looks
        await Store.UpdateSaveFindsAsync(id, d => d with { Finds = [new(deep, 1, 1, DateTimeOffset.UtcNow, false)] });
        var service = Service;
        Assert.Single(Selected(await service.ScanAsync(id)));
        await Store.UpdateSaveFindsAsync(id, d => d with { Finds = [] });
        var after = await service.ScanAsync(id);
        Assert.Equal(deep, Assert.Single(Selected(after)).Path);
        Assert.Equal(SaveDiscoveryPhase.SourcesFound, after.Phase);
    }

    [Fact]
    public async Task WithAnUnreadableFindsDocumentNothingNewIsSelected()
    {
        await SeedAsync();
        Put(Hoodlum);
        File.WriteAllText(Path.Combine(ProfileFolders.Resolve(Paths, id), "save-finds.json"), "{\"schemaVersion\":9}");
        var result = await Service.ScanAsync(id);
        Assert.Empty(Selected(result));                      // the rejected list cannot be known
        Assert.Contains(result.Document.Value!.Sources, s => s.Path == Hoodlum && s.CanConfirm);
    }

    [Fact]
    public async Task TheDocumentWrittenAfterAnAutomaticSelectionLoadsNormally()
    {
        await SeedAsync();
        Put(Hoodlum);
        await Service.ScanAsync(id);
        Assert.Equal(DocumentState.Ready, (await Store.LoadSaveDiscoveryAsync(id)).State);
    }

    [Fact]
    public async Task TheDatabaseIsAskedWithTheStoreNumberToo()
    {
        await SeedAsync(Profile with { Name = "A Renamed Copy", ExecutablePath = Path.Combine(root, "Games", "Copy", "x.exe"), WorkingDirectory = Path.Combine(root, "Games", "Copy") });
        var cache = Path.Combine(Paths.Root, "Cache");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "ludusavi-manifest.yaml"),
            "Frostpunk:\n  files:\n    \"<winAppData>/11bitstudios/Frostpunk/Default/saves\":\n      tags:\n        - save\n  steam:\n    id: 323190\n");
        var official = Put(Path.Combine(Env.Roaming, "11bitstudios", "Frostpunk", "Default", "saves"), "city.save");
        var service = new SaveDiscoveryService(Paths, Store, Env with { Database = new SaveLocationDatabase(Paths) });
        var source = Assert.Single((await service.ScanAsync(id)).Document.Value!.Sources, s => s.Path == official);
        Assert.True(source.CanConfirm);                      // nothing in the path names this renamed copy, so the player is asked
        Assert.Contains("database", source.Evidence);
    }
}
