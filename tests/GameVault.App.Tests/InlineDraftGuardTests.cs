namespace GameVault.App.Tests;
public sealed class InlineDraftGuardTests
{
    [Fact] public async Task CleanAndBusyFormsDoNotAskToDiscard()
    {
        var guard=new InlineDraftGuard("saved");
        Assert.True(await guard.CanLeaveAsync(false,"saved",()=>throw new Exception("No prompt for clean state")));
        Assert.False(await guard.CanLeaveAsync(true,"changed",()=>throw new Exception("No prompt during save")));
    }
    [Fact] public async Task CancellationRetainsDirtyStateUntilExplicitSave()
    {
        var guard=new InlineDraftGuard("saved");var prompts=0;
        Task<bool> Cancel(){prompts++;return Task.FromResult(false);}
        Assert.False(await guard.CanLeaveAsync(false,"draft",Cancel));
        Assert.False(await guard.CanLeaveAsync(false,"draft",Cancel));Assert.Equal(2,prompts);
        guard.AcceptSaved("draft");
        Assert.True(await guard.CanLeaveAsync(false,"draft",()=>throw new Exception("Successful save is the new baseline")));
    }
    [Fact] public async Task RepeatedNavigationCannotQueueTwoDiscardPrompts()
    {
        var guard=new InlineDraftGuard("saved");var choice=new TaskCompletionSource<bool>();
        var first=guard.CanLeaveAsync(false,"draft",()=>choice.Task);
        Assert.False(first.IsCompleted);
        Assert.False(await guard.CanLeaveAsync(false,"draft",()=>throw new Exception("Only one active prompt")));
        choice.SetResult(false);Assert.False(await first);
        Assert.True(await guard.CanLeaveAsync(false,"draft",()=>Task.FromResult(true)));
    }
}
