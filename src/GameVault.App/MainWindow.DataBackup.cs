using System.Diagnostics;
using System.IO;
using System.Windows;
using GameVault.Infrastructure.Storage;

namespace GameVault.App;

// One-step backup and restore of GameVault's own data (ticket #89).
public partial class MainWindow
{
    private bool DataBackupBlocked()
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this)) { ShowToast("GameVault is busy", "Finish what you are doing first, then try again.", true); return true; }
        if (launches.GetActiveStates().Length > 0 || items.Any(i => backups.IsWorking(i.Game.Id)))
        {
            ShowToast("Games or backups are still running", "Close running games and wait for game backups to finish first.", true);
            return true;
        }
        return false;
    }

    private static string Size(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
    };

    private async void BackUpDataClicked(object sender, RoutedEventArgs e)
    {
        if (DataBackupBlocked()) return;
        var bulky = DataBackupBulky.IsChecked == true;
        busy = true; UpdateButtons();
        try
        {
            var estimate = await Task.Run(() => DataArchive.Estimate(paths, bulky));
            if (await InlinePrompts.ShowAsync(this, $"This will back up {estimate.FileCount} file(s), about {Size(estimate.Bytes)} before compression{(bulky ? "" : ", without game backups and recorded media")}.\n\nNext you choose the folder to save the zip in.", "Back up GameVault data", MessageBoxButton.OKCancel, MessageBoxResult.OK) != MessageBoxResult.OK) return;
            var folder = NativePickers.Folder(this, "Choose where to save the GameVault data backup");
            if (folder is null) return;
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
            var zip = await Task.Run(() => DataArchive.CreateAsync(paths, folder, bulky, version));
            ShowToast("GameVault data backed up", Path.GetFileName(zip), false, "Show in folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true }));
        }
        catch (Exception error) when (IsExpected(error) || error is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            ShowToast("The data backup failed", error.Message, true);
        }
        finally { EndOperation(); }
    }

    private async void RestoreDataClicked(object sender, RoutedEventArgs e)
    {
        if (DataBackupBlocked()) return;
        var file = NativePickers.File(this, "Choose a GameVault data backup", "GameVault data backup (*.zip)|*.zip");
        if (file is null) return;
        busy = true; UpdateButtons();
        try
        {
            DataArchiveInfo info;
            try { info = await Task.Run(() => DataArchive.ValidateAsync(file)); }
            catch (Exception error) when (IsExpected(error))
            {
                ShowToast("That backup cannot be used", error.Message + " Nothing was changed.", true);
                return;
            }
            var created = info.Manifest.CreatedAt.ToLocalTime().ToString("d MMM yyyy HH:mm");
            var message = $"This backup was made on {created} and holds {info.Manifest.Files.Length} file(s){(info.Manifest.IncludesBulky ? "" : ", without game backups and recorded media")}.\n\n" +
                "Restoring REPLACES your whole library with it. Your current Data and Profiles folders are not deleted: they are moved into a \"Replaced-…\" folder next to GameVault.exe, including their game backups and recordings.\n\nGameVault will close when the restore is finished. Open it again afterwards.\n\nRestore now?";
            if (await InlinePrompts.ShowAsync(this, message, "Restore GameVault data?", MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var result = await Task.Run(() => DataArchive.RestoreAsync(paths, file));
            await InlinePrompts.ShowAsync(this, $"Restored {result.FileCount} file(s). The previous library was kept in:\n{result.ReplacedFolder}\n\nGameVault will close now. Open it again to use the restored library.", "Restore finished", MessageBoxButton.OK, MessageBoxResult.OK);
            EndOperation();
            RequestQuit();
            return;
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowToast("The restore failed. Your library was put back as it was.", error.Message, true);
        }
        finally { EndOperation(); }
    }
}
