using System.Windows;
using GameVault.Core;

namespace GameVault.App;

// First-run welcome (issue #26): replaces the empty library message for a library that has never had a game.
public partial class MainWindow
{
    private WelcomeView? welcome;

    private void UpdateWelcome(bool showingRemoved)
    {
        var show = WelcomeRules.ShouldShow(items.Length, showingRemoved, settings.WelcomeDismissed);
        if (show && welcome is null)
        {
            welcome = new WelcomeView(settings.Theme, embedded: true);
            welcome.ActionChosen += async (_, action) => await RunWelcomeActionAsync(action);
            WelcomeHost.Content = welcome;
        }
        WelcomeHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) EmptyState.Visibility = Visibility.Collapsed;
    }

    private async Task RunWelcomeActionAsync(WelcomeAction action)
    {
        switch (action)
        {
            case WelcomeAction.AddGame: AddGame(this, new RoutedEventArgs()); break;
            case WelcomeAction.ImportFromStores: AddFromStoresClicked(this, new RoutedEventArgs()); break;
            case WelcomeAction.ImportOldFolder: ImportOldFolderClicked(this, new RoutedEventArgs()); break;
            case WelcomeAction.ChooseTheme: await OpenThemePickerAsync(); break;
            case WelcomeAction.DownloadSaveDatabase: DownloadSaveDatabaseClicked(this, new RoutedEventArgs()); break;
            case WelcomeAction.Skip: await DismissWelcomeAsync(); break;
        }
    }

    /// <summary>Hides the welcome for good. Settings that cannot be saved (read-only) only hide it for this run.</summary>
    private async Task DismissWelcomeAsync()
    {
        WelcomeHost.Visibility = Visibility.Collapsed;
        if (settings.WelcomeDismissed) { ApplyFilters(null); return; }
        var updated = settings with { WelcomeDismissed = true };
        if (!settingsReadOnly)
        {
            try { await store.SaveSettingsAsync(updated); }
            catch (Exception error) when (IsExpected(error)) { ShowToast("Could not remember that", error.Message, true); return; }
        }
        settings = updated;
        ApplyFilters(null);
    }

    /// <summary>Command palette: "Show the welcome". Opens the same content as a drawer and runs the chosen action once it is closed.</summary>
    private async Task ShowWelcomeDrawerAsync()
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this)) return;
        editorOpen = true; UpdateButtons();
        WelcomeAction? chosen = null;
        try
        {
            var view = new WelcomeView(settings.Theme, embedded: false);
            view.ActionChosen += (_, action) => chosen = action;
            await ShowInlineEditorAsync(view, h => view.Completed += h, h => view.Completed -= h);
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not open the welcome", error.Message, true); }
        finally { editorOpen = false; CompleteInlineEditor(); UpdateButtons(); ContinueDeferredQuit(); }
        if (chosen is { } action) await RunWelcomeActionAsync(action);
    }
}
