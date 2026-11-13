using System.IO;
using System.Text.Json;
using GameVault.Infrastructure.Saves;
using Xunit.Abstractions;
using GameVault.Core;
namespace GameVault.App.Tests;
public sealed class WorkflowValidationTests(ITestOutputHelper output)
{
    [Fact] [Trait("Category","Workflow")]
    public async Task FixtureProtocolLoadsProgressAndRejectsTraversalAndReplayedSequence()
    {
        await using var fixture=await WorkflowFixture.CreateAsync(false);await fixture.StartAsync();
        Assert.True((await fixture.SendAsync("save","Local/slot.bin","chapter-a")).Ok);
        Assert.Equal("chapter-a",(await fixture.SendAsync("load","Local/slot.bin")).Progress);
        var replay=await fixture.SendAsync("save","Local/slot.bin","replayed",1);Assert.False(replay.Ok);
        var unsafePath=await fixture.SendAsync("save","../escape.bin","outside");Assert.False(unsafePath.Ok);Assert.False(File.Exists(Path.Combine(fixture.Root,"escape.bin")));
        Assert.Equal("chapter-a",(await fixture.SendAsync("load","Local/slot.bin")).Progress);await fixture.ExitAsync();
    }
    [Fact] [Trait("Category","Workflow")]
    public async Task CompletePlayBackupRetentionRestoreUndoAndControlledUninstall()
    {
        await using var fixture=await WorkflowFixture.CreateAsync();var id=fixture.Profile.Id;Assert.Empty(fixture.Profile.SaveSources);Assert.False(File.Exists(fixture.SavePath));
        await fixture.StartAsync();Assert.True((await fixture.SendAsync("save",fixture.SaveRelative,"chapter-a")).Ok);
        await WorkflowFixture.WaitUntil(async()=>(await fixture.Discovery.ReadAsync(id)).Phase==SaveDiscoveryPhase.SourcesFound);
        fixture.Clock.Advance(12,12);Assert.Empty(await fixture.Launch.CheckpointActiveAsync());
        var originalSession=fixture.Launch.GetSessionProgress(id)!.SessionId;fixture.Clock.Advance(0,600);Assert.Empty(await fixture.Launch.CheckpointActiveAsync());Assert.Equal(12,fixture.Launch.GetSessionProgress(id)!.SessionSeconds);
        var hidden=false;var lifetime=new TrayLifetime(()=>hidden=true,()=>hidden=false,()=>Task.FromResult(true),()=>throw new InvalidOperationException("Hide must not quit."));await lifetime.CloseAsync(true,true);Assert.True(hidden);
        await fixture.RecoverAbandonedCheckpointAsync();Assert.Equal(originalSession,fixture.Launch.GetSessionProgress(id)!.SessionId);Assert.Equal(12,fixture.Launch.GetSessionProgress(id)!.SessionSeconds);
        fixture.Clock.Advance(3,3);await fixture.ExitAsync();Assert.True(hidden);var first=(await fixture.Backups.ReadAsync(id)).LastSuccess!;Assert.NotNull(first);Assert.Equal(15,Assert.Single((await fixture.Store.LoadProfileAsync(id)).Statistics.Value!.Sessions).DurationSeconds);
        await fixture.History.SetPinnedAsync(id,first.Manifest.Id,true);Assert.True((await fixture.History.ApplyRetentionAsync(id,new(1,false),new(20,false))).Complete);
        await fixture.StartAsync();Assert.True((await fixture.SendAsync("save",fixture.SaveRelative,"chapter-b")).Ok);Assert.True((await fixture.SendAsync("save",fixture.ExtraRelative,"later-slot")).Ok);fixture.Clock.Advance(4,4);await fixture.ExitAsync();var middle=(await fixture.Backups.ReadAsync(id)).LastSuccess!;
        await fixture.StartAsync();Assert.True((await fixture.SendAsync("save",fixture.SaveRelative,"chapter-c")).Ok);fixture.Clock.Advance(4,4);await fixture.ExitAsync();
        var history=await fixture.History.ListAsync(id);Assert.Equal(2,history.Entries.Length);Assert.True(history.Entries.Single(e=>e.Backup.Manifest.Id==first.Manifest.Id).Pinned);Assert.False(Directory.Exists(middle.Directory));
        var unrelated=Path.Combine(Path.GetDirectoryName(fixture.SavePath)!,"readme.txt");File.WriteAllText(unrelated,"unrelated user note");
        var request=new RestoreRequest(id,new(first.Manifest.Id,null),[],[]);var preview=await fixture.Restores.PreviewAsync(request);var restored=await fixture.Restores.ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.Completed,restored.Phase);Assert.False(File.Exists(Path.Combine(fixture.Working,fixture.ExtraRelative)));Assert.Equal("unrelated user note",File.ReadAllText(unrelated));
        await fixture.StartAsync();var loadedOld=await fixture.SendAsync("load",fixture.SaveRelative);Assert.True(loadedOld.Ok,loadedOld.Error);Assert.Equal("chapter-a",loadedOld.Progress);fixture.Clock.Advance(2,2);await fixture.ExitAsync();
        var undoRequest=new RestoreRequest(id,new(null,restored.RecoveryId),[],[]);var undoPreview=await fixture.Restores.PreviewAsync(undoRequest);Assert.Equal(RestorePhase.Completed,(await fixture.Restores.ExecuteAsync(undoRequest,undoPreview.Fingerprint)).Phase);
        await fixture.StartAsync();var loadedCurrent=await fixture.SendAsync("load",fixture.SaveRelative);Assert.True(loadedCurrent.Ok,loadedCurrent.Error);Assert.Equal("chapter-c",loadedCurrent.Progress);Assert.Equal("later-slot",(await fixture.SendAsync("load",fixture.ExtraRelative)).Progress);fixture.Clock.Advance(2,2);await fixture.ExitAsync();
        Assert.True(Directory.Exists(restored.RecoveryDirectory));Assert.Equal("unrelated user note",File.ReadAllText(unrelated));
        var statistics=(await fixture.Store.LoadProfileAsync(id)).Statistics.Value!;Assert.Equal(5,statistics.Sessions.Length);Assert.Equal(5,statistics.Sessions.Select(s=>s.Id).Distinct().Count());Assert.Equal(27,statistics.Sessions.Sum(s=>s.DurationSeconds));
        var before=fixture.ProfileHashes();fixture.RemoveCopiedInstallation();var listed=Assert.Single((await fixture.Library.ListAsync()).Games);Assert.True(listed.ExecutableMissing);Assert.Equal(id,listed.Id);Assert.Equal(before.OrderBy(p=>p.Key),fixture.ProfileHashes().OrderBy(p=>p.Key));
        Report("full-workflow",new{sessions=5,simulatedAwakeSeconds=27,simulatedExcludedSleepSeconds=600,simulatedExcludedDowntimeSeconds=300,retainedPinnedAndUnpinned=2,restoredProgress=loadedOld.Progress,undoneProgress=loadedCurrent.Progress,controlledInstallationRemovalPreservedProfile=true,abandonedCheckpointReplay=true});
    }
    [Fact] [Trait("Category","Workflow")]
    public async Task FailureBoundariesPreserveSavesAndVerifiedHistory()
    {
        await using var fixture=await WorkflowFixture.CreateAsync(false);var id=fixture.Profile.Id;
        Assert.Equal(BackupPhase.Failed,(await fixture.Backups.BackUpAsync(id,false)).Phase);Assert.Empty((await fixture.History.ListAsync(id)).Entries);
        var bad=Path.Combine(fixture.Installation,"broken.exe");File.WriteAllText(bad,"invalid executable fixture");var invalid=(await fixture.Library.SaveAsync(new(null,"Broken fixture",bad,fixture.Working,[]))).Profile;await fixture.Launch.RunAsync(invalid.Id).WaitAsync(TimeSpan.FromSeconds(8));Assert.Equal(LaunchPhase.Failed,fixture.Launch.GetState(invalid.Id).Phase);Assert.Empty((await fixture.Store.LoadProfileAsync(invalid.Id)).Statistics.Value!.Sessions);
        await fixture.StartAsync();Assert.True((await fixture.SendAsync("save",fixture.SaveRelative,"old")).Ok);fixture.Clock.Advance(5,5);await fixture.ExitAsync();var first=(await fixture.Backups.BackUpAsync(id,false)).LastSuccess!;
        using(var locked=new FileStream(fixture.SavePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){var failed=await fixture.Backups.BackUpAsync(id,false);Assert.Equal(BackupPhase.Failed,failed.Phase);Assert.Equal(first.Directory,failed.LastSuccess!.Directory);}
        await fixture.StartAsync();Assert.True((await fixture.SendAsync("save",fixture.SaveRelative,"current")).Ok);fixture.Clock.Advance(4,4);await fixture.ExitAsync();
        var fullDisk=new BackupService(fixture.Paths,fixture.Store,fixture.Discovery,new(),fixture.Launch.IsGameRunningAsync,p=>{if(p=="copy")throw new IOException("Synthetic insufficient disk space",unchecked((int)0x80070070));});var noSpace=await fullDisk.BackUpAsync(id,false);Assert.Equal(BackupPhase.Failed,noSpace.Phase);Assert.Equal(first.Directory,noSpace.LastSuccess!.Directory);Assert.Single((await fixture.History.ListAsync(id)).Entries);
        var restores=new RestoreService(fixture.Paths,fixture.Store,new(),fixture.Launch.IsGameRunningAsync,p=>{if(p=="after-mutation")throw new IOException("Synthetic failed restore");});var request=new RestoreRequest(id,new(first.Manifest.Id,null),[],[]);var preview=await restores.PreviewAsync(request);var rollback=await restores.ExecuteAsync(request,preview.Fingerprint);Assert.Equal(RestorePhase.RolledBack,rollback.Phase);Assert.True(Directory.Exists(rollback.RecoveryDirectory));await fixture.Store.RequireNoPendingRestoreAsync(id);
        await fixture.StartAsync();var loaded=await fixture.SendAsync("load",fixture.SaveRelative);Assert.True(loaded.Ok,loaded.Error);Assert.Equal("current",loaded.Progress);await fixture.ExitAsync();
        Report("failure-boundaries",new{missingSaves="failed-without-empty-backup",invalidLaunch="failed-without-session",lockedSave="previous-backup-preserved",insufficientSpace="injected-IO-failure",failedRestore="rolled-back",currentProgressLoaded=loaded.Progress,actualDiskFilling=false});
    }
    private void Report(string scenario,object observations)
    {
        var json=JsonSerializer.Serialize(new{schemaVersion=1,scenario,automatedPassed=true,realGameValidated=false,uiValidated=false,observations},new JsonSerializerOptions{WriteIndented=true});output.WriteLine(json);
        var directory=Environment.GetEnvironmentVariable("GAMEVAULT_TEST_EVIDENCE_DIRECTORY");if(string.IsNullOrWhiteSpace(directory))return;
        if(!Path.IsPathFullyQualified(directory))throw new IOException("Workflow evidence directory must be absolute.");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,scenario+".json"),json);
    }}