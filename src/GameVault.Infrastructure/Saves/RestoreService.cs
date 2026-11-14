using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;

public sealed partial class RestoreService
{
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly GameOperationLeases leases;
    private readonly Func<Guid,Task<bool>> isRunning;
    private readonly Action<string>? checkpoint;
    private readonly BackupStorage backups;
    private readonly RestoreStorage recovery;
    public RestoreService(ApplicationPaths paths,AppDataStore store,GameOperationLeases leases,Func<Guid,Task<bool>> isRunning):this(paths,store,leases,isRunning,null){}
    internal RestoreService(ApplicationPaths paths,AppDataStore store,GameOperationLeases leases,Func<Guid,Task<bool>> isRunning,Action<string>? checkpoint)
    {this.paths=paths;this.store=store;this.leases=leases;this.isRunning=isRunning;this.checkpoint=checkpoint;backups=new(paths);recovery=new(paths);}
    private IDisposable Acquire(Guid id)=>leases.TryAcquire(id)??throw new InvalidOperationException("A game session, backup, restore or history operation is active. Retry when it finishes.");
    private async Task Idle(Guid id,CancellationToken token){token.ThrowIfCancellationRequested();if(await isRunning(id).ConfigureAwait(false))throw new InvalidOperationException("Close the game before previewing or restoring saves.");}
    public async Task<RestoreRecoveries> ListRecoveriesAsync(Guid id)
    {
        var warnings=new List<string>();RestoreRecoveryInfo[] snapshots=[];
        try{snapshots=await Task.Run(()=>recovery.List(id,warnings)).ConfigureAwait(false);}catch(Exception e)when(Expected(e)){warnings.Add(e.Message);}
        var journal=await store.LoadRestoreJournalAsync(id).ConfigureAwait(false);if(journal.Message is string message)warnings.Add(message);
        if(journal.Value?.Phase is RestorePhase.Prepared or RestorePhase.RecoveryRequired)warnings.Add("A restore needs recovery before the game can launch. Undo recovery "+(journal.Value.RequiredRecoveryId??journal.Value.OperationId).ToString("N")+".");
        return new(snapshots,journal,warnings.ToArray());
    }
    public async Task<BackupSource[]> GetTargetsAsync(Guid id,RestoreChoice choice,CancellationToken token=default)
    {
        using var lease=Acquire(id);using var backupLease=backups.Acquire();await Idle(id,token).ConfigureAwait(false);await store.RequireRestoreAllowedAsync(id,choice.RecoveryId).ConfigureAwait(false);
        return (await Task.Run(()=>LoadImage(id,choice,token),token).ConfigureAwait(false)).Sources;
    }
    public async Task<RestorePreview> PreviewAsync(RestoreRequest request,CancellationToken token=default)
    {request=Freeze(request);using var lease=Acquire(request.GameId);using var backupLease=backups.Acquire();return (await Build(request,token).ConfigureAwait(false)).Preview;}
    public async Task<RestoreResult> ExecuteAsync(RestoreRequest request,string expectedFingerprint,CancellationToken token=default)
    {
        request=Freeze(request);using var lease=Acquire(request.GameId);Guid? operation=null;string? folder=null;var prepared=false;var held=new List<FileStream>();var mutated=new List<RestoreChange>();var createdDirectories=new List<string>();Plan? plan=null;RestoreRecoveryDocument? document=null;IDisposable? backupLease=null;
        try
        {
            backupLease=backups.Acquire();
            plan=await Build(request,token).ConfigureAwait(false);
            if(string.IsNullOrWhiteSpace(expectedFingerprint)||plan.Preview.Fingerprint!=expectedFingerprint)throw new IOException("The backup, destinations or current saves changed. Preview again before restoring.");
            var op=Guid.NewGuid();var stage=Path.Combine(recovery.Root(request.GameId),".pending-"+op.ToString("N"));recovery.Ensure(stage);
            var before=plan.Before.Files.Select(f=>new BackupFile(f.SourceId,f.RelativePath,Key(f.SourceId,f.RelativePath),f.Size,f.Hash)).ToArray();
            document=new(1,request.GameId,op,DateTimeOffset.UtcNow,request.Choice,plan.Targets,before,plan.Before.Directories,plan.Preview.Changes,plan.Image.Files);
            await Task.Run(()=>
            {
                foreach(var file in plan.Image.Files){token.ThrowIfCancellationRequested();var target=Path.Combine(stage,"Selected",file.StoragePath);recovery.Ensure(Path.GetDirectoryName(target)!);RestoreFiles.CopyVerified(plan.Image.Payload[Key(file.SourceId,file.RelativePath)],target,file.Size,file.Sha256,token);}
                foreach(var file in plan.Before.Files){checkpoint?.Invoke("preserve");token.ThrowIfCancellationRequested();var target=Path.Combine(stage,"Before",Key(file.SourceId,file.RelativePath));recovery.Ensure(Path.GetDirectoryName(target)!);RestoreFiles.CopyVerified(file.Path,target,file.Size,file.Hash,token);}
                recovery.Write(stage,document);
                var current=RestoreFiles.Inventory(plan.Targets,plan.Image.FileNames,plan.Policy,token);
                if(RestoreStorage.Fingerprint(current)!=RestoreStorage.Fingerprint(plan.Before))throw new IOException("Destination saves changed during preservation. Nothing was replaced.");
                recovery.Check(stage);folder=recovery.Folder(request.GameId,op);recovery.Check(folder);Directory.Move(stage,folder);operation=op;
            },token).ConfigureAwait(false);
            checkpoint?.Invoke("prepared");held=await Task.Run(()=>recovery.HoldVerified(folder!,document,token),token).ConfigureAwait(false);await CheckProfile(plan,token).ConfigureAwait(false);
            var freshInventory=await Task.Run(()=>RestoreFiles.Inventory(plan.Targets,plan.Image.FileNames,plan.Policy,token),token).ConfigureAwait(false);
            if(RestoreStorage.Fingerprint(freshInventory)!=RestoreStorage.Fingerprint(plan.Before))throw new IOException("Destination files changed just before restore. Preview again; nothing was replaced.");
            await store.BeginRestoreAsync(new(1,request.GameId,op,RestorePhase.Prepared,DateTimeOffset.UtcNow,"Restore prepared; current saves preserved at "+folder),request.Choice.RecoveryId).ConfigureAwait(false);prepared=true;
            foreach(var change in plan.Preview.Changes)
            {
                await CheckProfile(plan,token).ConfigureAwait(false);
                var payload=change.Action==RestoreAction.Write?Path.Combine(folder!,"Selected",Key(change.SourceId,change.RelativePath)):null;
                mutated.Add(change);
                await Task.Run(()=>Mutate(plan,change,payload,change.AfterHash,change.AfterSize,change.BeforeHash,change.BeforeSize,createdDirectories,()=>{},token),token).ConfigureAwait(false);
                checkpoint?.Invoke("after-mutation");
            }
            await Task.Run(()=>
            {
                VerifyChanges(plan.Preview.Changes,false,token);
                foreach(var file in plan.Image.Files)RestoreFiles.VerifyFile(RestoreFiles.Target(plan.Targets.Single(t=>t.Id==file.SourceId),file.RelativePath),file.Size,file.Sha256,token);
            },token).ConfigureAwait(false);
            await CheckProfile(plan,token).ConfigureAwait(false);await store.FinishRestoreAsync(request.GameId,op,RestorePhase.Completed,"Restore completed and verified. Before-state recovery: "+folder).ConfigureAwait(false);
            return new(RestorePhase.Completed,"Saves restored and verified. The previous saves remain available under pre-restore recovery.",operation,folder);
        }
        catch(Exception error)when(Expected(error)||error is OperationCanceledException)
        {
            if(!prepared||plan is null||document is null)return new(RestorePhase.Failed,"Restore did not replace saves. "+error.Message,operation,folder);
            try
            {
                foreach(var change in mutated.AsEnumerable().Reverse())
                {
                    checkpoint?.Invoke("rollback");await CheckProfile(plan,default).ConfigureAwait(false);
                    if(await Task.Run(()=>MatchesBefore(change)).ConfigureAwait(false))continue;
                    var payload=change.BeforeHash is not null?Path.Combine(folder!,"Before",Key(change.SourceId,change.RelativePath)):null;
                    await Task.Run(()=>Mutate(plan,change,payload,change.BeforeHash,change.BeforeSize,change.AfterHash,change.AfterSize,new List<string>(),()=>{},default)).ConfigureAwait(false);
                }
                await Task.Run(()=>VerifyChanges(mutated.ToArray(),true,default)).ConfigureAwait(false);
                foreach(var directory in createdDirectories.OrderByDescending(p=>p.Length))try{plan.Policy.Validate(directory);if(Directory.Exists(directory)&&!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory,false);}catch(IOException){}
                var pending=await store.LoadRestoreJournalAsync(request.GameId).ConfigureAwait(false);
                if(pending.Value?.RequiredRecoveryId is Guid originalRecovery)
                {
                    var message="Undo failed and returned to the earlier unresolved state. The original restore still needs recovery "+originalRecovery.ToString("N")+". "+error.Message;
                    await store.FinishRestoreAsync(request.GameId,operation!.Value,RestorePhase.RecoveryRequired,message).ConfigureAwait(false);
                    return new(RestorePhase.RecoveryRequired,message,originalRecovery,recovery.Folder(request.GameId,originalRecovery));
                }
                await store.FinishRestoreAsync(request.GameId,operation!.Value,RestorePhase.RolledBack,"Restore failed and the previous file state was restored. "+error.Message).ConfigureAwait(false);
                return new(RestorePhase.RolledBack,"Restore failed; changed files were rolled back to their previous state. Recovery copy kept. "+error.Message,operation,folder);
            }
            catch(Exception rollback)when(Expected(rollback)||rollback is OperationCanceledException)
            {
                var state=await store.LoadRestoreJournalAsync(request.GameId).ConfigureAwait(false);
                var authoritative=state.Value?.RequiredRecoveryId??operation!.Value;var authoritativeFolder=recovery.Folder(request.GameId,authoritative);
                var message="Restore needs recovery. Close the game and undo recovery "+authoritative.ToString("N")+". Copy kept at "+authoritativeFolder+". "+rollback.Message;
                try{await store.FinishRestoreAsync(request.GameId,operation!.Value,RestorePhase.RecoveryRequired,message).ConfigureAwait(false);}catch(Exception e)when(Expected(e)){message+=" Journal needs attention: "+e.Message;}
                return new(RestorePhase.RecoveryRequired,message,authoritative,authoritativeFolder);
            }
        }
        finally{foreach(var stream in held)stream.Dispose();backupLease?.Dispose();}
    }
    private static bool MatchesBefore(RestoreChange change)
    {
        if(change.BeforeHash is null)return !File.Exists(change.TargetPath)&&!Directory.Exists(change.TargetPath);
        if(!File.Exists(change.TargetPath))return false;
        SaveSourcePolicy.CheckLinks(change.TargetPath);using var stream=new FileStream(change.TargetPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        return stream.Length==change.BeforeSize&&BackupStorage.Hash(stream,default)==change.BeforeHash;
    }
    private async Task CheckProfile(Plan plan,CancellationToken token)
    {
        await Idle(plan.Request.GameId,token).ConfigureAwait(false);var current=await store.LoadProfileAsync(plan.Request.GameId).ConfigureAwait(false);
        if(current.Profile.State!=DocumentState.Ready||current.Profile.Value is not GameProfile profile||SaveDiscoveryIdentity.For(profile)!=plan.ProfileIdentity)throw new IOException("The game profile changed during restore. Recovery was preserved.");
        plan.Policy.ValidateSet(plan.Targets.Select(t=>t.Path));
    }
    private void Mutate(Plan plan,RestoreChange change,string? payload,string? wantHash,long? wantSize,string? expectedHash,long? expectedSize,List<string> created,Action changed,CancellationToken token)
    {
        var path=plan.Policy.Validate(change.TargetPath);CheckExpected();
        if(wantHash is null)
        {
            checkpoint?.Invoke("replace");CheckExpected();Idle(plan.Request.GameId,token).GetAwaiter().GetResult();
            if(expectedHash is not null){File.Delete(path);changed();}return;
        }
        var missing=new Stack<string>();for(var parent=Path.GetDirectoryName(path);parent is not null&&!Directory.Exists(parent);parent=Path.GetDirectoryName(parent)){plan.Policy.Validate(parent);if(File.Exists(parent))throw new IOException("Restore parent path is a file.");missing.Push(parent);}
        while(missing.TryPop(out var directory)){plan.Policy.Validate(directory);Directory.CreateDirectory(directory);created.Add(directory);}
        var temporary=path+".gamevault-"+Guid.NewGuid().ToString("N")+".tmp";var owned=false;
        try
        {
            plan.Policy.Validate(temporary);RestoreFiles.VerifyFile(payload!,wantSize!.Value,wantHash,token);
            using(var input=new FileStream(payload!,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough)){owned=true;BackupStorage.Copy(input,output,token);output.Flush(true);}
            RestoreFiles.VerifyFile(temporary,wantSize.Value,wantHash,token);checkpoint?.Invoke("replace");CheckExpected();Idle(plan.Request.GameId,token).GetAwaiter().GetResult();plan.Policy.Validate(path);
            if(expectedHash is null)File.Move(temporary,path,false);else File.Replace(temporary,path,null);changed();
        }
        finally{if(owned&&File.Exists(temporary))try{SaveSourcePolicy.CheckLinks(temporary);File.Delete(temporary);}catch(IOException){}}
        void CheckExpected()
        {
            token.ThrowIfCancellationRequested();plan.Policy.Validate(path);
            if(expectedHash is null){if(File.Exists(path)||Directory.Exists(path))throw new IOException("A destination appeared after preview: "+path);}
            else RestoreFiles.VerifyFile(path,expectedSize!.Value,expectedHash,token);
        }
    }
    private static void VerifyChanges(RestoreChange[] changes,bool before,CancellationToken token)
    {
        foreach(var change in changes)
        {
            token.ThrowIfCancellationRequested();var hash=before?change.BeforeHash:change.AfterHash;var size=before?change.BeforeSize:change.AfterSize;SaveSourcePolicy.CheckLinks(change.TargetPath);
            if(hash is null){if(File.Exists(change.TargetPath)||Directory.Exists(change.TargetPath))throw new IOException("A removed or absent restore destination unexpectedly exists.");}
            else RestoreFiles.VerifyFile(change.TargetPath,size!.Value,hash,token);
        }
    }
    private static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException or System.ComponentModel.Win32Exception or System.Security.SecurityException or NotSupportedException;
}
