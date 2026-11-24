using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure.Diagnostics;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Updates;

namespace GameVault.App;

/// <summary>The running app's own version as written by the build (for example 0.1.0-preview.2).</summary>
internal static class AppVersionInfo
{
    public static string Current
    {
        get
        {
            var assembly = typeof(AppVersionInfo).Assembly;
            var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (text is not null && text.Contains('+')) text = text[..text.IndexOf('+')];
            return !string.IsNullOrWhiteSpace(text) ? text : assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}

// "Tell the player when a newer version is available" (issue #38): notify only, one anonymous request, nothing is downloaded or installed.
public partial class MainWindow
{
    private UpdateInfo? availableUpdate;
    private bool checkingForUpdates;

    private void ShowUpdateSettingsState(string status, bool available)
    {
        UpdateStatusText.Text = status;
        OpenReleasePageButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        UpdateNowButton.Visibility = available && availableUpdate is { } update && CanUpdateInPlace(update) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Update now is offered only for the copy the installer put on this PC and only when the release carries a verifiable installer.</summary>
    private static bool CanUpdateInPlace(UpdateInfo update) => update.Installer is not null && InstalledCopy.IsThisTheInstalledCopy();

    private async void UpdateNowClicked(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is { } update) await BeginUpdateAsync(update);
    }

    private bool updatingInPlace;

    /// <summary>
    /// Update in place after the player confirms: download and verify the release's installer, quit GameVault normally and let the installer replace the files
    /// over the installed copy (Data and Profiles are not part of it), then it opens the new version. Any failure leaves the current version running.
    /// </summary>
    private async Task BeginUpdateAsync(UpdateInfo update)
    {
        if (!CanUpdateInPlace(update)) { OpenReleasePage(update); return; }
        if (updatingInPlace || busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this))
        {
            ShowToast("GameVault is busy", "Finish what you are doing, then choose Update now again.", true);
            return;
        }
        var megabytes = Math.Max(1, update.Installer!.Size / (1024 * 1024));
        var answer = await InlinePrompts.ShowAsync(this,
            $"GameVault {update.Version} (about {megabytes} MB) will be downloaded from GitHub, checked against its published checksum and installed over this copy.\n\n" +
            "GameVault closes while it updates and opens again afterwards. Your games, saves, backups, notes and settings stay exactly where they are.\n\nUpdate now?",
            "Update GameVault", MessageBoxButton.YesNo, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;
        updatingInPlace = true;
        UpdateNowButton.IsEnabled = CheckForUpdatesButton.IsEnabled = false;
        var progress = new Progress<DownloadProgress>(p => UpdateStatusText.Text = p.Total > 0 ? $"Downloading GameVault {update.Version}… {p.Received * 100 / p.Total}%" : "Downloading…");
        try
        {
            string installer;
            using (var downloader = new UpdateDownloader())
                installer = await Task.Run(() => downloader.DownloadAsync(update, App.UpdatesFolder, progress, CancellationToken.None));
            UpdateStatusText.Text = "Downloaded and checked. Closing GameVault to install the update…";
            App.PendingUpdateInstaller = installer;
            var result = await lifetime.QuitAsync();
            if (result != WindowCloseResult.Quit)
            {
                App.PendingUpdateInstaller = null;     // GameVault stayed open: nothing may start later by surprise
                UpdateStatusText.Text = "The update was downloaded but GameVault stayed open (a game or a backup is still running). Choose Update now again when it has finished.";
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App.PendingUpdateInstaller = null;
            AppLog.Warn("In-place update failed: " + error.Message);
            UpdateStatusText.Text = "The update was not installed: " + error.Message.TrimEnd('.') + ". Your current version is untouched.";
            ShowToast("The update was not installed", error.Message + " Your current version is untouched.", true);
        }
        finally
        {
            updatingInPlace = false;
            UpdateNowButton.IsEnabled = CheckForUpdatesButton.IsEnabled = true;
        }
    }

    /// <summary>Starts a few seconds after the window is up, at most once per <see cref="UpdateCheckRules.MinimumInterval"/>, and only when the player allows it.</summary>
    private async void StartUpdateCheckLater(object? sender, RoutedEventArgs e)
    {
        Loaded -= StartUpdateCheckLater;
        ShowUpdateSettingsState($"You have version {AppVersionInfo.Current}.", false);
        ShowCachedUpdateNotice();
        _ = Task.Run(() => UpdateDownloader.CleanOld(App.UpdatesFolder, TimeSpan.FromDays(2)));
        if (!UpdateCheckRules.ShouldCheckAtStart(settings.CheckForUpdates, UpdateCheckStamp.Read(paths.Root), DateTimeOffset.UtcNow)) return;
        try { await Task.Delay(TimeSpan.FromSeconds(8)); }
        catch (TaskCanceledException) { return; }
        if (stoppingObservation || quitPreparing) return;
        await CheckForUpdatesAsync(manual: false);
    }

    /// <summary>After a restart the notice comes back from the cache (the start-up check runs at most every 12 hours): newer than this app and not dismissed.</summary>
    private void ShowCachedUpdateNotice()
    {
        var cached = UpdateNoticeCache.Read(paths.Root);
        if (cached.Latest is not { } latest || !ReleaseVersion.TryParse(AppVersionInfo.Current, out var running) || !ReleaseVersion.TryParse(latest.Version, out var version) ||
            version <= running || !UpdateChecker.IsReleaseLink(latest.Url)) return;
        availableUpdate = latest;
        ShowUpdateSettingsState($"Version {latest.Version} is available (you have {AppVersionInfo.Current}).", true);
        ShowUpdateNotice(latest);
    }

    private async void CheckForUpdatesClicked(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (checkingForUpdates) return;
        checkingForUpdates = true;
        CheckForUpdatesButton.IsEnabled = false;
        if (manual) UpdateStatusText.Text = "Checking…";
        try
        {
            UpdateCheckResult result;
            using (var http = new PoliteHttp("GitHub"))
                result = await Task.Run(() => new UpdateChecker(http).CheckAsync(AppVersionInfo.Current, CancellationToken.None));
            if (result.Status != UpdateStatus.Failed) { UpdateCheckStamp.Write(paths.Root, DateTimeOffset.UtcNow); UpdateNoticeCache.WriteLatest(paths.Root, result.Update); }
            else AppLog.Warn("Update check failed: " + result.Message);
            availableUpdate = result.Update;
            if (result.Status == UpdateStatus.Failed && !manual) { ShowUpdateSettingsState($"You have version {AppVersionInfo.Current}.", false); return; }   // a quiet start-up check says nothing when offline
            ShowUpdateSettingsState(result.Describe(), result.Status == UpdateStatus.Available);
            ShowUpdateNotice(result.Status == UpdateStatus.Available ? result.Update : null);        // a quiet entry in the bell, no toast
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            AppLog.Warn("Update check failed: " + error.Message);
            if (manual) ShowUpdateSettingsState("Could not check for updates: " + error.Message.TrimEnd('.') + ".", false);
        }
        finally
        {
            checkingForUpdates = false;
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private void OpenReleasePageClicked(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is { } update) OpenReleasePage(update);
    }

    /// <summary>Opens the release page in the player's browser, only for a link that points at this repository's releases on github.com.</summary>
    private void OpenReleasePage(UpdateInfo update)
    {
        if (!UpdateChecker.IsReleaseLink(update.Url)) { ShowToast("Could not open the release page", "The link was not a GameVault release link.", true); return; }
        try { Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowToast("Could not open the release page", error.Message, true); }
    }

    private async void UpdatePreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || busy || settingsReadOnly || stoppingObservation || quitPreparing) return;
        var enabled = CheckForUpdatesChoice.IsChecked == true;
        if (enabled == settings.CheckForUpdates) return;
        busy = true; UpdateButtons();
        try
        {
            var updated = settings with { CheckForUpdates = enabled };
            await store.SaveSettingsAsync(updated);
            settings = updated;
            OperationNotice.Text = enabled ? "GameVault will check for new versions when it starts." : "GameVault will not check for new versions on its own.";
        }
        catch (Exception error) when (IsExpected(error))
        {
            initializing = true; CheckForUpdatesChoice.IsChecked = settings.CheckForUpdates; initializing = false;
            OperationNotice.Text = "Could not save the update setting. " + error.Message;
        }
        finally { EndOperation(); }
    }
}
