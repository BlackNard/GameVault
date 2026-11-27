using System.Globalization;
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
            journalView.GameRequested += async (_, id) => await NavigateToAsync(NavRoute.Game(id, "Sessions"));
            journalView.AddRequested += async (_, game) => await EditJournalAsync(game, null);
            journalView.EditRequested += async (_, which) => await EditJournalAsync(which.Game, which.Entry);
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

    private async Task EditJournalAsync(Guid? game, Guid? entryId)
    {
        var library = items.Where(i => i.Game is { IsReadOnly: false, Profile.IsArchived: false }).ToArray();
        if (library.Length == 0) return;
        try
        {
            var chosen = library.FirstOrDefault(i => i.Game.Id == game) ?? library[0];
            var existing = entryId is { } wanted ? (await store.LoadJournalAsync(chosen.Game.Id)).Value?.Entries.FirstOrDefault(e => e.Id == wanted) : null;
            if (entryId is not null && existing is null) return;
            var entry = existing ?? new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Now), null, "", [], null);
            var runs = new Dictionary<Guid, Playthrough[]>();
            foreach (var item in library) runs[item.Game.Id] = (await store.LoadPlaythroughsAsync(item.Game.Id)).Value?.Runs ?? [];

            ComboBox gameBox = null!, runBox = null!;
            TextBox date = null!, minutes = null!, note = null!, tags = null!;
            var form = new FormView(settings.Theme, existing is null ? "Add journal entry" : "Edit journal entry", async () =>
            {
                var target = FormView.Chosen<Guid>(gameBox);
                int? played = null;
                if (minutes.Text.Trim() is { Length: > 0 } typed)
                    played = int.TryParse(typed, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ? value : throw new ArgumentException("Enter the minutes as a whole number, or leave the box empty.");
                var run = runBox.SelectedItem is ComboBoxItem { Tag: Guid id } && id != Guid.Empty ? id : (Guid?)null;
                await store.SaveJournalEntryAsync(target, entry with
                {
                    Date = FormView.Date(date) ?? throw new ArgumentException("Enter the date."), Minutes = played, Note = note.Text,
                    Tags = tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries), PlaythroughId = run
                });
            }, existing is null ? null : () => store.DeleteJournalEntryAsync(chosen.Game.Id, existing.Id));
            // An existing entry stays with its game.
            gameBox = form.Choice("Game", (existing is null ? library : [chosen]).Select(i => (i.Game.Id, i.Name)), chosen.Game.Id);
            date = form.Text("Date (year-month-day)", FormView.DateText(entry.Date), 10);
            minutes = form.Text("Minutes played (optional)", entry.Minutes?.ToString(CultureInfo.CurrentCulture), 6);
            note = form.Text("Note", entry.Note, 8000, lines: true);
            tags = form.Text("Tags, separated by commas (up to 8)", string.Join(", ", entry.Tags), 260);
            runBox = form.Choice("Playthrough (optional)", RunChoices(runs[chosen.Game.Id]), entry.PlaythroughId ?? Guid.Empty);
            gameBox.SelectionChanged += (_, _) =>
            {
                runBox.Items.Clear();
                foreach (var (id, label) in RunChoices(runs.GetValueOrDefault(FormView.Chosen<Guid>(gameBox)) ?? [])) runBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
                runBox.SelectedIndex = 0;
            };
            if (await ShowFormAsync(form) && JournalVisible) await LoadJournalAsync(null);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { ShowToast("The journal entry could not be opened", error.Message, true); }
    }

    private static IEnumerable<(Guid, string)> RunChoices(Playthrough[] runs) => runs.Select(r => (r.Id, r.Title)).Prepend((Guid.Empty, "None"));
}
