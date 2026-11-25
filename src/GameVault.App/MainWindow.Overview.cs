using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

// Global Backups and Captures pages: every game's backups, or the newest captures across all games, in one place (UI overhaul part two, ticket #90).
public partial class MainWindow
{
    private sealed record OverviewRow(LibraryItemView Item, string Detail, string PrimaryLabel, bool PrimaryEnabled, CaptureMedia? Media = null);

    private sealed record CaptureGroup(LibraryItemView Item, string Summary, CaptureCard[] Cards, string More)
    {
        public Visibility MoreVisibility => More.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private NavKind overviewKind = NavKind.Backups;
    private int overviewRevision;
    private const int MaxCapturesPerGame = 6;

    private bool OverviewVisible => OverviewPanel.Visibility == Visibility.Visible;

    private async void ShowBackupsOverview(object sender, RoutedEventArgs e) => await NavigateToAsync(NavRoute.Backups);
    private async void ShowCapturesOverview(object sender, RoutedEventArgs e) => await NavigateToAsync(NavRoute.Captures);

    private async Task ShowOverviewAsync(NavKind kind)
    {
        overviewKind = kind;
        libraryVisible = false;
        LibraryPanel.Visibility = SettingsPanel.Visibility = GamePage.Visibility = Visibility.Collapsed;
        OverviewPanel.Visibility = Visibility.Visible;
        OverviewTitle.Text = kind == NavKind.Backups ? "Backups" : "Captures";
        OverviewSubtitle.Text = kind == NavKind.Backups ? "The newest verified backup of every game. Back up one now, or open its page for history and restore." : "Screenshots and recordings, grouped by game with the newest first.";
        RecordRoute();
        await RefreshOverviewAsync();
    }

    private async Task RefreshOverviewAsync()
    {
        if (!OverviewVisible) return;
        var revision = ++overviewRevision;
        var kind = overviewKind;
        var rows = new List<OverviewRow>();
        var groups = new List<CaptureGroup>();
        try
        {
            if (kind == NavKind.Backups)
            {
                foreach (var item in items.Where(i => i.Game.Profile is { IsArchived: false }))
                {
                    var result = await backups.ReadAsync(item.Game.Id);
                    var detail = result.LastSuccess is CompletedBackup last
                        ? $"Last verified backup {ContinuePlaying.Ago(last.Manifest.CreatedAt, DateTimeOffset.Now).ToLowerInvariant()} · {last.Manifest.Files.Length} file(s)"
                        : result.Phase == BackupPhase.Working ? "Backing up…" : "No verified backup yet";
                    rows.Add(new OverviewRow(item, detail, result.Phase == BackupPhase.Working ? "Working…" : "Back up now", !backups.IsWorking(item.Game.Id) && item.Game.Profile is { IsArchived: false } && !item.Game.IsReadOnly));
                }
            }
            else if (capture is not null)
            {
                foreach (var item in items.Where(i => i.Game.Profile is not null))
                {
                    var media = (await capture.ListMediaAsync(item.Game.Id)).OrderByDescending(m => m.CapturedUtc).ToArray();
                    if (media.Length == 0) continue;
                    var videos = media.Count(m => m.Kind == CaptureMediaKind.Video);
                    var shots = media.Length - videos;
                    var cards = media.Take(MaxCapturesPerGame).Select(CardFor).ToArray();
                    groups.Add(new CaptureGroup(item, $"{media.Length} capture(s) · {shots} screenshot(s) · {videos} recording(s)", cards,
                        media.Length > cards.Length ? $"+ {media.Length - cards.Length} older. Open the game's Media tab to see all." : ""));
                }
                groups = [.. groups.OrderByDescending(g => g.Cards[0].Media.CapturedUtc)];
            }
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowToast("Could not read the list", error.Message, true);
        }
        if (revision != overviewRevision || !OverviewVisible) return;
        OverviewList.ItemsSource = rows;
        CaptureGroups.ItemsSource = groups;
        OverviewList.Visibility = kind == NavKind.Backups ? Visibility.Visible : Visibility.Collapsed;
        CaptureGroups.Visibility = kind == NavKind.Captures ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in rows) _ = row.Item.LoadRailIconAsync();
        foreach (var group in groups) _ = group.Item.LoadRailIconAsync();
        OverviewEmpty.Visibility = rows.Count == 0 && groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OverviewEmptyText.Text = kind == NavKind.Backups ? "No games to back up yet. Add a game from the Library." : "No captures yet. Screenshots and recordings you take while playing will appear here.";
    }

    private static OverviewRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as OverviewRow;

    private void OverviewPrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        GameList.SelectedItem = row.Item;
        BackUpNow(sender, e);
    }

    private void CaptureGroupFolderClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CaptureGroup group) OpenCaptureFolder(group.Item.Game.Id);
    }

    private async void CaptureGroupOpenGameClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CaptureGroup group) await NavigateToAsync(NavRoute.Game(group.Item.Game.Id, "Media"));
    }

    private async void OverviewOpenGameClicked(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        await NavigateToAsync(NavRoute.Game(row.Item.Game.Id, "Saves"));
    }
}
