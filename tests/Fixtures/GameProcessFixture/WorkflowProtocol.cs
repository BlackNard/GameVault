using System.Diagnostics;
using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Saves;

internal static class WorkflowProtocol
{
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,RespectNullableAnnotations=true,RespectRequiredConstructorParameters=true};
    private sealed record Command(Guid Session,int Sequence,Guid RequestId,string Action,string? Path,string? Progress);
    private sealed record Result(Guid Session,int Sequence,Guid RequestId,bool Ok,string? Progress,string? Error);
    private sealed record SaveData(string Format,int Version,string Progress);
    public static async Task RunAsync(string directory)
    {
        var sandbox=Path.GetFullPath(directory);var parent=Path.GetDirectoryName(sandbox)!;var temporary=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if(!parent.StartsWith(temporary+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(parent).StartsWith("GameVault workflow ",StringComparison.Ordinal))throw new IOException("Workflow fixture requires its own temporary sandbox.");
        var token=Guid.ParseExact((await File.ReadAllTextAsync(Path.Combine(sandbox,"workflow-mode.txt"))).Trim(),"N");
        var policy=new SaveSourcePolicy(ApplicationPaths.FromRoot(Path.Combine(sandbox,".fixture-reserved")));
        var commands=Path.Combine(sandbox,"Commands",token.ToString("N"));var results=Path.Combine(sandbox,"Results",token.ToString("N"));policy.Validate(commands);policy.Validate(results);Directory.CreateDirectory(commands);Directory.CreateDirectory(results);
        var processed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var elapsed=Stopwatch.StartNew();var sequence=0;var request=Guid.Empty;
        while(elapsed.Elapsed<TimeSpan.FromSeconds(30)&&!File.Exists(Path.Combine(sandbox,"exit.txt")))
        {
            var pending=Directory.EnumerateFiles(commands,"*.json").Take(1001).ToArray();if(pending.Length>1000)throw new IOException("Fixture command count exceeded its bound.");
            var commandFile=pending.Order(StringComparer.Ordinal).FirstOrDefault(file=>!processed.Contains(file));
            if(commandFile is null){await Task.Delay(20);continue;}policy.Validate(commandFile);
            Command? command;
            try
            {
                if(new FileInfo(commandFile).Length>1024*1024)throw new IOException("Command exceeds fixture limit.");
                using var input=new FileStream(commandFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);command=await JsonSerializer.DeserializeAsync<Command>(input,Json);
            }
            catch(JsonException){processed.Add(commandFile);continue;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){await Task.Delay(20);continue;}
            processed.Add(commandFile);
            if(command is null||command.Session!=token||command.RequestId==Guid.Empty||command.RequestId==request){await Task.Delay(20);continue;}
            request=command.RequestId;Result result;var exit=false;
            try
            {
                if(command.Sequence<=sequence||command.Sequence>10000)throw new IOException("Stale or invalid sequence rejected.");sequence=command.Sequence;
                if(command.Action=="exit"){exit=true;result=new(token,sequence,request,true,null,null);}
                else
                {
                    var relative=command.Path??throw new IOException("A narrow relative save path is required.");
                    var parts=relative.Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries);
                    if(Path.IsPathRooted(relative)||parts.Length<2||new[]{"Commands","Results",".fixture-reserved"}.Contains(parts[0],StringComparer.OrdinalIgnoreCase)||parts.Any(p=>p is "." or ".."))throw new IOException("Save path must stay below a sandbox subdirectory.");
                    var path=policy.Validate(Path.GetFullPath(Path.Combine(sandbox,relative)));
                    if(!path.StartsWith(sandbox+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Save path escaped its sandbox.");
                    if(command.Action=="save")
                    {
                        if(command.Progress is null||command.Progress.Length>128*1024)throw new IOException("Invalid synthetic progress.");
                        var folder=Path.GetDirectoryName(path)!;policy.Validate(folder);Directory.CreateDirectory(folder);policy.Validate(folder);
                        var stage=path+".writing-"+Guid.NewGuid().ToString("N");await File.WriteAllTextAsync(stage,JsonSerializer.Serialize(new SaveData("GameVaultFixtureSave",1,command.Progress),Json));policy.Validate(path);File.Move(stage,path,true);
                        result=new(token,sequence,request,true,command.Progress,null);
                    }
                    else if(command.Action=="load")
                    {
                        if(new FileInfo(path).Length>1024*1024)throw new IOException("Save exceeds fixture limit.");
                        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);var save=await JsonSerializer.DeserializeAsync<SaveData>(stream,Json);
                        if(save is not {Format:"GameVaultFixtureSave",Version:1})throw new IOException("Synthetic save format is not playable by this fixture.");
                        result=new(token,sequence,request,true,save.Progress,null);
                    }
                    else throw new IOException("Unknown fixture action.");
                }
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException){result=new(token,command.Sequence,request,false,null,error.Message);}
            var output=Path.Combine(results,"result-"+Guid.NewGuid().ToString("N")+".tmp");await File.WriteAllTextAsync(output,JsonSerializer.Serialize(result,Json));File.Move(output,Path.Combine(results,request.ToString("N")+".json"),false);
            if(exit)return;
        }
    }
}