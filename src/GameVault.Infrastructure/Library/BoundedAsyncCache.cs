namespace GameVault.Infrastructure.Library;

/// <summary>Shared, byte-bounded LRU with deduplicated background loads. Cancellation belongs to each caller.</summary>
public sealed class BoundedAsyncCache<T> where T : class
{
    private sealed record Entry(T Value, long Bytes, LinkedListNode<string> Node);
    private sealed class Request
    {
        public readonly TaskCompletionSource<T> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly CancellationTokenSource Cancel = new();
        public int Waiters;
    }
    private readonly object sync = new();
    private readonly long budget;
    private readonly Func<T, long> measure;
    private readonly SemaphoreSlim loaders;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Request> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> recent = new();
    private long bytes;
    public long RetainedBytes { get { lock (sync) return bytes; } }
    public int RetainedCount { get { lock (sync) return entries.Count; } }
    public BoundedAsyncCache(long budgetBytes, int concurrency, Func<T, long> measure)
    {
        if (budgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        if (concurrency <= 0) throw new ArgumentOutOfRangeException(nameof(concurrency));
        this.budget = budgetBytes; this.measure = measure ?? throw new ArgumentNullException(nameof(measure));
        loaders = new(concurrency, concurrency);
    }
    public Task<T> GetAsync(string key, Func<Task<T>> load, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Request request;
        lock (sync)
        {
            if (entries.TryGetValue(key, out var hit)) {
                recent.Remove(hit.Node); recent.AddFirst(hit.Node); return Task.FromResult(hit.Value);
            }
            if (!pending.TryGetValue(key, out request!)) {
                request = new(); pending.Add(key, request);
                var captured = request;
                _ = Task.Run(() => LoadAsync(key, load, captured));
            }
            request.Waiters++;
        }
        return AwaitCallerAsync(key, request, cancellationToken);
    }
    private async Task<T> AwaitCallerAsync(string key, Request request, CancellationToken cancellationToken)
    {
        try { return await request.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally {
            lock (sync) {
                request.Waiters--;
                if (request.Waiters == 0 && !request.Completion.Task.IsCompleted) {
                    if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, request)) pending.Remove(key);
                    request.Cancel.Cancel();
                }
            }
        }
    }
    public void Invalidate(string key)
    {
        lock (sync) { Remove(key); pending.Remove(key); }
    }
    private async Task LoadAsync(string key, Func<Task<T>> load, Request request)
    {
        try {
            await loaders.WaitAsync(request.Cancel.Token).ConfigureAwait(false);
            T value;
            try { request.Cancel.Token.ThrowIfCancellationRequested(); value = await load().ConfigureAwait(false); } finally { loaders.Release(); }
            var cost = measure(value);
            if (cost < 0) throw new InvalidOperationException("Cache size cannot be negative.");
            lock (sync) {
                if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, request)) {
                    pending.Remove(key);
                    if (cost <= budget) {
                        while (bytes > budget - cost && recent.Last is not null) Remove(recent.Last.Value);
                        var node = recent.AddFirst(key); entries.Add(key, new(value, cost, node)); bytes += cost;
                    }
                }
            }
            request.Completion.TrySetResult(value);
        } catch (Exception error) {
            lock (sync) { if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, request)) pending.Remove(key); }
            if (error is OperationCanceledException || request.Cancel.IsCancellationRequested) request.Completion.TrySetCanceled();
            else request.Completion.TrySetException(error);
        } finally { request.Cancel.Dispose();
        }
    }
    private void Remove(string key)
    {
        if (!entries.Remove(key, out var entry)) return;
        recent.Remove(entry.Node); bytes -= entry.Bytes;
    }
}
