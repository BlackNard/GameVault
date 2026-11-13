using GameVault.Core;
namespace GameVault.Infrastructure.Saves;

internal sealed record RestoreInput(Guid SourceId,string RelativePath,string Path,long Size,string Hash);
internal sealed record RestoreInventory(RestoreInput[] Files,string[] Directories);
internal static class RestoreFiles
{
    public static string Target(BackupSource source,string relative)=>source.Kind==SaveSourceKind.File?source.Path:Path.GetFullPath(Path.Combine(source.Path,relative));
    public static RestoreInventory Inventory(BackupSource[] targets,Dictionary<Guid,string> fileNames,SaveSourcePolicy policy,CancellationToken token)
    {
        var files=new List<RestoreInput>();var dirs=new List<string>();int entries=0;long bytes=0;
        foreach(var source in targets)
        {
            policy.Validate(source.Path);token.ThrowIfCancellationRequested();
            if(source.Kind==SaveSourceKind.File){if(Directory.Exists(source.Path))throw new IOException("A file destination points to a folder.");if(File.Exists(source.Path))Add(source.Path,fileNames[source.Id]);continue;}
            if(File.Exists(source.Path))throw new IOException("A folder destination points to a file.");if(!Directory.Exists(source.Path))continue;
            var queue=new Queue<(string Path,int Depth)>();queue.Enqueue((source.Path,0));
            while(queue.TryDequeue(out var current))
            {
                token.ThrowIfCancellationRequested();SaveSourcePolicy.CheckLinks(current.Path);dirs.Add(current.Path);
                foreach(var child in new DirectoryInfo(current.Path).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();if(++entries>8192)throw new IOException("Destination exceeds the 8192-entry restore inspection limit.");SaveSourcePolicy.CheckLinks(child.FullName);
                    if((child.Attributes&FileAttributes.Directory)!=0){if(current.Depth>=6)throw new IOException("Destination exceeds the restore depth limit.");queue.Enqueue((child.FullName,current.Depth+1));}
                    else Add(child.FullName,Path.GetRelativePath(source.Path,child.FullName).Replace('\\','/'));
                }
            }
            void Add(string path,string relative)
            {
                if(files.Count>=4096||!RestoreStorage.Relative(relative))throw new IOException("Destination exceeded its safe file/path limit.");SaveSourcePolicy.CheckLinks(path);
                using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);bytes=checked(bytes+input.Length);if(bytes>4L*1024*1024*1024)throw new IOException("Current saves exceed the 4 GiB preservation limit. Nothing was replaced.");
                files.Add(new(source.Id,relative,path,input.Length,BackupStorage.Hash(input,token)));
            }
        }
        return new(files.OrderBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray(),dirs.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public static void VerifyFile(string path,long size,string hash,CancellationToken token)
    {
        SaveSourcePolicy.CheckLinks(path);using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length!=size||BackupStorage.Hash(stream,token)!=hash)throw new IOException("File changed or failed integrity verification: "+path);
    }
    public static void CopyVerified(string inputPath,string outputPath,long size,string hash,CancellationToken token)
    {
        SaveSourcePolicy.CheckLinks(inputPath);SaveSourcePolicy.CheckLinks(outputPath);
        using(var input=new FileStream(inputPath,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            if(input.Length!=size||BackupStorage.Hash(input,token)!=hash)throw new IOException("Source changed before preservation or staging.");input.Position=0;
            using var output=new FileStream(outputPath,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough);BackupStorage.Copy(input,output,token);output.Flush(true);
        }
        VerifyFile(outputPath,size,hash,token);
    }
}