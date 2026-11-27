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

// Updates (issues #38 and #42): one anonymous check against the GitHub releases list, then, like a store launcher, the update downloads in the background,
// is verified, and installs over the installed copy when GameVault is closed (or on Restart and update now). Data and Profiles are never touched.
public partial class MainWindow
{
    private UpdateInfo? availableUpdate;
    private PendingUpdate? readyUpdate;
    private bool checkingForUpdates;
    private bool updatingInPlace;

    private static PendingUpdateStore PendingUpdates => new(App.UpdatesFolder);

    /// <summary>Whether updates may install on their own right now (the player's two settings).</summary>
    private bool AutoUpdateOn => settings.AutoUpdate && settings.CheckForUpdates;

    private bool IsGameRunning() => launches.GetActiveStates().Length > 0;

    private void ShowUpdateSettingsState(string status, bool available)
    {
        UpdateStatusText.Text = status;
        OpenReleasePageButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        var offered = readyUpdate is not null || available && availableUpdate is { } update && CanUpdateInPlace(update);
        UpdateNowButton.Visibility = offered ? Visibility.Visible : Visibility.Collapsed;
        UpdateNowButton.Content = readyUpdate is not null ? "Restart and update now" : "Update now";
    }

    /// <summary>In-place updates are offered only for the copy the installer put on this PC and only when the release carries a verifiable installer.</summary>
    private static bool CanUpdateInPlace(UpdateInfo update) => update.Installer is not null && InstalledCopy.IsThisTheInstalledCopy();

    private bool IsReady(UpdateInfo update) => readyUpdate is { } ready && string.Equals(ready.Tag, update.Tag, StringComparison.Ordinal);

    // ---------- start-up ----------

    /// <summary>The check the start-up began before this window existed (null when the player turned checking off).</summary>
    internal Task<UpdateCheckResult>? StartupCheck { get; set; }

    /// <summary>
    /// Shows what the start-up check found (it ran before the window opened, so a newer release is already in the bell); then, while GameVault stays open
    /// (for example in the tray), it looks again every few hours.
    /// </summary>
    private async void StartUpdateCheckLater(object? sender, RoutedEventArgs e)
    {
        Loaded -= StartUpdateCheckLater;
        App.AutoUpdateEnabled = AutoUpdateOn;
        ShowUpdateSettingsState($"You have version {AppVersionInfo.Current}.", false);
        LoadPendingUpdate();
        ShowCachedUpdateNotice();
        var keep = readyUpdate?.InstallerName;
        _ = Task.Run(() => UpdateDownloader.CleanOld(App.UpdatesFolder, TimeSpan.FromDays(2), keep: keep));
        try
        {
            if (settings.CheckForUpdates && !stoppingObservation && !quitPreparing) await CheckForUpdatesAsync(manual: false, StartupCheck);
            while (!stoppingObservation)
            {
                await Task.Delay(AutoUpdateRules.RecheckInterval);
                if (stoppingObservation || quitPreparing) break;
                if (settings.CheckForUpdates) await CheckForUpdatesAsync(manual: false);
            }
        }
        catch (TaskCanceledException) { }
    }

    /// <summary>Reads the update that is waiting from an earlier run: forgets it if it is installed already or damaged, reports one that failed too often.</summary>
    private void LoadPendingUpdate()
    {
        readyUpdate = null;
        var store = PendingUpdates;
        var pending = store.Read();
        if (pending is null) return;
        if (!ReleaseVersion.TryParse(AppVersionInfo.Current, out var running) || AutoUpdateRules.IsInstalledAlready(pending, running)) { store.Clear(); return; }
        if (AutoUpdateRules.HasGivenUp(pending, running))
        {
            var page = new UpdateInfo(pending.Version, pending.Tag, $"https://github.com/{UpdateChecker.DefaultRepository}/releases/tag/{pending.Tag}", true, null);
            PostNotice(new Notice(UpdateIdPrefix + pending.Tag, NoticeKind.Update, $"GameVault {pending.Version} could not be installed automatically",
                "The installer did not finish after several tries. Your current version keeps working; the release page has the download.", DateTimeOffset.UtcNow, true, "Open release page"),
                () => OpenReleasePage(page));
            store.Clear();
            return;
        }
        if (!store.IsIntact(pending)) { store.Clear(); return; }
        readyUpdate = pending;
        availableUpdate ??= new UpdateInfo(pending.Version, pending.Tag, $"https://github.com/{UpdateChecker.DefaultRepository}/releases/tag/{pending.Tag}", true, null);
        ShowUpdateSettingsState($"Version {pending.Version} is ready and installs when you close GameVault (you have {AppVersionInfo.Current}).", true);
        ShowUpdateNotice(availableUpdate);
    }

