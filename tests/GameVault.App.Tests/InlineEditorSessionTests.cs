namespace GameVault.App.Tests;

public sealed class InlineEditorSessionTests
{
    [Fact]
    public async Task ApprovedDiscardCompletesEditorAndWaitsForHostCleanup()
    {
        var guard = new InlineDraftGuard("saved");
        var session = new InlineEditorSession(() => guard.CanLeaveAsync(false, "draft", () => Task.FromResult(true)));
        var leaving = session.TryLeaveAsync();
        Assert.False(await session.Completion);
        Assert.False(leaving.IsCompleted);
        session.EditorFinished();
        Assert.True(await leaving);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task RejectedOrBusyEditorStaysOpen(bool busy, bool discard)
    {
        var guard = new InlineDraftGuard("saved");
        var session = new InlineEditorSession(() => guard.CanLeaveAsync(busy, "draft", () => Task.FromResult(discard)));
        Assert.False(await session.TryLeaveAsync());
        Assert.False(session.Completion.IsCompleted);
        session.Complete(true);
        Assert.True(await session.Completion);
    }

    [Fact]
    public async Task RepeatedQuitDoesNotQueueAnotherDiscardDecision()
    {
        var decision = new TaskCompletionSource<bool>();
        var prompts = 0;
        var session = new InlineEditorSession(() => { prompts++; return decision.Task; });
        var first = session.TryLeaveAsync();
        Assert.False(await session.TryLeaveAsync());
        Assert.Equal(1, prompts);
        decision.SetResult(false);
        Assert.False(await first);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SaveCompletingDuringLeaveDecisionKeepsItsSavedResult()
    {
        var decision = new TaskCompletionSource<bool>();
        var session = new InlineEditorSession(() => decision.Task);
        var leaving = session.TryLeaveAsync();
        session.Complete(true);
        session.EditorFinished();
        decision.SetResult(true);
        Assert.False(await leaving);
        Assert.True(await session.Completion);
    }
}
