using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class SaveDiscoveryPersistenceTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault discovery persistence",Guid.NewGuid().ToString("N"));
    private readonly Guid id=Guid.NewGuid();
    private AppDataStore Store=>new(ApplicationPaths.FromRoot(Path.Combine(root,"app")));
    private GameProfile Profile=>new(1,id,"Game",Path.Combine(root,"game","game.exe"),Path.Combine(root,"game"),[],GameStatus.Playing,20,false,false);
    private string FilePath=>Path.Combine(ProfileFolders.Resolve(ApplicationPaths.FromRoot(Path.Combine(root,"app")),id),"save-discovery.json");
    private async Task Seed(){Directory.CreateDirectory(Path.Combine(root,"app"));await Store.InitializeAsync();await Store.SaveProfileAsync(Profile);}
    private SaveDiscoveryDocument Document()=>new(1,id,SaveDiscoveryIdentity.For(Profile),true,DateTimeOffset.UtcNow,[new(Guid.NewGuid(),Path.Combine(root,"saves"),SaveSourceKind.Folder,SaveSourceOrigin.KnownRule,true,SaveSourceAccess.Missing,"Known game-specific location",null)],[]);
    [Fact] public async Task IndependentMappingRoundTripPreservesSourceIdAndOtherData()
    {
        await Seed();var profile=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(FilePath)!,"profile.json"));var stats=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(FilePath)!,"statistics.json"));
        var doc=Document();await Store.SaveDiscoveryAsync(doc);var read=await Store.LoadSaveDiscoveryAsync(id);
        Assert.Equal(doc.Sources[0],read.Value!.Sources[0]);Assert.Equal(doc.Fingerprint,read.Value.Fingerprint);
        Assert.Equal(profile,File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(FilePath)!,"profile.json")));Assert.Equal(stats,File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(FilePath)!,"statistics.json")));
        Assert.Equal(SaveDiscoveryIdentity.For(Profile),SaveDiscoveryIdentity.For(Profile with{Name="Renamed"}));
    }
    [Fact] public async Task StaleIdentityAndUnsafeSelectedMappingsCannotBeCommitted()
    {
        await Seed();var doc=Document();await Store.SaveProfileAsync(Profile with{ExecutablePath=Path.Combine(root,"game","different.exe")});
        await Assert.ThrowsAsync<IOException>(()=>Store.SaveDiscoveryAsync(doc));await Store.SaveProfileAsync(Profile);
        await Assert.ThrowsAsync<IOException>(()=>Store.SaveDiscoveryAsync(doc with{Sources=[doc.Sources[0] with{Path=Path.Combine(root,"app","Profiles")}]}));
    }
    [Fact] public async Task FailedAndProtectedDocumentsKeepPreviousMapping()
    {
        await Seed();var doc=Document();await Store.SaveDiscoveryAsync(doc);
        var failure=new AppDataStore(ApplicationPaths.FromRoot(Path.Combine(root,"app")),p=>{if(p==FilePath)throw new IOException("fixture");});
        await Assert.ThrowsAsync<IOException>(()=>failure.SaveDiscoveryAsync(doc with{Automatic=false}));Assert.True((await Store.LoadSaveDiscoveryAsync(id)).Value!.Automatic);
        await Store.SaveDiscoveryAsync(doc with{Automatic=false});File.WriteAllText(FilePath,"broken");Assert.Equal(DocumentState.Recovered,(await Store.LoadSaveDiscoveryAsync(id)).State);
        await Assert.ThrowsAsync<IOException>(()=>Store.SaveDiscoveryAsync(doc));File.WriteAllText(FilePath,"{\"schemaVersion\":99}");
        Assert.Equal(DocumentState.Unsupported,(await Store.LoadSaveDiscoveryAsync(id)).State);await Assert.ThrowsAsync<IOException>(()=>Store.SaveDiscoveryAsync(doc));
    }
    [Fact] public async Task StaleScanCannotOverwriteNewerDiscoverySettings()
    {
        await Seed();var doc=Document();await Store.SaveDiscoveryAsync(doc,0);
        await Store.SaveDiscoveryAsync(doc with{Automatic=false,Revision=2},1);
        await Assert.ThrowsAsync<IOException>(()=>Store.SaveDiscoveryAsync(doc with{Automatic=true,Revision=2},1));
        var current=await Store.LoadSaveDiscoveryAsync(id);Assert.False(current.Value!.Automatic);Assert.Equal(2,current.Value.Revision);
    }    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}