    /// <summary>After a restart the notice comes back from the cache (the check runs at most every 12 hours at start): newer than this app and not dismissed.</summary>
    private void ShowCachedUpdateNotice()
    {
        var cached = UpdateNoticeCache.Read(paths.Root);
        if (cached.Latest is not { } latest || !ReleaseVersion.TryParse(AppVersionInfo.Current, out var running) || !ReleaseVersion.TryParse(latest.Version, out var version) ||
            version <= running || !UpdateChecker.IsReleaseLink(latest.Url)) return;
        availableUpdate = latest;
        ShowUpdateSettingsState(readyUpdate is { } ready && IsReady(latest)
            ? $"Version {ready.Version} is ready and installs when you close GameVault (you have {AppVersionInfo.Current})."
            : $"Version {latest.Version} is available (you have {AppVersionInfo.Current}).", true);
        ShowUpdateNotice(latest);
    }

    // ---------- checking ----------

    /// <summary>The one request to GitHub's releases list.</summary>
    internal static async Task<UpdateCheckResult> AskForUpdateAsync()
    {
        using var http = new PoliteHttp("GitHub");
        return await Task.Run(() => new UpdateChecker(http).CheckAsync(AppVersionInfo.Current, CancellationToken.None));
    }

    /// <summary>Bringing a running GameVault back is a start to the player, so it looks again if the last look was a while ago.</summary>
    private void CheckWhenReopened()
    {
        if (!stoppingObservation && !quitPreparing && IsLoaded &&
            UpdateCheckRules.ShouldCheckWhenReopened(settings.CheckForUpdates, UpdateCheckStamp.Read(paths.Root), DateTimeOffset.UtcNow))
            _ = CheckForUpdatesAsync(manual: false);
    }

    private async void CheckForUpdatesClicked(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual, Task<UpdateCheckResult>? started = null)
    {
        if (checkingForUpdates) return;
        checkingForUpdates = true;
        CheckForUpdatesButton.IsEnabled = false;
        if (manual) UpdateStatusText.Text = "Checking…";
        try
        {
            var result = await (started ?? AskForUpdateAsync());
            if (result.Status != UpdateStatus.Failed) { UpdateCheckStamp.Write(paths.Root, DateTimeOffset.UtcNow); UpdateNoticeCache.WriteLatest(paths.Root, result.Update); }
            else AppLog.Warn("Update check failed: " + result.Message);
            if (result.Status == UpdateStatus.Failed && !manual) return;        // a quiet check says nothing when offline, and keeps what is already shown
            availableUpdate = result.Update;
            ShowUpdateSettingsState(result.Describe(), result.Status == UpdateStatus.Available);
            ShowUpdateNotice(result.Status == UpdateStatus.Available ? result.Update : null);        // a quiet entry in the bell, no toast
            if (result.Update is { } update) await DownloadInBackgroundAsync(update);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            AppLog.Warn("Update check failed: " + error.Message);
            if (manual) ShowUpdateSettingsState("Could not check for updates: " + error.Message.TrimEnd('.') + ".", false);
        }
        finally
        {
            checkingForUpdates = false;
            CheckForUpdatesButton.IsEnabled = !updatingInPlace;
        }
    }

    // ---------- downloading ----------

    /// <summary>Downloads and verifies the installer without being asked (Steam style) while automatic updates are on and no game is running; it installs later.</summary>
    private async Task DownloadInBackgroundAsync(UpdateInfo update)
    {
        if (updatingInPlace || !AutoUpdateRules.ShouldDownload(AutoUpdateOn, CanUpdateInPlace(update), IsGameRunning(), readyUpdate, update)) return;
        await DownloadAsync(update, quiet: true);
    }

