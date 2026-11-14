using GameVault.Infrastructure.Library;
namespace GameVault.Tests;
public sealed class ThumbnailCacheTests
{
    [Fact]
    public async Task ReusesValuesAndEvictsLeastRecentlyUsedWithinBudget()
    {
        var cache = new BoundedAsyncCache<byte[]>(8, 2, x => x.Length);
        var calls = 0;
        Task<byte[]> Load() { calls++; return Task.FromResult(new byte[4]); }
        var first = await cache.GetAsync("a", Load);
        await cache.GetAsync("b", Load);
        Assert.Same(first, await cache.GetAsync("a", Load));
        await cache.GetAsync("c", Load);
        await cache.GetAsync("b", Load);
        Assert.Equal(4, calls);
        Assert.Equal(8, cache.RetainedBytes);
    }
    [Fact]
    public async Task DeduplicatesRequestsWithoutLettingOneCancelledCallerCancelAnother()
    {
        var cache = new BoundedAsyncCache<byte[]>(8, 2, x => x.Length);
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task<byte[]> Load() { Interlocked.Increment(ref calls); return completion.Task; }
        using var cancel = new CancellationTokenSource();
        var first = cache.GetAsync("a", Load, cancel.Token);
        var second = cache.GetAsync("a", Load);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        completion.SetResult(new byte[4]);
        Assert.Equal(4, (await second).Length);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task BoundsConcurrentLoadsAndDoesNotCacheOversizedValues()
    {
        var cache = new BoundedAsyncCache<byte[]>(8, 2, x => x.Length);
        var active = 0; var maximum = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<byte[]> Load() {
            var count = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref maximum, Math.Max(maximum, count));
            if (count == 2) entered.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref active); return new byte[9];
        }
        var tasks = Enumerable.Range(0, 10).Select(i => cache.GetAsync(i.ToString(), Load)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, active);
        release.SetResult(); await Task.WhenAll(tasks);
        Assert.Equal(2, maximum); Assert.Equal(0, cache.RetainedBytes);
    }
    [Fact]
    public async Task InvalidatedInFlightResultCannotRepopulateCache()
    {
        var cache = new BoundedAsyncCache<byte[]>(8, 2, x => x.Length);
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = cache.GetAsync("a", () => completion.Task);
        cache.Invalidate("a");
        completion.SetResult(new byte[4]); await old;
        Assert.Equal(0, cache.RetainedBytes);
        var fresh = new byte[3];
        Assert.Same(fresh, await cache.GetAsync("a", () => Task.FromResult(fresh)));
    }
    [Fact]
    public async Task LastCancelledWaiterSkipsQueuedDecode()
    {
        var cache = new BoundedAsyncCache<byte[]>(8, 1, x => x.Length);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = cache.GetAsync("first", async () => { started.SetResult(); await release.Task; return new byte[1]; });
        await started.Task;
        using var cancel = new CancellationTokenSource();
        var calls = 0;
        var queued = cache.GetAsync("queued", () => { Interlocked.Increment(ref calls); return Task.FromResult(new byte[1]); }, cancel.Token);
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        release.SetResult(); await first;
        await cache.GetAsync("barrier", () => Task.FromResult(new byte[1]));
        Assert.Equal(0, calls);
    }}
