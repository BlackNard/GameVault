using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class RestoreTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault restore",Guid.NewGuid().ToString("N"));
    private readonly Guid id=Guid.NewGuid();
    private ApplicationPaths Paths=>ApplicationPaths.FromRoot(Path.Combine(root,"app"));
    private AppDataStore Store=>new(Paths);
    private string Saves=>Path.Combine(root,"saves");
    private SaveDiscoveryEnvironment Env=>new(Path.Combine(root,"user"),Path.Combine(root,"docs"),Path.Combine(root,"saved"),Path.Combine(root,"roam"),Path.Combine(root,"local"),Path.Combine(root,"public"),[]);
    private BackupService Backups=>new(Paths,Store,new(Paths,Store,Env),new(),_=>Task.FromResult(false));
    private RestoreService Service(Func<Guid,Task<bool>>? running=null,Action<string>? checkpoint=null)=>new(Paths,Store,new(),running??(_=>Task.FromResult(false)),checkpoint);
    private async Task<CompletedBackup> Seed(bool file=false)
    {
        Directory.CreateDirectory(Paths.Root);Directory.CreateDirectory(Saves);File.WriteAllText(Path.Combine(Saves,"slot.sav"),"old");
        await Store.InitializeAsync();await Store.SaveProfileAsync(new(1,id,"Example",Path.Combine(root,"game.exe"),root,[file?Path.Combine(Saves,"slot.sav"):Saves],GameStatus.Playing,20,false,false));
        return (await Backups.BackUpAsync(id,false)).LastSuccess!;
    }
    private RestoreRequest Request(CompletedBackup backup,RestoreDestination[]? maps=null,string[]? remove=null)=>new(id,new(backup.Manifest.Id,null),maps??[],remove??[]);
    [Fact] public async Task RestoreTrackedFilesAndUndoAfterRestartKeepsUnrelatedData()
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");File.WriteAllText(Path.Combine(Saves,"later.sav"),"later");await Backups.BackUpAsync(id,false);File.WriteAllText(Path.Combine(Saves,"notes.txt"),"unrelated");
        var request=Request(backup);var service=Service();var preview=await service.PreviewAsync(request);Assert.Contains(preview.Changes,c=>c.Action==RestoreAction.Remove&&c.TargetPath.EndsWith("later.sav"));Assert.Contains(preview.OtherFiles,f=>f.Path.EndsWith("notes.txt"));
        var result=await service.ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.Completed,result.Phase);Assert.Equal("old",File.ReadAllText(Path.Combine(Saves,"slot.sav")));Assert.False(File.Exists(Path.Combine(Saves,"later.sav")));Assert.Equal("unrelated",File.ReadAllText(Path.Combine(Saves,"notes.txt")));
        Assert.NotNull(result.RecoveryId);var undo=new RestoreRequest(id,new(null,result.RecoveryId),[],[]);var undoPreview=await Service().PreviewAsync(undo);var undone=await Service().ExecuteAsync(undo,undoPreview.Fingerprint);
        Assert.Equal(RestorePhase.Completed,undone.Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));Assert.Equal("later",File.ReadAllText(Path.Combine(Saves,"later.sav")));Assert.Equal("unrelated",File.ReadAllText(Path.Combine(Saves,"notes.txt")));
        Assert.Equal(2,(await Service().ListRecoveriesAsync(id)).Snapshots.Length);
    }
    [Fact] public async Task UnknownFilesRequireExplicitRemovalAndStalePreviewCannotWrite()
    {
        var backup=await Seed();var extra=Path.Combine(Saves,"unknown.sav");File.WriteAllText(extra,"extra");var request=Request(backup);var preview=await Service().PreviewAsync(request);Assert.DoesNotContain(preview.Changes,c=>c.TargetPath==extra);
        File.WriteAllText(Path.Combine(Saves,"slot.sav"),"changed after preview");var stale=await Service().ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.Failed,stale.Phase);Assert.Equal("changed after preview",File.ReadAllText(Path.Combine(Saves,"slot.sav")));
        request=Request(backup,remove:[extra]);preview=await Service().PreviewAsync(request);var done=await Service().ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.Completed,done.Phase);Assert.False(File.Exists(extra));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task MovedOrMissingDestinationsSupportFolderAndFileMappings(bool file)
    {
        var backup=await Seed(file);var target=Path.Combine(root,"reinstalled",file?"renamed.sav":"saves");var request=Request(backup,[new(backup.Manifest.Sources[0].Id,target)]);var preview=await Service().PreviewAsync(request);
        Assert.All(preview.Changes,c=>Assert.StartsWith(target,c.TargetPath));var result=await Service().ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.Completed,result.Phase);Assert.Equal("old",File.ReadAllText(file?target:Path.Combine(target,"slot.sav")));
    }
    [Theory] [InlineData("preserve")] [InlineData("prepared")]
    public async Task PreservationFailureAbortsBeforeAnyReplacement(string point)
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");var service=Service(checkpoint:p=>{if(p==point)throw new IOException("Synthetic preservation failure");});var request=Request(backup);var preview=await service.PreviewAsync(request);var result=await service.ExecuteAsync(request,preview.Fingerprint);
        Assert.Equal(RestorePhase.Failed,result.Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));
    }
    [Fact] public async Task PartialFailureRollsBackAndKeepsRecovery()
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");var service=Service(checkpoint:p=>{if(p=="after-mutation")throw new IOException("Synthetic copy failure");});var request=Request(backup);var preview=await service.PreviewAsync(request);var result=await service.ExecuteAsync(request,preview.Fingerprint);
        Assert.Equal(RestorePhase.RolledBack,result.Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));Assert.True(Directory.Exists(result.RecoveryDirectory));await Store.RequireNoPendingRestoreAsync(id);
    }
    [Fact] public async Task RollbackFailureIsDurablyBlockedAndUndoRecoversIt()
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");var service=Service(checkpoint:p=>{if(p is "after-mutation" or "rollback")throw new IOException("Synthetic failure");});var request=Request(backup);var preview=await service.PreviewAsync(request);var result=await service.ExecuteAsync(request,preview.Fingerprint);
        Assert.Equal(RestorePhase.RecoveryRequired,result.Phase);await Assert.ThrowsAsync<IOException>(()=>Store.RequireNoPendingRestoreAsync(id));Assert.True(Directory.Exists(result.RecoveryDirectory));
        var undo=new RestoreRequest(id,new(null,result.RecoveryId),[],[]);var fresh=Service();var undoPreview=await fresh.PreviewAsync(undo);Assert.Equal(RestorePhase.Completed,(await fresh.ExecuteAsync(undo,undoPreview.Fingerprint)).Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));await Store.RequireNoPendingRestoreAsync(id);
    }
    [Fact] public async Task RunningGameUnsafeTargetsAndCorruptPayloadCannotRestore()
    {
        var backup=await Seed();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(_=>Task.FromResult(true)).PreviewAsync(Request(backup)));
        await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(Request(backup,[new(backup.Manifest.Sources[0].Id,Paths.Profiles)])));
        File.WriteAllText(Path.Combine(backup.Directory,"Files",backup.Manifest.Files[0].StoragePath),"corrupt");await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(Request(backup)));Assert.Equal("old",File.ReadAllText(Path.Combine(Saves,"slot.sav")));
    }
    [Fact] public async Task RecoveryCopyIsReverifiedBeforeReplacingCurrentSaves()
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");
        var service=Service(checkpoint:p=>{if(p=="prepared"){var rootRecovery=Path.Combine(Paths.Profiles,id.ToString("N"),"RestoreRecovery");var folder=Assert.Single(Directory.GetDirectories(rootRecovery));var file=Assert.Single(Directory.GetFiles(Path.Combine(folder,"Before"),"*",SearchOption.AllDirectories));File.WriteAllText(file,"damaged");}});
        var request=Request(backup);var preview=await service.PreviewAsync(request);var result=await service.ExecuteAsync(request,preview.Fingerprint);
        Assert.Equal(RestorePhase.Failed,result.Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));
    }
    [Fact] public async Task PendingOrProtectedJournalBlocksBackupsHistoryAndOtherRestores()
    {
        var backup=await Seed();await Store.BeginRestoreAsync(new(1,id,Guid.NewGuid(),RestorePhase.Prepared,DateTimeOffset.UtcNow,"Synthetic interrupted restore"),null);
        Assert.Equal(BackupPhase.Failed,(await Backups.BackUpAsync(id,false)).Phase);
        var history=new BackupHistoryService(Paths,Store,new(),_=>Task.FromResult(false));await Assert.ThrowsAsync<IOException>(()=>history.SetPinnedAsync(id,backup.Manifest.Id,true));await Assert.ThrowsAsync<IOException>(()=>history.ApplyRetentionAsync(id,new(1,false),new(20,false)));
        await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(Request(backup)));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CancellationOrExternalRelaunchAfterMutationKeepsSpecificRecoveryState(bool externalGame)
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");using var stop=new CancellationTokenSource();var running=false;
        var service=Service(_=>Task.FromResult(running),p=>{if(p=="after-mutation"){if(externalGame)running=true;else stop.Cancel();}});var request=Request(backup);var preview=await service.PreviewAsync(request);var result=await service.ExecuteAsync(request,preview.Fingerprint,stop.Token);
        Assert.Equal(externalGame?RestorePhase.RecoveryRequired:RestorePhase.RolledBack,result.Phase);Assert.True(Directory.Exists(result.RecoveryDirectory));if(!externalGame)Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));
    }
    [Fact] public async Task LockedOrUnsafePayloadCannotReplaceSaves()
    {
        var backup=await Seed();using(var locked=new FileStream(Path.Combine(Saves,"slot.sav"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(Request(backup)));
        var manifest=Path.Combine(backup.Directory,"manifest.json");File.WriteAllText(manifest,File.ReadAllText(manifest).Replace("\"relativePath\": \"slot.sav\"","\"relativePath\": \"../outside.sav\""));
        await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(Request(backup)));Assert.Equal("old",File.ReadAllText(Path.Combine(Saves,"slot.sav")));
    }
    [Fact] public async Task MultipleSourcesAreRestoredAndRecoverySurvivesOrdinaryRetention()
    {
        await Seed();var other=Path.Combine(root,"other","second.sav");Directory.CreateDirectory(Path.GetDirectoryName(other)!);File.WriteAllText(other,"second old");var profile=(await Store.LoadProfileAsync(id)).Profile.Value!;await Store.SaveProfileAsync(profile with{SaveSources=[Saves,other]});var backup=(await Backups.BackUpAsync(id,false)).LastSuccess!;
        File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");File.WriteAllText(other,"second new");var request=Request(backup);var preview=await Service().PreviewAsync(request);var result=await Service().ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.Completed,result.Phase);Assert.Equal("second old",File.ReadAllText(other));
        var history=new BackupHistoryService(Paths,Store,new(),_=>Task.FromResult(false));var applied=await history.ApplyRetentionAsync(id,new(1,false),new(20,false));Assert.True(applied.Complete,applied.Message);Assert.True(Directory.Exists(result.RecoveryDirectory));Assert.Single((await Service().ListRecoveriesAsync(id)).Snapshots);
    }
    [Fact] public async Task FileParentCollisionIsRejectedInPreview()
    {
        await Seed();var nested=Path.Combine(Saves,"nested");Directory.CreateDirectory(nested);var file=Path.Combine(nested,"slot.sav");File.WriteAllText(file,"nested save");var backup=(await Backups.BackUpAsync(id,false)).LastSuccess!;
        File.Delete(file);Directory.Delete(nested);File.WriteAllText(nested,"unrelated file");await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(Request(backup)));Assert.Equal("unrelated file",File.ReadAllText(nested));
    }
    [Fact] public async Task ProtectedJournalAndLinkedDestinationStayUntouched()
    {
        var backup=await Seed();var state=Path.Combine(Paths.Profiles,id.ToString("N"),"restore-state.json");File.WriteAllText(state,"{\"schemaVersion\":99}");await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(Request(backup)));Assert.Equal("{\"schemaVersion\":99}",File.ReadAllText(state));
    }
    [Fact] public async Task JournalWriteFailureBeforePrepareCannotReplaceSaves()
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");var failing=new AppDataStore(Paths,path=>{if(path.EndsWith("restore-state.json",StringComparison.Ordinal))throw new IOException("Synthetic journal failure");});
        var service=new RestoreService(Paths,failing,new(),_=>Task.FromResult(false));var request=Request(backup);var preview=await service.PreviewAsync(request);var result=await service.ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.Failed,result.Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));
    }
    [Fact] public async Task LinkedDestinationIsRejectedWithoutFollowingIt()
    {
        if(!OperatingSystem.IsWindows())return;var backup=await Seed();var target=Path.Combine(root,"linked");
        var info=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"/c","mklink","/J",target,Saves})info.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(info)!;await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);
        try{await Assert.ThrowsAnyAsync<IOException>(()=>Service().PreviewAsync(Request(backup,[new(backup.Manifest.Sources[0].Id,target)])));Assert.Equal("old",File.ReadAllText(Path.Combine(Saves,"slot.sav")));}finally{Directory.Delete(target);}
    }
    [Fact] public async Task InterruptedPreparedStateCanBeRecoveredAfterRestart()
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");var request=Request(backup);var preview=await Service().PreviewAsync(request);var completed=await Service().ExecuteAsync(request,preview.Fingerprint);
        await Store.BeginRestoreAsync(new(1,id,completed.RecoveryId!.Value,RestorePhase.Prepared,DateTimeOffset.UtcNow,"Synthetic crash after preparation"),null);
        var restarted=Service();Assert.Contains((await restarted.ListRecoveriesAsync(id)).Warnings,w=>w.Contains("needs recovery",StringComparison.Ordinal));await Assert.ThrowsAsync<IOException>(()=>restarted.PreviewAsync(request));
        var undo=new RestoreRequest(id,new(null,completed.RecoveryId),[],[]);var fresh=await restarted.PreviewAsync(undo);Assert.Equal(RestorePhase.Completed,(await restarted.ExecuteAsync(undo,fresh.Fingerprint)).Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));await Store.RequireNoPendingRestoreAsync(id);
    }
    [Fact] public async Task FailedFinalJournalCommitKeepsDurableRecoveryGuard()
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");int writes=0;
        var failing=new AppDataStore(Paths,path=>{if(path.EndsWith("restore-state.json",StringComparison.Ordinal)&&Interlocked.Increment(ref writes)>1)throw new IOException("Synthetic final journal failure");});
        var service=new RestoreService(Paths,failing,new(),_=>Task.FromResult(false));var request=Request(backup);var preview=await service.PreviewAsync(request);var result=await service.ExecuteAsync(request,preview.Fingerprint);
        Assert.Equal(RestorePhase.RecoveryRequired,result.Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));Assert.True(Directory.Exists(result.RecoveryDirectory));await Assert.ThrowsAsync<IOException>(()=>Store.RequireNoPendingRestoreAsync(id));
    }
    [Fact] public async Task ConcurrentOperationAndOverlappingMappingsCannotRestore()
    {
        var backup=await Seed();using(var held=new GameOperationLeases().TryAcquire(id)!)await Assert.ThrowsAsync<InvalidOperationException>(()=>Service().PreviewAsync(Request(backup)));
        await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(Request(backup,[new(backup.Manifest.Sources[0].Id,Path.Combine(Saves,"..","unsafe"))])));
        Assert.Equal("old",File.ReadAllText(Path.Combine(Saves,"slot.sav")));
    }
    [Fact] public async Task FailedUndoCannotClearTheOriginalRecoveryGuard()
    {
        var backup=await Seed();File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");var request=Request(backup);
        var broken=Service(checkpoint:p=>{if(p is "after-mutation" or "rollback")throw new IOException("original failure");});var preview=await broken.PreviewAsync(request);var original=await broken.ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.RecoveryRequired,original.Phase);
        var undo=new RestoreRequest(id,new(null,original.RecoveryId),[],[]);var failingUndo=Service(checkpoint:p=>{if(p=="after-mutation")throw new IOException("undo failed");});var undoPreview=await failingUndo.PreviewAsync(undo);var failed=await failingUndo.ExecuteAsync(undo,undoPreview.Fingerprint);
        Assert.Equal(RestorePhase.RecoveryRequired,failed.Phase);Assert.Equal(original.RecoveryId,failed.RecoveryId);await Assert.ThrowsAsync<IOException>(()=>Store.RequireNoPendingRestoreAsync(id));Assert.Equal(BackupPhase.Failed,(await Backups.BackUpAsync(id,false)).Phase);
        var pending=(await Store.LoadRestoreJournalAsync(id)).Value!;
        await Store.BeginRestoreAsync(pending with{Phase=RestorePhase.Prepared},original.RecoveryId);
        await Assert.ThrowsAsync<IOException>(()=>Service().PreviewAsync(new(id,new(null,pending.OperationId),[],[])));
        var correct=await Service().PreviewAsync(undo);Assert.Equal(RestorePhase.Completed,(await Service().ExecuteAsync(undo,correct.Fingerprint)).Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));await Store.RequireNoPendingRestoreAsync(id);
    }
    [Fact] public async Task AnUnchangedSelectedFileChangingDuringRestoreCannotReportSuccess()
    {
        await Seed();var stable=Path.Combine(Saves,"stable.sav");File.WriteAllText(stable,"stable old");var backup=(await Backups.BackUpAsync(id,false)).LastSuccess!;File.WriteAllText(Path.Combine(Saves,"slot.sav"),"new");
        var service=Service(checkpoint:p=>{if(p=="after-mutation")File.WriteAllText(stable,"external change");});var request=Request(backup);var preview=await service.PreviewAsync(request);var result=await service.ExecuteAsync(request,preview.Fingerprint);
        Assert.Equal(RestorePhase.RolledBack,result.Phase);Assert.Equal("new",File.ReadAllText(Path.Combine(Saves,"slot.sav")));Assert.Equal("external change",File.ReadAllText(stable));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}