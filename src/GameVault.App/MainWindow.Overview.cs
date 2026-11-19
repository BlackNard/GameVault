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

    private NavKind overviewKind = NavKind.Backups;
    private int overviewRevision;

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
        OverviewSubtitle.Text = kind == NavKind.Backups ? "The newest verified backup of every game. Back up one now, or open its page for history and restore." : "The newest screenshots and recordings across all games.";
        RecordRoute();
        await RefreshOverviewAsync();
    }

    private async Task RefreshOverviewAsync()
    {
        if (!OverviewVisible) return;
        var revision = ++overviewRevision;
        var kind = overviewKind;
        var rows = new List<OverviewRow>();
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
                var all = new List<(LibraryItemView Item, CaptureMedia Media)>();
                foreach (var item in items.Where(i => i.Game.Profile is not null))
                    foreach (var media in await capture.ListMediaAsync(item.Game.Id)) all.Add((item, media));
                foreach (var (item, media) in all.OrderByDescending(x => x.Media.CapturedUtc).Take(200))
                {
                    var kindText = media.Kind == CaptureMediaKind.Video ? "Recording" : "Screenshot";
                    var state = media.Status == CaptureMediaStatus.Complete ? "" : " · incomplete";
                    rows.Add(new OverviewRow(item, $"{kindText} · {media.CapturedUtc.ToLocalTime():d MMM yyyy HH:mm}{state}", "Open", media.Status == CaptureMediaStatus.Complete, media));
                }
            }
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowToast("Could not read the list", error.Message, true);
        }
        if (revision != overviewRevision || !OverviewVisible) return;
        OverviewList.ItemsSource = rows;
        foreach (var row in rows) _ = row.Item.LoadRailIconAsync();
        OverviewEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OverviewEmptyText.Text = kind == NavKind.Backups ? "No games to back up yet. Add a game from the Library." : "No captures yet. Screenshots and recordings you take while playing will appear here.";
    }

    private static OverviewRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as OverviewRow;

    private async void OverviewPrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (overviewKind == NavKind.Backups)
        {
            GameList.SelectedItem = row.Item;
            BackUpNow(sender, e);
            return;
        }
        if (row.Media is null) return;
        try
        {
            if (capture is null) return;
            var current = (await capture.ListMediaAsync(row.Media.ProfileId)).SingleOrDefault(m => m.Id == row.Media.Id && m.Status == CaptureMediaStatus.Complete)
                ?? throw new IOException("This capture is no longer available.");
            Process.Start(new ProcessStartInfo(current.FilePath) { UseShellExecute = true });
        }
        catch (Exception error) when (IsExpected(error) || error is System.ComponentModel.Win32Exception)
        {
            ShowToast("Could not open the capture", error.Message, true);
        }
    }

    private async void OverviewOpenGameClicked(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        await NavigateToAsync(NavRoute.Game(row.Item.Game.Id, overviewKind == NavKind.Captures ? "Media" : "Saves"));
    }
}
