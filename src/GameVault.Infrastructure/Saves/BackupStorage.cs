using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;

internal sealed class BackupStorage(ApplicationPaths paths)
{
    private readonly ManagedPaths managed=new(paths);
    private readonly JsonDocuments documents=new(new ManagedPaths(paths));
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,Converters={new JsonStringEnumConverter()}};
    public string Root(Guid id)=>Path.Combine(paths.Profiles,id.ToString("N"),"Backups");
    private string StatusFile(Guid id)=>Path.Combine(paths.Profiles,id.ToString("N"),"backup-status.json");
    public void Check(string path){SaveSourcePolicy.CheckLinks(paths.Root);managed.Check(path);SaveSourcePolicy.CheckLinks(path);}
    public void Ensure(string path){Check(path);managed.EnsureDirectory(path);Check(path);}
    public StoredDocument<BackupStatus> ReadStatus(Guid id)=>documents.Read<BackupStatus>(StatusFile(id),s=>s.SchemaVersion==1&&s.GameId==id&&Enum.IsDefined(s.Phase)&&s.Message is {Length:>0 and <8192}&&s.UpdatedAt!=default);
    public void WriteStatus(BackupResult result)
    {
        Check(StatusFile(result.GameId));documents.Write(StatusFile(result.GameId),new BackupStatus(1,result.GameId,result.Phase,result.Message,DateTimeOffset.UtcNow),s=>s.SchemaVersion==1&&s.GameId==result.GameId&&Enum.IsDefined(s.Phase)&&s.Message is {Length:>0 and <8192}&&s.UpdatedAt!=default);
    }
    public CompletedBackup? Latest(Guid id,CancellationToken token)
    {
        var root=Root(id);Check(root);if(!Directory.Exists(root))return null;
        var directories=Directory.EnumerateDirectories(root).Take(4097).ToArray();
        if(directories.Length>4096)throw new IOException("Backup history exceeded its inspection limit; existing snapshots were preserved.");
        CompletedBackup? latest=null;
        foreach(var folder in directories)
        {
            token.ThrowIfCancellationRequested();Check(folder);
            if(Path.GetFileName(folder).StartsWith(".pending-",StringComparison.Ordinal))continue;
            var file=Path.Combine(folder,"manifest.json");Check(file);
            if(new FileInfo(file).Length>8*1024*1024)throw new IOException("A backup manifest exceeded its inspection limit.");
            var stored=documents.Read<BackupManifest>(file,m=>Valid(m,id));
            if(stored.State!=DocumentState.Ready)throw new IOException(stored.Message??"An incomplete backup was preserved; inspect it before retrying.");
            var manifest=stored.Value!;
            if(Path.GetFileName(folder)!=Name(manifest))throw new IOException("A backup folder does not match its manifest identity.");
            if(latest is null||manifest.CreatedAt>latest.Manifest.CreatedAt||manifest.CreatedAt==latest.Manifest.CreatedAt&&manifest.Id.CompareTo(latest.Manifest.Id)>0)latest=new(folder,manifest);
        }
        if(latest is not null)Verify(latest,token);
        return latest;
    }
    public static string Name(BackupManifest m)=>m.CreatedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'",System.Globalization.CultureInfo.InvariantCulture)+"-"+m.Id.ToString("N");
    public void WriteManifest(string folder,BackupManifest manifest)
    {
        if(!Valid(manifest,manifest.GameId))throw new IOException("Invalid backup manifest; no snapshot committed.");
        var file=Path.Combine(folder,"manifest.json");Check(file);
        using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
        JsonSerializer.Serialize(stream,manifest,Json);if(stream.Position>8*1024*1024)throw new IOException("Backup manifest exceeded the 8 MiB inspection limit.");stream.Flush(true);
    }
    public void Verify(CompletedBackup backup,CancellationToken token)
    {
        Check(backup.Directory);
        foreach(var file in backup.Manifest.Files)
        {
            token.ThrowIfCancellationRequested();var path=Path.Combine(backup.Directory,"Files",file.StoragePath);Check(path);
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(stream.Length!=file.Size||Hash(stream,token)!=file.Sha256)throw new IOException("A backup file failed integrity verification. Existing snapshots were preserved.");
        }
    }
    internal static string Hash(Stream input,CancellationToken token)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];int read;
        while(true){token.ThrowIfCancellationRequested();read=input.Read(buffer);if(read==0)break;hash.AppendData(buffer,0,read);}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    internal static void Copy(Stream input,Stream output,CancellationToken token)
    {
        var buffer=new byte[65536];int read;
        while(true){token.ThrowIfCancellationRequested();read=input.Read(buffer);if(read==0)break;output.Write(buffer,0,read);}
    }
    private static bool Relative(string value)=>!string.IsNullOrEmpty(value)&&value.Length<=1024&&!Path.IsPathRooted(value)&&!value.Contains('\\')&&
        value.Split('/').All(p=>p.Length>0&&p is not "." and not ".."&&p.IndexOfAny(Path.GetInvalidFileNameChars())<0&&!p.EndsWith('.')&&!char.IsWhiteSpace(p[^1]));
    private static bool Valid(BackupManifest m,Guid id)
    {
        if(m.SchemaVersion!=1||m.GameId!=id||m.Id==Guid.Empty||m.CreatedAt==default||m.Fingerprint is not {Length:64}||m.Sources is not {Length:>0 and <=32}||m.Files is not {Length:>0 and <=4096}||m.Deleted is null||m.Deleted.Length>4096)return false;
        if(m.Sources.Any(s=>s is null||s.Id==Guid.Empty||!Path.IsPathFullyQualified(s.Path)||!Enum.IsDefined(s.Kind))||m.Sources.Select(s=>s.Id).Distinct().Count()!=m.Sources.Length)return false;
        var ids=m.Sources.Select(s=>s.Id).ToHashSet();
        if(m.Files.Any(f=>f is null||!ids.Contains(f.SourceId)||!Relative(f.RelativePath)||f.StoragePath!=f.SourceId.ToString("N")+"/"+f.RelativePath||f.Size<0||f.Size>4L*1024*1024*1024||f.Sha256 is not {Length:64}||f.Sha256.Any(c=>!Uri.IsHexDigit(c))))return false;
        return m.Files.Sum(f=>f.Size)<=4L*1024*1024*1024&&m.Files.Select(f=>f.StoragePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()==m.Files.Length&&m.Deleted.All(d=>d is not null&&d.SourceId!=Guid.Empty&&Relative(d.RelativePath));
    }
}