using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class BackupDestinationTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault destinations",Guid.NewGuid().ToString("N"));
    private readonly Guid id=Guid.NewGuid();
    private ApplicationPaths Paths=>ApplicationPaths.FromRoot(Path.Combine(root,"app"));
    private string Saves=>Path.Combine(root,"saves");
    private AppDataStore Store=>new(Paths);
    private BackupDestinationService Destinations=>new(Paths);
    private BackupService Backups=>new(Paths,Store,new(Paths,Store,new(Path.Combine(root,"user"),Path.Combine(root,"docs"),Path.Combine(root,"saved"),Path.Combine(root,"roam"),Path.Combine(root,"local"),Path.Combine(root,"public"),[])),new(),_=>Task.FromResult(false));
    private async Task Seed()
    {
        Directory.CreateDirectory(Paths.Root);Directory.CreateDirectory(Saves);File.WriteAllText(Path.Combine(Saves,"slot.sav"),"original");await Store.InitializeAsync();
        await Store.SaveProfileAsync(new(1,id,"Example",Path.Combine(root,"install","game.exe"),Path.Combine(root,"install"),[Saves],GameStatus.Playing,20,false,false));
    }
    private string Folder(string name){var path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    [Fact] public async Task PortableDefaultAndStableOverridesPersist()
    {
        await Seed();var portable=Path.Combine(ProfileFolders.Resolve(Paths, id),"Backups");Assert.Equal(portable,await Destinations.ResolveCurrentAsync(id));
        var external=Folder("external");await Destinations.SetDefaultAsync(external);Assert.Equal(Path.Combine(external,id.ToString("N"),"Backups"),await Destinations.ResolveCurrentAsync(id));
        var perGame=Folder("override");await Destinations.SetGameAsync(id,perGame);Assert.Equal(perGame,(await Destinations.ReadAsync(id)).GameRoot);
        await Destinations.SetGameAsync(id,null);Assert.Equal(external,(await Destinations.ReadAsync(id)).DefaultRoot);
        await Destinations.SetDefaultAsync(null);Assert.Equal(portable,await Destinations.ResolveCurrentAsync(id));
        Assert.Contains(Path.Combine(perGame,id.ToString("N"),"Backups"),(await Destinations.ReadAsync(id)).HistoricalDirectories);
    }
    [Fact] public async Task OldLocationsRemainReadableAndRestorableAfterSwitch()
    {
        await Seed();var original=(await Backups.BackUpAsync(id,false)).LastSuccess!;await Destinations.SetDefaultAsync(Folder("external"));
        File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");var newer=await Backups.BackUpAsync(id,false);Assert.Equal(BackupPhase.Complete,newer.Phase);Assert.NotEqual(Path.GetDirectoryName(original.Directory),Path.GetDirectoryName(newer.LastSuccess!.Directory));
        var history=await new BackupHistoryService(Paths,Store,new(),_=>Task.FromResult(false)).ListAsync(id);Assert.Equal(2,history.Entries.Length);
        var restore=new RestoreService(Paths,Store,new(),_=>Task.FromResult(false));var request=new RestoreRequest(id,new(original.Manifest.Id,null),[],[]);var preview=await restore.PreviewAsync(request);
        Assert.Equal(RestorePhase.Completed,(await restore.ExecuteAsync(request,preview.Fingerprint)).Phase);Assert.Equal("original",File.ReadAllText(Path.Combine(Saves,"slot.sav")));Assert.True(Directory.Exists(original.Directory));
    }
    [Fact] public async Task MissingDiskFailsWithoutPortableFallback()
    {
        await Seed();var external=Folder("external");await Destinations.SetDefaultAsync(external);Directory.Delete(external);
        await Assert.ThrowsAnyAsync<IOException>(()=>Destinations.ResolveCurrentAsync(id));Assert.Equal(BackupPhase.Failed,(await Backups.BackUpAsync(id,false)).Phase);Assert.Empty(Directory.GetDirectories(Path.Combine(ProfileFolders.Resolve(Paths, id),"Backups")));
    }
    [Fact] public async Task RejectsManagedDataInstallSavesAndTraversal()
    {
        await Seed();foreach(var path in new[]{Paths.Data,Paths.Profiles,Saves,Folder("install"),root,Path.Combine(Folder("external"),"..","external")})
            await Assert.ThrowsAsync<IOException>(()=>Destinations.SetDefaultAsync(path));
    }
    [Fact] public async Task SharedStoreLockPreventsCrossInstanceMutation()
    {
        await Seed();var external=Folder("external");await Destinations.SetDefaultAsync(external);
        using var held=new FileStream(Path.Combine(external,".gamevault-backup.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Assert.Equal(BackupPhase.Failed,(await Backups.BackUpAsync(id,false)).Phase);
    }
    [Fact] public async Task RejectsSourceOverlapIntroducedAfterDestinationSelection()
    {
        await Seed();var external=Folder("external");await Destinations.SetDefaultAsync(external);
        var profile=(await Store.LoadProfileAsync(id)).Profile.Value!;await Store.SaveProfileAsync(profile with{SaveSources=[external]});
        Assert.Equal(BackupPhase.Failed,(await Backups.BackUpAsync(id,false)).Phase);
    }
    [Fact] public async Task CorruptMappingIsPreservedAndNeverFallsBack()
    {
        await Seed();var file=Path.Combine(Paths.Data,"backup-destinations.json");File.WriteAllText(file,"{\"schemaVersion\":99}");
        await Assert.ThrowsAnyAsync<IOException>(()=>Destinations.ResolveCurrentAsync(id));await Assert.ThrowsAnyAsync<IOException>(()=>Destinations.SetDefaultAsync(null));
        Assert.Contains("99",File.ReadAllText(file));Assert.Equal(BackupPhase.Failed,(await Backups.BackUpAsync(id,false)).Phase);
    }
    [Fact] public async Task SharedRootSerializesIndependentPortableInstallations()
    {
        await Seed();var external=Folder("external");await Destinations.SetDefaultAsync(external);
        var other=ApplicationPaths.FromRoot(Folder("other-app"));await new AppDataStore(other).InitializeAsync();await new BackupDestinationService(other).SetDefaultAsync(external);
        using var held=new BackupStorage(Paths).Acquire();await Assert.ThrowsAnyAsync<IOException>(()=>new BackupDestinationService(other).SetDefaultAsync(null));
    }
    [Fact] public async Task RestoreCannotOverwriteExternalBackupStorage()
    {
        await Seed();await Destinations.SetDefaultAsync(Folder("external"));var backup=(await Backups.BackUpAsync(id,false)).LastSuccess!;
        var request=new RestoreRequest(id,new(backup.Manifest.Id,null),[new(backup.Manifest.Sources[0].Id,Path.GetDirectoryName(backup.Directory)!)],[]);
        await Assert.ThrowsAnyAsync<IOException>(()=>new RestoreService(Paths,Store,new(),_=>Task.FromResult(false)).PreviewAsync(request));
    }
    [Fact] public async Task LinkedDestinationIsRejected()
    {
        await Seed();var real=Folder("real");var link=Path.Combine(root,"link");
        if(OperatingSystem.IsWindows())
        {
            var info=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"/c","mklink","/J",link,real})info.ArgumentList.Add(arg);
            using var process=System.Diagnostics.Process.Start(info)!;await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link,real);
        try{await Assert.ThrowsAnyAsync<IOException>(()=>Destinations.SetDefaultAsync(link));}finally{Directory.Delete(link);}
    }
    [Fact] public async Task HistoricalStoreThatBecomesLiveSavesCannotBePruned()
    {
        await Seed();var external=Folder("external");await Destinations.SetDefaultAsync(external);var old=(await Backups.BackUpAsync(id,false)).LastSuccess!;
        await Destinations.SetDefaultAsync(Folder("new-external"));var profile=(await Store.LoadProfileAsync(id)).Profile.Value!;
        await Store.SaveProfileAsync(profile with{SaveSources=[external]});
        await Assert.ThrowsAnyAsync<IOException>(()=>new BackupHistoryService(Paths,Store,new(),_=>Task.FromResult(false)).ApplyRetentionAsync(id,new(1,false),new(20,false)));
        Assert.True(Directory.Exists(old.Directory));
    }
    [Fact] public async Task OfflineHistoricalStoreCannotGrantLeaseThatMissesReconnectedSharedRoot()
    {
        await Seed();var external=Folder("external");await Destinations.SetDefaultAsync(external);await Destinations.SetDefaultAsync(null);
        var other=ApplicationPaths.FromRoot(Folder("other-app"));await new AppDataStore(other).InitializeAsync();await new BackupDestinationService(other).SetDefaultAsync(external);
        var disconnected=Path.Combine(root,"disconnected-store");Directory.Move(external,disconnected);IDisposable? lease=null;
        try
        {
            var history=await new BackupHistoryService(Paths,Store,new(),_=>Task.FromResult(false)).ListAsync(id);Assert.Contains(history.Warnings,w=>w.Contains("offline",StringComparison.OrdinalIgnoreCase));
            var acquisitionError=Record.Exception(()=>lease=new BackupStorage(Paths).Acquire());
            Directory.Move(disconnected,external);
            using var competingLease=new BackupStorage(other).Acquire();
            Assert.IsAssignableFrom<IOException>(acquisitionError);Assert.Null(lease);
            Assert.Equal(BackupPhase.Failed,(await Backups.BackUpAsync(id,false)).Phase);
        }
        finally{lease?.Dispose();if(Directory.Exists(disconnected))Directory.Move(disconnected,external);}
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
