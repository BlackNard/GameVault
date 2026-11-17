using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;

internal sealed class RestoreStorage(ApplicationPaths paths)
{
    private readonly BackupStorage managed=new(paths);
    private readonly JsonDocuments documents=new(new ManagedPaths(paths));
    public string Root(Guid gameId)=>Path.Combine(ProfileFolders.Resolve(paths,gameId),"RestoreRecovery");
    public string Folder(Guid gameId,Guid id)=>Path.Combine(Root(gameId),id.ToString("N"));
    public void Check(string path)=>managed.Check(path);
    public void Ensure(string path)=>managed.Ensure(path);
    public RestoreRecoveryDocument Read(Guid gameId,Guid id)
    {
        var file=Path.Combine(Folder(gameId,id),"recovery.json");Check(file);
        if(new FileInfo(file).Length>32*1024*1024)throw new IOException("Restore recovery document exceeded its inspection limit.");
        var doc=documents.Read<RestoreRecoveryDocument>(file,d=>Valid(d,gameId,id));
        if(doc.State!=DocumentState.Ready)throw new IOException(doc.Message??"Restore recovery is unavailable; copies were kept unchanged.");return doc.Value!;
    }
    public void Write(string stage,RestoreRecoveryDocument value)
    {
        var file=Path.Combine(stage,"recovery.json");Check(file);
        documents.Write(file,value,d=>Valid(d,value.GameId,value.Id));
        if(new FileInfo(file).Length>32*1024*1024)throw new IOException("Restore recovery document exceeded its inspection limit.");
    }
    public RestoreRecoveryInfo[] List(Guid id,List<string> warnings)
    {
        var root=Root(id);Check(root);if(!Directory.Exists(root))return [];
        var folders=Directory.EnumerateFileSystemEntries(root).Take(4097).ToArray();if(folders.Length>4096)throw new IOException("Recovery history reached its inspection limit; existing copies were preserved.");
        var result=new List<RestoreRecoveryInfo>();
        foreach(var folder in folders)
        {
            try
            {
                Check(folder);var name=Path.GetFileName(folder);
                if(!Guid.TryParseExact(name,"N",out var operation)||operation==Guid.Empty){warnings.Add("An incomplete or unknown recovery folder was preserved: "+name);continue;}
                var doc=Read(id,operation);result.Add(new(doc.Id,doc.CreatedAt,folder,"Before restore "+(doc.Choice.BackupId??doc.Choice.RecoveryId)?.ToString("N")[..8]));
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){warnings.Add(e.Message);}
        }
        return result.OrderByDescending(r=>r.CreatedAt).ThenByDescending(r=>r.Id).ToArray();
    }
    public List<FileStream> HoldVerified(string folder,RestoreRecoveryDocument document,CancellationToken token)
    {
        var held=new List<FileStream>();
        try
        {
            var manifest=Path.Combine(folder,"recovery.json");Check(manifest);held.Add(new FileStream(manifest,FileMode.Open,FileAccess.Read,FileShare.Read));
            if(Fingerprint(Read(document.GameId,document.Id))!=Fingerprint(document))throw new IOException("Recovery metadata changed before restore; nothing was replaced.");
            foreach(var (files,part) in new[]{(document.BeforeFiles,"Before"),(document.SelectedFiles,"Selected")})
                foreach(var file in files)
                {
                    token.ThrowIfCancellationRequested();var path=Path.Combine(folder,part,file.StoragePath);Check(path);var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);held.Add(stream);
                    if(stream.Length!=file.Size||BackupStorage.Hash(stream,token)!=file.Sha256)throw new IOException("Recovery/staged payload failed verification; nothing was replaced.");
                }
            return held;
        }
        catch{foreach(var stream in held)stream.Dispose();throw;}
    }
    public static string Fingerprint(object value)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    internal static bool Relative(string path)=>!string.IsNullOrWhiteSpace(path)&&path.Length<=1024&&!Path.IsPathRooted(path)&&!path.Contains('\\')&&path.Split('/').All(p=>p is not "" and not "." and not ".."&&p.IndexOfAny(Path.GetInvalidFileNameChars())<0&&!p.EndsWith('.')&&!char.IsWhiteSpace(p[^1]));
    private static bool Hash(string? hash)=>hash is {Length:64}&&hash.All(Uri.IsHexDigit);
    private static bool Valid(RestoreRecoveryDocument d,Guid gameId,Guid id)
    {
        if(d.SchemaVersion!=1||d.GameId!=gameId||d.Id!=id||id==Guid.Empty||d.CreatedAt==default||d.Choice is null||(d.Choice.BackupId is null)==(d.Choice.RecoveryId is null)||d.Choice.BackupId==Guid.Empty||d.Choice.RecoveryId==Guid.Empty||d.Targets is not {Length:>0 and <=32}||d.BeforeFiles is null||d.BeforeFiles.Length>4096||d.SelectedFiles is null||d.SelectedFiles.Length>4096||d.BeforeDirectories is null||d.BeforeDirectories.Length>32768||d.Changes is null||d.Changes.Length>8192)return false;
        if(d.Targets.Any(t=>t is null||t.Id==Guid.Empty||!Path.IsPathFullyQualified(t.Path)||!Enum.IsDefined(t.Kind))||d.Targets.Select(t=>t.Id).Distinct().Count()!=d.Targets.Length)return false;
        var ids=d.Targets.Select(t=>t.Id).ToHashSet();
        bool Files(BackupFile[] files)=>files.All(f=>f is not null&&ids.Contains(f.SourceId)&&Relative(f.RelativePath)&&f.StoragePath==f.SourceId.ToString("N")+"/"+f.RelativePath&&f.Size>=0&&f.Size<=4L*1024*1024*1024&&Hash(f.Sha256))&&files.Sum(f=>f.Size)<=4L*1024*1024*1024&&files.Select(f=>f.StoragePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()==files.Length;
        return Files(d.BeforeFiles)&&Files(d.SelectedFiles)&&d.BeforeDirectories.All(Path.IsPathFullyQualified)&&d.Changes.All(c=>c is not null&&ids.Contains(c.SourceId)&&Relative(c.RelativePath)&&Path.IsPathFullyQualified(c.TargetPath)&&Enum.IsDefined(c.Action)&&
            (c.BeforeHash is null?c.BeforeSize is null:Hash(c.BeforeHash)&&c.BeforeSize>=0)&&(c.Action==RestoreAction.Write?Hash(c.AfterHash)&&c.AfterSize>=0:c.AfterHash is null&&c.AfterSize is null&&c.BeforeHash is not null))&&d.Changes.Select(c=>c.TargetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()==d.Changes.Length;
    }
}