using System.Windows;

namespace GameVault.App;

// Settings: "Show a small startup window while GameVault loads" (issue #28). The startup code reads this switch straight from settings.json.
public partial class MainWindow
{
    private async void SplashPreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || busy || settingsReadOnly || stoppingObservation || quitPreparing) return;
        var show = StartupSplashChoice.IsChecked == true;
        if (show == settings.ShowSplash) return;
        busy = true; UpdateButtons();
        try
        {
            var updated = settings with { ShowSplash = show };
            await store.SaveSettingsAsync(updated);
            settings = updated;
            OperationNotice.Text = show ? "The startup window will show next time." : "The startup window is off.";
        }
        catch (Exception error) when (IsExpected(error))
        {
            initializing = true; StartupSplashChoice.IsChecked = settings.ShowSplash; initializing = false;
            OperationNotice.Text = "Could not save the startup window setting. " + error.Message;
        }
        finally { EndOperation(); }
    }
}
