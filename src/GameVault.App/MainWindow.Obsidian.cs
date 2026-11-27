using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using GameVault.Infrastructure.Notes;

namespace GameVault.App;

// The Obsidian vault in Settings > Backups and data (issue #80).
public partial class MainWindow
{
    private ObsidianVault? obsidianVault;
    private string? obsidianPath, obsidianKnownId;

    private ObsidianVault Obsidian => obsidianVault ??= new(paths, store);

    private async Task RefreshObsidianAsync()
    {
        try
        {
            var status = await Obsidian.StatusAsync();
            obsidianPath = status.Path;
            var ready = status.State == ObsidianVaultState.Ready;
            obsidianKnownId = ready ? ObsidianApp.KnownVaultId(status.Path!, ObsidianApp.DefaultConfig) : null;
            ObsidianStatusText.Text = status.State switch
            {
                ObsidianVaultState.Ready => obsidianKnownId is null ? ObsidianApp.OpenHint(status.Path!) : status.Path!,
                ObsidianVaultState.Missing => status.Message + (status.Path is null ? "" : "\n" + status.Path),
                _ => "No vault yet. A new one goes in " + Obsidian.DefaultPath
            };
            static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
            ObsidianCreateButton.Visibility = ObsidianElsewhereButton.Visibility = ObsidianExistingButton.Visibility = Show(!ready);
            ObsidianCopyButton.Visibility = ObsidianFolderButton.Visibility = Show(ready);
            ObsidianCopyButton.Content = "Copy vault path";
            ObsidianAppButton.Visibility = Show(obsidianKnownId is not null);
            ObsidianUnlinkButton.Visibility = Show(status.State != ObsidianVaultState.NotLinked);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { ObsidianStatusText.Text = error.Message; }
    }

    private async Task ChangeVaultAsync(Func<Task> change)
    {
        try { await change(); }
        catch (Exception error) when (IsExpected(error) || error is IOException) { ShowToast("The vault was not changed", error.Message, true); }
        await RefreshObsidianAsync();
    }

    private async void ObsidianCreateClicked(object sender, RoutedEventArgs e) => await ChangeVaultAsync(() => Obsidian.CreateAsync(Obsidian.DefaultPath));

    private async void ObsidianElsewhereClicked(object sender, RoutedEventArgs e)
    {
        if (NativePickers.Folder(this, "Choose where to create the vault folder") is not { } folder) return;
        await ChangeVaultAsync(() => Obsidian.CreateAsync(Path.Combine(folder, "GameVault Notes")));
    }

    private async void ObsidianExistingClicked(object sender, RoutedEventArgs e)
    {
        if (NativePickers.Folder(this, "Choose your Obsidian vault") is not { } folder) return;
        await ChangeVaultAsync(() => Obsidian.UseExistingAsync(folder));
    }

    private async void ObsidianUnlinkClicked(object sender, RoutedEventArgs e) => await ChangeVaultAsync(Obsidian.UnlinkAsync);

    private void ObsidianCopyClicked(object sender, RoutedEventArgs e)
    {
        if (obsidianPath is not { } path) return;
        try { Clipboard.SetText(path); ObsidianCopyButton.Content = "Copied"; }
        catch (System.Runtime.InteropServices.ExternalException) { ShowToast("Could not copy the path", "Another program is using the clipboard. Try again.", true); }
    }

    private void ObsidianFolderClicked(object sender, RoutedEventArgs e)
    {
        if (obsidianPath is { } path && Directory.Exists(path)) OpenWithWindows(path);
    }

    private void ObsidianAppClicked(object sender, RoutedEventArgs e)
    {
        if (obsidianKnownId is { } id) OpenWithWindows(ObsidianApp.OpenUri(id));
    }

    private void OpenWithWindows(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException) { ShowToast("Could not open it", error.Message, true); }
    }
}
