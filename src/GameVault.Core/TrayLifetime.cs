namespace GameVault.Core;

public enum WindowCloseResult { Hidden, StayedOpen, Quit }
public sealed class TrayLifetime(Action hide,Action reopen,Func<Task<bool>> prepareQuit,Func<Task> quit)
{
    private readonly object gate=new();
    private Task<WindowCloseResult>? quitting;
    private bool finished;
    public Task<WindowCloseResult> CloseAsync(bool closeToTray,bool trayAvailable)
    {
        lock(gate)
        {
            if(finished)return Task.FromResult(WindowCloseResult.Quit);
            if(quitting is not null)return quitting;
            if(closeToTray)
            {
                if(trayAvailable){hide();return Task.FromResult(WindowCloseResult.Hidden);}
                reopen();return Task.FromResult(WindowCloseResult.StayedOpen);
            }
        }
        return QuitAsync();
    }
    public void Reopen(){lock(gate){if(!finished)reopen();}}
    public Task<WindowCloseResult> QuitAsync()
    {
        lock(gate)
        {
            if(finished)return Task.FromResult(WindowCloseResult.Quit);
            if(quitting is not null)return quitting;
            var completion=new TaskCompletionSource<WindowCloseResult>(TaskCreationOptions.RunContinuationsAsynchronously);quitting=completion.Task;
            _=Run(completion);return completion.Task;
        }
    }
    private async Task Run(TaskCompletionSource<WindowCloseResult> completion)
    {
        try
        {
            if(!await prepareQuit()){completion.TrySetResult(WindowCloseResult.StayedOpen);return;}
            await quit();lock(gate)finished=true;completion.TrySetResult(WindowCloseResult.Quit);
        }
        catch(Exception error){completion.TrySetException(error);}
        finally{lock(gate)quitting=null;}
    }
}