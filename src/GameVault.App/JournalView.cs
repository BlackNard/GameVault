using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

/// <summary>The Journal page (ticket #96): what was played and written, newest first, across all games.</summary>
public sealed class JournalView : UserControl
{
    private const int PageSize = 60;
    private readonly ComboBox gameChoice = new() { Width = 260, Margin = new Thickness(0, 0, 10, 8) };
    private readonly ComboBox monthChoice = new() { Width = 200, Margin = new Thickness(0, 0, 10, 8) };
    private readonly Button addButton = new() { Content = "Add entry…", Margin = new Thickness(0, 0, 0, 8), IsEnabled = false };
    private readonly TextBlock notice = Muted("");
    private readonly StackPanel entries = new();
    private readonly Button more = new() { Content = "Show more", HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private JournalFeed feed = new([], [], []);
    private DateOnly? day;
    private int shown = PageSize;
    private bool filling;

    public event EventHandler<Guid?>? AddRequested;
    public event EventHandler<(Guid Game, Guid Entry)>? EditRequested;
    /// <summary>A session's note is read and edited in its game's Sessions &amp; notes.</summary>
    public event EventHandler<Guid>? GameRequested;

    public JournalView()
    {
        var page = new StackPanel { MinWidth = 640, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 32) };
        page.Children.Add(new TextBlock { Text = "Journal", FontSize = 30, FontWeight = FontWeights.SemiBold });
        var intro = Muted("Your sessions and notes, and entries you add yourself. Added entries do not change recorded playtime.");
        intro.Margin = new Thickness(0, 6, 0, 14);
        page.Children.Add(intro);
        var filters = new WrapPanel();
        AutomationProperties.SetName(gameChoice, "Game");
        AutomationProperties.SetName(monthChoice, "Period");
        addButton.SetResourceReference(StyleProperty, "PrimaryButton");
        filters.Children.Add(gameChoice);
        filters.Children.Add(monthChoice);
        filters.Children.Add(addButton);
        page.Children.Add(filters);
        notice.Margin = new Thickness(0, 6, 0, 6);
        page.Children.Add(notice);
        page.Children.Add(entries);
        page.Children.Add(more);
        gameChoice.SelectionChanged += (_, _) => { if (!filling) { shown = PageSize; Render(); } };
        monthChoice.SelectionChanged += (_, _) => { if (!filling) { shown = PageSize; day = null; Render(); } };
        addButton.Click += (_, _) => AddRequested?.Invoke(this, SelectedGame);
        more.Click += (_, _) => { shown += PageSize; Render(); };
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private Guid? SelectedGame => gameChoice.SelectedItem is ComboBoxItem { Tag: Guid id } ? id : null;

    public void ShowProblem(string message) => notice.Text = "The journal could not be read. " + message;

    /// <summary>Shows the feed, keeping the chosen filters. <paramref name="onDay"/> narrows it to one day (from the statistics calendar).</summary>
    public void Show(JournalFeed data, DateOnly? onDay = null)
    {
        var game = SelectedGame;
        var month = (monthChoice.SelectedItem as ComboBoxItem)?.Tag as DateOnly?;
        feed = data;
        day = onDay;
        shown = PageSize;
        filling = true;
        gameChoice.Items.Clear();
        gameChoice.Items.Add(new ComboBoxItem { Content = "All games" });
        foreach (var (id, name, removed) in feed.Games) gameChoice.Items.Add(new ComboBoxItem { Content = removed ? name + " (removed)" : name, Tag = id });
        gameChoice.SelectedItem = gameChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => onDay is null && i.Tag is Guid id && id == game) ?? gameChoice.Items[0];
        monthChoice.Items.Clear();
        monthChoice.Items.Add(new ComboBoxItem { Content = "All time" });
        foreach (var start in feed.Items.Select(i => new DateOnly(i.When.Year, i.When.Month, 1)).Distinct().OrderDescending())
            monthChoice.Items.Add(new ComboBoxItem { Content = start.ToString("MMMM yyyy", CultureInfo.CurrentCulture), Tag = start });
        monthChoice.SelectedItem = monthChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => onDay is null && i.Tag is DateOnly start && start == month) ?? monthChoice.Items[0];
        filling = false;
        addButton.IsEnabled = feed.Games.Any(g => !g.Removed);
        Render();
    }

    private void Render()
    {
        var month = (monthChoice.SelectedItem as ComboBoxItem)?.Tag as DateOnly?;
        var (from, to) = day is { } one ? (one, (DateOnly?)one) : month is { } start ? (start, start.AddMonths(1).AddDays(-1)) : ((DateOnly?)null, (DateOnly?)null);
        var items = JournalRules.Filter(feed.Items, SelectedGame, from, to);
        var lines = new List<string>(feed.Warnings.Take(5));
        if (day is { } chosen) lines.Insert(0, "Showing " + chosen.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) + ". Choose a period to see more.");
        if (items.Length == 0) lines.Add(feed.Items.Length == 0 ? "Nothing yet. Play a game, or add an entry." : "Nothing matches.");
        notice.Text = string.Join("\n", lines);
        notice.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        entries.Children.Clear();
        DateOnly? heading = null;
        foreach (var item in items.Take(shown))
        {
            var date = DateOnly.FromDateTime(item.When);
            if (date != heading)
            {
                heading = date;
                entries.Children.Add(new TextBlock { Text = date.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
            }
            entries.Children.Add(Card(item));
        }
        more.Visibility = items.Length > shown ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border Card(JournalItem item)
    {
        var panel = new DockPanel();
        var button = new Button { Content = item.EntryId is null ? "Open" : "Edit", MinWidth = 70, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(button, (item.EntryId is null ? "Open the notes of " : "Edit the entry for ") + item.Game);
        button.Click += (_, _) => { if (item.EntryId is { } entry) EditRequested?.Invoke(this, (item.GameId, entry)); else GameRequested?.Invoke(this, item.GameId); };
        var removed = feed.Games.FirstOrDefault(g => g.Id == item.GameId).Removed;
        button.Visibility = removed ? Visibility.Collapsed : Visibility.Visible;
        DockPanel.SetDock(button, Dock.Right);
        panel.Children.Add(button);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = item.Game, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var facts = new List<string> { item.EntryId is null ? "Session at " + item.When.ToString("t", CultureInfo.CurrentCulture) : "Added entry" };
        if (item.Seconds is > 0) facts.Add(StatisticsView.Duration(item.Seconds.Value));
        facts.AddRange(item.Tags.Select(t => "#" + t));
        text.Children.Add(Muted(string.Join(" · ", facts)));
        if (item.Title is { Length: > 0 }) text.Children.Add(new TextBlock { Text = item.Title, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (item.Note is { Length: > 0 } note)
            text.Children.Add(new TextBlock { Text = note.Length > 600 ? note[..600] + "…" : note, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(text);
        var card = new Border { Child = panel, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
        card.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        card.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        return card;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        return block;
    }
}
