namespace GameVault.App;
/// <summary>Keeps the saved baseline until persistence succeeds and permits one discard decision at a time.</summary>
internal sealed class InlineDraftGuard(string savedState)
{
    private string savedState = savedState;
    private bool asking;
    public void AcceptSaved(string state) => savedState = state;
    public async Task<bool> CanLeaveAsync(bool isBusy,string currentState,Func<Task<bool>> confirmDiscard)
    {
        if(isBusy || asking) return false;
        if(currentState == savedState) return true;
        asking = true;
        try { return await confirmDiscard(); }
        finally { asking = false; }
    }
}
