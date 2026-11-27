using System.Globalization;
using System.IO;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

// The Statistics page and the statistics section of a game's page (ticket #61).
public partial class MainWindow
{
    private StatisticsView? statisticsView;
    private int statisticsRevision;

    private bool StatisticsVisible => StatisticsPanel.Visibility == Visibility.Visible;

    private async void ShowStatistics(object sender, RoutedEventArgs e) => await NavigateToAsync(NavRoute.Statistics);

    private async Task ShowStatisticsAsync()
    {
        if (statisticsView is null)
        {
            statisticsView = new StatisticsView();
            statisticsView.GameRequested += async (_, id) => await NavigateToAsync(NavRoute.Game(id, "Overview"));
            // A day of the calendar opens what happened on it.
            statisticsView.DayRequested += async (_, day) => { if (await NavigateToAsync(NavRoute.Journal)) await LoadJournalAsync(day); };
            StatisticsPanel.Content = statisticsView;
        }
        libraryVisible = false;
        LibraryPanel.Visibility = SettingsPanel.Visibility = GamePage.Visibility = OverviewPanel.Visibility = JournalPanel.Visibility = ListsPanel.Visibility = Visibility.Collapsed;
        StatisticsPanel.Visibility = Visibility.Visible;
        RecordRoute();
        var revision = ++statisticsRevision;
        try
        {
            var service = new StatisticsService(store, SteamHoursLinks);
            var data = await Task.Run(() => service.LoadAsync(TimeZoneInfo.Local));
            // The first day of the week is the one Windows is set to.
            if (revision == statisticsRevision) statisticsView.Show(data, DateOnly.FromDateTime(DateTime.Now), CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { if (revision == statisticsRevision) statisticsView.ShowProblem(error.Message); }
    }

    private Guid gameStatsFor;

    /// <summary>Shown for a game with recorded play; the figures are read only when the section is opened.</summary>
    private void RefreshGameStats(LibraryItemView? item)
    {
        var id = item?.Game.Id ?? Guid.Empty;
        if (id != gameStatsFor) { gameStatsFor = id; GameStatsSection.IsExpanded = false; GameStatsHost.Content = null; }
        GameStatsSection.Visibility = item?.Game is { Profile: not null, PlayedSeconds: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        if (GameStatsSection.IsExpanded) _ = LoadGameStatsAsync(id);
    }

    private async void GameStatsExpanded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, GameStatsSection)) await LoadGameStatsAsync(gameStatsFor);
    }

    private async Task LoadGameStatsAsync(Guid id)
    {
        try
        {
            var service = new StatisticsService(store, SteamHoursLinks);
            var game = await Task.Run(() => service.LoadGameAsync(id, TimeZoneInfo.Local));
            if (gameStatsFor != id || game is null) return;
            GameStatsHost.Content = StatisticsView.GameSummary(game, DateOnly.FromDateTime(DateTime.Now), CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException)
        {
            if (gameStatsFor == id) GameStatsHost.Content = new System.Windows.Controls.TextBlock { Text = "Statistics could not be read. " + error.Message, TextWrapping = TextWrapping.Wrap };
        }
    }
}
