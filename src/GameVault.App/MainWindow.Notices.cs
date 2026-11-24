using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GameVault.Core;
using GameVault.Infrastructure.Updates;

namespace GameVault.App;

internal sealed record NoticeView(string Id, string Title, string? Detail, bool IsProblem, string? ActionLabel, bool Unseen)
{
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ActionVisibility => ActionLabel is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility UnseenVisibility => Unseen ? Visibility.Visible : Visibility.Collapsed;
}

// The bell in the title bar and its list of notices (issue #40): update notices, "did not close normally" and backup-needs-attention.
// Notices stay until they are dismissed or resolved; toasts remain for passing messages.
public partial class MainWindow
{
    private const string UnexpectedExitId = "unexpected-exit";
    private const string UpdateIdPrefix = "update:";
    private const string BackupIdPrefix = "backup:";

    private readonly NoticeCenter notices = new();
    private readonly ObservableCollection<NoticeView> noticeViews = [];
    private readonly Dictionary<string, Action> noticeActions = [];

    private void InitializeNotices()
    {
        NoticeList.ItemsSource = noticeViews;
        notices.Changed += (_, _) => SyncNotices();
        SyncNotices();
    }

    private void SyncNotices()
    {
        noticeViews.Clear();
        foreach (var notice in notices.Items)
            noticeViews.Add(new NoticeView(notice.Id, notice.Title, notice.Detail, notice.IsProblem, notice.ActionLabel, !notices.IsSeen(notice.Id)));
        var unseen = notices.UnseenCount;
        NoticeBadge.Visibility = unseen > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoticeBadgeText.Text = unseen > 9 ? "9+" : unseen.ToString();
        var total = notices.Count;
        NoticeEmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearNoticesButton.IsEnabled = total > 0;
        System.Windows.Automation.AutomationProperties.SetName(NoticesButton,
            total == 0 ? "Notifications, none" : unseen > 0 ? $"Notifications, {unseen} new of {total}" : $"Notifications, {total}");
        NoticesButton.ToolTip = total == 0 ? "Notifications" : $"Notifications ({total})";
        if (notices.UnseenCount > 0 && NoticePanel.Visibility == Visibility.Visible) notices.MarkAllSeen();
    }

    private void PostNotice(Notice notice, Action? action = null)
    {
        if (action is not null) noticeActions[notice.Id] = action; else noticeActions.Remove(notice.Id);
        notices.Post(notice);
    }

    private void NoticesButtonClicked(object sender, RoutedEventArgs e)
    {
        if (NoticePanel.Visibility == Visibility.Visible) CloseNotices(); else OpenNotices();
    }

    private void OpenNotices()
    {
        NoticePanel.Visibility = Visibility.Visible;
        notices.MarkAllSeen();
        SyncNotices();
        NoticeCloseButton.Focus();
    }

    private void CloseNotices()
    {
        if (NoticePanel.Visibility != Visibility.Visible) return;
        NoticePanel.Visibility = Visibility.Collapsed;
        NoticesButton.Focus();
    }

    private void NoticeActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || !noticeActions.TryGetValue(id, out var action)) return;
        if (!id.StartsWith(UpdateIdPrefix, StringComparison.Ordinal)) CloseNotices();          // the browser opens next to the app; the others go to another screen
        action();
    }

    private void DismissNoticeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) DismissNotice(id);
    }

    private void ClearNoticesClicked(object sender, RoutedEventArgs e)
    {
        foreach (var notice in notices.Items.ToArray()) DismissNotice(notice.Id);
    }

    private void DismissNotice(string id)
    {
        if (id.StartsWith(UpdateIdPrefix, StringComparison.Ordinal)) UpdateNoticeCache.Dismiss(paths.Root, id[UpdateIdPrefix.Length..]);
        else if (id.StartsWith(BackupIdPrefix, StringComparison.Ordinal) && Guid.TryParse(id[BackupIdPrefix.Length..], out var game)) { backupAlerts.Acknowledge(game); UpdateTray(); }
        noticeActions.Remove(id);
        notices.Remove(id);
    }

    /// <summary>The backup problems the tray already tracks, shown in the list too; resolved or reviewed ones leave it.</summary>
    private void SyncBackupNotices()
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alert in backupAlerts.Alerts)
        {
            var id = BackupIdPrefix + alert.GameId;
            keep.Add(id);
            PostNotice(new Notice(id, NoticeKind.BackupAttention, $"Backup needs attention: {alert.GameName}", alert.Message, alert.CreatedAt, true, "Review"),
                () => ReviewBackupAlert(alert.GameId));
        }
        notices.RetainOnly(NoticeKind.BackupAttention, keep);
    }

    /// <summary>Posts (or clears) the update notice for what the newest check, or the cache from the last one, says.</summary>
    private void ShowUpdateNotice(UpdateInfo? update)
    {
        if (update is null) { notices.RetainOnly(NoticeKind.Update, new HashSet<string>()); return; }
        var id = UpdateIdPrefix + update.Tag;
        notices.RetainOnly(NoticeKind.Update, new HashSet<string> { id });
        if (string.Equals(UpdateNoticeCache.Read(paths.Root).DismissedTag, update.Tag, StringComparison.Ordinal)) { notices.Remove(id); return; }
        var inPlace = CanUpdateInPlace(update);
        PostNotice(new Notice(id, NoticeKind.Update, $"GameVault {update.Version} is available",
            inPlace ? $"You have {AppVersionInfo.Current}. Update now keeps your games, saves, backups and settings; nothing happens until you confirm."
                    : $"You have {AppVersionInfo.Current}. This copy is not the installed one, so the release page has the download.",
            update.PublishedAt ?? DateTimeOffset.UtcNow, false, inPlace ? "Update now" : "Open release page"),
            () => { if (inPlace) _ = BeginUpdateAsync(update); else OpenReleasePage(update); });
    }

    private static bool IsInside(DependencyObject? source, DependencyObject container)
    {
        for (var node = source; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, container)) return true;
        return false;
    }
}
