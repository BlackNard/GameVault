using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

// The Playthroughs section of a game's page (issue #95).
public partial class MainWindow
{
    private Guid runsFor;
    private int runsRequest;

    private async Task RefreshPlaythroughsAsync(LibraryItemView? item)
    {
        var request = ++runsRequest;
        var id = item?.Game.Id ?? Guid.Empty;
        if (id != runsFor) { runsFor = id; PlaythroughSection.IsExpanded = false; PlaythroughRows.Children.Clear(); }
        PlaythroughSection.Visibility = CanChangeStatus(item) ? Visibility.Visible : Visibility.Collapsed;
        if (!CanChangeStatus(item)) return;
        try
        {
            var stored = await store.LoadPlaythroughsAsync(id);
            var average = (await store.LoadMetadataAsync(id)).Value?.Get(MetadataField.TimeToBeat)?.Times?.MainSeconds;
            var history = await store.LoadHistoryAsync(id, includeJournals: false);
            var sessions = PlayStatistics.FromHistory(item!.Name, false, history, TimeZoneInfo.Local, 0).Spans ?? [];
            if (request != runsRequest) return;
            var runs = stored.Value?.Runs ?? [];
            PlaythroughSection.Header = runs.Length == 0 ? "Playthroughs" : $"Playthroughs ({runs.Length})";
            PlaythroughRows.Children.Clear();
            if (stored.Message is { } message) PlaythroughRows.Children.Add(Muted(message));
            foreach (var run in runs.OrderByDescending(r => r.Started ?? DateOnly.MaxValue))
                PlaythroughRows.Children.Add(RunRow(id, run, PlaythroughRules.Seconds(run, sessions, DateOnly.FromDateTime(DateTime.Now)), average));
            AddPlaythroughButton.IsEnabled = !stored.IsReadOnly;
        }
        catch (Exception error) when (IsExpected(error) || error is IOException)
        {
            if (request == runsRequest) { PlaythroughRows.Children.Clear(); PlaythroughRows.Children.Add(Muted(error.Message)); }
        }
    }

    private FrameworkElement RunRow(Guid game, Playthrough run, long? seconds, int? average)
    {
        var parts = new List<string> { run.Outcome.ToString() };
        if (run.Platform is { Length: > 0 }) parts.Add(run.Platform);
        if (run.Started is not null || run.Finished is not null) parts.Add($"{(run.Started?.ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? "?")} to {(run.Finished?.ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? "now")}");
        if (PlaythroughRules.AgainstAverage(seconds, average) is { } against) parts.Add(against);
        else if (seconds is > 0) parts.Add(StatisticsView.Duration(seconds.Value));
        if (run.HalfStars > 0) parts.Add(StarRating.Text(run.HalfStars));
        if (run.Mastered) parts.Add("Mastered");
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var edit = new Button { Content = "Edit", ToolTip = "Change or delete this playthrough", MinWidth = 70, VerticalAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(edit, "Edit " + run.Title);
        edit.Click += async (_, _) => await EditPlaythroughAsync(game, run);
        DockPanel.SetDock(edit, Dock.Right);
        row.Children.Add(edit);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = run.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(Muted(string.Join(" · ", parts), new Thickness(0, 2, 0, 0)));
        if (run.Review is { Length: > 0 } review) text.Children.Add(Muted(review, new Thickness(0, 4, 0, 0)));
        row.Children.Add(text);
        return row;
    }

    private async void AddPlaythroughClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is LibraryItemView item) await EditPlaythroughAsync(item.Game.Id, null);
    }

    private async Task EditPlaythroughAsync(Guid game, Playthrough? existing)
    {
        var run = existing ?? new Playthrough(Guid.NewGuid(), "", "PC", DateOnly.FromDateTime(DateTime.Now), null, PlaythroughOutcome.Playing, null, 0, null, false);
        TextBox title = null!, platform = null!, started = null!, finished = null!, hours = null!, review = null!;
        ComboBox outcome = null!;
        StarRatingControl stars = null!;
        CheckBox mastered = null!;
        var form = new FormView(settings.Theme, existing is null ? "Add playthrough" : "Edit playthrough", async () =>
        {
            int? minutes = null;
            if (hours.Text.Trim() is { Length: > 0 } typed)
                minutes = double.TryParse(typed, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && value is >= 0 and <= 16000
                    ? (int)Math.Round(value * 60) : throw new ArgumentException("Enter the hours as a number, or leave the box empty.");
            await store.SavePlaythroughAsync(game, run with
            {
                Title = title.Text.Trim(), Platform = platform.Text.Trim() is { Length: > 0 } p ? p : null, Started = FormView.Date(started), Finished = FormView.Date(finished),
                Outcome = FormView.Chosen<PlaythroughOutcome>(outcome), Minutes = minutes, HalfStars = stars.Value,
                Review = review.Text.Trim() is { Length: > 0 } r ? r : null, Mastered = mastered.IsChecked == true
            });
        }, existing is null ? null : () => store.DeletePlaythroughAsync(game, existing.Id));
        title = form.Text("Title", run.Title, 120, help: "For example: First run, or Claire.");
        outcome = form.Choice("How it stands", Enum.GetValues<PlaythroughOutcome>().Select(o => (o, o.ToString())), run.Outcome, "Whether this run is going on, finished or given up.");
        platform = form.Text("Platform", run.Platform, 80, help: "Where you played, for example PC.");
        started = form.Text("Started", FormView.DateText(run.Started), 10, help: "Year-month-day, for example 2026-11-27. Optional.");
        finished = form.Text("Finished", FormView.DateText(run.Finished), 10, help: "Year-month-day, for example 2026-11-27. Optional.");
        hours = form.Text("Hours", run.Minutes is { } m ? (m / 60.0).ToString("0.##", CultureInfo.CurrentCulture) : "", 8, help: "Leave empty to count the recorded sessions between the dates.");
        stars = form.Stars("Rating", run.HalfStars, "Click a star; the left half gives a half star.");
        review = form.Text("Short review", run.Review, 4000, lines: true, help: "What you thought of this run. Optional.");
        mastered = form.Check("Mastered", run.Mastered, "Everything in the game is done.");
        if (await ShowFormAsync(form)) await RefreshPlaythroughsAsync(GameList.SelectedItem as LibraryItemView);
    }

    /// <summary>Shows a form over the window and waits for it. True when something was saved or deleted.</summary>
    private async Task<bool> ShowFormAsync(FormView form)
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation) return false;
        var saved = false;
        void Done(object? sender, bool result) => saved = result;
        form.Completed += Done;
        busy = editorOpen = true; UpdateButtons();
        try { await ShowInlineEditorAsync(form, h => form.Completed += h, h => form.Completed -= h); }
        finally
        {
            form.Completed -= Done;
            busy = editorOpen = false;
            try { UpdateButtons(); ContinueDeferredQuit(); }
            finally { CompleteInlineEditor(); }
        }
        return saved;
    }
}
