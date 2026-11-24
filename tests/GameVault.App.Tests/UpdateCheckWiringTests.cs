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
}
