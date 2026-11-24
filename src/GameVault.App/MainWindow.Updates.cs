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
    }

    /// <summary>Starts a few seconds after the window is up, at most once per <see cref="UpdateCheckRules.MinimumInterval"/>, and only when the player allows it.</summary>
    private async void StartUpdateCheckLater(object? sender, RoutedEventArgs e)
    {
        Loaded -= StartUpdateCheckLater;
        ShowUpdateSettingsState($"You have version {AppVersionInfo.Current}.", false);
        ShowCachedUpdateNotice();
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
