using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;

public sealed partial class RestoreService
{
    private sealed record Image(BackupSource[] Sources,BackupFile[] Files,Dictionary<string,string> Payload,Dictionary<Guid,HashSet<string>> Owned,
        Dictionary<Guid,string> FileNames,string Identity,string Label,CompletedBackup[] History);
    private sealed record Plan(RestoreRequest Request,RestorePreview Preview,Image Image,BackupSource[] Targets,RestoreInventory Before,string ProfileIdentity,SaveSourcePolicy Policy);
    private static string Key(Guid sourceId,string relative)=>sourceId.ToString("N")+"/"+relative;
    private static RestoreRequest Freeze(RestoreRequest request)=>request with{Destinations=request.Destinations.ToArray(),ExtraRemovals=request.ExtraRemovals.ToArray()};
    private async Task<Plan> Build(RestoreRequest request,CancellationToken token)
    {
        if(request.GameId==Guid.Empty||request.Choice is null||(request.Choice.BackupId is null)==(request.Choice.RecoveryId is null)||request.Choice.BackupId==Guid.Empty||request.Choice.RecoveryId==Guid.Empty)throw new ArgumentException("Choose one completed backup or recovery snapshot.");
        await Idle(request.GameId,token).ConfigureAwait(false);await store.RequireRestoreAllowedAsync(request.GameId,request.Choice.RecoveryId).ConfigureAwait(false);
        var game=await store.LoadProfileAsync(request.GameId).ConfigureAwait(false);
        if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile)throw new IOException("A writable game profile is required for restore.");
        var image=await Task.Run(()=>LoadImage(request.GameId,request.Choice,token),token).ConfigureAwait(false);
        var maps=request.Destinations.Length==0?image.Sources.Select(s=>new RestoreDestination(s.Id,s.Path)).ToArray():request.Destinations;
        if(maps.Length!=image.Sources.Length||maps.Select(m=>m.SourceId).Distinct().Count()!=maps.Length||maps.Any(m=>!image.Sources.Any(s=>s.Id==m.SourceId)))throw new IOException("Every backup source needs exactly one destination.");
        var policy=new SaveSourcePolicy(paths,[profile.WorkingDirectory,Path.GetDirectoryName(profile.ExecutablePath)!]);
        var targets=maps.Select(m=>image.Sources.Single(s=>s.Id==m.SourceId) with{Path=policy.Validate(m.Path)}).OrderBy(s=>s.Id).ToArray();
        if(policy.ValidateSet(targets.Select(t=>t.Path)).Length!=targets.Length)throw new IOException("Restore destinations must be distinct and nonoverlapping.");
        foreach(var target in targets)
        {
            if(SaveSourcePolicy.Overlaps(target.Path,profile.ExecutablePath)||SaveSourcePolicy.Within(target.Path,paths.Root)||new BackupDestinationService(paths).ExternalRoots().Any(r=>SaveSourcePolicy.Overlaps(target.Path,r)))throw new IOException("Restore destinations must not overwrite the game executable or GameVault files.");
        }
        var before=await Task.Run(()=>RestoreFiles.Inventory(targets,image.FileNames,policy,token),token).ConfigureAwait(false);
        var desired=new Dictionary<string,(BackupFile File,string Target)>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in image.Files)
        {
            var target=RestoreFiles.Target(targets.Single(t=>t.Id==file.SourceId),file.RelativePath);policy.Validate(target);
            if(targets.Single(t=>t.Id==file.SourceId).Kind==SaveSourceKind.Folder&&!SaveSourcePolicy.Within(target,targets.Single(t=>t.Id==file.SourceId).Path))throw new IOException("Restore payload escaped its destination.");
            if(!desired.TryAdd(target,(file,target)))throw new IOException("Multiple save files resolve to the same destination.");
        }
        var desiredPaths=desired.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        for(int i=0;i<desiredPaths.Length;i++)for(int j=i+1;j<desiredPaths.Length;j++)if(SaveSourcePolicy.Overlaps(desiredPaths[i],desiredPaths[j]))throw new IOException("Restore file paths contain a file/directory collision.");
        var extras=request.ExtraRemovals.Select(policy.Validate).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(extras.Any(p=>desired.ContainsKey(p)||!before.Files.Any(f=>f.Path.Equals(p,StringComparison.OrdinalIgnoreCase))))throw new IOException("Extra removals must name currently listed destination files outside the chosen save set.");
        var changes=new List<RestoreChange>();var other=new List<RestoreExtraFile>();
        foreach(var (target,item) in desired)
        {
            var current=before.Files.SingleOrDefault(f=>f.Path.Equals(target,StringComparison.OrdinalIgnoreCase));
            if(Directory.Exists(target))throw new IOException("A restore file collides with an existing directory.");
            for(var parent=Path.GetDirectoryName(target);parent is not null;parent=Path.GetDirectoryName(parent))if(File.Exists(parent))throw new IOException("A restore file has an existing file in its parent path. Choose a different destination.");
            if(current?.Hash!=item.File.Sha256||current?.Size!=item.File.Size)changes.Add(new(item.File.SourceId,item.File.RelativePath,target,RestoreAction.Write,current?.Hash,current?.Size,item.File.Sha256,item.File.Size));
        }
        foreach(var file in before.Files.Where(f=>!desired.ContainsKey(f.Path)))
        {
            var original=image.Sources.Single(s=>s.Id==file.SourceId);var mapped=targets.Single(s=>s.Id==file.SourceId);
            var known=image.Owned[file.SourceId].Contains(file.RelativePath)||image.History.Any(b=>b.Manifest.Sources.Any(s=>s.Path.Equals(mapped.Path,StringComparison.OrdinalIgnoreCase)&&s.Kind==mapped.Kind&&b.Manifest.Files.Any(f=>f.SourceId==s.Id&&f.RelativePath.Equals(file.RelativePath,StringComparison.OrdinalIgnoreCase))));
            if(!known)other.Add(new(file.Path,extras.Contains(file.Path)));
            if(known||extras.Contains(file.Path))changes.Add(new(file.SourceId,file.RelativePath,file.Path,RestoreAction.Remove,file.Hash,file.Size,null,null));
        }
        changes=changes.OrderBy(c=>c.TargetPath,StringComparer.OrdinalIgnoreCase).ToList();
        var identity=SaveDiscoveryIdentity.For(profile);
        var fingerprint=RestoreStorage.Fingerprint(new{request.GameId,request.Choice,Identity=identity,ImageIdentity=image.Identity,Targets=targets,Before=before,Changes=changes,Extras=extras.Order(StringComparer.OrdinalIgnoreCase).ToArray()});
        return new(request,new(request.GameId,image.Label,targets.Select(t=>new RestoreDestination(t.Id,t.Path)).ToArray(),changes.ToArray(),other.ToArray(),fingerprint),image,targets,before,identity,policy);
    }
    private Image LoadImage(Guid id,RestoreChoice choice,CancellationToken token)
    {
        BackupSource[] sources;BackupFile[] files;Dictionary<string,string> payload=new(StringComparer.OrdinalIgnoreCase);var owned=new Dictionary<Guid,HashSet<string>>();var names=new Dictionary<Guid,string>();string identity,label;CompletedBackup[] history=[];
        if(choice.BackupId is Guid backupId)
        {
            var catalog=backups.Catalog(id,token);if(catalog.Warnings.Length>0)throw new IOException(string.Join(" ",catalog.Warnings));history=catalog.Backups;
            var selected=history.SingleOrDefault(b=>b.Manifest.Id==backupId)??throw new IOException("The selected completed backup is unavailable.");backups.ConfirmManifest(selected);backups.Verify(selected,token);
            sources=selected.Manifest.Sources;files=selected.Manifest.Files;identity=RestoreStorage.Fingerprint(selected.Manifest);label=$"Backup {selected.Manifest.CreatedAt.ToLocalTime():G} · {selected.Manifest.Id:N}";
            foreach(var source in sources)
            {
                if(!files.Any(f=>f.SourceId==source.Id&&f.Size>0))throw new IOException("A selected backup source has no nonempty payload.");
                owned[source.Id]=history.SelectMany(b=>b.Manifest.Files.Where(f=>b.Manifest.Sources.Any(s=>s.Id==f.SourceId&&(s.Id==source.Id||s.Path.Equals(source.Path,StringComparison.OrdinalIgnoreCase))&&s.Kind==source.Kind))).Select(f=>f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(source.Kind==SaveSourceKind.File)
                {
                    var single=files.Where(f=>f.SourceId==source.Id).ToArray();if(single.Length!=1||single[0].RelativePath!=Path.GetFileName(source.Path))throw new IOException("A file source must contain exactly its one named file.");names[source.Id]=single[0].RelativePath;
                }
            }
            foreach(var file in files)payload[Key(file.SourceId,file.RelativePath)]=Path.Combine(selected.Directory,"Files",file.StoragePath);
        }
        else
        {
            var recoveryId=choice.RecoveryId!.Value;var document=recovery.Read(id,recoveryId);sources=document.Targets;
            var affected=document.Changes.Select(c=>Key(c.SourceId,c.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            files=document.BeforeFiles.Where(f=>affected.Contains(Key(f.SourceId,f.RelativePath))).ToArray();identity=RestoreStorage.Fingerprint(document);label=$"Recovery before restore {document.CreatedAt.ToLocalTime():G} · {document.Id:N}";
            foreach(var change in document.Changes)
            {
                var target=RestoreFiles.Target(sources.Single(s=>s.Id==change.SourceId),change.RelativePath);
                if(!target.Equals(change.TargetPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Recovery target mappings do not match their change list.");
                var before=document.BeforeFiles.SingleOrDefault(f=>Key(f.SourceId,f.RelativePath).Equals(Key(change.SourceId,change.RelativePath),StringComparison.OrdinalIgnoreCase));
                if(change.BeforeHash is not null&&(before is null||before.Sha256!=change.BeforeHash||before.Size!=change.BeforeSize))throw new IOException("Recovery before-state does not match its journaled changes.");
            }
            foreach(var source in sources)
            {
                owned[source.Id]=document.Changes.Where(c=>c.SourceId==source.Id).Select(c=>c.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(source.Kind==SaveSourceKind.File)
                {
                    var labels=document.BeforeFiles.Where(f=>f.SourceId==source.Id).Select(f=>f.RelativePath).Concat(document.Changes.Where(c=>c.SourceId==source.Id).Select(c=>c.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if(labels.Length!=1)throw new IOException("Recovery file mapping is ambiguous.");names[source.Id]=labels[0];
                }
            }
            foreach(var file in files){var path=Path.Combine(recovery.Folder(id,recoveryId),"Before",file.StoragePath);recovery.Check(path);RestoreFiles.VerifyFile(path,file.Size,file.Sha256,token);payload[Key(file.SourceId,file.RelativePath)]=path;}
        }
        return new(sources,files,payload,owned,names,identity,label,history);
    }
}
