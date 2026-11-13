using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class BackupTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault backup",Guid.NewGuid().ToString("N"));
    private readonly Guid id=Guid.NewGuid();
    private ApplicationPaths Paths=>ApplicationPaths.FromRoot(Path.Combine(root,"app"));
    private AppDataStore Store=>new(Paths);
    private string Source=>Path.Combine(root,"saves");
    private string Backups=>Path.Combine(Paths.Profiles,id.ToString("N"),"Backups");
    private SaveDiscoveryEnvironment Env=>new(Path.Combine(root,"user"),Path.Combine(root,"docs"),Path.Combine(root,"saved"),Path.Combine(root,"roam"),Path.Combine(root,"local"),Path.Combine(root,"public"),[]);
    private BackupService Service(GameOperationLeases? leases=null,Func<Guid,Task<bool>>? running=null,Action<string>? checkpoint=null)=>new(Paths,Store,new SaveDiscoveryService(Paths,Store,Env),leases??new(),running??(_=>Task.FromResult(false)),checkpoint);
    private async Task Seed()
    {
        Directory.CreateDirectory(Paths.Root);Directory.CreateDirectory(Source);await File.WriteAllTextAsync(Path.Combine(Source,"slot.sav"),"first save");
        await Store.InitializeAsync();await Store.SaveProfileAsync(new(1,id,"Example",Path.Combine(root,"game.exe"),root,[Source],GameStatus.Playing,20,false,false));
    }
    [Fact] public async Task SnapshotsAreIndependentVerifiedAndRecordDeletedFiles()
    {
        await Seed();File.WriteAllText(Path.Combine(Source,"obsolete.sav"),"old");var service=Service();
        var first=await service.BackUpAsync(id,true);Assert.Equal(BackupPhase.Complete,first.Phase);Assert.NotNull(first.LastSuccess);Assert.Equal(2,first.LastSuccess.Manifest.Files.Length);
        var original=first.LastSuccess;Assert.Equal("first save",File.ReadAllText(Path.Combine(original.Directory,"Files",original.Manifest.Files.Single(f=>f.RelativePath=="slot.sav").StoragePath)));
        var unchanged=await service.BackUpAsync(id,true);Assert.Equal(BackupPhase.Unchanged,unchanged.Phase);Assert.Equal(original.Directory,unchanged.LastSuccess!.Directory);
        File.WriteAllText(Path.Combine(Source,"slot.sav"),"second save");File.Delete(Path.Combine(Source,"obsolete.sav"));
        var second=await service.BackUpAsync(id,true);Assert.Equal(BackupPhase.Complete,second.Phase);Assert.NotEqual(original.Directory,second.LastSuccess!.Directory);Assert.Single(second.LastSuccess.Manifest.Deleted);
        Assert.Equal("first save",File.ReadAllText(Path.Combine(original.Directory,"Files",original.Manifest.Files.Single(f=>f.RelativePath=="slot.sav").StoragePath)));
        Assert.Equal(second.LastSuccess.Directory,(await Service().ReadAsync(id)).LastSuccess!.Directory);
        Assert.Equal(4,Directory.GetFiles(Path.GetDirectoryName(Backups)!,"*.json").Length);
    }
    [Fact] public async Task ManualUnchangedSnapshotIsStillDated()
    {
        await Seed();var service=Service();var a=await service.BackUpAsync(id,false);var b=await service.BackUpAsync(id,false);
        Assert.Equal(BackupPhase.Complete,b.Phase);Assert.NotEqual(a.LastSuccess!.Directory,b.LastSuccess!.Directory);
    }
    [Theory] [InlineData("copy")] [InlineData("verify")] [InlineData("commit")]
    public async Task FailedStagesNeverDamageEarlierBackups(string point)
    {
        await Seed();var first=await Service().BackUpAsync(id,false);File.WriteAllText(Path.Combine(Source,"slot.sav"),"changed");
        var failed=await Service(checkpoint:p=>{if(p==point)throw new IOException("Synthetic disk/permission failure");}).BackUpAsync(id,true);
        Assert.Equal(BackupPhase.Failed,failed.Phase);Assert.Equal(first.LastSuccess!.Directory,failed.LastSuccess!.Directory);Assert.Single(Directory.GetDirectories(Backups),p=>!Path.GetFileName(p).StartsWith('.'));
        Assert.Equal(BackupPhase.Complete,(await Service().BackUpAsync(id,true)).Phase);
    }
    [Fact] public async Task MissingEmptyAndLockedSourcesDoNotPublishEmptySuccess()
    {
        await Seed();var service=Service();var first=await service.BackUpAsync(id,false);
        using(var locked=new FileStream(Path.Combine(Source,"slot.sav"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Assert.Equal(BackupPhase.Failed,(await service.BackUpAsync(id,true)).Phase);
        File.Delete(Path.Combine(Source,"slot.sav"));Assert.Equal(BackupPhase.Failed,(await service.BackUpAsync(id,true)).Phase);
        Directory.Delete(Source);var missing=await service.BackUpAsync(id,true);Assert.Equal(BackupPhase.Failed,missing.Phase);Assert.Equal(first.LastSuccess!.Directory,missing.LastSuccess!.Directory);
    }
    [Fact] public async Task ChangedInventoryAndCancellationDoNotPublish()
    {
        await Seed();var changed=await Service(checkpoint:p=>{if(p=="verify")File.WriteAllText(Path.Combine(Source,"new.sav"),"new");}).BackUpAsync(id,false);Assert.Equal(BackupPhase.Failed,changed.Phase);
        using var stop=new CancellationTokenSource();var canceled=await Service(checkpoint:p=>{if(p=="commit")stop.Cancel();}).BackUpAsync(id,false,stop.Token);Assert.Equal(BackupPhase.Failed,canceled.Phase);Assert.Null(canceled.LastSuccess);
    }
    [Fact] public async Task CorruptPayloadOrFutureManifestIsProtected()
    {
        await Seed();var service=Service();var first=(await service.BackUpAsync(id,false)).LastSuccess!;
        File.WriteAllText(Path.Combine(first.Directory,"Files",first.Manifest.Files[0].StoragePath),"tampered");Assert.Equal(BackupPhase.Failed,(await service.BackUpAsync(id,true)).Phase);
        var manifest=Path.Combine(first.Directory,"manifest.json");File.WriteAllText(manifest,"{\"schemaVersion\":99}");Assert.Equal(BackupPhase.Failed,(await service.BackUpAsync(id,false)).Phase);Assert.Equal("{\"schemaVersion\":99}",File.ReadAllText(manifest));
    }
    [Fact] public async Task ActiveOperationsAndExternalGamesAreRefused()
    {
        await Seed();var leases=new GameOperationLeases();using(var lease=leases.TryAcquire(id)!)Assert.Equal(BackupPhase.Blocked,(await Service(leases).BackUpAsync(id,false)).Phase);
        Assert.Equal(BackupPhase.Blocked,(await Service(running:_=>Task.FromResult(true)).BackUpAsync(id,false)).Phase);Assert.Empty(Directory.GetDirectories(Backups));
    }
    [Fact] public async Task ExplicitFileAndMultipleSourcesKeepIndependentMappings()
    {
        await Seed();var file=Path.Combine(root,"other","slot.sav");Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,"other");var profile=(await Store.LoadProfileAsync(id)).Profile.Value!;await Store.SaveProfileAsync(profile with{SaveSources=[Source,file]});
        var result=await Service().BackUpAsync(id,false);Assert.Equal(BackupPhase.Complete,result.Phase);Assert.Equal(2,result.LastSuccess!.Manifest.Sources.Length);Assert.Equal(2,result.LastSuccess.Manifest.Files.Select(f=>f.StoragePath).Distinct().Count());
    }
    [Fact] public async Task FinalCommitRechecksInventory()
    {
        await Seed();var result=await Service(checkpoint:p=>{if(p=="commit")File.WriteAllText(Path.Combine(Source,"late.sav"),"late");}).BackUpAsync(id,false);
        Assert.Equal(BackupPhase.Failed,result.Phase);Assert.Null(result.LastSuccess);
    }
    [Fact] public async Task BackupStatusNeverClaimsSuccessWhenSnapshotsDisappear()
    {
        await Seed();var first=await Service().BackUpAsync(id,false);Directory.Delete(first.LastSuccess!.Directory,true);
        Assert.Equal(BackupPhase.Failed,(await Service().ReadAsync(id)).Phase);
    }
    [Fact] public async Task ProtectedStatusPreservesLastVerifiedSnapshot()
    {
        await Seed();var first=await Service().BackUpAsync(id,false);var status=Path.Combine(Path.GetDirectoryName(Backups)!,"backup-status.json");File.WriteAllText(status,"{\"schemaVersion\":99}");
        var result=await Service().BackUpAsync(id,false);Assert.Equal(BackupPhase.Failed,result.Phase);Assert.Equal(first.LastSuccess!.Directory,result.LastSuccess!.Directory);Assert.Equal("{\"schemaVersion\":99}",File.ReadAllText(status));
    }
    [Fact] public async Task CorruptedStagedCopyCannotBeCommitted()
    {
        await Seed();var result=await Service(checkpoint:p=>{if(p=="verify"){var stage=Assert.Single(Directory.GetDirectories(Backups));File.WriteAllText(Assert.Single(Directory.GetFiles(Path.Combine(stage,"Files"),"*",SearchOption.AllDirectories)),"wrong bytes");}}).BackUpAsync(id,false);
        Assert.Equal(BackupPhase.Failed,result.Phase);Assert.Null(result.LastSuccess);
    }
    [Fact] public async Task AnExternalRelaunchPreventsCommit()
    {
        await Seed();var running=false;var result=await Service(running:_=>Task.FromResult(running),checkpoint:p=>{if(p=="verify")running=true;}).BackUpAsync(id,false);
        Assert.Equal(BackupPhase.Failed,result.Phase);Assert.Null(result.LastSuccess);
    }
    [Fact] public async Task ShutdownObservationIsIgnoredAndQueuedExitWorkIsDrained()
    {
        await Seed();var signals=new Signals();var waiting=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service=Service();var coordinator=new BackupCoordinator(signals,service,_=>{started.TrySetResult();return waiting.Task;});
        signals.End(id,false);await coordinator.FlushAsync();Assert.Empty(Directory.GetDirectories(Backups));
        signals.End(id,true);await started.Task.WaitAsync(TimeSpan.FromSeconds(5));await coordinator.DisposeAsync();waiting.TrySetResult();signals.End(id,true);await coordinator.FlushAsync();Assert.Empty(Directory.GetDirectories(Backups));
    }
    [Fact] public async Task LinkedBackupDestinationIsNotFollowed()
    {
        if(!OperatingSystem.IsWindows())return;
        await Seed();Directory.Delete(Backups);var target=Path.Combine(root,"unrelated");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"keep.txt"),"preserve");
        var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("/c");start.ArgumentList.Add("mklink");start.ArgumentList.Add("/J");start.ArgumentList.Add(Backups);start.ArgumentList.Add(target);
        using var process=System.Diagnostics.Process.Start(start)!;await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);
        try{var result=await Service().BackUpAsync(id,false);Assert.Equal(BackupPhase.Failed,result.Phase);Assert.Single(Directory.GetFiles(target));Assert.Equal("preserve",File.ReadAllText(Path.Combine(target,"keep.txt")));}
        finally{Directory.Delete(Backups);}
    }
    [Fact] public async Task WindowsProcessInspectionFailureIsRetryableAndDoesNotLeaveWorkingState()
    {
        await Seed();var service=Service(running:_=>throw new System.ComponentModel.Win32Exception(5,"Cannot inspect process"));
        var result=await service.BackUpAsync(id,false);Assert.Equal(BackupPhase.Failed,result.Phase);Assert.False(service.IsWorking(id));
    }
    [Fact] public async Task OneSourceBecomingEmptyCannotBeHiddenByAnotherReadableSource()
    {
        await Seed();var other=Path.Combine(root,"other.sav");File.WriteAllText(other,"other");var profile=(await Store.LoadProfileAsync(id)).Profile.Value!;await Store.SaveProfileAsync(profile with{SaveSources=[Source,other]});
        var discovery=new SaveDiscoveryService(Paths,Store,Env);discovery.Changed+=(_,_)=>File.Delete(Path.Combine(Source,"slot.sav"));
        var service=new BackupService(Paths,Store,discovery,new(),_=>Task.FromResult(false));var result=await service.BackUpAsync(id,false);
        Assert.Equal(BackupPhase.Failed,result.Phase);Assert.Null(result.LastSuccess);
    }
    [Fact] public async Task WorkingStateRetainsTheLastVerifiedBackup()
    {
        await Seed();var service=Service();var first=await service.BackUpAsync(id,false);var events=new List<BackupResult>();service.Changed+=(_,result)=>events.Add(result);
        File.WriteAllText(Path.Combine(Source,"slot.sav"),"updated");await service.BackUpAsync(id,false);
        Assert.All(events.Where(e=>e.Phase==BackupPhase.Working),e=>Assert.Equal(first.LastSuccess!.Directory,e.LastSuccess?.Directory));
    }
    [Fact] public async Task CompletedManifestRemainsSuccessfulIfStatusWriteFails()
    {
        await Seed();var service=Service(checkpoint:p=>{if(p=="commit")Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Backups)!,"backup-status.json"));});
        var result=await service.BackUpAsync(id,false);Assert.Equal(BackupPhase.Complete,result.Phase);Assert.NotNull(result.LastSuccess);Assert.Contains("status could not be saved",result.Message);
        Assert.Equal(result.LastSuccess.Directory,(await Service().ReadAsync(id)).LastSuccess!.Directory);
    }
    private sealed class Signals:IGameSessionSignals
    {
        public event EventHandler<LaunchState>? StateChanged {add{} remove{}}
        public event EventHandler<GameSessionEnded>? SessionEnded;
        public void End(Guid gameId,bool confirmed)=>SessionEnded?.Invoke(this,new(gameId,confirmed));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}