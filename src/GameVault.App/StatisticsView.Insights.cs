using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

// The second stage (ticket #61): session figures, shares, patterns, a calendar, the year in review and the summary on a game's own page.
public sealed partial class StatisticsView
{
    private readonly WrapPanel insightFigures = new();
    private readonly TextBlock completedNames = Muted("");
    private readonly WrapPanel shares = new();
    private readonly Grid weekdayChart = new() { Height = 130 };
    private readonly Grid hourChart = new() { Height = 130 };
    private readonly TextBlock patternNote = Muted("");
    private readonly Grid calendar = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox reviewYear = new() { Width = 130, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
    private readonly StackPanel review = new();
    private Expander? insightSection, shareSection, patternSection;

    /// <summary>A section that stays closed until the player opens it.</summary>
    private static Expander Section(string header, UIElement content)
    {
        var holder = new Border { Child = content, Padding = new Thickness(0, 10, 0, 6) };
        var expander = new Expander { Header = header, Content = holder };
        expander.SetResourceReference(StyleProperty, "QuietExpander");
        return expander;
    }

    /// <summary>The sections that follow the chosen period.</summary>
    private void AddPeriodSections()
    {
        var sessions = new StackPanel();
        sessions.Children.Add(insightFigures);
        completedNames.Margin = new Thickness(0, 0, 0, 6);
        sessions.Children.Add(completedNames);
        sessions.Children.Add(Muted("Completion dates are recorded from this version on, when you set a game to Completed."));
        insightSection = Section("Sessions and completions", sessions);
        insightSection.Margin = new Thickness(0, 18, 0, 0);
        body.Children.Add(insightSection);

        shareSection = Section("Where the time went", shares);
        body.Children.Add(shareSection);

        var patterns = new StackPanel();
        patterns.Children.Add(Muted("By day of the week", new Thickness(0, 0, 0, 6)));
        patterns.Children.Add(weekdayChart);
        patterns.Children.Add(Muted("By time of day (when sessions ran)", new Thickness(0, 14, 0, 6)));
        patterns.Children.Add(hourChart);
        patterns.Children.Add(patternNote);
        patternSection = Section("When you play", patterns);
        body.Children.Add(patternSection);
    }

    private void AddHistorySections()
    {
        body.Children.Add(Section("Activity calendar", new ScrollViewer { Content = calendar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }));
        var year = new StackPanel();
        AutomationProperties.SetName(reviewYear, "Year to review");
        reviewYear.SelectionChanged += (_, _) => { if (!filling) RenderReview(); };
        year.Children.Add(reviewYear);
        year.Children.Add(review);
        body.Children.Add(Section("Year in review", year));
    }

    private void RenderPeriodSections((DateOnly From, DateOnly To)? range, string label)
    {
        insightSection!.Header = "Sessions and completions · " + label;
        shareSection!.Header = "Where the time went · " + label;
        patternSection!.Header = "When you play · " + label;

        var insight = PlayStatistics.Insight(games, range);
        insightFigures.Children.Clear();
        insightFigures.Children.Add(Card("Average session", Duration(insight.AverageSeconds)));
        insightFigures.Children.Add(Card("Longest session", Duration(insight.LongestSeconds), insight.LongestGame is null ? null : $"{insight.LongestGame} · {Day(insight.LongestOn!.Value)}"));
        insightFigures.Children.Add(Card("New games started", insight.NewGames.ToString(CultureInfo.CurrentCulture)));
        insightFigures.Children.Add(Card("Games completed", insight.Completed.Length.ToString(CultureInfo.CurrentCulture)));
        if (PlayStatistics.AverageStars(games, range) is { } stars)
            insightFigures.Children.Add(Card("Your average rating", stars.ToString("0.#", CultureInfo.CurrentCulture) + " of 5", "of the rated games played"));
        var backlog = games.Where(g => !g.Removed).ToArray();
        completedNames.Text = (insight.Completed.Length > 0 ? "Completed: " + string.Join(", ", insight.Completed) + "\n" : "") +
            "Library now: " + string.Join(" · ", PlayStatuses.All.Select(s => (Status: s, Count: backlog.Count(g => PlayStatuses.From(g.Status, g.Detail) == s)))
                .Where(s => s.Count > 0).Select(s => $"{s.Count} {PlayStatuses.Label(s.Status).ToLower(CultureInfo.CurrentCulture)}"));

        shares.Children.Clear();
        shares.Children.Add(ShareList("By game", PlayStatistics.GameShares(games, range, 6)));
        shares.Children.Add(ShareList("By genre", PlayStatistics.GenreShares(games, range).Take(8).ToArray()));
        shares.Children.Add(ShareList("By status now", PlayStatistics.StatusShares(games, range)));

        var week = PlayStatistics.Weekdays(games, range);
        var names = CultureInfo.CurrentCulture.DateTimeFormat;
        Bars(weekdayChart, 420, 90, [.. Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)weekStart + i) % 7))
            .Select(d => (names.GetAbbreviatedDayName(d), names.GetDayName(d), week[(int)d]))]);
        var hours = PlayStatistics.Hours(games, range);
        Bars(hourChart, 720, 90, [.. Enumerable.Range(0, 24).Select(h => (h % 3 == 0 ? $"{h:00}" : " ", $"{h:00}:00 to {(h + 1) % 24:00}:00", hours[h]))]);
        patternNote.Text = hours.Sum() == 0 ? "Nothing played in this period." : "";
    }

    private static FrameworkElement ShareList(string title, PlayShare[] parts)
    {
        var list = new StackPanel { Width = 290, Margin = new Thickness(0, 0, 24, 12) };
        list.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        if (parts.Length == 0) list.Children.Add(Muted("Nothing played."));
        foreach (var part in parts)
        {
            var line = new DockPanel { ToolTip = $"{part.Name}: {Duration(part.Seconds)}" };
            var percent = Muted(part.Fraction.ToString("P0", CultureInfo.CurrentCulture));
            DockPanel.SetDock(percent, Dock.Right);
            line.Children.Add(percent);
            line.Children.Add(new TextBlock { Text = part.Name, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) });
            list.Children.Add(line);
            var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Width = Math.Max(3, 290 * part.Fraction), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 8) };
            bar.SetResourceReference(Border.BackgroundProperty, "AppAccent");
            list.Children.Add(bar);
        }
        return list;
    }

    /// <summary>Columns of equal width; the tallest one is the largest value. Hovering a column shows its figure.</summary>
    private static void Bars(Grid chart, double width, double height, IReadOnlyList<(string Caption, string Name, long Seconds)> items)
    {
        chart.Children.Clear();
        chart.ColumnDefinitions.Clear();
        chart.Width = width;
        chart.HorizontalAlignment = HorizontalAlignment.Left;
        var peak = items.Count == 0 ? 0 : items.Max(i => i.Seconds);
        for (var i = 0; i < items.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var (text, name, seconds) = items[i];
            var column = new DockPanel { Background = System.Windows.Media.Brushes.Transparent, ToolTip = $"{name}: {Duration(seconds)}" };
            var caption = Muted(text);
            caption.FontSize = 11;
            caption.TextAlignment = TextAlignment.Center;
            DockPanel.SetDock(caption, Dock.Bottom);
            column.Children.Add(caption);
            var bar = new Border { Height = seconds == 0 ? 2 : Math.Max(3, height * seconds / peak), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3, 3, 0, 0), Margin = new Thickness(2, 0, 2, 4) };
            bar.SetResourceReference(Border.BackgroundProperty, seconds == 0 ? "AppLine" : "AppAccent");
            bar.Loaded += (_, _) => Motion.Grow(bar, true);
            column.Children.Add(bar);
            AutomationProperties.SetName(column, $"{name}: {Duration(seconds)}");
            Grid.SetColumn(column, i);
            chart.Children.Add(column);
        }
    }

    private void RenderCalendar()
    {
        var days = PlayStatistics.Calendar(games, today, weekStart, 53);
        var busiest = days.Max(d => d.Seconds);
        calendar.Children.Clear();
        calendar.ColumnDefinitions.Clear();
        calendar.RowDefinitions.Clear();
        calendar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        for (var i = 0; i < 53; i++) calendar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
        for (var i = 0; i < 9; i++) calendar.RowDefinitions.Add(new RowDefinition { Height = i is 0 ? new GridLength(18) : i == 8 ? GridLength.Auto : new GridLength(13) });
        void Place(UIElement element, int row, int column, int span = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            calendar.Children.Add(element);
        }
        var format = CultureInfo.CurrentCulture;
        foreach (var row in new[] { 0, 2, 4 })
        {
            var label = Muted(format.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(((int)weekStart + row) % 7)));
            label.FontSize = 10;
            Place(label, row + 1, 0);
        }
        var labelled = -4;
        for (var i = 0; i < days.Length; i++)
        {
            var day = days[i];
            var column = i / 7 + 1;
            // A month is named over the first week that begins in it, when there is room.
            if (i % 7 == 0 && day.Date.Day <= 7 && column - labelled >= 3 && column <= 51)
            {
                var month = Muted(day.Date.ToString("MMM", format));
                month.FontSize = 10;
                Place(month, 0, column, 3);
                labelled = column;
            }
            var level = PlayStatistics.Level(day.Seconds, busiest);
            var cell = Cell(level);
            cell.ToolTip = $"{Day(day.Date)}: {Duration(day.Seconds)}";
            if (level > 0) AutomationProperties.SetName(cell, (string)cell.ToolTip);
            Place(cell, i % 7 + 1, column);
        }
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        legend.Children.Add(Muted("Less "));
        for (var level = 0; level <= 4; level++) legend.Children.Add(Cell(level));
        legend.Children.Add(Muted(" More"));
        Place(legend, 8, 1, 53);
    }

    private static Border Cell(int level)
    {
        var cell = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(2), Margin = new Thickness(1), Opacity = level switch { 1 => 0.3, 2 => 0.5, 3 => 0.75, _ => 1 } };
        cell.SetResourceReference(Border.BackgroundProperty, level == 0 ? "AppRaised" : "AppAccent");
        return cell;
    }

    private void FillReviewYears()
    {
        var chosen = reviewYear.SelectedItem as int?;
        filling = true;
        reviewYear.Items.Clear();
        foreach (var year in PlayStatistics.Years(games, today)) reviewYear.Items.Add(year.Year);
        reviewYear.SelectedItem = chosen is { } wanted && reviewYear.Items.Contains(wanted) ? wanted : today.Year;
        filling = false;
        RenderReview();
    }

    private void RenderReview()
    {
        review.Children.Clear();
        if (reviewYear.SelectedItem is not int year) return;
        var value = PlayStatistics.Review(games, year);
        if (value.Stats.Seconds == 0) { review.Children.Add(Muted("Nothing played in " + year + ".")); return; }
        var cards = new WrapPanel();
        cards.Children.Add(Card("Time played", Duration(value.Stats.Seconds), $"{value.Stats.Sessions} sessions on {value.Stats.DaysPlayed} days"));
        cards.Children.Add(Card("Games played", value.Stats.Games.ToString(CultureInfo.CurrentCulture), $"{value.Insight.NewGames} new this year"));
        cards.Children.Add(Card("Games completed", value.Insight.Completed.Length.ToString(CultureInfo.CurrentCulture), value.Insight.Completed.Length == 0 ? null : string.Join(", ", value.Insight.Completed.Take(3))));
        cards.Children.Add(Card("Longest session", Duration(value.Insight.LongestSeconds), value.Insight.LongestGame));
        cards.Children.Add(Card("Longest streak", value.LongestStreak == 1 ? "1 day" : value.LongestStreak + " days"));
        if (value.BusiestMonth is { } month) cards.Children.Add(Card("Busiest month", month.ToString("MMMM", CultureInfo.CurrentCulture), Duration(value.BusiestSeconds)));
        if (value.TopGenre is { } genre) cards.Children.Add(Card("Favourite genre", genre));
        review.Children.Add(cards);
        review.Children.Add(new TextBlock { Text = "Top games", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) });
        var largest = value.Stats.Top[0].Seconds;
        foreach (var entry in value.Stats.Top.Take(TopShown)) review.Children.Add(Row(entry, (double)entry.Seconds / largest));
    }

    /// <summary>The figures of one game, shown on its own page.</summary>
    public static FrameworkElement GameSummary(GamePlay game, DateOnly today, DayOfWeek weekStart)
    {
        GamePlay[] one = [game];
        var panel = new StackPanel();
        if (game.SessionDates.Length == 0) { panel.Children.Add(Muted("No sessions recorded yet.")); return panel; }
        var stats = PlayStatistics.Summarize(one, (DateOnly.MinValue, DateOnly.MaxValue));
        var insight = PlayStatistics.Insight(one, null);
        var streak = PlayStatistics.Streaks(one, today);
        var cards = new WrapPanel();
        cards.Children.Add(Card("Recorded sessions", stats.Sessions.ToString(CultureInfo.CurrentCulture), $"on {stats.DaysPlayed} days"));
        cards.Children.Add(Card("Average session", Duration(insight.AverageSeconds)));
        cards.Children.Add(Card("Longest session", Duration(insight.LongestSeconds), Day(insight.LongestOn!.Value)));
        cards.Children.Add(Card("Longest streak", streak.Longest == 1 ? "1 day" : streak.Longest + " days"));
        cards.Children.Add(Card("First played", Day(game.SessionDates.Min())));
        if (game.CompletedOn is { } done) cards.Children.Add(Card("Completed", Day(done)));
        panel.Children.Add(cards);
        panel.Children.Add(Muted("Last 12 months", new Thickness(0, 4, 0, 6)));
        var chart = new Grid { Height = 120 };
        Bars(chart, 560, 90, [.. PlayStatistics.Chart(one, ActivityGrouping.Month, today, weekStart, 12)
            .Select(b => (b.Start.ToString("MMM", CultureInfo.CurrentCulture), b.Start.ToString("MMMM yyyy", CultureInfo.CurrentCulture), b.Seconds))]);
        panel.Children.Add(chart);
        return panel;
    }

    private static TextBlock Muted(string text, Thickness margin)
    {
        var block = Muted(text);
        block.Margin = margin;
        return block;
    }
}
