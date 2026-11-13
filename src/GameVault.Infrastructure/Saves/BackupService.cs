using System.Collections.Concurrent;
using System.Security.Cryptography;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;

public sealed class BackupService
{
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly SaveDiscoveryService discovery;
    private readonly GameOperationLeases leases;
    private readonly Func<Guid,Task<bool>> isRunning;
    private readonly Action<string>? checkpoint;
    private readonly BackupStorage storage;
    private readonly ConcurrentDictionary<Guid,BackupResult> current=[];
    public event EventHandler<BackupResult>? Changed;
    public BackupService(ApplicationPaths paths,AppDataStore store,SaveDiscoveryService discovery,GameOperationLeases leases,Func<Guid,Task<bool>> isRunning)
        :this(paths,store,discovery,leases,isRunning,null){}
    internal BackupService(ApplicationPaths paths,AppDataStore store,SaveDiscoveryService discovery,GameOperationLeases leases,Func<Guid,Task<bool>> isRunning,Action<string>? checkpoint)
    {this.paths=paths;this.store=store;this.discovery=discovery;this.leases=leases;this.isRunning=isRunning;this.checkpoint=checkpoint;storage=new(paths);}
    public bool IsWorking(Guid id)=>current.TryGetValue(id,out var value)&&value.Phase==BackupPhase.Working;
    public Task<BackupResult> ReadAsync(Guid id)=>Task.Run(()=>
    {
        if(current.TryGetValue(id,out var live)&&live.Phase==BackupPhase.Working)return live;
        try
        {
            var latest=storage.Latest(id,default);var status=storage.ReadStatus(id);
            if(status.IsReadOnly)return new BackupResult(id,BackupPhase.Failed,status.Message??"Backup status needs recovery.",latest);
            if(latest is null&&status.Value?.Phase is BackupPhase.Complete or BackupPhase.Unchanged)return new(id,BackupPhase.Failed,"The previous backup is no longer available. No verified snapshot was found.");
            return new(id,status.Value?.Phase??(latest is null?BackupPhase.None:BackupPhase.Complete),status.Value?.Message??(latest is null?"No verified backup yet.":"Verified backup available."),latest);
        }
        catch(Exception e)when(Expected(e)){return new(id,BackupPhase.Failed,"Backup history needs attention. "+e.Message);}
    });
    public async Task<BackupResult> BackUpAsync(Guid id,bool automatic,CancellationToken token=default)
    {
        using var lease=leases.TryAcquire(id);
        if(lease is null)return new(id,BackupPhase.Blocked,"A launch, session or backup is active. Retry after it finishes.");
        CompletedBackup? last=null;
        Publish(new(id,BackupPhase.Working,"Checking saves and preparing a verified backup…",current.GetValueOrDefault(id)?.LastSuccess));
        BackupResult result;
        try
        {
            token.ThrowIfCancellationRequested();last=await Task.Run(()=>storage.Latest(id,token),token).ConfigureAwait(false);
            Publish(new(id,BackupPhase.Working,"Checking saves and preparing a verified backup…",last));
            var status=await Task.Run(()=>storage.ReadStatus(id),token).ConfigureAwait(false);JsonDocuments.RequireWritable(status);
            if(await isRunning(id).ConfigureAwait(false))result=new(id,BackupPhase.Blocked,"The game is running. Close it and retry the backup.",last);
            else
            {
                var game=await store.LoadProfileAsync(id).ConfigureAwait(false);
                if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile||profile.IsArchived)throw new IOException("A writable game in your library is required.");
                var sources=await discovery.RequireVerifiedSourcesAsync(id,token).ConfigureAwait(false);
                var fingerprint=SaveDiscoveryIdentity.For(profile);
                result=await Task.Run(()=>Capture(id,profile,sources,fingerprint,last,automatic,token),token).ConfigureAwait(false);
                if(result.Phase==BackupPhase.Complete&&result.LastSuccess is CompletedBackup committed)
                {
                    var retention=await new BackupHistoryService(paths,store,leases,isRunning).CleanupAfterSuccessAsync(id,committed.Manifest.Id,token).ConfigureAwait(false);
                    if(!retention.Complete||retention.Removed>0)result=result with{Message=result.Message+" "+retention.Message};
                }
            }
        }
        catch(Exception e)when(Expected(e)||e is OperationCanceledException)
        {result=new(id,BackupPhase.Failed,e is OperationCanceledException?"Backup canceled; earlier snapshots were preserved. Retry when ready.":"Backup could not finish. Earlier snapshots were preserved. Retry after resolving the source or storage problem. "+e.Message,last);}
        try{await Task.Run(()=>storage.WriteStatus(result)).ConfigureAwait(false);}
        catch(Exception e)when(Expected(e)){result=result with{Message=result.Message+" Backup status could not be saved: "+e.Message};}
        Publish(result);return result;
    }
    private BackupResult Capture(Guid id,GameProfile profile,SaveSourceMapping[] sources,string fingerprint,CompletedBackup? last,bool automatic,CancellationToken token)
    {
        var policy=new SaveSourcePolicy(paths,[Path.GetDirectoryName(profile.ExecutablePath)!,profile.WorkingDirectory]);policy.ValidateSet(sources.Select(s=>s.Path));
        var inventory=Inventory(sources,policy,token);var held=new List<FileStream>();
        try
        {
            long bytes=0;var files=new List<BackupFile>();
            foreach(var item in inventory)
            {
                token.ThrowIfCancellationRequested();SaveSourcePolicy.CheckLinks(item.Path);
                var stream=new FileStream(item.Path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.SequentialScan);held.Add(stream);
                bytes=checked(bytes+stream.Length);if(bytes>4L*1024*1024*1024)throw new IOException("Save data exceeds the 4 GiB snapshot limit. Choose narrower sources.");
                files.Add(new(item.SourceId,item.RelativePath,item.SourceId.ToString("N")+"/"+item.RelativePath,stream.Length,BackupStorage.Hash(stream,token)));stream.Position=0;
            }
            if(sources.Any(source=>!files.Any(file=>file.SourceId==source.Id&&file.Size>0&&SaveCandidateRules.PayloadMatches(file.RelativePath,source.Payload))))throw new IOException("A selected source no longer contains nonempty save data. Retry after saving finishes.");
            if(files.Count==0||bytes==0)throw new IOException("No nonempty save data exists; an empty snapshot was not created.");
            if(automatic&&last is not null&&last.Manifest.Fingerprint==fingerprint&&SameFiles(last.Manifest.Files,files.ToArray()))
            {ValidateCurrent();return new(id,BackupPhase.Unchanged,"Saves are unchanged; the last verified backup was kept.",last);}
            var manifest=new BackupManifest(1,id,Guid.NewGuid(),DateTimeOffset.UtcNow,fingerprint,sources.Select(s=>new BackupSource(s.Id,s.Path,s.Kind)).ToArray(),files.ToArray(),
                last?.Manifest.Files.Where(f=>!files.Any(n=>n.SourceId==f.SourceId&&n.RelativePath.Equals(f.RelativePath,StringComparison.OrdinalIgnoreCase))).Select(f=>new DeletedSaveFile(f.SourceId,f.RelativePath)).ToArray()??[]);
            var root=storage.Root(id);storage.Ensure(root);var stage=Path.Combine(root,".pending-"+manifest.Id.ToString("N"));storage.Ensure(stage);
            for(var i=0;i<files.Count;i++)
            {
                token.ThrowIfCancellationRequested();var target=Path.Combine(stage,"Files",files[i].StoragePath);storage.Ensure(Path.GetDirectoryName(target)!);storage.Check(target);checkpoint?.Invoke("copy");
                using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough);BackupStorage.Copy(held[i],output,token);output.Flush(true);
            }
            checkpoint?.Invoke("verify");storage.Verify(new(stage,manifest),token);ValidateCurrent();
            storage.WriteManifest(stage,manifest);checkpoint?.Invoke("commit");ValidateCurrent();token.ThrowIfCancellationRequested();
            storage.Check(stage);var final=Path.Combine(root,BackupStorage.Name(manifest));storage.Check(final);Directory.Move(stage,final);
            return new(id,BackupPhase.Complete,"Save backup completed and verified.",new(final,manifest));

            void ValidateCurrent()
            {
                token.ThrowIfCancellationRequested();policy.ValidateSet(sources.Select(s=>s.Path));
                var again=Inventory(sources,policy,token);
                if(!inventory.SequenceEqual(again))throw new IOException("Save files changed while backing up. Retry after the game finishes saving.");
                for(var i=0;i<held.Count;i++){token.ThrowIfCancellationRequested();held[i].Position=0;if(held[i].Length!=files[i].Size||BackupStorage.Hash(held[i],token)!=files[i].Sha256)throw new IOException("A save changed during the backup. Retry after saving finishes.");}
                var fresh=store.LoadProfileAsync(id).GetAwaiter().GetResult();
                if(fresh.Profile.State!=DocumentState.Ready||fresh.Profile.Value is not GameProfile latest||latest.IsArchived||SaveDiscoveryIdentity.For(latest)!=fingerprint)throw new IOException("The game profile changed during backup. Retry with its current sources.");
                var selected=discovery.ReadAsync(id).GetAwaiter().GetResult();
                if(selected.Phase!=SaveDiscoveryPhase.SourcesFound||!sources.Select(s=>(s.Id,s.Path)).OrderBy(s=>s.Id).SequenceEqual((selected.Document.Value?.Sources.Where(s=>s.Selected).Select(s=>(s.Id,s.Path))??[]).OrderBy(s=>s.Id)))throw new IOException("Selected save sources changed during backup. Retry.");
                if(isRunning(id).GetAwaiter().GetResult())throw new IOException("The game started during backup. Retry after exit.");
            }
        }
        finally{foreach(var stream in held)stream.Dispose();}
    }
    private sealed record InputFile(Guid SourceId,string RelativePath,string Path);
    private static InputFile[] Inventory(SaveSourceMapping[] sources,SaveSourcePolicy policy,CancellationToken token)
    {
        var files=new List<InputFile>();var entries=0;
        foreach(var source in sources)
        {
            var path=policy.Validate(source.Path);var attrs=File.GetAttributes(path);
            if(source.Kind==SaveSourceKind.File)
            {if((attrs&FileAttributes.Directory)!=0)throw new IOException("Save source changed type.");Add(path,Path.GetFileName(path));continue;}
            if((attrs&FileAttributes.Directory)==0)throw new IOException("Save source changed type.");
            var queue=new Queue<(string Path,int Depth)>();queue.Enqueue((path,0));
            while(queue.TryDequeue(out var item))
            {
                token.ThrowIfCancellationRequested();SaveSourcePolicy.CheckLinks(item.Path);
                foreach(var child in new DirectoryInfo(item.Path).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();if(++entries>8192)throw new IOException("Source exceeds the 8192-entry inspection limit.");SaveSourcePolicy.CheckLinks(child.FullName);
                    if((child.Attributes&FileAttributes.Directory)!=0)
                    {if(item.Depth>=6)throw new IOException("Source exceeds the depth limit; choose a narrower folder.");queue.Enqueue((child.FullName,item.Depth+1));}
                    else Add(child.FullName,Path.GetRelativePath(path,child.FullName).Replace('\\','/'));
                }
            }
            void Add(string file,string relative)
            {if(files.Count>=4096)throw new IOException("Source exceeds the 4096-file snapshot limit.");SaveSourcePolicy.CheckLinks(file);files.Add(new(source.Id,relative,file));}
        }
        return files.OrderBy(f=>f.SourceId).ThenBy(f=>f.RelativePath,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static bool SameFiles(BackupFile[] a,BackupFile[] b)=>a.Length==b.Length&&a.OrderBy(f=>f.StoragePath,StringComparer.OrdinalIgnoreCase).SequenceEqual(b.OrderBy(f=>f.StoragePath,StringComparer.OrdinalIgnoreCase));
    private static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or System.Security.SecurityException or System.ComponentModel.Win32Exception or NotSupportedException;
    private void Publish(BackupResult value)
    {
        current[value.GameId]=value;
        if(Changed is not null)foreach(EventHandler<BackupResult> handler in Changed.GetInvocationList())try{handler(this,value);}catch{ /* UI notification must not corrupt backup outcome. */ }
    }
}