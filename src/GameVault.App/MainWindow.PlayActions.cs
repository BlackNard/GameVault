using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GameVault.Core;
using GameVault.Infrastructure.Launching;

namespace GameVault.App;

// The Play menu for games with several play actions, and hook results (ticket #87).
public partial class MainWindow
{
    private PlayAction[] currentPlayActions = [];
    private Guid? currentDefaultAction;
    private Guid playActionsFor;

    private async Task RefreshPlayActionsAsync(LibraryItemView? item)
    {
        if (item?.Game.Profile is null) { PlayMenuButton.Visibility = Visibility.Collapsed; currentPlayActions = []; return; }
        var id = item.Game.Id;
        playActionsFor = id;
        PlayAction[] actions = [];
        Guid? defaultId = null;
        try
        {
            var loaded = await launchSettings.LoadAsync(id);
            actions = loaded.Value?.PlayActions ?? [];
            defaultId = loaded.Value?.DefaultActionId;
        }
        catch (Exception error) when (IsExpected(error)) { }
        if (playActionsFor != id) return;
        currentPlayActions = actions;
        currentDefaultAction = defaultId;
        PlayMenuButton.Visibility = actions.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.ToolTip = LaunchRules.DefaultAction(new LaunchSettings(1, id, null, actions, defaultId)) is { } def ? "Play: " + def.Name : "Play";
    }

    private async void PlayMenuClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PlayMenuButton, Placement = PlacementMode.Bottom };
        var game = new MenuItem { Header = "Game executable", IsChecked = currentDefaultAction is null };
        game.Click += async (_, _) => await PlayWithAsync(Guid.Empty);
        menu.Items.Add(game);
        foreach (var action in currentPlayActions)
        {
            var captured = action.Id;
            var entry = new MenuItem { Header = action.Name + (action.RunAsAdministrator ? " (administrator)" : ""), IsChecked = action.Id == currentDefaultAction };
            entry.Click += async (_, _) => await PlayWithAsync(captured);
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
        await Task.CompletedTask;
    }

    private async Task PlayWithAsync(Guid? actionId)
    {
        if (busy || editorOpen || stoppingObservation || quitPreparing || GameList.SelectedItem is not LibraryItemView { Game.IsReadOnly: false, Game.Profile: { IsArchived: false } } item) return;
        try
        {
            var run = launches.RunAsync(item.Game.Id, actionId);
            ShowSelection();
            await run;
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = error.Message; }
    }

    private void HookCompleted(object? sender, LaunchHookResult result)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            var name = libraryRows.TryGetValue(result.GameId, out var item) ? item.Name : "Game";
            GameVault.Infrastructure.Diagnostics.AppLog.Info($"{result.Outcome.Label} hook: {(result.Outcome.Succeeded ? "ok" : "failed")}: {result.Outcome.Message}");
            if (!result.Outcome.Succeeded) ShowToast($"{result.Outcome.Label} program did not run cleanly", $"{name}: {result.Outcome.Message} The game and playtime tracking carry on.", true);
        });
    }
}
