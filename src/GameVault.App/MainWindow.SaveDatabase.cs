using System.Windows;
using GameVault.Infrastructure.Saves;

namespace GameVault.App;

// Optional save-location database (ticket #84): downloaded only when the owner asks, used offline afterwards.
public partial class MainWindow
{
    private readonly SaveLocationDatabase saveLocations;
    private bool downloadingSaveDatabase;

    private void RefreshSaveDatabaseStatus()
    {
        var status = saveLocations.Status();
        SaveDatabaseStatusText.Text = status.Available
            ? $"{status.Games:N0} games known · downloaded {(status.DownloadedAt is { } at ? at.ToLocalTime().ToString("d MMM yyyy") : "earlier")}. Used offline."
            : "Not downloaded. Without it GameVault only uses its built-in rules and your own save folders.";
        DownloadSaveDatabaseButton.Content = status.Available ? "Update the save-location database" : "Download the save-location database";
    }

    private async void DownloadSaveDatabaseClicked(object sender, RoutedEventArgs e)
    {
        if (downloadingSaveDatabase) return;
        downloadingSaveDatabase = true;
        DownloadSaveDatabaseButton.IsEnabled = false;
        SaveDatabaseStatusText.Text = "Downloading (about 18 MB)…";
        try
        {
            var status = await Task.Run(() => saveLocations.DownloadAsync(null, CancellationToken.None));
            ShowToast("Save-location database ready", $"{status.Games:N0} games. Open a game's Saves tab and refresh discovery to use it.");
        }
        catch (Exception error) when (IsExpected(error) || error is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            ShowToast("Could not download the database", error.Message, true);
        }
        finally
        {
            downloadingSaveDatabase = false;
            DownloadSaveDatabaseButton.IsEnabled = true;
            RefreshSaveDatabaseStatus();
        }
    }
}
