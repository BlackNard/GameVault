namespace GameVault.App;

/// <summary>Routes quit through the active editor's discard/save guard and normal completion path.</summary>
internal sealed class InlineEditorSession(Func<Task<bool>> canLeave)
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource editorFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool asking;
    public Task<bool> Completion => completion.Task;
    public void Complete(bool saved) => completion.TrySetResult(saved);
    public void EditorFinished() => editorFinished.TrySetResult();

    public async Task<bool> TryLeaveAsync()
    {
        if (asking || completion.Task.IsCompleted) return false;
        asking = true;
        try
        {
            if (!await canLeave() || !completion.TrySetResult(false)) return false;
            await editorFinished.Task;
            return true;
        }
        finally { asking = false; }
    }
}
