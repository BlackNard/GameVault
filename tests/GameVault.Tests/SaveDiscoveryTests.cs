using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class SaveDiscoveryTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault discovery",Guid.NewGuid().ToString("N"));
    private readonly Guid id=Guid.NewGuid();
    private ApplicationPaths Paths=>ApplicationPaths.FromRoot(Path.Combine(root,"app"));
    private AppDataStore Store=>new(Paths);
    private SaveDiscoveryEnvironment Env=>new(Path.Combine(root,"user"),Path.Combine(root,"documents"),Path.Combine(root,"savedgames"),Path.Combine(root,"roaming"),Path.Combine(root,"local"),Path.Combine(root,"public"),[]);
    private SaveDiscoveryService Service=>new(Paths,Store,Env);
    private GameProfile Profile=>new(1,id,"Resident Evil 2",Path.Combine(root,"Resident Evil 2","re2.exe"),Path.Combine(root,"Resident Evil 2"),[],GameStatus.Playing,20,false,false);
    private string Known=>Path.Combine(Env.PublicDocuments,"Steam","CODEX","883710","remote","win64_save");
    private async Task Seed(GameProfile? profile=null){Directory.CreateDirectory(Paths.Root);await Store.InitializeAsync();await Store.SaveProfileAsync(profile??Profile);}
    private static void Save(string folder,string file="data000.bin"){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,file),"Synthetic save");}
    [Fact] public async Task FirstSaveAppearingAfterEmptyScanIsDiscoveredWithoutManualConfiguration()
    {
        await Seed();var waiting=await Service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.WaitingForSave,waiting.Phase);
        var sourceId=waiting.Document.Value!.Sources.Single(s=>s.Path==Known).Id;Save(Known);
        var found=await Service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.SourcesFound,found.Phase);
        var source=Assert.Single(found.Document.Value!.Sources,s=>s.Selected);Assert.Equal(sourceId,source.Id);Assert.Equal(Known,source.Path);
        Assert.Single(await Service.RequireVerifiedSourcesAsync(id));Directory.Delete(Known,true);
        var missing=await Service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.WaitingForSave,missing.Phase);Assert.Contains(missing.Document.Value!.Sources,s=>s.Id==sourceId);
        await Assert.ThrowsAsync<IOException>(()=>Service.RequireVerifiedSourcesAsync(id));
    }
    [Fact] public async Task MultipleKnownLocationsAndGenericCandidatesRequireConfirmation()
    {
        await Seed();Save(Known);var other=Path.Combine(Env.Roaming,"EMPRESS","883710","remote","win64_save");Save(other);
        var service=Service;var result=await service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.NeedsConfirmation,result.Phase);Assert.DoesNotContain(result.Document.Value!.Sources,s=>s.Selected);
        var selected=result.Document.Value.Sources.Single(s=>s.Path==Known);result=await service.ConfirmAsync(id,selected.Id);
        Assert.Equal(SaveSourceOrigin.Confirmed,Assert.Single(result.Document.Value!.Sources,s=>s.Selected).Origin);
        var generic=Profile with{Name="Unlisted Adventure",ExecutablePath=Path.Combine(root,"Unlisted Adventure","game.exe"),WorkingDirectory=Path.Combine(root,"Unlisted Adventure")};
        await Store.SaveProfileAsync(generic);var candidate=Path.Combine(Env.SavedGames,"Unlisted Adventure");Save(candidate,"slot.sav");
        result=await service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.NeedsConfirmation,result.Phase);
        Assert.DoesNotContain(result.Document.Value!.Sources,s=>s.Selected);await Assert.ThrowsAsync<IOException>(()=>service.ConfirmAsync(id,selected.Id));
    }
    private GameProfile Rockstar(string name,string exe)=>Profile with{Name=name,ExecutablePath=Path.Combine(root,"installed",name,exe),WorkingDirectory=Path.Combine(root,"installed",name)};

    [Fact] public async Task GtaIvSavesUnderRockstarGamesAreFoundOnceTheFirstSaveExists()
    {
        await Seed(Rockstar("Grand Theft Auto IV - The Complete Edition","GTAIV.exe"));
        var profiles=Path.Combine(Env.Documents,"Rockstar Games","GTA IV","Profiles");
        Save(Path.Combine(profiles,"1A2B3C4D"),"settings.cfg");                    // settings alone are not a save
        var waiting=await Service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.WaitingForSave,waiting.Phase);
        Assert.DoesNotContain(waiting.Document.Value!.Sources,s=>s.Selected);
        Save(Path.Combine(profiles,"1A2B3C4D"),"SGTA400");
        var found=await Service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.SourcesFound,found.Phase);
        Assert.Equal(profiles,Assert.Single(found.Document.Value!.Sources,s=>s.Selected).Path);
        Assert.Single(await Service.RequireVerifiedSourcesAsync(id));
    }

    [Fact] public async Task GtaIvSavesOfAnInstallationWithoutTheRockstarLauncherAreFound()
    {
        // The layout seen on a real installation: %APPDATA%/Goldberg SocialClub Emu Saves/GTA IV/<profile>/SGTA400, with settings elsewhere.
        await Seed(Rockstar("Grand Theft Auto IV - The Complete Edition","GTAIV.exe"));
        Save(Path.Combine(Env.Local,"Rockstar Games","GTA IV","Settings"),"SETTINGS.CFG");
        var saves=Path.Combine(Env.Roaming,"Goldberg SocialClub Emu Saves","GTA IV");
        Save(Path.Combine(saves,"0F74F4C4"),"SGTA400");Save(Path.Combine(saves,"0F74F4C4"),"ControlMap.dat");
        var found=await Service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.SourcesFound,found.Phase);
        Assert.Equal(saves,Assert.Single(found.Document.Value!.Sources,s=>s.Selected).Path);
        Assert.Single(await Service.RequireVerifiedSourcesAsync(id));
    }

    [Fact] public async Task TwoGtaIvLocationsWithSavesNeedConfirmation()
    {
        await Seed(Rockstar("GTA IV","GTAIV.exe"));
        Save(Path.Combine(Env.Documents,"Rockstar Games","GTA IV","Profiles","AA"),"SGTA400");
        var older=Path.Combine(Env.Local,"Rockstar Games","GTA IV","savegames");Save(Path.Combine(older,"user_ee000000"),"SGTA401");
        var result=await Service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.NeedsConfirmation,result.Phase);
        var pick=result.Document.Value!.Sources.Single(s=>s.Path==older);
        Assert.Equal(older,Assert.Single((await Service.ConfirmAsync(id,pick.Id)).Document.Value!.Sources,s=>s.Selected).Path);
    }

    [Theory]
    [InlineData("Grand Theft Auto V Legacy","GTA5.exe","GTA V","SGTA50000")]
    [InlineData("My copy","RDR2.exe","Red Dead Redemption 2","SRDR30000")]
    [InlineData("Red Dead Redemption 2","launcher.exe","Red Dead Redemption 2","SRDR30001")]
    public async Task OtherRockstarGamesAreFoundByProgramOrTitle(string name,string exe,string folder,string file)
    {
        await Seed(Rockstar(name,exe));
        var profiles=Path.Combine(Env.Documents,"Rockstar Games",folder,"Profiles");Save(Path.Combine(profiles,"BB"),file);
        var result=await Service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.SourcesFound,result.Phase);
        Assert.Equal(profiles,Assert.Single(result.Document.Value!.Sources,s=>s.Selected).Path);
    }

    [Fact] public async Task ViceCityIsNotMistakenForGtaFiveAndGtaVSettingsAreNotASave()
    {
        await Seed(Rockstar("Grand Theft Auto Vice City","gta-vc.exe"));
        Save(Path.Combine(Env.Documents,"Rockstar Games","GTA V","Profiles","CC"),"SGTA50000");
        var result=await Service.ScanAsync(id);Assert.DoesNotContain(result.Document.Value!.Sources,s=>s.Path.Contains("GTA V"+Path.DirectorySeparatorChar,StringComparison.Ordinal));
        Assert.False(SaveCandidateRules.PayloadMatches("CC/pc_settings.bin","save"));
        Assert.True(SaveCandidateRules.PayloadMatches("CC/SGTA50015","save"));
        Assert.False(SaveCandidateRules.PayloadMatches("CC/SGTAabc","save"));
    }

    [Fact] public async Task ManualOverridesArePreferredAndIncompleteSetsCannotFeedBackups()
    {
        var a=Path.Combine(root,"manual","one.sav");var b=Path.Combine(root,"manual","two.sav");Save(Path.GetDirectoryName(a)!,"one.sav");
        await Seed(Profile with{SaveSources=[a,b]});Save(Known);var result=await Service.ScanAsync(id);
        Assert.DoesNotContain(result.Document.Value!.Sources,s=>s.Origin!=SaveSourceOrigin.Manual&&s.Selected);
        await Assert.ThrowsAsync<IOException>(()=>Service.RequireVerifiedSourcesAsync(id));File.WriteAllText(b,"Second");
        Assert.Equal(2,(await Service.RequireVerifiedSourcesAsync(id)).Length);
    }
    [Fact] public async Task DisabledModeProtectedDocumentsAndIdentityChangesNeverReuseStaleVerification()
    {
        await Seed();Save(Known);var service=Service;await service.ScanAsync(id);var disabled=await service.SetAutomaticAsync(id,false);Assert.Equal(SaveDiscoveryPhase.Disabled,disabled.Phase);
        Assert.DoesNotContain(disabled.Document.Value!.Sources,s=>s.Selected);Assert.All(disabled.Document.Value.Sources,s=>Assert.Equal(SaveSourceAccess.NotChecked,s.Access));Assert.Equal(disabled.Document.Value.Revision,(await service.ScanAsync(id)).Document.Value!.Revision);await service.SetAutomaticAsync(id,true);
        await Store.SaveProfileAsync(Profile with{ExecutablePath=Path.Combine(root,"Other","other.exe"),WorkingDirectory=Path.Combine(root,"Other")});
        Assert.Equal(SaveDiscoveryPhase.Error,(await service.ReadAsync(id)).Phase);Assert.DoesNotContain((await service.ScanAsync(id)).Document.Value!.Sources,s=>s.Selected);
        var file=Path.Combine(ProfileFolders.Resolve(Paths, id),"save-discovery.json");File.WriteAllText(file,"{\"schemaVersion\":99}");
        var result=await service.ScanAsync(id);Assert.Equal(SaveDiscoveryPhase.Error,result.Phase);Assert.Equal(DocumentState.Unsupported,result.Document.State);Assert.Equal("{\"schemaVersion\":99}",File.ReadAllText(file));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}