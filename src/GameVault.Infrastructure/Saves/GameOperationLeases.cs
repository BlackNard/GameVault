using System.Collections.Concurrent;
namespace GameVault.Infrastructure.Saves;

// Shared by launch and backup, including independently constructed service instances.
public sealed class GameOperationLeases
{
    private static readonly ConcurrentDictionary<Guid,byte> Active=[];
    public IDisposable? TryAcquire(Guid id)=>Active.TryAdd(id,0)?new Lease(id):null;
    public bool IsBusy(Guid id)=>Active.ContainsKey(id);
    private sealed class Lease(Guid id):IDisposable
    {
        private int released;
        public void Dispose(){if(Interlocked.Exchange(ref released,1)==0)Active.TryRemove(id,out _);}
    }
}