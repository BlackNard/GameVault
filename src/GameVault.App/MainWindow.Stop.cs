using System.Windows;
using GameVault.Core;

namespace GameVault.App;

// Stop (issue #113): confirm, ask the game to close, and force it only after a second yes.
public partial class MainWindow
{
    private readonly HashSet<Guid> stoppingGames = [];

    private bool IsRunning(Guid id) => launches.GetState(id).Phase == LaunchPhase.Running;

    private async void StopNowPlayingClicked(object sender, RoutedEventArgs e)
    {
        if (nowPlayingGame is Guid id) await StopGameAsync(id);
    }

    private async Task StopGameAsync(Guid id)
    {
        if (stoppingObservation || quitPreparing || !IsRunning(id) || stoppingGames.Contains(id) || InlinePrompts.IsOpen(this)) return;
        var name = items.FirstOrDefault(i => i.Game.Id == id)?.Name ?? "the game";
        if (await InlinePrompts.ShowAsync(this, "Unsaved progress may be lost.", $"Stop {name}?", MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!stoppingGames.Add(id)) return;
        try
        {
            ShowStopState();
            var result = await Task.Run(() => launches.StopAsync(id, force: false));
            if (result.Outcome == StopOutcome.StillRunning)
            {
                if (await InlinePrompts.ShowAsync(this, $"{name} is still open. Force stop ends it at once; anything not saved is lost.", "Force stop?",
                    MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                result = await Task.Run(() => launches.StopAsync(id, force: true));
            }
            if (result.Outcome is StopOutcome.Failed or StopOutcome.Cancelled) ShowToast($"{name} was not stopped", result.Message, true);
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast($"{name} was not stopped", error.Message, true); }
        finally { stoppingGames.Remove(id); ShowStopState(); }
    }

    private void ShowStopState()
    {
        if (stoppingObservation) return;
        ShowSelection();
        UpdateNowPlaying();
    }
}
