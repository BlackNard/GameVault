using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

/// <summary>The Statistics page (ticket #61, first stage): period figures, most played games, streaks, an hours chart and a comparison of two months or years.
/// Charts are plain WPF shapes in theme colours.</summary>
public sealed class StatisticsView : UserControl
{
    private const int TopShown = 5;
    private readonly TextBlock notice = Muted("");
    private readonly WrapPanel periodTabs = new() { Margin = new Thickness(0, 0, 0, 14) };
    private readonly WrapPanel figures = new();
    private readonly TextBlock undated = Muted("");
    private readonly StackPanel most = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button showAll = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel streaks = new();
    private readonly WrapPanel chartTabs = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock chartPeak = Muted("");
    private readonly Grid chart = new() { Height = 170 };
    private readonly ComboBox compareKind = new() { Width = 130, Margin = new Thickness(0, 0, 10, 8) };
    private readonly ComboBox firstChoice = new() { Width = 190, Margin = new Thickness(0, 0, 10, 8) };
    private readonly ComboBox secondChoice = new() { Width = 190, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Grid comparison = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel body = new();
    private readonly TextBlock empty = Muted("Reading your play history…");

    private GamePlay[] games = [];
    private DateOnly today;
    private DayOfWeek weekStart;
    private StatsPeriod period = StatsPeriod.Week;
    private ActivityGrouping grouping = ActivityGrouping.Day;
    private bool everyGame, filling;

    /// <summary>A game in "Most played" was chosen.</summary>
    public event EventHandler<Guid>? GameRequested;

    public StatisticsView()
    {
        var page = new StackPanel { MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 32) };
        page.Children.Add(new TextBlock { Text = "Statistics", FontSize = 30, FontWeight = FontWeights.SemiBold });
        notice.Margin = new Thickness(0, 6, 0, 16);
        page.Children.Add(notice);
        page.Children.Add(empty);

        foreach (var (value, label) in new[] { (StatsPeriod.Today, "Today"), (StatsPeriod.Week, "This week"), (StatsPeriod.Month, "This month"), (StatsPeriod.Year, "This year"), (StatsPeriod.AllTime, "All time") })
            periodTabs.Children.Add(Tab(label, value, () => { period = value; everyGame = false; RenderPeriod(); }));
        body.Children.Add(periodTabs);
        body.Children.Add(figures);
        undated.Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(undated);
        body.Children.Add(Heading("Most played"));
        body.Children.Add(most);
        showAll.Click += (_, _) => { everyGame = !everyGame; RenderPeriod(); };
        body.Children.Add(showAll);

        body.Children.Add(Heading("Streaks"));
        body.Children.Add(streaks);

        body.Children.Add(Heading("Hours played"));
        foreach (var (value, label) in new[] { (ActivityGrouping.Day, "Last 30 days"), (ActivityGrouping.Week, "Last 12 weeks"), (ActivityGrouping.Month, "Last 12 months") })
            chartTabs.Children.Add(Tab(label, value, () => { grouping = value; RenderChart(); }));
        body.Children.Add(chartTabs);
        chartPeak.Margin = new Thickness(0, 0, 0, 4);
        body.Children.Add(chartPeak);
        body.Children.Add(chart);

        var compare = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var choices = new WrapPanel();
        compareKind.Items.Add("Months");
        compareKind.Items.Add("Years");
        compareKind.SelectedIndex = 0;
        AutomationProperties.SetName(compareKind, "Compare months or years");
        AutomationProperties.SetName(firstChoice, "First period");
        AutomationProperties.SetName(secondChoice, "Second period");
        compareKind.SelectionChanged += (_, _) => { if (!filling) FillChoices(false); };
        firstChoice.SelectionChanged += (_, _) => { if (!filling) RenderComparison(); };
        secondChoice.SelectionChanged += (_, _) => { if (!filling) RenderComparison(); };
        choices.Children.Add(compareKind);
        choices.Children.Add(firstChoice);
        choices.Children.Add(secondChoice);
        compare.Children.Add(choices);
        compare.Children.Add(comparison);
        var expander = new Expander { Header = "Compare months or years", Content = compare, Margin = new Thickness(0, 22, 0, 0) };
        expander.SetResourceReference(StyleProperty, "QuietExpander");
        body.Children.Add(expander);
        page.Children.Add(body);
        body.Visibility = Visibility.Collapsed;

        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    public static string Duration(long seconds) => seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60:00}m" : seconds >= 60 ? $"{seconds / 60}m" : seconds > 0 ? "under 1m" : "0m";

