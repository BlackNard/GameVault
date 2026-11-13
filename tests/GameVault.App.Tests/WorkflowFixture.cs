using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Playtime;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;
namespace GameVault.App.Tests;

internal sealed record FixtureCommand(Guid Session,int Sequence,Guid RequestId,string Action,string? Path,string? Progress);
internal sealed record FixtureResult(Guid Session,int Sequence,Guid RequestId,bool Ok,string? Progress,string? Error);
internal sealed class WorkflowFixture:IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    public string Root{get;}=Path.Combine(Path.GetTempPath(),"GameVault workflow "+Guid.NewGuid().ToString("N"));
    public string Installation=>Path.Combine(Root,"Resident Evil 2");
    public string Working=>Path.Combine(Root,"Working");
    public string Executable=>Path.Combine(Installation,"re2.exe");
    public string SaveRelative=>"Public/Steam/CODEX/883710/remote/win64_save/data000.bin";
    public string ExtraRelative=>"Public/Steam/CODEX/883710/remote/win64_save/data001.bin";
    public string SavePath=>Path.Combine(Working,SaveRelative);
    public ApplicationPaths Paths=>ApplicationPaths.FromRoot(Path.Combine(Root,"App"));
    public AppDataStore Store{get;private set;}=null!;
    public LibraryService Library{get;private set;}=null!;
    public GameProfile Profile{get;private set;}=null!;
    public LaunchService Launch{get;private set;}=null!;
    public SaveDiscoveryService Discovery{get;private set;}=null!;
    public BackupService Backups{get;private set;}=null!;
    public BackupHistoryService History=>new(Paths,Store,new(),Launch.IsGameRunningAsync);
    public RestoreService Restores=>new(Paths,Store,new(),Launch.IsGameRunningAsync);
    public WorkflowClock Clock{get;}=new();
    private readonly WindowsGameProcessSystem processes=new();
    private SaveDiscoveryCoordinator discoveryCoordinator=null!;
    private BackupCoordinator? backupCoordinator;
    private readonly bool automatic;
    private Task? run;private Guid session;private int sequence;
    private WorkflowFixture(bool automatic){this.automatic=automatic;}
    public static async Task<WorkflowFixture> CreateAsync(bool automatic=true)
    {
        var fixture=new WorkflowFixture(automatic);
        try{await fixture.Initialize();return fixture;}
        catch{await fixture.DisposeAsync();throw;}
    }
    private async Task Initialize()
    {
        Directory.CreateDirectory(Paths.Root);Directory.CreateDirectory(Installation);Directory.CreateDirectory(Working);
        foreach(var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"ProcessFixture")))File.Copy(file,Path.Combine(Installation,Path.GetFileName(file)));
        File.Copy(Path.Combine(Installation,"GameProcessFixture.exe"),Executable);
        Store=new(Paths);Library=new(Paths,Store);Profile=(await Library.SaveAsync(new(null,"Resident Evil 2",Executable,Working,[]))).Profile;StartRuntime();
    }
    private void StartRuntime()
    {
        Launch=new(Paths,Store,processes,TimeSpan.FromSeconds(5),TimeSpan.FromMilliseconds(25),Clock);
        Discovery=new(Paths,Store,new(Path.Combine(Working,"User"),Path.Combine(Working,"Documents"),Path.Combine(Working,"Saved Games"),Path.Combine(Working,"Roaming"),Path.Combine(Working,"Local"),Path.Combine(Working,"Public"),[]));
        Backups=new(Paths,Store,Discovery,new(),Launch.IsGameRunningAsync);discoveryCoordinator=new(Launch,Discovery,TimeSpan.FromMilliseconds(80));
        if(automatic)backupCoordinator=new(Launch,Backups,Launch.WaitForIdleAsync);
    }
    public async Task StartAsync()
    {
        session=Guid.NewGuid();File.WriteAllText(Path.Combine(Working,"workflow-mode.txt"),session.ToString("N"));
        foreach(var name in new[]{"ready.txt","exit.txt"}){var path=Path.Combine(Working,name);if(File.Exists(path))File.Delete(path);}
        run=Launch.RunAsync(Profile.Id);
        await WaitUntil(()=>Task.FromResult(Launch.GetState(Profile.Id).Phase==LaunchPhase.Running&&File.Exists(Path.Combine(Working,"ready.txt"))));
    }
    public async Task<FixtureResult> SendAsync(string action,string? path=null,string? progress=null,int? sequenceOverride=null)
    {
        var command=new FixtureCommand(session,sequenceOverride??++sequence,Guid.NewGuid(),action,path,progress);
        var commands=Path.Combine(Working,"Commands",session.ToString("N"));Directory.CreateDirectory(commands);
        var temporary=Path.Combine(commands,"command-"+command.RequestId.ToString("N")+".tmp");await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(command,Json));File.Move(temporary,Path.Combine(commands,$"{command.Sequence:D6}-{command.RequestId:N}.json"),false);
        FixtureResult? result=null;
        await WaitUntil(async()=>
        {
            var resultFile=Path.Combine(Working,"Results",session.ToString("N"),command.RequestId.ToString("N")+".json");if(!File.Exists(resultFile))return false;
            using var stream=new FileStream(resultFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            var candidate=await JsonSerializer.DeserializeAsync<FixtureResult>(stream,Json);
            if(candidate?.Session!=session||candidate.RequestId!=command.RequestId)return false;result=candidate;return true;
        });
        return result!;
    }
    public async Task ExitAsync()
    {
        var response=await SendAsync("exit");if(!response.Ok)throw new IOException(response.Error);
        if(run is not null)await run.WaitAsync(TimeSpan.FromSeconds(8));
        await WaitUntil(()=>Task.FromResult(Launch.GetState(Profile.Id).Phase==LaunchPhase.Stopped));await DrainAsync();
    }
    public async Task DrainAsync(){if(backupCoordinator is not null)await backupCoordinator.FlushAsync();await discoveryCoordinator.FlushAsync(Profile.Id);}
    public async Task RecoverAbandonedCheckpointAsync()
    {
        await Launch.SaveCheckpointAsync(Profile.Id);var abandoned=(await Store.LoadSessionJournalsAsync(Profile.Id)).Single().Document.Value!;
        if(abandoned.State!=SessionState.Active)throw new IOException("Fixture expected an active checkpoint.");
        await Launch.DisposeAsync();if(run is not null)await run;await discoveryCoordinator.DisposeAsync();if(backupCoordinator is not null)await backupCoordinator.DisposeAsync();
        // Controlled replay of an abandoned Active checkpoint, not a claim of a real OS crash.
        await Store.SaveSessionJournalAsync(abandoned);Clock.Advance(300,300);StartRuntime();run=null;
        var notices=await Launch.RecoverAsync();if(notices.Length==0)throw new IOException("Fixture session was not recovered.");
        await WaitUntil(()=>Task.FromResult(Launch.GetState(Profile.Id).Phase==LaunchPhase.Running));
    }
    public Dictionary<string,string> ProfileHashes()=>Directory.GetFiles(Path.Combine(Paths.Profiles,Profile.Id.ToString("N")),"*",SearchOption.AllDirectories)
        .ToDictionary(p=>Path.GetRelativePath(Paths.Profiles,p),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),StringComparer.OrdinalIgnoreCase);
    public void RemoveCopiedInstallation()
    {
        if(processes.Find(Executable).Length>0)throw new IOException("The test-owned executable is still running.");
        var full=Path.GetFullPath(Installation);if(!full.Equals(Path.Combine(Path.GetFullPath(Root),"Resident Evil 2"),StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected fixture removal target.");
        Directory.Delete(full,true);
    }
    public static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline=Stopwatch.StartNew();while(!await condition()){if(deadline.Elapsed>TimeSpan.FromSeconds(8))throw new TimeoutException("The windowless workflow fixture did not reach its expected state.");await Task.Delay(20);}
    }
    public async ValueTask DisposeAsync()
    {
        if(Launch is not null)await Launch.DisposeAsync();if(discoveryCoordinator is not null)await discoveryCoordinator.DisposeAsync();if(backupCoordinator is not null)await backupCoordinator.DisposeAsync();
        if(Directory.Exists(Working))await File.WriteAllTextAsync(Path.Combine(Working,"exit.txt"),"cleanup");
        if(File.Exists(Executable))foreach(var candidate in processes.Find(Executable))using(var process=processes.Open(candidate.Identity))if(process is not null)await process.WaitForExitAsync(default).WaitAsync(TimeSpan.FromSeconds(16));
        var full=Path.GetFullPath(Root);var temporary=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if(!full.StartsWith(temporary+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("GameVault workflow ",StringComparison.Ordinal))throw new IOException("Unsafe fixture cleanup target.");
        if(Directory.Exists(full))Directory.Delete(full,true);
    }
}
internal sealed class WorkflowClock:IPlaytimeClock
{
    private long awake,wall;
    public long AwakeTicks=>Interlocked.Read(ref awake);
    public DateTimeOffset UtcNow=>new DateTimeOffset(2026,11,13,10,0,0,TimeSpan.Zero).AddTicks(Interlocked.Read(ref wall));
    public void Advance(int awakeSeconds,int wallSeconds){Interlocked.Add(ref awake,TimeSpan.FromSeconds(awakeSeconds).Ticks);Interlocked.Add(ref wall,TimeSpan.FromSeconds(wallSeconds).Ticks);}
}