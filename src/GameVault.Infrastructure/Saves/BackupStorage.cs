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
    private readonly BackupDestinationService destinations=new(paths);
    public string Root(Guid id)=>destinations.ResolveCurrent(id);
    public bool OwnsRoot(Guid id,string root)=>destinations.Directories(id).Contains(root,StringComparer.OrdinalIgnoreCase);
    public IDisposable Acquire()=>destinations.Acquire();
    public void ValidateDestinations()=>destinations.ValidateKnownRoots();
    private string StatusFile(Guid id)=>Path.Combine(ProfileFolders.Resolve(paths,id),"backup-status.json");
    public void Check(string path)
    {
        SaveSourcePolicy.CheckLinks(paths.Root);path=Path.GetFullPath(path);SaveSourcePolicy.CheckLinks(path);
        if(SaveSourcePolicy.Within(path,paths.Root)){managed.Check(path);return;}
        foreach(var root in destinations.ExternalRoots())
        {
            var relative=Path.GetRelativePath(root,path).Split(Path.DirectorySeparatorChar);
            if(relative.Length>=2&&Guid.TryParseExact(relative[0],"N",out var id)&&id!=Guid.Empty&&relative[1]=="Backups"&&SaveSourcePolicy.Within(path,Path.Combine(root,relative[0],"Backups")))return;
        }
        throw new IOException("The path is outside registered backup storage.");
    }
    private JsonDocuments DocumentsFor(string file)=>SaveSourcePolicy.Within(file,paths.Root)?documents:new(new ManagedPaths(ApplicationPaths.FromRoot(destinations.ExternalRoots().Where(r=>SaveSourcePolicy.Within(file,r)).OrderByDescending(r=>r.Length).First())));
    public void Ensure(string path){Check(path);if(SaveSourcePolicy.Within(path,paths.Root))managed.EnsureDirectory(path);else {var root=destinations.ExternalRoots().First(r=>SaveSourcePolicy.Within(path,r));if(!Directory.Exists(root))throw new DirectoryNotFoundException("The backup destination is unavailable.");Directory.CreateDirectory(path);}Check(path);}
    public StoredDocument<BackupStatus> ReadStatus(Guid id)=>documents.Read<BackupStatus>(StatusFile(id),s=>s.SchemaVersion==1&&s.GameId==id&&Enum.IsDefined(s.Phase)&&s.Message is {Length:>0 and <8192}&&s.UpdatedAt!=default);
    public void WriteStatus(BackupResult result)
    {
        Check(StatusFile(result.GameId));documents.Write(StatusFile(result.GameId),new BackupStatus(1,result.GameId,result.Phase,result.Message,DateTimeOffset.UtcNow),s=>s.SchemaVersion==1&&s.GameId==result.GameId&&Enum.IsDefined(s.Phase)&&s.Message is {Length:>0 and <8192}&&s.UpdatedAt!=default);
    }
    public sealed record CatalogResult(CompletedBackup[] Backups,string[] Warnings,int RecoveryFolders);
    public CatalogResult Catalog(Guid id,CancellationToken token)
    {
        var backups=new List<CompletedBackup>();var warnings=new List<string>();var recovery=0;
        foreach(var root in destinations.Directories(id))
        {
        string[] entries;
        try
        {
            Check(root);
            var external=destinations.ExternalRoots().FirstOrDefault(r=>SaveSourcePolicy.Within(root,r));
            if(external is not null&&!Directory.Exists(external))throw new IOException("Backup location is offline: "+external);
            if(!Directory.Exists(root))continue;
            entries=Directory.EnumerateFileSystemEntries(root).Take(4097).ToArray();
            if(entries.Length>4096)throw new IOException("Backup history exceeded its inspection limit; existing snapshots were preserved.");
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){warnings.Add(e.Message);continue;}
        foreach(var folder in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                Check(folder);
                if(!Directory.Exists(folder))throw new IOException("An unrecognized file in Backups was preserved.");
                if(Path.GetFileName(folder).StartsWith(".pending-",StringComparison.Ordinal)||Path.GetFileName(folder).StartsWith(".prune-",StringComparison.Ordinal)){recovery++;continue;}
                var file=Path.Combine(folder,"manifest.json");Check(file);
                if(new FileInfo(file).Length>8*1024*1024)throw new IOException("A backup manifest exceeded its inspection limit.");
                var stored=DocumentsFor(file).Read<BackupManifest>(file,m=>Valid(m,id));
                if(stored.State!=DocumentState.Ready)throw new IOException(stored.Message??"An incomplete backup was preserved; inspect it before retrying.");
                var manifest=stored.Value!;
                if(Path.GetFileName(folder)!=Name(manifest))throw new IOException("A backup folder does not match its manifest identity.");
                backups.Add(new(folder,manifest));
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {warnings.Add(Path.GetFileName(folder)+": "+e.Message);}
        }
        }
        if(backups.GroupBy(b=>b.Manifest.Id).Any(g=>g.Count()>1))warnings.Add("Duplicate snapshot identities were found across backup locations; resolve them before restore or cleanup.");
        return new(backups.OrderByDescending(b=>b.Manifest.CreatedAt).ThenByDescending(b=>b.Manifest.Id).ToArray(),warnings.ToArray(),recovery);
    }
    public CompletedBackup? Latest(Guid id,CancellationToken token)
    {
        var catalog=Catalog(id,token);
        if(catalog.Warnings.Length>0)throw new IOException(string.Join(" ",catalog.Warnings));
        var latest=catalog.Backups.FirstOrDefault();if(latest is not null)Verify(latest,token);return latest;
    }
    public static string Name(BackupManifest m)=>m.CreatedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'",System.Globalization.CultureInfo.InvariantCulture)+"-"+m.Id.ToString("N");
    public void WriteManifest(string folder,BackupManifest manifest)
    {
        if(!Valid(manifest,manifest.GameId))throw new IOException("Invalid backup manifest; no snapshot committed.");
        var file=Path.Combine(folder,"manifest.json");Check(file);
        using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
        JsonSerializer.Serialize(stream,manifest,Json);if(stream.Position>8*1024*1024)throw new IOException("Backup manifest exceeded the 8 MiB inspection limit.");stream.Flush(true);
    }
    public void ConfirmManifest(CompletedBackup backup)
    {
        var file=Path.Combine(backup.Directory,"manifest.json");Check(file);
        if(new FileInfo(file).Length>8*1024*1024)throw new IOException("Backup manifest exceeded its inspection limit.");
        var current=DocumentsFor(file).Read<BackupManifest>(file,m=>Valid(m,backup.Manifest.GameId));
        if(current.State!=DocumentState.Ready||JsonSerializer.Serialize(current.Value,Json)!=JsonSerializer.Serialize(backup.Manifest,Json))
            throw new IOException("Backup manifest changed or needs recovery. Cleanup stopped.");
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