    public void ShowProblem(string message)
    {
        body.Visibility = Visibility.Collapsed;
        empty.Visibility = Visibility.Visible;
        empty.Text = "Statistics could not be read. " + message;
    }

    public void Show(StatisticsData data, DateOnly now, DayOfWeek firstDayOfWeek)
    {
        games = data.Games;
        today = now;
        weekStart = firstDayOfWeek;
        notice.Text = string.Join("\n", new[] { "Worked out on this PC from your recorded sessions. Removed games still count." }.Concat(data.Warnings.Take(5)));
        var any = games.Any(g => g.SessionDates.Length > 0 || g.UndatedSeconds > 0);
        empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        empty.Text = "Nothing recorded yet. Play a game from GameVault and it appears here.";
        body.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (!any) return;
        RenderPeriod();
        RenderStreaks();
        RenderChart();
        FillChoices(true);
    }

    private void RenderPeriod()
    {
        Mark(periodTabs, period);
        var stats = PlayStatistics.Summarize(games, PlayStatistics.Range(period, today, weekStart));
        figures.Children.Clear();
        figures.Children.Add(Card("Time played", Duration(stats.Seconds)));
        figures.Children.Add(Card("Sessions", stats.Sessions.ToString(CultureInfo.CurrentCulture)));
        figures.Children.Add(Card("Games", stats.Games.ToString(CultureInfo.CurrentCulture)));
        figures.Children.Add(Card("Days played", stats.DaysPlayed.ToString(CultureInfo.CurrentCulture)));
        var hasUndated = games.Any(g => g.UndatedSeconds != 0);
        undated.Visibility = hasUndated ? Visibility.Visible : Visibility.Collapsed;
        undated.Text = period == StatsPeriod.AllTime ? "Includes time without a date (Steam hours and typed-in time)." : "Steam hours and typed-in time have no date, so they count only in All time.";

        most.Children.Clear();
        if (stats.Top.Length == 0) most.Children.Add(Muted("Nothing played in this period."));
        var largest = stats.Top.Length == 0 ? 1 : stats.Top[0].Seconds;
        foreach (var entry in everyGame ? stats.Top : stats.Top.Take(TopShown)) most.Children.Add(Row(entry, (double)entry.Seconds / largest));
        showAll.Visibility = stats.Top.Length > TopShown ? Visibility.Visible : Visibility.Collapsed;
        showAll.Content = everyGame ? "Show fewer" : $"Show all {stats.Top.Length} games";
    }