    /// <summary>Downloads, verifies and records the installer. Returns the verified pending update, or null (the reason is shown in Settings).</summary>
    private async Task<PendingUpdate?> DownloadAsync(UpdateInfo update, bool quiet)
    {
        updatingInPlace = true;
        UpdateNowButton.IsEnabled = CheckForUpdatesButton.IsEnabled = false;
        var progress = new Progress<DownloadProgress>(p => UpdateStatusText.Text = p.Total > 0
            ? $"Downloading GameVault {update.Version}{(quiet ? " in the background" : "")}… {p.Received * 100 / p.Total}%" : "Downloading…");
        try
        {
            string installer;
            using (var downloader = new UpdateDownloader())
                installer = await Task.Run(() => downloader.DownloadAsync(update, App.UpdatesFolder, progress, CancellationToken.None));
            var pending = new PendingUpdate(update.Tag, update.Version, Path.GetFileName(installer), PendingUpdateStore.HashFile(installer), new FileInfo(installer).Length, DateTimeOffset.UtcNow);
            PendingUpdates.Write(pending);
            readyUpdate = pending;
            ShowUpdateSettingsState($"Version {update.Version} is ready and installs when you close GameVault (you have {AppVersionInfo.Current}).", true);
            ShowUpdateNotice(update);
            return pending;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warn("Update download failed: " + error.Message);
            ShowUpdateSettingsState("The update could not be downloaded: " + error.Message.TrimEnd('.') + ". Your current version is untouched; GameVault will try again later.", true);
            if (!quiet) ShowToast("The update was not downloaded", error.Message + " Your current version is untouched.", true);
            return null;
        }
        finally
        {
            updatingInPlace = false;
            UpdateNowButton.IsEnabled = CheckForUpdatesButton.IsEnabled = true;
        }
    }

    // ---------- installing ----------

    private async void UpdateNowClicked(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is { } update) await BeginUpdateAsync(update);
    }

    /// <summary>
    /// Update now / Restart and update now: use the verified download (or download it first), quit GameVault normally and let the installer replace the files over the
    /// installed copy and reopen the new version. Any failure leaves the current version running.
    /// </summary>
    private async Task BeginUpdateAsync(UpdateInfo update)
    {
        if (!CanUpdateInPlace(update) && !IsReady(update)) { OpenReleasePage(update); return; }
        if (updatingInPlace) { UpdateStatusText.Text = "The update is still downloading. It will be ready in a moment."; return; }
        if (busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this))
        {
            ShowToast("GameVault is busy", "Finish what you are doing, then choose Update now again.", true);
            return;
        }
        var pending = IsReady(update) ? readyUpdate : await DownloadAsync(update, quiet: false);
        if (pending is null) return;
        if (!PendingUpdates.IsIntact(pending))
        {
            PendingUpdates.Clear();
            readyUpdate = null;
            UpdateStatusText.Text = "The downloaded update failed its check and was deleted. Choose Update now again to download it afresh.";
            return;
        }
        var (canPrepare, reason) = await Task.Run(() => (UpdateSafety.CanPrepare(paths, App.UpdatesFolder, out var why), why));
        if (!canPrepare) { UpdateStatusText.Text = reason + " Nothing was changed."; return; }
        UpdateStatusText.Text = "Closing GameVault to install the update…";
        App.PendingUpdateInstaller = PendingUpdates.InstallerPath(pending);
        App.RelaunchAfterUpdate = true;
        var result = await lifetime.QuitAsync();
        if (result != WindowCloseResult.Quit)
        {
            App.PendingUpdateInstaller = null;     // GameVault stayed open (a game or a backup is still running): nothing may start later by surprise
            App.RelaunchAfterUpdate = false;
            UpdateStatusText.Text = $"Version {pending.Version} is ready, but GameVault stayed open (a game or a backup is still running). It installs when you close GameVault.";
        }
    }

    // ---------- links and settings ----------

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
        var check = CheckForUpdatesChoice.IsChecked == true;
        var auto = AutoUpdateChoice.IsChecked == true;
        if (check == settings.CheckForUpdates && auto == settings.AutoUpdate) return;
        busy = true; UpdateButtons();
        try
        {
            var updated = settings with { CheckForUpdates = check, AutoUpdate = auto };
            await store.SaveSettingsAsync(updated);
            settings = updated;
            App.AutoUpdateEnabled = AutoUpdateOn;
            OperationNotice.Text = !check ? "GameVault will not check for new versions on its own."
                : auto ? "GameVault will keep itself up to date: updates download in the background and install when you close it."
                : "GameVault will tell you when a new version is available and wait for you to choose Update now.";
        }
        catch (Exception error) when (IsExpected(error))
        {
            initializing = true; CheckForUpdatesChoice.IsChecked = settings.CheckForUpdates; AutoUpdateChoice.IsChecked = settings.AutoUpdate; initializing = false;
            OperationNotice.Text = "Could not save the update setting. " + error.Message;
        }
        finally { EndOperation(); }
    }
}
