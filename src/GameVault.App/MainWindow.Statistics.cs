using System.Globalization;
using System.IO;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

// The Statistics page (ticket #61).
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
            StatisticsPanel.Content = statisticsView;
        }
        libraryVisible = false;
        LibraryPanel.Visibility = SettingsPanel.Visibility = GamePage.Visibility = OverviewPanel.Visibility = Visibility.Collapsed;
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
}