    private FrameworkElement Row(GameTime entry, double share)
    {
        var removed = games.First(g => g.GameId == entry.GameId).Removed;
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6), Width = 640, HorizontalAlignment = HorizontalAlignment.Left };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        FrameworkElement name;
        if (removed) name = new TextBlock { Text = entry.Name + " (removed)", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        else
        {
            var open = new Button { Content = new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 2, 0, 2), ToolTip = "Open " + entry.Name };
            open.SetResourceReference(StyleProperty, "TabLink");
            open.Click += (_, _) => GameRequested?.Invoke(this, entry.GameId);
            AutomationProperties.SetName(open, "Open " + entry.Name);
            name = open;
        }
        row.Children.Add(name);
        var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Width = Math.Max(4, 280 * share), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        bar.SetResourceReference(Border.BackgroundProperty, "AppAccent");
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);
        var time = new TextBlock { Text = Duration(entry.Seconds), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(time, 2);
        row.Children.Add(time);
        return row;
    }

    private void RenderStreaks()
    {
        var value = PlayStatistics.Streaks(games, today);
        static string Days(int count) => count == 1 ? "1 day" : count + " days";
        streaks.Children.Clear();
        streaks.Children.Add(Card("Current streak", Days(value.Current), value.Current == 0 ? "Play today to start one" : null));
        streaks.Children.Add(Card("Longest streak", Days(value.Longest),
            value is { LongestStart: { } from, LongestEnd: { } to } ? from == to ? Day(from) : $"{Day(from)} to {Day(to)}" : null));
    }

    private void RenderChart()
    {
        Mark(chartTabs, grouping);
        var bars = PlayStatistics.Chart(games, grouping, today, weekStart, grouping == ActivityGrouping.Day ? 30 : 12);
        var peak = bars.Max(b => b.Seconds);
        chartPeak.Text = peak == 0 ? "Nothing played in this time." : "Tallest bar: " + Duration(peak);
        chart.Children.Clear();
        chart.ColumnDefinitions.Clear();
        chart.Width = 720;
        chart.HorizontalAlignment = HorizontalAlignment.Left;
        for (var i = 0; i < bars.Length; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var item = bars[i];
            var label = grouping == ActivityGrouping.Month ? item.Start.ToString("MMM", CultureInfo.CurrentCulture)
                : grouping == ActivityGrouping.Week ? item.Start.ToString("d MMM", CultureInfo.CurrentCulture) : item.Start.Day.ToString(CultureInfo.CurrentCulture);
            var full = grouping == ActivityGrouping.Month ? item.Start.ToString("MMMM yyyy", CultureInfo.CurrentCulture) : grouping == ActivityGrouping.Week ? "Week of " + Day(item.Start) : Day(item.Start);
            var column = new DockPanel { Background = System.Windows.Media.Brushes.Transparent, ToolTip = $"{full}: {Duration(item.Seconds)}" };
            // Thirty day numbers do not all fit: every fifth one is written, counted back from today.
            var caption = Muted(grouping != ActivityGrouping.Day || (bars.Length - 1 - i) % 5 == 0 ? label : " ");
            caption.FontSize = 11;
            caption.TextAlignment = TextAlignment.Center;
            DockPanel.SetDock(caption, Dock.Bottom);
            column.Children.Add(caption);
            var bar = new Border { Height = item.Seconds == 0 ? 2 : Math.Max(3, 140.0 * item.Seconds / peak), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3, 3, 0, 0), Margin = new Thickness(2, 0, 2, 4) };
            bar.SetResourceReference(Border.BackgroundProperty, item.Seconds == 0 ? "AppLine" : "AppAccent");
            column.Children.Add(bar);
            AutomationProperties.SetName(column, $"{full}: {Duration(item.Seconds)}");
            Grid.SetColumn(column, i);
            chart.Children.Add(column);
        }
    }

    private sealed record Choice(DateOnly Start, string Label) { public override string ToString() => Label; }

    private void FillChoices(bool keep)
    {
        var years = compareKind.SelectedIndex == 1;
        var first = keep ? (firstChoice.SelectedItem as Choice)?.Start : null;
        var second = keep ? (secondChoice.SelectedItem as Choice)?.Start : null;
        var starts = years ? PlayStatistics.Years(games, today) : PlayStatistics.Months(games, today);
        filling = true;
        foreach (var box in new[] { firstChoice, secondChoice })
        {
            box.Items.Clear();
            foreach (var start in starts) box.Items.Add(new Choice(start, start.ToString(years ? "yyyy" : "MMMM yyyy", CultureInfo.CurrentCulture)));
        }
        int Index(DateOnly? wanted, int otherwise) => wanted is { } date && Array.IndexOf(starts, date) is var at and >= 0 ? at : Math.Min(otherwise, starts.Length - 1);
        // By default the newest period is compared with the one before it.
        firstChoice.SelectedIndex = Index(first, 0);
        secondChoice.SelectedIndex = Index(second, 1);
        filling = false;
        RenderComparison();
    }

    private void RenderComparison()
    {
        comparison.Children.Clear();
        comparison.RowDefinitions.Clear();
        comparison.ColumnDefinitions.Clear();
        if (firstChoice.SelectedItem is not Choice a || secondChoice.SelectedItem is not Choice b) return;
        Func<DateOnly, (DateOnly, DateOnly)> range = compareKind.SelectedIndex == 1 ? PlayStatistics.YearRange : PlayStatistics.MonthRange;
        var first = PlayStatistics.Summarize(games, range(a.Start));
        var second = PlayStatistics.Summarize(games, range(b.Start));
        foreach (var width in new[] { 130, 190, 190, 130 }) comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        void Line(string label, string left, string right, string difference, bool header = false)
        {
            var row = comparison.RowDefinitions.Count;
            comparison.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var texts = new[] { label, left, right, difference };
            for (var column = 0; column < texts.Length; column++)
            {
                var cell = column == 0 || header ? Muted(texts[column]) : new TextBlock { Text = texts[column], TextWrapping = TextWrapping.Wrap };
                cell.Margin = new Thickness(0, 0, 10, 8);
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                comparison.Children.Add(cell);
            }
        }
        static string Signed(long value, string text) => value > 0 ? "+" + text : value < 0 ? "−" + text : "same";
        static string Count(long value) => Signed(value, Math.Abs(value).ToString(CultureInfo.CurrentCulture));
        static string Top(PeriodStats stats) => stats.Top.Length == 0 ? "—" : $"{stats.Top[0].Name} · {Duration(stats.Top[0].Seconds)}";
        Line("", a.Label, b.Label, "Difference", true);
        Line("Time played", Duration(first.Seconds), Duration(second.Seconds), Signed(first.Seconds - second.Seconds, Duration(Math.Abs(first.Seconds - second.Seconds))));
        Line("Sessions", first.Sessions.ToString(CultureInfo.CurrentCulture), second.Sessions.ToString(CultureInfo.CurrentCulture), Count(first.Sessions - second.Sessions));
        Line("Games", first.Games.ToString(CultureInfo.CurrentCulture), second.Games.ToString(CultureInfo.CurrentCulture), Count(first.Games - second.Games));
        Line("Days played", first.DaysPlayed.ToString(CultureInfo.CurrentCulture), second.DaysPlayed.ToString(CultureInfo.CurrentCulture), Count(first.DaysPlayed - second.DaysPlayed));
        Line("Most played", Top(first), Top(second), "");
    }

    private static string Day(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        return block;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 10) };

    private static Border Card(string label, string value, string? detail = null)
    {
        var panel = new StackPanel();
        var caption = Muted(label);
        caption.FontSize = 12;
        panel.Children.Add(caption);
        panel.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
        if (detail is not null) { var small = Muted(detail); small.FontSize = 12; panel.Children.Add(small); }
        var card = new Border { Child = panel, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 12, 10), MinWidth = 150 };
        card.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        card.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        return card;
    }

    private static Button Tab(string label, object value, Action choose)
    {
        var button = new Button { Content = label, Tag = value };
        button.SetResourceReference(StyleProperty, "TabLink");
        button.Click += (_, _) => choose();
        return button;
    }

    private static void Mark(Panel tabs, object chosen)
    {
        foreach (var button in tabs.Children.OfType<Button>())
        {
            var on = chosen.Equals(button.Tag);
            button.SetResourceReference(BorderBrushProperty, on ? "AppAccent" : "AppLine");
            button.SetResourceReference(ForegroundProperty, on ? "AppText" : "AppMuted");
            AutomationProperties.SetItemStatus(button, on ? "Selected" : "");
        }
    }
}
