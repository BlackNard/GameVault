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
    public void Update_now_asks_first_downloads_then_quits_normally_and_starts_the_installer_only_after_the_folder_lock_is_released()
    {
        var updates = Source("MainWindow.Updates.cs");
        var confirm = updates.IndexOf("\"Update GameVault\", MessageBoxButton.YesNo", StringComparison.Ordinal);
        var download = updates.IndexOf("downloader.DownloadAsync(", StringComparison.Ordinal);
        var pending = updates.IndexOf("App.PendingUpdateInstaller = installer;", StringComparison.Ordinal);
        var quit = updates.IndexOf("await lifetime.QuitAsync()", StringComparison.Ordinal);
        Assert.True(confirm > 0 && confirm < download, "The player must confirm before anything is downloaded.");
        Assert.True(download < pending && pending < quit, "Download and verify first, then quit.");
        Assert.Contains("if (result != WindowCloseResult.Quit)", updates);
        Assert.Contains("App.PendingUpdateInstaller = null;     // GameVault stayed open", updates);
        Assert.Contains("CanUpdateInPlace(UpdateInfo update) => update.Installer is not null && InstalledCopy.IsThisTheInstalledCopy()", updates);
        Assert.DoesNotContain("Process.Start", updates.Substring(updates.IndexOf("BeginUpdateAsync(UpdateInfo update)", StringComparison.Ordinal), 2500));
        var app = Source("App.xaml.cs");
        Assert.True(app.IndexOf("workspace?.Dispose();", app.IndexOf("OnExit", StringComparison.Ordinal), StringComparison.Ordinal) < app.IndexOf("UpdateLauncher.Start(installer, UpdatesFolder)", StringComparison.Ordinal),
            "The installer may only start after this process released the application folder lock.");
        Assert.Contains("PendingUpdateInstaller is { } installer", app);
        Assert.DoesNotContain("PendingUpdateInstaller =", app);                    // only the update flow sets it
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("x:Name=\"UpdateNowButton\"", xaml);
        var notices = Source("MainWindow.Notices.cs");
        Assert.Contains("inPlace ? \"Update now\" : \"Open release page\"", notices);
        Assert.Contains("InstallLocationRules.IsInstalledCopy(AppContext.BaseDirectory, RegisteredInstallLocation())", Source("InstalledCopy.cs"));
        Assert.Contains("{11DE0F05-D06D-4594-A970-85DDE91DC106}_is1", Source("InstalledCopy.cs"));
    }
}
