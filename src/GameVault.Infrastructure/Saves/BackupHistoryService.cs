using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;

public sealed class BackupHistoryService
{
    private readonly AppDataStore store;
    private readonly GameOperationLeases leases;
    private readonly Func<Guid,Task<bool>> isRunning;
    private readonly BackupStorage storage;
    private readonly Action<string>? checkpoint;
    public BackupHistoryService(ApplicationPaths paths,AppDataStore store,GameOperationLeases leases,Func<Guid,Task<bool>> isRunning):this(paths,store,leases,isRunning,null){}
    internal BackupHistoryService(ApplicationPaths paths,AppDataStore store,GameOperationLeases leases,Func<Guid,Task<bool>> isRunning,Action<string>? checkpoint)
    {this.store=store;this.leases=leases;this.isRunning=isRunning;this.checkpoint=checkpoint;storage=new(paths);}
    public async Task<BackupHistorySnapshot> ListAsync(Guid id,CancellationToken token=default)
    {
        var warnings=new List<string>();BackupStorage.CatalogResult catalog=new([],[],0);BackupRetentionPolicy? policy=null;var writable=false;Guid[] pins=[];
        try
        {
            var game=await store.LoadProfileAsync(id).ConfigureAwait(false);writable=game.Profile.State==DocumentState.Ready;
            if(game.Profile.Value is GameProfile profile)policy=new(profile.BackupLimit,profile.KeepAllBackups);
            if(!writable)warnings.Add(game.Profile.Message??"The game profile needs attention.");
            var stored=await store.LoadBackupPinsAsync(id).ConfigureAwait(false);pins=stored.Value?.PinnedIds??[];
            if(stored.IsReadOnly){writable=false;warnings.Add(stored.Message??"Backup pins need recovery. Existing copies are protected.");}
            catalog=await Task.Run(()=>storage.Catalog(id,token),token).ConfigureAwait(false);warnings.AddRange(catalog.Warnings);
        }
        catch(Exception e)when(Expected(e)){writable=false;warnings.Add(e.Message);}
        return new(id,catalog.Backups.Select(b=>new BackupHistoryEntry(b,pins.Contains(b.Manifest.Id))).ToArray(),policy,writable&&warnings.Count==0,warnings.ToArray(),catalog.RecoveryFolders);
    }
    public async Task<BackupHistorySnapshot> SetPinnedAsync(Guid id,Guid backupId,bool pinned,CancellationToken token=default)
    {
        using var lease=Acquire(id);await RequireIdle(id,token).ConfigureAwait(false);
        var catalog=await Task.Run(()=>storage.Catalog(id,token),token).ConfigureAwait(false);
        if(catalog.Warnings.Length>0||!catalog.Backups.Any(b=>b.Manifest.Id==backupId))throw new IOException("Refresh backup history and resolve its warnings before changing pins.");
        var doc=await store.LoadBackupPinsAsync(id).ConfigureAwait(false);JsonDocuments.RequireWritable(doc);
        var ids=doc.Value?.PinnedIds??[];ids=pinned?ids.Append(backupId).Distinct().ToArray():ids.Where(i=>i!=backupId).ToArray();
        token.ThrowIfCancellationRequested();await store.SaveBackupPinsAsync(new(1,id,checked((doc.Value?.Revision??0)+1),ids),doc.Value?.Revision??0).ConfigureAwait(false);
        return await ListAsync(id,token).ConfigureAwait(false);
    }
    public async Task<RetentionResult> ApplyRetentionAsync(Guid id,BackupRetentionPolicy policy,BackupRetentionPolicy expected,CancellationToken token=default)
    {
        using var lease=Acquire(id);await RequireIdle(id,token).ConfigureAwait(false);
        var pins=await store.LoadBackupPinsAsync(id).ConfigureAwait(false);
        if(pins.IsReadOnly)return new(false,0,"Settings were not changed. "+pins.Message,await ListAsync(id,token).ConfigureAwait(false));
        await store.SetBackupRetentionAsync(id,policy,expected).ConfigureAwait(false);
        return await CleanupAfterSuccessAsync(id,null,token).ConfigureAwait(false);
    }
    // Caller must already hold the game's operation lease. Only new successful captures or explicit Apply call this.
    internal async Task<RetentionResult> CleanupAfterSuccessAsync(Guid id,Guid? newlyCompleted,CancellationToken token)
    {
        var removed=0;string message;var complete=true;
        try
        {
            await RequireIdle(id,token).ConfigureAwait(false);
            var game=await store.LoadProfileAsync(id).ConfigureAwait(false);
            if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile)throw new IOException("The game profile needs attention; no snapshots were pruned.");
            var policy=new BackupRetentionPolicy(profile.BackupLimit,profile.KeepAllBackups);
            var pins=await store.LoadBackupPinsAsync(id).ConfigureAwait(false);JsonDocuments.RequireWritable(pins);
            var pinned=pins.Value?.PinnedIds??[];
            var catalog=await Task.Run(()=>storage.Catalog(id,token),token).ConfigureAwait(false);
            if(catalog.Warnings.Length>0)throw new IOException(string.Join(" ",catalog.Warnings));
            if(newlyCompleted is Guid fresh&&!catalog.Backups.Any(b=>b.Manifest.Id==fresh))throw new IOException("The new completed backup could not be confirmed. Cleanup was skipped.");
            var latest=catalog.Backups.FirstOrDefault();
            var candidates=policy.KeepEverything?[]:catalog.Backups.Where(b=>!pinned.Contains(b.Manifest.Id)).Skip(policy.Limit)
                .Where(b=>b.Manifest.Id!=latest?.Manifest.Id&&b.Manifest.Id!=newlyCompleted).ToArray();
            if(candidates.Length>0)
            {
                await Task.Run(()=>
                {
                    if(latest is not null)storage.Verify(latest,token);
                    if(newlyCompleted is Guid freshId)storage.Verify(catalog.Backups.Single(b=>b.Manifest.Id==freshId),token);
                    // Validate the entire deletion plan before removing any candidate.
                    foreach(var candidate in candidates){storage.Verify(candidate,token);OwnedTree(candidate,token);}
                },token).ConfigureAwait(false);
                foreach(var candidate in candidates)
                {
                    token.ThrowIfCancellationRequested();await RequireIdle(id,token).ConfigureAwait(false);
                    await CheckState().ConfigureAwait(false);
                    await Task.Run(()=>Prune(candidate,()=>CheckState().GetAwaiter().GetResult(),token),token).ConfigureAwait(false);removed++;
                }
            }
            async Task CheckState()
            {
                await RequireIdle(id,token).ConfigureAwait(false);
                var currentPins=await store.LoadBackupPinsAsync(id).ConfigureAwait(false);JsonDocuments.RequireWritable(currentPins);
                var currentGame=await store.LoadProfileAsync(id).ConfigureAwait(false);
                if(currentPins.Value?.Revision!=pins.Value?.Revision||!pinned.SequenceEqual(currentPins.Value?.PinnedIds??[])||currentGame.Profile.State!=DocumentState.Ready||currentGame.Profile.Value is not GameProfile current||current.BackupLimit!=policy.Limit||current.KeepAllBackups!=policy.KeepEverything)
                    throw new IOException("Pins or retention settings changed during cleanup. Refresh before retrying.");
            }
            message=policy.KeepEverything?"Keep everything saved. No backups were removed.":$"Retention applied: {removed} old unpinned backup(s) removed. Pinned and recovery copies were preserved.";
        }
        catch(Exception e)when(Expected(e)||e is OperationCanceledException)
        {complete=false;message="Cleanup stopped; remaining copies were preserved. Settings may already be saved. "+(e is OperationCanceledException?"Cleanup was canceled.":e.Message);}
        return new(complete,removed,message,await ListAsync(id).ConfigureAwait(false));
    }
    private IDisposable Acquire(Guid id)=>leases.TryAcquire(id)??throw new InvalidOperationException("A game session, backup or history change is active. Retry after it finishes.");
    private async Task RequireIdle(Guid id,CancellationToken token)
    {token.ThrowIfCancellationRequested();await store.RequireNoPendingRestoreAsync(id).ConfigureAwait(false);if(await isRunning(id).ConfigureAwait(false))throw new InvalidOperationException("Close the game before changing backup pins or retention.");}
    private sealed record Tree(string[] Files,string[] Directories);
    private Tree OwnedTree(CompletedBackup backup,CancellationToken token)
    {
        var folder=Path.GetFullPath(backup.Directory);var root=storage.Root(backup.Manifest.GameId);
        if(!string.Equals(Path.GetDirectoryName(folder),root,StringComparison.OrdinalIgnoreCase))throw new IOException("Cleanup path is not a direct child of this game's Backups directory.");
        storage.Check(folder);
        var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.Combine(folder,"manifest.json")};var expectedDirs=new HashSet<string>(StringComparer.OrdinalIgnoreCase){folder};
        foreach(var file in backup.Manifest.Files)
        {
            var full=Path.GetFullPath(Path.Combine(folder,"Files",file.StoragePath));
            if(!SaveSourcePolicy.Within(full,folder))throw new IOException("Backup file escaped its snapshot directory.");expected.Add(full);
            for(var parent=Path.GetDirectoryName(full);parent is not null&&SaveSourcePolicy.Within(parent,folder);parent=Path.GetDirectoryName(parent))expectedDirs.Add(parent);
        }
        var files=new List<string>();var dirs=new List<string>();var queue=new Queue<string>();queue.Enqueue(folder);var count=0;
        while(queue.TryDequeue(out var current))
        {
            token.ThrowIfCancellationRequested();storage.Check(current);dirs.Add(current);
            foreach(var entry in Directory.EnumerateFileSystemEntries(current))
            {
                token.ThrowIfCancellationRequested();if(++count>16384)throw new IOException("Backup tree exceeded its cleanup inspection limit.");storage.Check(entry);
                if(Directory.Exists(entry)){if(!expectedDirs.Contains(entry))throw new IOException("Unexpected folders were preserved; cleanup cannot remove this snapshot safely.");queue.Enqueue(entry);}
                else {if(!expected.Contains(entry))throw new IOException("Unexpected files were preserved; cleanup cannot remove this snapshot safely.");files.Add(entry);}
            }
        }
        if(!expected.SetEquals(files)||!expectedDirs.SetEquals(dirs))throw new IOException("Backup tree no longer matches its manifest. Cleanup stopped.");
        return new(files.ToArray(),dirs.OrderByDescending(d=>d.Length).ToArray());
    }
    private void Prune(CompletedBackup backup,Action checkState,CancellationToken token)
    {
        checkpoint?.Invoke("before-prune");token.ThrowIfCancellationRequested();checkState();storage.ConfirmManifest(backup);storage.Verify(backup,token);OwnedTree(backup,token);
        var folder=backup.Directory;
        if(Path.GetFileName(folder)!=BackupStorage.Name(backup.Manifest))throw new IOException("Backup identity changed before cleanup.");
        var quarantine=Path.Combine(storage.Root(backup.Manifest.GameId),".prune-"+backup.Manifest.Id.ToString("N")+"-"+Guid.NewGuid().ToString("N"));storage.Check(quarantine);
        Directory.Move(folder,quarantine);var moved=backup with{Directory=quarantine};var tree=OwnedTree(moved,token);checkState();storage.ConfirmManifest(moved);
        foreach(var file in tree.Files){checkpoint?.Invoke("delete");token.ThrowIfCancellationRequested();storage.Check(file);File.Delete(file);}
        foreach(var directory in tree.Directories){token.ThrowIfCancellationRequested();storage.Check(directory);Directory.Delete(directory,false);}
    }
    private static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException or System.ComponentModel.Win32Exception or System.Security.SecurityException;
}