using GameVault.Core;
namespace GameVault.Infrastructure.Saves;

public sealed class BackupCoordinator : IAsyncDisposable
{
    private readonly IGameSessionSignals signals;
    private readonly BackupService backups;
    private readonly Func<Guid,Task> waitForIdle;
    private readonly object gate=new();
    private readonly HashSet<Task> tasks=[];
    private readonly CancellationTokenSource stop=new();
    private bool disposed;
    private Task? drain;
    public BackupCoordinator(IGameSessionSignals signals,BackupService backups,Func<Guid,Task> waitForIdle)
    {this.signals=signals;this.backups=backups;this.waitForIdle=waitForIdle;signals.SessionEnded+=Ended;}
    private void Ended(object? sender,GameSessionEnded ended)
    {
        if(!ended.ConfirmedExit)return;
        lock(gate)
        {
            if(disposed)return;
            var task=Task.Run(async()=>
            {
                try{await waitForIdle(ended.GameId).WaitAsync(stop.Token).ConfigureAwait(false);await backups.BackUpAsync(ended.GameId,true,stop.Token).ConfigureAwait(false);}
                catch(OperationCanceledException)when(stop.IsCancellationRequested){}
            });
            tasks.Add(task);
            _=task.ContinueWith(completed=>{lock(gate)tasks.Remove(completed);},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        }
    }
    public Task FlushAsync(){lock(gate)return Task.WhenAll(tasks.ToArray());}
    public ValueTask DisposeAsync()
    {
        lock(gate)
        {
            if(drain is not null)return new(drain);
            disposed=true;signals.SessionEnded-=Ended;stop.Cancel();drain=Drain(tasks.ToArray());return new(drain);
        }
    }
    private async Task Drain(Task[] pending){try{await Task.WhenAll(pending).ConfigureAwait(false);}finally{stop.Dispose();}}
}