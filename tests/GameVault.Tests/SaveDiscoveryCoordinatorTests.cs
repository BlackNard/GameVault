using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class SaveDiscoveryCoordinatorTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault lifecycle discovery",Guid.NewGuid().ToString("N"));
    [Fact] public async Task SaveCreatedDuringFirstSessionIsFoundAndFinalExitScanIsDrained()
    {
        var paths=ApplicationPaths.FromRoot(Path.Combine(root,"app"));Directory.CreateDirectory(paths.Root);var store=new AppDataStore(paths);await store.InitializeAsync();var id=Guid.NewGuid();
        var profile=new GameProfile(1,id,"Resident Evil 2",Path.Combine(root,"Resident Evil 2","re2.exe"),Path.Combine(root,"Resident Evil 2"),[],GameStatus.Playing,20,false,false);await store.SaveProfileAsync(profile);
        var env=new SaveDiscoveryEnvironment(Path.Combine(root,"user"),Path.Combine(root,"docs"),Path.Combine(root,"saved"),Path.Combine(root,"roaming"),Path.Combine(root,"local"),Path.Combine(root,"public"),[]);
        var scanner=new SaveDiscoveryService(paths,store,env);var signals=new Signals();await using var coordinator=new SaveDiscoveryCoordinator(signals,scanner,TimeSpan.FromMilliseconds(15));
        var first=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var found=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanner.Changed+=(_,change)=>{if(change.Result.Phase==SaveDiscoveryPhase.WaitingForSave)first.TrySetResult();if(change.Result.Phase==SaveDiscoveryPhase.SourcesFound)found.TrySetResult();};
        signals.State(id,LaunchPhase.Running);await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var save=Path.Combine(env.PublicDocuments,"Steam","CODEX","883710","remote","win64_save");Directory.CreateDirectory(save);File.WriteAllText(Path.Combine(save,"data000.bin"),"First session save");
        await found.Task.WaitAsync(TimeSpan.FromSeconds(5));signals.End(id,true);await coordinator.FlushAsync(id);
        Assert.Equal(SaveDiscoveryPhase.SourcesFound,(await scanner.ReadAsync(id)).Phase);Assert.Equal("First session save",File.ReadAllText(Path.Combine(save,"data000.bin")));
    }
    [Fact] public async Task DisposeCancelsAndDrainsPendingDiscoveryWithoutStartingAnotherScan()
    {
        var scanner=new BlockingScanner();var signals=new Signals();var coordinator=new SaveDiscoveryCoordinator(signals,scanner,TimeSpan.FromMilliseconds(10));var id=Guid.NewGuid();
        signals.State(id,LaunchPhase.Running);await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));await coordinator.DisposeAsync();
        Assert.True(scanner.Cancelled);signals.State(id,LaunchPhase.Running);Assert.Equal(1,scanner.Calls);
    }
    private sealed class Signals:IGameSessionSignals
    {
        public event EventHandler<LaunchState>? StateChanged;
        public event EventHandler<GameSessionEnded>? SessionEnded;
        public void State(Guid id,LaunchPhase phase)=>StateChanged?.Invoke(this,new(id,phase,"Fixture"));
        public void End(Guid id,bool confirmed)=>SessionEnded?.Invoke(this,new(id,confirmed));
    }
    private sealed class BlockingScanner:ISaveDiscoveryScanner
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Cancelled;public int Calls;
        public async Task<SaveDiscoveryResult> ScanAsync(Guid id,CancellationToken token=default)
        {
            Calls++;Started.TrySetResult();try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){Cancelled=true;throw;}
            throw new InvalidOperationException("Unreachable");
        }
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}