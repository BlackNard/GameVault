using System.IO;
using System.Text.RegularExpressions;

namespace GameVault.App.Tests;

public sealed class UpdateCheckWiringTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Source(string name) => File.ReadAllText(Path.Combine(Root(), "src", "GameVault.App", name));

    [Fact]
    public void The_running_app_knows_its_preview_number_from_the_build_properties()
    {
        var props = File.ReadAllText(Path.Combine(Root(), "Directory.Build.props"));
        var version = Regex.Match(props, @"<GameVaultVersion>([^<]+)</GameVaultVersion>").Groups[1].Value;
        var preview = Regex.Match(props, @"<GameVaultPreview>(\d+)</GameVaultPreview>").Groups[1].Value;
        Assert.Equal($"{version}-preview.{preview}", AppVersionInfo.Current);
    }

    [Fact]
    public void The_start_up_check_is_throttled_optional_quiet_and_never_installs_or_opens_other_sites()
    {
        var window = Source("MainWindow.xaml.cs");
        Assert.Contains("Loaded += StartUpdateCheckLater", window);
        Assert.Contains("CheckForUpdatesChoice.IsChecked=settings.CheckForUpdates", window);
        var code = Source("MainWindow.Updates.cs");
        Assert.Contains("UpdateCheckRules.ShouldCheckAtStart(settings.CheckForUpdates, UpdateCheckStamp.Read(paths.Root)", code);
        Assert.Contains("if (result.Status == UpdateStatus.Failed && !manual)", code);                          // a quiet start-up check says nothing when offline
        Assert.Contains("UpdateChecker.IsReleaseLink(update.Url)", code);                                      // the link is checked again right before opening it
        Assert.Contains("\"Open release page\"", Source("MainWindow.Notices.cs"));
        Assert.DoesNotContain("ShowToast($\"GameVault", code);                                                  // the update notice is a quiet entry in the bell, not a toast
        Assert.Contains("ShowUpdateNotice(", code);
        Assert.DoesNotContain("DownloadFile", code);
        Assert.DoesNotContain("WebClient", code);
        Assert.DoesNotContain("GetBytesAsync", code);                                                           // the app code never fetches anything itself
        var xaml = Source("MainWindow.xaml");
        foreach (var name in new[] { "CheckForUpdatesButton", "CheckForUpdatesChoice", "UpdateStatusText", "OpenReleasePageButton" })
            Assert.Contains($"x:Name=\"{name}\"", xaml);
    }

    [Fact]
    public void The_bell_lists_update_unexpected_exit_and_backup_notices_and_closes_with_escape_or_a_click_outside()
    {
        var xaml = Source("MainWindow.xaml");
        foreach (var name in new[] { "NoticesButton", "NoticeBadge", "NoticeBadgeText", "NoticePanel", "NoticeList", "NoticeEmptyText", "ClearNoticesButton", "NoticeCloseButton" })
            Assert.Contains($"x:Name=\"{name}\"", xaml);
        Assert.Contains("Click=\"NoticeActionClicked\"", xaml);
        Assert.Contains("Click=\"DismissNoticeClicked\"", xaml);
        var shell = Source("MainWindow.Shell.cs");
        Assert.Contains("InitializeNotices();", shell);
        Assert.Contains("key == Key.Escape && NoticePanel.Visibility == Visibility.Visible", shell);
        Assert.Contains("!IsInside(e.OriginalSource as DependencyObject, NoticePanel)", shell);
        Assert.Contains("PostNotice(new Notice(UnexpectedExitId", Source("MainWindow.Diagnostics.cs"));
        var tray = Source("MainWindow.Tray.cs");
        Assert.Contains("SyncBackupNotices();}", tray);
        Assert.Contains("UpdateTray();SyncBackupNotices();", tray);
        var notices = Source("MainWindow.Notices.cs");
        Assert.Contains("UpdateNoticeCache.Dismiss(paths.Root", notices);
        Assert.Contains("backupAlerts.Acknowledge(game)", notices);
        Assert.Contains("ShowCachedUpdateNotice();", Source("MainWindow.Updates.cs"));
    }

    [Fact]
    public void Updates_download_in_the_background_install_only_when_closed_or_asked_and_the_file_is_checked_again_right_before_it_starts()
    {
        var updates = Source("MainWindow.Updates.cs");
        Assert.Contains("AutoUpdateRules.ShouldDownload(AutoUpdateOn, CanUpdateInPlace(update), IsGameRunning(), readyUpdate, update)", updates);    // never while a game runs
        Assert.Contains("await DownloadInBackgroundAsync(update)", updates);
        Assert.Contains("await Task.Delay(AutoUpdateRules.RecheckInterval)", updates);                                                  // a launcher left in the tray still looks again
        var pending = updates.IndexOf("App.PendingUpdateInstaller = PendingUpdates.InstallerPath(pending);", StringComparison.Ordinal);
        var intact = updates.IndexOf("!PendingUpdates.IsIntact(pending)", StringComparison.Ordinal);
        var quit = updates.IndexOf("await lifetime.QuitAsync()", StringComparison.Ordinal);
        Assert.True(intact > 0 && intact < pending && pending < quit, "Check the file, then ask to quit.");
        Assert.Contains("if (result != WindowCloseResult.Quit)", updates);
        Assert.Contains("App.PendingUpdateInstaller = null;     // GameVault stayed open", updates);
        Assert.Contains("CanUpdateInPlace(UpdateInfo update) => update.Installer is not null && InstalledCopy.IsThisTheInstalledCopy()", updates);
        Assert.Contains("PendingUpdates.Clear();", updates);
        var app = Source("App.xaml.cs");
        var exit = app[app.IndexOf("protected override void OnExit", StringComparison.Ordinal)..];
        Assert.True(exit.IndexOf("workspace?.Dispose();", StringComparison.Ordinal) < exit.IndexOf("StartPendingUpdate(RelaunchAfterUpdate)", StringComparison.Ordinal),
            "The installer may only start after this process released the application folder lock.");
        Assert.Contains("else if (AutoUpdateEnabled) StartPendingUpdate(relaunch: false);", exit);            // a normal close installs without reopening the app
        Assert.Contains("!AutoUpdateRules.ShouldInstall(true, pending, running) || !store.IsIntact(pending)) return;", app);        // checked again right before it starts
        Assert.Contains("store.RecordAttempt(", app);
        Assert.Contains("AutoUpdateRules.ShouldInstall(true, pending, running)", app);
        var startup = app[app.IndexOf("protected override async void OnStartup", StringComparison.Ordinal)..];
        Assert.True(startup.IndexOf("TryInstallWaitingUpdateAtStart()", StringComparison.Ordinal) < startup.IndexOf("ApplicationWorkspace.Open", StringComparison.Ordinal), "A waiting update installs before the library is opened.");
        Assert.Contains("UpdatePreferences.ParseAutoUpdate(ReadSettingsText(paths))", app);
        Assert.DoesNotContain("PendingUpdateInstaller =", app);                                               // only the update flow sets it
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("x:Name=\"UpdateNowButton\"", xaml);
        Assert.Contains("x:Name=\"AutoUpdateChoice\"", xaml);
        var notices = Source("MainWindow.Notices.cs");
        Assert.Contains("\"Restart and update now\"", notices);
        Assert.Contains("InstallLocationRules.IsInstalledCopy(AppContext.BaseDirectory, RegisteredInstallLocation())", Source("InstalledCopy.cs"));
        Assert.Contains("{11DE0F05-D06D-4594-A970-85DDE91DC106}_is1", Source("InstalledCopy.cs"));
    }

    [Fact]
    public void The_library_is_protected_around_every_update_a_safety_copy_before_and_a_fingerprint_check_after()
    {
        var app = Source("App.xaml.cs");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(app, @"StartWithSafetyCopy\(store, pending").Count);            // exit path and start-up path
        Assert.DoesNotContain("UpdateLauncher.Start(store.InstallerPath(counted)", app.Replace("return UpdateLauncher.Start(store.InstallerPath(counted), UpdatesFolder, relaunch, Environment.ProcessId);", ""));
        var helper = app[app.IndexOf("private static bool StartWithSafetyCopy", StringComparison.Ordinal)..];
        Assert.True(helper.IndexOf("UpdateSafety.PrepareAsync", StringComparison.Ordinal) < helper.IndexOf("UpdateLauncher.Start", StringComparison.Ordinal), "No safety copy, no installer.");
        Assert.Contains("return false;", helper[..helper.IndexOf("UpdateLauncher.Start", StringComparison.Ordinal)]);
        var startup = app[app.IndexOf("protected override async void OnStartup", StringComparison.Ordinal)..];
        var verify = startup.IndexOf("LibraryIsSafeAfterUpdate(paths)", StringComparison.Ordinal);
        Assert.True(verify > 0 && verify < startup.IndexOf("SplashPreferences.Parse", StringComparison.Ordinal) && verify < startup.IndexOf("ApplicationWorkspace.Open", StringComparison.Ordinal),
            "The library is compared with the fingerprint before the splash window and before the library is opened.");
        Assert.Contains("MessageBoxButton.YesNo", app);
        Assert.Contains("UpdateSafety.CanPrepare(paths, App.UpdatesFolder", Source("MainWindow.Updates.cs"));
    }
}
