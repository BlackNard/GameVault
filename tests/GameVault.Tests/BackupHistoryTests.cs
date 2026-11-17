using System.Security.Cryptography;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class BackupHistoryTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault retention",Guid.NewGuid().ToString("N"));
    private readonly Guid id=Guid.NewGuid();
    private ApplicationPaths Paths=>ApplicationPaths.FromRoot(Path.Combine(root,"app"));
    private AppDataStore Store=>new(Paths);
    private string BackupRoot=>Path.Combine(ProfileFolders.Resolve(Paths, id),"Backups");
    private BackupHistoryService Service(Func<Guid,Task<bool>>? running=null,Action<string>? checkpoint=null)=>new(Paths,Store,new(),running??(_=>Task.FromResult(false)),checkpoint);
    private async Task Seed()
    {
        Directory.CreateDirectory(Paths.Root);await Store.InitializeAsync();await Store.SaveProfileAsync(new(1,id,"Example",Path.Combine(root,"game.exe"),root,[],GameStatus.Playing,20,false,false));
    }
    private CompletedBackup Add(int minute=0,Guid? backupId=null)
    {
        var sourceId=Guid.NewGuid();var bytes=Encoding.UTF8.GetBytes("save "+minute);var file=new BackupFile(sourceId,"slot.sav",sourceId.ToString("N")+"/slot.sav",bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
        var manifest=new BackupManifest(1,id,backupId??Guid.NewGuid(),new DateTimeOffset(2026,11,13,12,0,0,TimeSpan.Zero).AddMinutes(minute),new string('A',64),[new(sourceId,Path.Combine(root,"save"),SaveSourceKind.Folder)],[file],[]);
        var folder=Path.Combine(BackupRoot,BackupStorage.Name(manifest));var storage=new BackupStorage(Paths);storage.Ensure(Path.Combine(folder,"Files",sourceId.ToString("N")));File.WriteAllBytes(Path.Combine(folder,"Files",file.StoragePath),bytes);storage.WriteManifest(folder,manifest);return new(folder,manifest);
    }
    [Fact] public async Task HistoryOrdersTimestampCollisionsAndReportsRecordedPayloadAndPins()
    {
        await Seed();var a=Add(0,Guid.Parse("00000000-0000-0000-0000-000000000001"));var b=Add(0,Guid.Parse("00000000-0000-0000-0000-000000000002"));var c=Add(1);var service=Service();
        await service.SetPinnedAsync(id,a.Manifest.Id,true);var history=await Service().ListAsync(id);
        Assert.Equal(new[]{c.Manifest.Id,b.Manifest.Id,a.Manifest.Id},history.Entries.Select(e=>e.Backup.Manifest.Id));Assert.True(history.Entries[^1].Pinned);Assert.Equal(history.Entries.Sum(e=>e.SizeBytes),history.TotalBytes);Assert.Equal(20,history.Policy!.Limit);
        Assert.Equal(3,Directory.GetDirectories(BackupRoot).Length);
    }
    [Fact] public async Task DefaultRetentionKeepsTwentyUnpinnedPlusAllPinsAndNeverRecovery()
    {
        await Seed();var all=Enumerable.Range(0,24).Select(i=>Add(i)).ToArray();var service=Service();await service.SetPinnedAsync(id,all[0].Manifest.Id,true);await service.SetPinnedAsync(id,all[1].Manifest.Id,true);
        var pending=Path.Combine(BackupRoot,".pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(pending);File.WriteAllText(Path.Combine(pending,"partial.sav"),"recover");
        var result=await service.ApplyRetentionAsync(id,new(20,false),new(20,false));Assert.True(result.Complete,result.Message);Assert.Equal(2,result.Removed);Assert.Equal(22,result.History.Entries.Length);Assert.Equal(1,result.History.RecoveryFolders);Assert.Equal("recover",File.ReadAllText(Path.Combine(pending,"partial.sav")));
        Assert.True(Directory.Exists(all[0].Directory));Assert.True(Directory.Exists(all[^1].Directory));
    }
    [Fact] public async Task CustomPolicyAndKeepEverythingPersistWithoutLosingProfileData()
    {
        await Seed();var before=(await Store.LoadProfileAsync(id)).Profile.Value!;for(int i=0;i<5;i++)Add(i);var service=Service();
        var keep=await service.ApplyRetentionAsync(id,new(1,true),new(20,false));Assert.True(keep.Complete);Assert.Equal(5,keep.History.Entries.Length);
        var trim=await service.ApplyRetentionAsync(id,new(2,false),new(1,true));Assert.True(trim.Complete,trim.Message);Assert.Equal(3,trim.Removed);Assert.Equal(2,trim.History.Entries.Length);
        var after=(await Store.LoadProfileAsync(id)).Profile.Value!;Assert.Equal(before with{BackupLimit=2},after);
        await Assert.ThrowsAsync<IOException>(()=>service.ApplyRetentionAsync(id,new(3,false),new(20,false)));
    }
    [Fact] public async Task PinAndUnpinDoNotPruneUntilExplicitApply()
    {
        await Seed();var old=Add(0);Add(1);var service=Service();await service.ApplyRetentionAsync(id,new(1,true),new(20,false));await service.SetPinnedAsync(id,old.Manifest.Id,true);await service.SetPinnedAsync(id,old.Manifest.Id,false);Assert.True(Directory.Exists(old.Directory));
        var result=await service.ApplyRetentionAsync(id,new(1,false),new(1,true));Assert.True(result.Complete);Assert.False(Directory.Exists(old.Directory));
    }
    [Theory] [InlineData("extra")] [InlineData("corrupt")]
    public async Task UnexpectedOrCorruptCandidateStopsCleanupBeforeAnyDeletion(string kind)
    {
        await Seed();var a=Add(0);var b=Add(1);var latest=Add(2);
        File.WriteAllText(kind=="extra"?Path.Combine(b.Directory,"unrelated.txt"):Path.Combine(b.Directory,"Files",b.Manifest.Files[0].StoragePath),"do not remove");
        var result=await Service().ApplyRetentionAsync(id,new(1,false),new(20,false));Assert.False(result.Complete);Assert.Equal(0,result.Removed);Assert.True(Directory.Exists(a.Directory));Assert.True(Directory.Exists(b.Directory));Assert.True(Directory.Exists(latest.Directory));
    }
    [Fact] public async Task ProtectedPinsBlockAllPruningAndPinWrites()
    {
        await Seed();var old=Add(0);Add(1);var file=Path.Combine(Path.GetDirectoryName(BackupRoot)!,"backup-pins.json");File.WriteAllText(file,"{\"schemaVersion\":99}");
        await Assert.ThrowsAsync<IOException>(()=>Service().SetPinnedAsync(id,old.Manifest.Id,true));var result=await Service().ApplyRetentionAsync(id,new(1,false),new(20,false));Assert.False(result.Complete);Assert.Equal("{\"schemaVersion\":99}",File.ReadAllText(file));Assert.True(Directory.Exists(old.Directory));
    }
    [Fact] public async Task InterruptedCleanupPreservesQuarantineAndLatestWithoutAutoRetry()
    {
        await Seed();var old=Add(0);var latest=Add(1);var result=await Service(checkpoint:p=>{if(p=="delete")throw new IOException("Synthetic locked file");}).ApplyRetentionAsync(id,new(1,false),new(20,false));
        Assert.False(result.Complete);Assert.True(Directory.Exists(latest.Directory));Assert.False(Directory.Exists(old.Directory));var recovery=Assert.Single(Directory.GetDirectories(BackupRoot),p=>Path.GetFileName(p).StartsWith(".prune-"));
        var again=await Service().ApplyRetentionAsync(id,new(1,false),new(1,false));Assert.True(again.Complete);Assert.True(Directory.Exists(recovery));Assert.Equal(1,again.History.RecoveryFolders);
    }
    [Fact] public async Task ActiveOperationOrExternalGameBlocksMutations()
    {
        await Seed();var backup=Add();var leases=new GameOperationLeases();using(var held=leases.TryAcquire(id)!)await Assert.ThrowsAsync<InvalidOperationException>(()=>Service().SetPinnedAsync(id,backup.Manifest.Id,true));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(_=>Task.FromResult(true)).ApplyRetentionAsync(id,new(1,false),new(20,false)));
        Assert.False(Assert.Single((await Service().ListAsync(id)).Entries).Pinned);
    }
    [Theory] [InlineData("pin")] [InlineData("manifest")]
    public async Task LastMomentProtectionChangesAreRecheckedBeforePruning(string change)
    {
        await Seed();var old=Add(0);Add(1);var fired=false;
        var result=await Service(checkpoint:p=>
        {
            if(p!="before-prune"||fired)return;fired=true;
            if(change=="manifest")File.WriteAllText(Path.Combine(old.Directory,"manifest.json"),"{\"schemaVersion\":99}");
            else Store.SaveBackupPinsAsync(new(1,id,1,[old.Manifest.Id]),0).GetAwaiter().GetResult();
        }).ApplyRetentionAsync(id,new(1,false),new(20,false));
        Assert.False(result.Complete);Assert.True(Directory.Exists(old.Directory));
    }
    [Fact] public async Task FutureManifestDoesNotHideOtherHistoryOrPermitCleanup()
    {
        await Seed();var old=Add(0);var latest=Add(1);File.WriteAllText(Path.Combine(old.Directory,"manifest.json"),"{\"schemaVersion\":99}");
        var list=await Service().ListAsync(id);Assert.Single(list.Entries);Assert.NotEmpty(list.Warnings);Assert.False(list.CanChange);
        var result=await Service().ApplyRetentionAsync(id,new(1,false),new(20,false));Assert.False(result.Complete);Assert.True(Directory.Exists(old.Directory));Assert.True(Directory.Exists(latest.Directory));
    }
    [Fact] public async Task ClockAdjustmentStillProtectsTheJustCompletedSnapshot()
    {
        await Seed();var earlier=Add(0);var recent=Add(1);var settings=new BackupRetentionPolicy(1,false);await Store.SetBackupRetentionAsync(id,settings,new(20,false));
        using var lease=new GameOperationLeases().TryAcquire(id)!;var result=await Service().CleanupAfterSuccessAsync(id,earlier.Manifest.Id,default);
        Assert.True(result.Complete,result.Message);Assert.True(Directory.Exists(earlier.Directory));Assert.True(Directory.Exists(recent.Directory));
    }
    [Fact] public async Task CancellationBeforePruningPreservesCompletedSnapshots()
    {
        await Seed();var old=Add(0);var latest=Add(1);using var stop=new CancellationTokenSource();
        var result=await Service(checkpoint:p=>{if(p=="before-prune")stop.Cancel();}).ApplyRetentionAsync(id,new(1,false),new(20,false),stop.Token);
        Assert.False(result.Complete);Assert.True(Directory.Exists(old.Directory));Assert.True(Directory.Exists(latest.Directory));
    }
    [Fact] public async Task LinkedCandidateTreePreservesUnrelatedTargetAndOtherBackups()
    {
        if(!OperatingSystem.IsWindows())return;
        await Seed();var old=Add(0);var latest=Add(1);var target=Path.Combine(root,"unrelated");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"keep.txt"),"keep");var link=Path.Combine(old.Directory,"linked");
        var info=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"/c","mklink","/J",link,target})info.ArgumentList.Add(arg);
        using var process=System.Diagnostics.Process.Start(info)!;await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);
        try{var result=await Service().ApplyRetentionAsync(id,new(1,false),new(20,false));Assert.False(result.Complete);Assert.True(Directory.Exists(old.Directory));Assert.True(Directory.Exists(latest.Directory));Assert.Equal("keep",File.ReadAllText(Path.Combine(target,"keep.txt")));}
        finally{Directory.Delete(link);}
    }
    [Fact] public async Task RecoveredPinsRemainReadOnlyAndNeverLoseTheProtectedCopy()
    {
        await Seed();var old=Add(0);Add(1);var service=Service();await service.SetPinnedAsync(id,old.Manifest.Id,true);await service.SetPinnedAsync(id,old.Manifest.Id,false);
        var file=Path.Combine(Path.GetDirectoryName(BackupRoot)!,"backup-pins.json");File.WriteAllText(file,"broken");
        Assert.Equal(DocumentState.Recovered,(await Store.LoadBackupPinsAsync(id)).State);var result=await service.ApplyRetentionAsync(id,new(1,false),new(20,false));Assert.False(result.Complete);Assert.True(Directory.Exists(old.Directory));Assert.Equal("broken",File.ReadAllText(file));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}