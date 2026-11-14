using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;

/// <summary>Changes future destinations only. Existing snapshots stay at their original stable-ID locations.</summary>
public sealed class BackupDestinationService(ApplicationPaths paths)
{
    private readonly ManagedPaths managed=new(paths);
    private readonly JsonDocuments documents=new(new ManagedPaths(paths));
    private string FileName=>Path.Combine(paths.Data,"backup-destinations.json");
    public Task<BackupDestinationSnapshot> ReadAsync(Guid? gameId=null)=>Task.Run(()=>Read(gameId));
    public Task<string> ResolveCurrentAsync(Guid gameId)=>Task.Run(()=>ResolveCurrent(gameId));
    public Task SetDefaultAsync(string? root)=>Task.Run(()=>Set(null,root));
    public Task SetGameAsync(Guid gameId,string? root)=>Task.Run(()=>{RequireId(gameId);Set(gameId,root);});
    internal BackupDestinationSnapshot Read(Guid? id)
    {
        if(id.HasValue)RequireId(id.Value);var doc=Load();var chosen=id.HasValue?doc.GameRoots.GetValueOrDefault(id.Value):null;
        var effective=chosen??doc.DefaultRoot;
        return new(doc.DefaultRoot,chosen,id.HasValue?DirectoryFor(effective,id.Value):effective??paths.Profiles,
            id.HasValue?new[]{DirectoryFor(null,id.Value)}.Concat(doc.HistoricalRoots.Select(r=>DirectoryFor(r,id.Value))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray():doc.HistoricalRoots);
    }
    internal string ResolveCurrent(Guid id)
    {
        var snapshot=Read(id);var root=snapshot.GameRoot??snapshot.DefaultRoot;
        if(root is not null)ValidateRoot(root,false);
        SaveSourcePolicy.CheckLinks(snapshot.EffectiveDirectory);return snapshot.EffectiveDirectory;
    }
    internal string[] Directories(Guid id)=>Read(id).HistoricalDirectories.Append(Read(id).EffectiveDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    internal string[] ExternalRoots()=>Load().HistoricalRoots;
    private string DirectoryFor(string? root,Guid id)=>Path.Combine(root??paths.Profiles,id.ToString("N"),"Backups");
    private static void RequireId(Guid id){if(id==Guid.Empty)throw new ArgumentException("A stable game ID is required.");}
    private BackupDestinationDocument Load()
    {
        SaveSourcePolicy.CheckLinks(paths.Root);var doc=documents.Read<BackupDestinationDocument>(FileName,Valid);JsonDocuments.RequireWritable(doc);
        return doc.Value??new(1,null,[],[]);
    }
    private static bool Valid(BackupDestinationDocument d)=>d.SchemaVersion==1&&d.GameRoots is not null&&d.GameRoots.Count<=4096&&d.GameRoots.All(p=>p.Key!=Guid.Empty&&ValidRoot(p.Value))&&d.HistoricalRoots is {Length:<=256}&&d.HistoricalRoots.All(ValidRoot)&&(d.DefaultRoot is null||ValidRoot(d.DefaultRoot))&&
        (d.DefaultRoot is null||d.HistoricalRoots.Contains(d.DefaultRoot,StringComparer.OrdinalIgnoreCase))&&d.GameRoots.Values.All(r=>d.HistoricalRoots.Contains(r,StringComparer.OrdinalIgnoreCase));
    private static bool ValidRoot(string? root)=>!string.IsNullOrWhiteSpace(root)&&Path.IsPathFullyQualified(root)&&root==SaveSourcePolicy.Canonical(root);
    private void Set(Guid? id,string? root)
    {
        using var held=Acquire();var doc=Load();root=root is null?null:ValidateRoot(root,true);
        var overrides=new Dictionary<Guid,string>(doc.GameRoots);if(id.HasValue){if(root is null)overrides.Remove(id.Value);else overrides[id.Value]=root;}
        var history=root is null?doc.HistoricalRoots:doc.HistoricalRoots.Append(root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(history.Length>256)throw new IOException("Backup destination history reached its limit; existing locations were preserved.");
        documents.Write(FileName,new BackupDestinationDocument(1,id.HasValue?doc.DefaultRoot:root,overrides,history),Valid);
    }
    private string ValidateRoot(string root,bool probe)
    {
        // Reuse the save policy's local-path, traversal, protected-root and link checks.
        root=new SaveSourcePolicy(paths).Validate(root);
        if(SaveSourcePolicy.Overlaps(root,paths.Root))throw new IOException("Backup destinations must be outside the GameVault application folder.");
        if(!Directory.Exists(root))throw new DirectoryNotFoundException("The selected backup disk or folder is unavailable; reconnect it or explicitly choose a new destination.");
        var store=new AppDataStore(paths);
        if(Directory.Exists(paths.Profiles))foreach(var folder in Directory.EnumerateDirectories(paths.Profiles))
        {
            if(!Guid.TryParseExact(Path.GetFileName(folder),"N",out var id))continue;
            var game=store.LoadProfileAsync(id).GetAwaiter().GetResult();
            if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile)throw new IOException("A game profile needs recovery before a backup destination can be validated.");
            var sources=store.LoadSaveDiscoveryAsync(id).GetAwaiter().GetResult();JsonDocuments.RequireWritable(sources);
            var protectedPaths=profile.SaveSources.Concat(sources.Value?.Sources.Where(s=>s.Selected).Select(s=>s.Path)??[]).Append(Path.GetDirectoryName(profile.ExecutablePath)!).Append(profile.WorkingDirectory);
            if(protectedPaths.Any(p=>SaveSourcePolicy.Overlaps(root,p)))throw new IOException("Backup destinations must not overlap any game's installation or live saves.");
        }
        if(probe)
        {
            if(new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace<16L*1024*1024)throw new IOException("The backup destination has insufficient free space.");
            var test=Path.Combine(root,".gamevault-write-"+Guid.NewGuid().ToString("N"));SaveSourcePolicy.CheckLinks(test);
            using var stream=new FileStream(test,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose|FileOptions.WriteThrough);stream.WriteByte(0);stream.Flush(true);
        }
        return root;
    }
    internal void ValidateKnownRoots()
    {
        foreach(var root in ExternalRoots()){SaveSourcePolicy.CheckLinks(root);if(Directory.Exists(root))ValidateRoot(root,false);}
    }
    // A persistent lock filename avoids delete/recreate races across instances sharing a root.
    internal IDisposable Acquire()
    {
        SaveSourcePolicy.CheckLinks(paths.Root);managed.EnsureDirectory(paths.Data);var streams=new List<FileStream>();
        try
        {
            Lock(Path.Combine(paths.Data,"backup-store.lock"));
            foreach(var root in ExternalRoots().Order(StringComparer.OrdinalIgnoreCase))
            {
                SaveSourcePolicy.CheckLinks(root);
                if(!Directory.Exists(root))throw new DirectoryNotFoundException("A registered backup location is offline. Reconnect every configured historical location before changing backup storage or running backup, restore or retention operations: "+root);
                Lock(Path.Combine(root,".gamevault-backup.lock"));
            }
            ValidateKnownRoots();return new Held(streams);
        }
        catch{foreach(var stream in streams)stream.Dispose();throw;}
        void Lock(string file){SaveSourcePolicy.CheckLinks(file);streams.Add(new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));}
    }
    private sealed class Held(List<FileStream> streams):IDisposable {public void Dispose(){foreach(var stream in streams)stream.Dispose();}}
}
