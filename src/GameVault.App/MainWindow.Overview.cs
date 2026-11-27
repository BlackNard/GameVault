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

    /// <summary>A game that has captures, shown as a folder on the Captures page.</summary>
    private sealed record CaptureFolder(LibraryItemView Item, string Summary, string Latest, DateTimeOffset Newest, System.Windows.Media.ImageSource? FolderCover);

    private NavKind overviewKind = NavKind.Backups;
    private int overviewRevision;
    private Guid? captureFolder;      // the game whose captures are open, or null for the list of folders

    private bool OverviewVisible => OverviewPanel.Visibility == Visibility.Visible;

    private async void ShowBackupsOverview(object sender, RoutedEventArgs e) => await NavigateToAsync(NavRoute.Backups);
    private async void ShowCapturesOverview(object sender, RoutedEventArgs e) => await NavigateToAsync(NavRoute.Captures);

    private async Task ShowOverviewAsync(NavKind kind, Guid? game = null)
    {
        overviewKind = kind;
        captureFolder = kind == NavKind.Captures ? game : null;
        CaptureGalleryPage.Show([]);
        libraryVisible = false;
        LibraryPanel.Visibility = SettingsPanel.Visibility = GamePage.Visibility = StatisticsPanel.Visibility = JournalPanel.Visibility = Visibility.Collapsed;
        OverviewPanel.Visibility = Visibility.Visible;
        OverviewTitle.Text = kind == NavKind.Backups ? "Backups" : captureFolder is Guid open ? items.FirstOrDefault(i => i.Game.Id == open)?.Name ?? "Captures" : "Captures";
        OverviewSubtitle.Text = kind == NavKind.Backups ? "The newest verified backup of every game. Back up one now, or open its page for history and restore." : captureFolder is null ? "One folder for every game that has screenshots or recordings, newest first. Open a folder to see them." : "";
        CaptureFolderBar.Visibility = captureFolder is null ? Visibility.Collapsed : Visibility.Visible;
        OverviewScroll.Visibility = captureFolder is null ? Visibility.Visible : Visibility.Collapsed;
        CaptureGalleryPage.Visibility = Visibility.Collapsed;
        RecordRoute();
        await RefreshOverviewAsync();
    }

    private async Task RefreshOverviewAsync()
    {
        if (!OverviewVisible) return;
        var revision = ++overviewRevision;
        var kind = overviewKind;
        var rows = new List<OverviewRow>();
        var folder = captureFolder;
        var folders = new List<CaptureFolder>();
        var cards = new List<CaptureCard>();
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
            else if (capture is not null && folder is Guid opened)
            {
                var media = (await capture.ListMediaAsync(opened)).OrderByDescending(m => m.CapturedUtc).ToArray();
                cards.AddRange(media.Select(CardFor));
                if (revision == overviewRevision) OverviewSubtitle.Text = CaptureSummary(media);
            }
            else if (capture is not null)
            {
                foreach (var item in items.Where(i => i.Game.Profile is not null))
                {
                    var media = await capture.ListMediaAsync(item.Game.Id);
                    if (media.Length == 0) continue;
                    var newest = media.Max(m => m.CapturedUtc);
                    System.Windows.Media.ImageSource? cover = null;
                    if (item.Game.CoverFile is string file)
                    {
                        try { cover = await CoverImages.LoadThumbnailAsync(file); }
                        catch (Exception error) when (IsExpected(error) || error is ArgumentException or NotSupportedException) { }
                    }
                    folders.Add(new CaptureFolder(item, CaptureSummary(media), "Latest " + newest.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture), newest, cover));
                }
                folders = [.. folders.OrderByDescending(f => f.Newest)];
            }
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowToast("Could not read the list", error.Message, true);
        }
        if (revision != overviewRevision || !OverviewVisible) return;
        OverviewList.ItemsSource = rows;
        CaptureFolders.ItemsSource = folders;
        CaptureGalleryPage.Show(cards);
        OverviewList.Visibility = kind == NavKind.Backups ? Visibility.Visible : Visibility.Collapsed;
        CaptureFolders.Visibility = kind == NavKind.Captures && folder is null ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in rows) _ = row.Item.LoadRailIconAsync();
        var empty = rows.Count == 0 && folders.Count == 0 && cards.Count == 0;
        OverviewEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        OverviewScroll.Visibility = cards.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        CaptureGalleryPage.Visibility = cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OverviewEmptyText.Text = kind == NavKind.Backups ? "No games to back up yet. Add a game from the Library." : "No captures yet. Screenshots and recordings you take while playing will appear here.";
    }

    private static OverviewRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as OverviewRow;

    private void OverviewPrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        GameList.SelectedItem = row.Item;
        BackUpNow(sender, e);
    }

    private static string CaptureSummary(CaptureMedia[] media)
    {
        var videos = media.Count(m => m.Kind == CaptureMediaKind.Video);
        return $"{media.Length} capture(s) · {media.Length - videos} screenshot(s) · {videos} recording(s)";
    }

    private async void CaptureFolderClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CaptureFolder folder) await NavigateToAsync(NavRoute.CapturesOf(folder.Item.Game.Id));
    }

    private async void AllCapturesClicked(object sender, RoutedEventArgs e) => await NavigateToAsync(NavRoute.Captures);

    private void CaptureFolderOpenClicked(object sender, RoutedEventArgs e)
    {
        if (captureFolder is Guid game) OpenCaptureFolder(game);
    }

    private async void CaptureFolderGameClicked(object sender, RoutedEventArgs e)
    {
        if (captureFolder is Guid game) await NavigateToAsync(NavRoute.Game(game, "Media"));
    }

    private async void OverviewOpenGameClicked(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        await NavigateToAsync(NavRoute.Game(row.Item.Game.Id, "Saves"));
    }
}
