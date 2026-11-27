using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

/// <summary>The Journal page (ticket #96): the player's own entries, about a game or about nothing in particular, newest first. Session notes stay on their game.</summary>
public sealed class JournalView : UserControl
{
    private const int PageSize = 60;
    private readonly ComboBox gameChoice = new() { Width = 260, Margin = new Thickness(0, 0, 10, 8), ToolTip = "Show entries for" };
    private readonly ComboBox monthChoice = new() { Width = 200, Margin = new Thickness(0, 0, 10, 8), ToolTip = "Period" };
    private readonly Button addButton = new() { Content = "Add entry…", Margin = new Thickness(0, 0, 0, 8), IsEnabled = false, ToolTip = "Write about a game, or anything else" };
    private readonly TextBlock notice = Muted("");
    private readonly StackPanel entries = new();
    private readonly Button more = new() { Content = "Show more", HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private JournalFeed feed = new([], [], []);
    private int shown = PageSize;
    private bool filling;

    /// <summary>A new entry, for the game being shown (<see cref="Guid.Empty"/> for none).</summary>
    public event EventHandler<Guid>? AddRequested;
    public event EventHandler<JournalItem>? EditRequested;
    /// <summary>Entries are exported one at a time (the owner's choice: never a whole pile by accident).</summary>
    public event EventHandler<JournalItem>? ExportRequested;

    public JournalView()
    {
        var page = new StackPanel { MinWidth = 640, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 32) };
        page.Children.Add(new TextBlock { Text = "Journal", FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
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
        monthChoice.SelectionChanged += (_, _) => { if (!filling) { shown = PageSize; Render(); } };
        addButton.Click += (_, _) => AddRequested?.Invoke(this, SelectedGame ?? Guid.Empty);
        more.Click += (_, _) => { shown += PageSize; Render(); };
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private Guid? SelectedGame => gameChoice.SelectedItem is ComboBoxItem { Tag: Guid id } ? id : null;

    public void ShowProblem(string message) { notice.Text = "The journal could not be read. " + message; notice.Visibility = Visibility.Visible; }

    /// <summary>Shows the feed, keeping the chosen filters.</summary>
    public void Show(JournalFeed data)
    {
        var game = SelectedGame;
        var month = (monthChoice.SelectedItem as ComboBoxItem)?.Tag as DateOnly?;
        feed = data;
        shown = PageSize;
        filling = true;
        gameChoice.Items.Clear();
        gameChoice.Items.Add(new ComboBoxItem { Content = "All entries" });
        gameChoice.Items.Add(new ComboBoxItem { Content = "No game", Tag = Guid.Empty });
        // Only games that have something to show, or can still get an entry.
        foreach (var (id, name, removed) in feed.Games.Where(g => !g.Removed || feed.Items.Any(i => i.GameId == g.Id)))
            gameChoice.Items.Add(new ComboBoxItem { Content = removed ? name + " (removed)" : name, Tag = id });
        gameChoice.SelectedItem = gameChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is Guid id && id == game) ?? gameChoice.Items[0];
        monthChoice.Items.Clear();
        monthChoice.Items.Add(new ComboBoxItem { Content = "All time" });
        foreach (var start in feed.Items.Select(i => new DateOnly(i.Entry.Date.Year, i.Entry.Date.Month, 1)).Distinct().OrderDescending())
            monthChoice.Items.Add(new ComboBoxItem { Content = start.ToString("MMMM yyyy", CultureInfo.CurrentCulture), Tag = start });
        monthChoice.SelectedItem = monthChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is DateOnly start && start == month) ?? monthChoice.Items[0];
        filling = false;
        addButton.IsEnabled = true;
        Render();
    }

    private JournalItem[] Matching()
    {
        var month = (monthChoice.SelectedItem as ComboBoxItem)?.Tag as DateOnly?;
        return JournalRules.Filter(feed.Items, SelectedGame, month, month?.AddMonths(1).AddDays(-1));
    }

    private void Render()
    {
        var items = Matching();
        var lines = new List<string>(feed.Warnings.Take(5));
        if (items.Length == 0) lines.Add(feed.Items.Length == 0 ? "No entries yet." : "Nothing matches.");
        notice.Text = string.Join("\n", lines);
        notice.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        entries.Children.Clear();
        DateOnly? heading = null;
        foreach (var item in items.Take(shown))
        {
            if (item.Entry.Date != heading)
            {
                heading = item.Entry.Date;
                entries.Children.Add(new TextBlock { Text = item.Entry.Date.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
            }
            entries.Children.Add(Card(item));
        }
        more.Visibility = items.Length > shown ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border Card(JournalItem item)
    {
        var entry = item.Entry;
        var name = entry.Title ?? item.Game ?? "Entry";
        var panel = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        // An entry of a removed game can still be exported, but not changed.
        var removed = item.GameId != Guid.Empty && feed.Games.Any(g => g.Id == item.GameId && g.Removed);
        if (!removed)
        {
            var edit = new Button { Content = "Edit", ToolTip = "Change or delete this entry", MinWidth = 70, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(edit, "Edit " + name);
            edit.Click += (_, _) => EditRequested?.Invoke(this, item);
            buttons.Children.Add(edit);
        }
        var export = new Button { Content = "Export", MinWidth = 70, ToolTip = "Export to Obsidian" };
        AutomationProperties.SetName(export, "Export " + name + " to Obsidian");
        export.Click += (_, _) => ExportRequested?.Invoke(this, item);
        buttons.Children.Add(export);
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(buttons);
        var text = new StackPanel();
        if (entry.Title is { } title) text.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var facts = new List<string>();
        if (item.Game is { } game) facts.Add(removed ? game + " (removed)" : game);
        if (entry.Minutes is > 0) facts.Add(StatisticsView.Duration(entry.Minutes.Value * 60L));
        facts.AddRange(entry.Tags.Select(t => "#" + t.TrimStart('#')));
        if (facts.Count > 0) text.Children.Add(Muted(string.Join(" · ", facts)));
        if (entry.Note is { Length: > 0 } note)
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
