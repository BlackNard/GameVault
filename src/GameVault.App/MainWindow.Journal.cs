using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

// The Journal page (issue #96).
public partial class MainWindow
{
    private JournalView? journalView;
    private int journalRevision;

    private bool JournalVisible => JournalPanel.Visibility == Visibility.Visible;

    private async void ShowJournal(object sender, RoutedEventArgs e) => await NavigateToAsync(NavRoute.Journal);

    private async Task ShowJournalAsync(DateOnly? onDay = null)
    {
        if (journalView is null)
        {
            journalView = new JournalView();
            journalView.OpenRequested += async (_, which) => await OpenJournalAtAsync(which.Game, which.Line, false);
            journalView.AddRequested += (_, game) => ChooseJournalGame(game);
            JournalPanel.Content = journalView;
        }
        libraryVisible = false;
        LibraryPanel.Visibility = SettingsPanel.Visibility = GamePage.Visibility = OverviewPanel.Visibility = StatisticsPanel.Visibility = ListsPanel.Visibility = Visibility.Collapsed;
        JournalPanel.Visibility = Visibility.Visible;
        RecordRoute();
        await LoadJournalAsync(onDay);
    }

    private async Task LoadJournalAsync(DateOnly? onDay)
    {
        var revision = ++journalRevision;
        try
        {
            var feed = await Task.Run(() => new JournalService(store).LoadAsync(TimeZoneInfo.Local));
            if (revision == journalRevision) journalView?.Show(feed, onDay);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { if (revision == journalRevision) journalView?.ShowProblem(error.Message); }
    }

    /// <summary>One journal, one editor: a line of the Journal page opens in its game's journal.</summary>
    private async Task OpenJournalAtAsync(Guid game, Guid? line, bool addNew)
    {
        if (await NavigateToAsync(NavRoute.Game(game, "Sessions")) && notesView is not null) await notesView.OpenEntryAsync(line, addNew);
    }

    private void ChooseJournalGame(Guid? chosen)
    {
        if (chosen is { } one) { _ = OpenJournalAtAsync(one, null, true); return; }
        var menu = new ContextMenu { PlacementTarget = journalView, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint, StaysOpen = false };
        foreach (var item in items.Where(i => i.Game is { IsReadOnly: false, Profile.IsArchived: false }))
        {
            var id = item.Game.Id;
            var entry = new MenuItem { Header = item.Name };
            entry.Click += async (_, _) => await OpenJournalAtAsync(id, null, true);
            menu.Items.Add(entry);
        }
        menu.IsOpen = menu.Items.Count > 0;
    }
}
