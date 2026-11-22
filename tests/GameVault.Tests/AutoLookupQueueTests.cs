using GameVault.Core;

namespace GameVault.Tests;

public sealed class AutoLookupQueueTests
{
    private static AutoLookupQueue.Job Job(int n, bool urgent = false) => new(new Guid(n, 0, 0, new byte[8]), "Game " + n, !urgent, urgent);

    private static string[] Drain(AutoLookupQueue queue)
    {
        var names = new List<string>();
        while (queue.TryDequeue(out var job)) names.Add(job.Name);
        return [.. names];
    }

    [Fact]
    public void A_game_added_while_the_library_pass_is_running_goes_to_the_front_instead_of_being_dropped()
    {
        var queue = new AutoLookupQueue();
        queue.Enqueue([Job(1), Job(2), Job(3)]);                                    // the startup pass over the library
        Assert.True(queue.TryDequeue(out var running));                             // game 1 is being looked up right now
        Assert.Equal("Game 1", running.Name);
        queue.Enqueue([Job(9, urgent: true)]);                                      // the owner adds a game meanwhile
        Assert.Equal(new[] { "Game 9", "Game 2", "Game 3" }, Drain(queue));          // it is next, nothing is lost
    }

    [Fact]
    public void Several_new_games_keep_their_order_ahead_of_the_waiting_ones()
    {
        var queue = new AutoLookupQueue();
        queue.Enqueue([Job(1), Job(2)]);
        queue.Enqueue([Job(7, true), Job(8, true)]);
        Assert.Equal(new[] { "Game 7", "Game 8", "Game 1", "Game 2" }, Drain(queue));
    }

    [Fact]
    public void A_game_is_queued_once_and_an_urgent_request_moves_a_waiting_game_forward_with_a_full_lookup()
    {
        var queue = new AutoLookupQueue();
        queue.Enqueue([Job(1), Job(2), Job(3)]);
        queue.Enqueue([Job(2), Job(3)]);                                            // asking again changes nothing
        Assert.Equal(3, queue.Count);
        queue.Enqueue([Job(3, urgent: true)]);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(("Game 3", true, false), (first.Name, first.Urgent, first.OnlyNeverLookedUp));   // moved up and no longer "only if never looked up"
        Assert.Equal(new[] { "Game 1", "Game 2" }, Drain(queue));
    }

    [Fact]
    public void An_empty_queue_and_clearing_behave()
    {
        var queue = new AutoLookupQueue();
        Assert.False(queue.TryDequeue(out _));
        queue.Enqueue([Job(1), Job(2)]);
        queue.Clear();
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public async Task Concurrent_requests_never_lose_a_game()
    {
        var queue = new AutoLookupQueue();
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => queue.Enqueue([Job(i, urgent: i % 5 == 0)]))));
        Assert.Equal(200, queue.Count);
        Assert.Equal(200, Drain(queue).Distinct().Count());
    }
}
