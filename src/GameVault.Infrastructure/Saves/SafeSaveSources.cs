using GameVault.Core;
namespace GameVault.Infrastructure.Saves;
public sealed class SaveSourcePolicy
{
    private readonly ApplicationPaths paths;
    private readonly string[] broad;
    public SaveSourcePolicy(ApplicationPaths paths,IEnumerable<string>? additionalBroadRoots=null)
    {
        this.paths=paths;
        broad=new[]{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Saved Games")}
            .Concat(additionalBroadRoots??[]).Where(p=>!string.IsNullOrWhiteSpace(p)&&Path.IsPathFullyQualified(p)).Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public string Validate(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||path.StartsWith("//",StringComparison.Ordinal))
            throw new IOException("Choose an absolute local save file or folder; network and device paths are not supported.");
        try
        {
            var raw=path.Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries);
            if(raw.Any(p=>p is "." or ".."||p.EndsWith('.')||char.IsWhiteSpace(p[^1])))throw new IOException("Save paths must not contain traversal components.");
            var full=Canonical(path);var root=Path.GetPathRoot(full)!;
            if(OperatingSystem.IsWindows()&&new DriveInfo(root).DriveType==DriveType.Network)throw new IOException("Mapped network drives are not supported as local save sources.");
            if(full.Equals(Canonical(root),StringComparison.OrdinalIgnoreCase)||broad.Contains(full,StringComparer.OrdinalIgnoreCase))
                throw new IOException("Choose a game's narrow save location, not an entire drive, user folder or shared system folder.");
            foreach(var part in full[root.Length..].Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries))
                if(part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new IOException("The save path contains unsupported characters or alternate streams.");
            var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if(!string.IsNullOrWhiteSpace(windows)&&Within(full,Canonical(windows)))throw new IOException("Windows system folders cannot be save sources.");
            if(Overlaps(full,paths.Data)||Overlaps(full,paths.Profiles))throw new IOException("Save sources must not overlap GameVault data or backup/profile destinations.");
            CheckLinks(full);return full;
        }
        catch(ArgumentException e){throw new IOException("The save path is invalid.",e);}
    }
    public string[] ValidateSet(IEnumerable<string> values)
    {
        var set=values.Select(Validate).Distinct(StringComparer.OrdinalIgnoreCase).Take(33).ToArray();
        if(set.Length>32)throw new IOException("Choose at most 32 narrow save locations.");
        for(var i=0;i<set.Length;i++)for(var j=i+1;j<set.Length;j++)if(Overlaps(set[i],set[j]))throw new IOException("Save sources must not contain or overlap each other.");
        return set;
    }
    internal static string Canonical(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    internal static bool Within(string path,string parent)=>path.Equals(parent,StringComparison.OrdinalIgnoreCase)||path.StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    internal static bool Overlaps(string a,string b)=>Within(Canonical(a),Canonical(b))||Within(Canonical(b),Canonical(a));
    internal static void CheckLinks(string path)
    {
        for(string? current=path;current is not null;current=Path.GetDirectoryName(current))
        {
            var name=Path.GetFileName(current);
            if(name.Length>0)
            {
                var stem=name.Split('.')[0].Normalize(System.Text.NormalizationForm.FormKC).ToUpperInvariant();
                if(name.EndsWith('.')||char.IsWhiteSpace(name[^1])||new[]{"CON","PRN","AUX","NUL","CONIN$","CONOUT$"}.Contains(stem)||
                    (stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3] is >= '1' and <= '9'))
                    throw new UnsafeSaveSourceException("Windows device names and trailing-dot/space aliases are not valid save paths.");
            }
            try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new UnsafeSaveSourceException("Linked files/junctions are not followed; choose the actual narrow local save folder.");}
            catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
        }
    }
}
internal sealed class UnsafeSaveSourceException(string message):IOException(message);
public sealed record SourceProbe(SaveSourceKind Kind,SaveSourceAccess Access,int Files,string? Message);
public sealed class SaveSourceInspector(SaveSourcePolicy policy,int maxDepth=6,int maxEntries=4096)
{
    public SourceProbe Inspect(string path,CancellationToken token=default,Func<string,bool>? isSaveFile=null)
    {
        var kind=SaveSourceKind.Folder;int files=0,entries=0;bool payload=false;
        try
        {
            token.ThrowIfCancellationRequested();
            try{path=policy.Validate(path);}catch(IOException e){return new(kind,SaveSourceAccess.Unsafe,0,e.Message);}
            FileAttributes attributes;
            try{attributes=File.GetAttributes(path);}catch(FileNotFoundException){return new(kind,SaveSourceAccess.Missing,0,"Waiting for the game to create this source.");}
            catch(DirectoryNotFoundException){return new(kind,SaveSourceAccess.Missing,0,"Waiting for the game to create this source.");}
            if((attributes&FileAttributes.Directory)==0){kind=SaveSourceKind.File;ProbeFile(path);}
            else
            {
                var queue=new Queue<(string Path,int Depth)>();queue.Enqueue((path,0));
                while(queue.TryDequeue(out var item))
                {
                    token.ThrowIfCancellationRequested();SaveSourcePolicy.CheckLinks(item.Path);
                    var children=new DirectoryInfo(item.Path).EnumerateFileSystemInfos().Take(Math.Max(1,maxEntries-entries+1)).OrderBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).ToArray();
                    foreach(var child in children)
                    {
                        token.ThrowIfCancellationRequested();if(++entries>maxEntries)return new(kind,SaveSourceAccess.Limited,files,"Source scan reached its entry limit. Choose a smaller save folder.");
                        if((child.Attributes&FileAttributes.ReparsePoint)!=0)throw new UnsafeSaveSourceException("A source contains a linked file/junction; it was not followed.");
                        SaveSourcePolicy.CheckLinks(child.FullName);
                        if((child.Attributes&FileAttributes.Directory)!=0)
                        {if(item.Depth>=maxDepth)return new(kind,SaveSourceAccess.Limited,files,"Source scan reached its depth limit. Choose a smaller save folder.");queue.Enqueue((child.FullName,item.Depth+1));}
                        else ProbeFile(child.FullName);
                    }
                }
            }
            return new(kind,payload?SaveSourceAccess.Ready:SaveSourceAccess.Empty,files,payload?null:"No readable, nonempty save files found yet.");
        }
        catch(UnsafeSaveSourceException e){return new(kind,SaveSourceAccess.Unsafe,files,e.Message);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {return new(kind,SaveSourceAccess.Unreadable,files,"Source could not be read completely. Check access or retry after the game closes. "+e.Message);}
        void ProbeFile(string file)
        {
            token.ThrowIfCancellationRequested();SaveSourcePolicy.CheckLinks(file);
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,1,FileOptions.SequentialScan);
            var nonempty=stream.ReadByte()>=0;files++;if(nonempty&&(isSaveFile is null||isSaveFile(file)))payload=true;
        }
    }
}