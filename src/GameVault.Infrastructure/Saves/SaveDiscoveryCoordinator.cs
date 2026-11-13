using GameVault.Core;
namespace GameVault.Infrastructure.Saves;
public sealed class SaveDiscoveryCoordinator:IAsyncDisposable
{
    private readonly IGameSessionSignals signals;private readonly ISaveDiscoveryScanner scanner;private readonly TimeSpan interval;
    private readonly object gate=new();private readonly Dictionary<Guid,Work> workers=[];private readonly Dictionary<Guid,string> errors=[];
    private bool disposed;private Task? drain;
    public SaveDiscoveryCoordinator(IGameSessionSignals signals,ISaveDiscoveryScanner scanner,TimeSpan? interval=null)
    {
        this.signals=signals;this.scanner=scanner;this.interval=interval??TimeSpan.FromSeconds(10);
        if(this.interval<=TimeSpan.Zero||this.interval.TotalMilliseconds>int.MaxValue)throw new ArgumentException("Invalid discovery interval.");
        signals.StateChanged+=StateChanged;signals.SessionEnded+=SessionEnded;
    }
    public string[] Errors{get{lock(gate)return errors.Values.ToArray();}}
    private void StateChanged(object? sender,LaunchState state)=>Request(state.GameId,state.Phase==LaunchPhase.Running);
    private void SessionEnded(object? sender,GameSessionEnded ended)=>Request(ended.GameId,false);
    private void Request(Guid id,bool running)
    {
        lock(gate)
        {
            if(disposed)return;
            if(workers.TryGetValue(id,out var previous))
            {previous.Running=running;previous.Again=true;if(previous.Signal.CurrentCount==0)previous.Signal.Release();return;}
            var work=new Work(id){Running=running};workers.Add(id,work);work.Task=Task.Run(()=>Run(work));
        }
    }
    private async Task Run(Work work)
    {
        try
        {
            while(true)
            {
                work.Stop.Token.ThrowIfCancellationRequested();
                try{await scanner.ScanAsync(work.Id,work.Stop.Token).ConfigureAwait(false);lock(gate)errors.Remove(work.Id);}
                catch(OperationCanceledException)when(work.Stop.IsCancellationRequested){break;}
                catch(Exception e){lock(gate)errors[work.Id]="Save discovery needs attention. "+e.Message;}
                lock(gate)
                {
                    if(disposed)break;
                    if(work.Again){work.Again=false;work.Signal.Wait(0);continue;}
                    if(!work.Running){workers.Remove(work.Id);break;}
                }
                await work.Signal.WaitAsync(interval,work.Stop.Token).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException)when(work.Stop.IsCancellationRequested){}
        finally
        {
            lock(gate){if(workers.TryGetValue(work.Id,out var current)&&ReferenceEquals(current,work))workers.Remove(work.Id);}
            work.Stop.Dispose();work.Signal.Dispose();
        }
    }
    public Task FlushAsync(Guid id){lock(gate)return workers.TryGetValue(id,out var work)?work.Task:Task.CompletedTask;}
    public ValueTask DisposeAsync()
    {
        lock(gate)
        {
            if(drain is not null)return new(drain);
            disposed=true;signals.StateChanged-=StateChanged;signals.SessionEnded-=SessionEnded;
            foreach(var work in workers.Values)work.Stop.Cancel();
            drain=Task.WhenAll(workers.Values.Select(w=>w.Task));return new(drain);
        }
    }
    private sealed class Work(Guid id)
    {
        public Guid Id{get;}=id;public bool Running;public bool Again;
        public readonly CancellationTokenSource Stop=new();public readonly SemaphoreSlim Signal=new(0,1);public Task Task=Task.CompletedTask;
    }
}