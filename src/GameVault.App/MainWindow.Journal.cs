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
    private bool journalExporting;

    private bool JournalVisible => JournalPanel.Visibility == Visibility.Visible;

    private async void ShowJournal(object sender, RoutedEventArgs e) => await NavigateToAsync(NavRoute.Journal);

    private async Task ShowJournalAsync()
    {
        if (journalView is null)
        {
            journalView = new JournalView();
            journalView.AddRequested += async (_, game) => await EditJournalAsync(game, null);
            journalView.EditRequested += async (_, item) => await EditJournalAsync(item.GameId, item.Entry.Id);
            journalView.ExportRequested += async (_, item) => await ExportJournalAsync(item);
            JournalPanel.Content = journalView;
        }
        libraryVisible = false;
        LibraryPanel.Visibility = SettingsPanel.Visibility = GamePage.Visibility = OverviewPanel.Visibility = StatisticsPanel.Visibility = ListsPanel.Visibility = Visibility.Collapsed;
        JournalPanel.Visibility = Visibility.Visible;
        RecordRoute();
        await LoadJournalAsync();
    }

    private async Task LoadJournalAsync()
    {
        var revision = ++journalRevision;
        try
        {
            var feed = await Task.Run(() => new JournalService(store).LoadAsync());
            if (revision == journalRevision) journalView?.Show(feed);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { if (revision == journalRevision) journalView?.ShowProblem(error.Message); }
    }

    private async Task ExportJournalAsync(JournalItem item)
    {
        if (journalView is null || journalExporting) return;
        journalExporting = true;
        try
        {
            var vault = Obsidian;
            if (!await ObsidianPrompts.ReadyVaultAsync(journalView, vault)) return;
            var result = await vault.ExportEntriesAsync([item], conflict => ObsidianPrompts.ConflictAsync(journalView, conflict));
            ShowToast("Exported to Obsidian", await ObsidianPrompts.SummaryAsync(vault, result), result.Failures.Length > 0);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException or ArgumentException) { ShowToast("The entry was not exported", error.Message, true); }
        finally { journalExporting = false; }
    }

    private async Task EditJournalAsync(Guid game, Guid? entryId)
    {
        var library = items.Where(i => i.Game is { IsReadOnly: false, Profile.IsArchived: false }).ToArray();
        if (game != Guid.Empty && library.All(i => i.Game.Id != game)) game = Guid.Empty;
        try
        {
            var stored = entryId is null ? [] : game == Guid.Empty
                ? (await store.LoadGeneralJournalAsync()).Value?.Entries ?? []
                : (await store.LoadJournalAsync(game)).Value?.Entries ?? [];
            var existing = stored.FirstOrDefault(e => e.Id == entryId);
            if (entryId is not null && existing is null) return;
            var entry = existing ?? new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Now), null, "", [], null);
            var runs = new Dictionary<Guid, Playthrough[]>();
            foreach (var item in library) runs[item.Game.Id] = (await store.LoadPlaythroughsAsync(item.Game.Id)).Value?.Runs ?? [];

            ComboBox gameBox = null!, runBox = null!;
            TextBox title = null!, date = null!, minutes = null!, note = null!, tags = null!;
            var form = new FormView(settings.Theme, existing is null ? "Add journal entry" : "Edit journal entry", async () =>
            {
                var target = FormView.Chosen<Guid>(gameBox);
                int? played = null;
                if (minutes.Text.Trim() is { Length: > 0 } typed)
                    played = int.TryParse(typed, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ? value : throw new ArgumentException("Enter the minutes as a whole number, or leave the box empty.");
                var run = runBox.SelectedItem is ComboBoxItem { Tag: Guid id } && id != Guid.Empty ? id : (Guid?)null;
                await store.SaveJournalEntryAsync(target, entry with
                {
                    Title = title.Text, Date = FormView.Date(date) ?? throw new ArgumentException("Enter the date."), Minutes = played, Note = note.Text,
                    Tags = tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries), PlaythroughId = run
                });
            }, existing is null ? null : () => store.DeleteJournalEntryAsync(game, existing.Id));
            title = form.Text("Title (optional)", entry.Title, 200);
            // An existing entry stays where it is.
            var choices = library.Select(i => (i.Game.Id, i.Name)).Prepend((Guid.Empty, "No game"));
            gameBox = form.Choice("Game", existing is null ? choices : choices.Where(c => c.Item1 == game), game);
            date = form.Text("Date (year-month-day)", FormView.DateText(entry.Date), 10);
            note = form.Text("Note", entry.Note, 8000, lines: true);
            tags = form.Text("Tags, separated by commas (up to 8)", string.Join(", ", entry.Tags), 260);
            minutes = form.Text("Minutes played (optional)", entry.Minutes?.ToString(CultureInfo.CurrentCulture), 6);
            runBox = form.Choice("Playthrough (optional)", RunChoices(runs.GetValueOrDefault(game) ?? []), entry.PlaythroughId ?? Guid.Empty);
            gameBox.SelectionChanged += (_, _) =>
            {
                runBox.Items.Clear();
                foreach (var (id, label) in RunChoices(runs.GetValueOrDefault(FormView.Chosen<Guid>(gameBox)) ?? [])) runBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
                runBox.SelectedIndex = 0;
            };
            if (await ShowFormAsync(form) && JournalVisible) await LoadJournalAsync();
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { ShowToast("The journal entry could not be opened", error.Message, true); }
    }

    private static IEnumerable<(Guid, string)> RunChoices(Playthrough[] runs) => runs.Select(r => (r.Id, r.Title)).Prepend((Guid.Empty, "None"));
}
