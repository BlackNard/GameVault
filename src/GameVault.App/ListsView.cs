using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

/// <summary>A library game as the Lists page knows it.</summary>
public sealed record ListGame(Guid Id, string Name, bool Removed);

/// <summary>The Lists page (ticket #97): favourites and the player's own lists, in their order.</summary>
public sealed class ListsView : UserControl
{
    private const int TopFavourites = 4;
    private readonly WrapPanel tabs = new() { Margin = new Thickness(0, 0, 0, 14) };
    private readonly StackPanel body = new();
    private ListsDocument document = ListsDocument.Empty;
    private Dictionary<Guid, ListGame> games = [];
    private Guid selected;          // empty: the favourites

    public event EventHandler? NewListRequested;
    public event EventHandler<GameList>? EditListRequested;
    public event EventHandler<GameList>? AddEntryRequested;
    public event EventHandler<(GameList List, ListEntry Entry)>? EditEntryRequested;
    public event EventHandler<Func<ListsDocument, ListsDocument>>? ChangeRequested;
    public event EventHandler<Guid>? GameRequested;

    public ListsView()
    {
        var page = new StackPanel { MinWidth = 640, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 32) };
        page.Children.Add(new TextBlock { Text = "Lists", FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        page.Children.Add(tabs);
        page.Children.Add(body);
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    public void ShowProblem(string message) { body.Children.Clear(); body.Children.Add(Muted("The lists could not be read. " + message)); }

    public void Select(Guid list) => selected = list;

    public void Show(ListsDocument lists, IEnumerable<ListGame> library)
    {
        document = lists;
        games = library.ToDictionary(g => g.Id);
        if (selected != Guid.Empty && lists.Lists.All(l => l.Id != selected)) selected = Guid.Empty;
        Render();
    }

    private void Render()
    {
        tabs.Children.Clear();
        tabs.Children.Add(Tab("Favourites", Guid.Empty));
        foreach (var list in document.Lists) tabs.Children.Add(Tab(list.Name, list.Id));
        var add = new Button { Content = "New list…", Margin = new Thickness(8, 0, 0, 0) };
        add.Click += (_, _) => NewListRequested?.Invoke(this, EventArgs.Empty);
        tabs.Children.Add(add);
        body.Children.Clear();
        if (document.Lists.FirstOrDefault(l => l.Id == selected) is { } chosen) RenderList(chosen);
        else RenderFavourites();
    }

    private void RenderFavourites()
    {
        var favourites = document.Favourites.Where(games.ContainsKey).Select(f => games[f]).ToArray();
        if (favourites.Length == 0) { body.Children.Add(Muted("No favourites yet. Mark a game with Favourite on its page or in its menu.")); return; }
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (game, index) in favourites.Take(TopFavourites).Select((g, i) => (g, i)))
        {
            var panel = new StackPanel { Width = 180 };
            panel.Children.Add(Muted("#" + (index + 1)));
            panel.Children.Add(new TextBlock { Text = game.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            top.Children.Add(Boxed(panel));
        }
        body.Children.Add(top);
        for (var i = 0; i < favourites.Length; i++)
        {
            var game = favourites[i];
            var at = i;
            var actions = new List<(string, string, bool, Action)>
            {
                ("↑", "Move up", i > 0, () => Change(d => Swap(d, game.Id, -1))),
                ("↓", "Move down", i < favourites.Length - 1, () => Change(d => Swap(d, game.Id, 1))),
                ("Remove", "Remove from favourites", true, () => Change(d => GameListRules.ToggleFavourite(d, game.Id)))
            };
            body.Children.Add(Row($"{at + 1}.", game.Name, game.Removed ? "Removed from the library" : null, game.Removed ? null : game.Id, actions));
        }
    }

    private static ListsDocument Swap(ListsDocument document, Guid game, int by)
    {
        var order = document.Favourites.ToList();
        var from = order.IndexOf(game);
        var to = from + by;
        if (from < 0 || to < 0 || to >= order.Count) return document;
        (order[from], order[to]) = (order[to], order[from]);
        return document with { Favourites = [.. order] };
    }

    private void RenderList(GameList list)
    {
        if (list.Description.Length > 0) { var text = Muted(list.Description); text.Margin = new Thickness(0, 0, 0, 10); body.Children.Add(text); }
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var addEntry = new Button { Content = "Add a game…", Margin = new Thickness(0, 0, 10, 0) };
        addEntry.SetResourceReference(StyleProperty, "PrimaryButton");
        addEntry.Click += (_, _) => AddEntryRequested?.Invoke(this, list);
        var edit = new Button { Content = "Edit list…" };
        edit.Click += (_, _) => EditListRequested?.Invoke(this, list);
        buttons.Children.Add(addEntry);
        buttons.Children.Add(edit);
        body.Children.Add(buttons);
        if (list.Entries.Length == 0) body.Children.Add(Muted("This list is empty."));
        for (var i = 0; i < list.Entries.Length; i++)
        {
            var entry = list.Entries[i];
            var game = entry.GameId is { } id && games.TryGetValue(id, out var found) ? found : null;
            ListsDocument Replace(ListsDocument d, Func<ListEntry[], ListEntry[]> change) =>
                d.Lists.FirstOrDefault(l => l.Id == list.Id) is { } current ? GameListRules.Upsert(d, current with { Entries = change(current.Entries) }) : d;
            var actions = new List<(string, string, bool, Action)>
            {
                ("↑", "Move up", i > 0, () => Change(d => Replace(d, e => GameListRules.Move(e, entry.Id, -1)))),
                ("↓", "Move down", i < list.Entries.Length - 1, () => Change(d => Replace(d, e => GameListRules.Move(e, entry.Id, 1)))),
                ("Note…", "Edit the note", true, () => EditEntryRequested?.Invoke(this, (list, entry))),
                ("Remove", "Remove from the list", true, () => Change(d => Replace(d, e => [.. e.Where(x => x.Id != entry.Id)])))
            };
            var detail = string.Join(" · ", new[] { game is null ? "Not in the library" : game.Removed ? "Removed from the library" : null, entry.Note }.Where(s => !string.IsNullOrEmpty(s)));
            body.Children.Add(Row(list.Ranked ? $"{i + 1}." : "•", game?.Name ?? entry.Title, detail, game is { Removed: false } ? game.Id : null, actions));
        }
    }

    private void Change(Func<ListsDocument, ListsDocument> change) => ChangeRequested?.Invoke(this, change);

    private Border Row(string number, string name, string? detail, Guid? open, IEnumerable<(string Text, string Name, bool Enabled, Action Run)> actions)
    {
        var panel = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions)
        {
            var button = new Button { Content = action.Text, IsEnabled = action.Enabled, Margin = new Thickness(6, 0, 0, 0), MinWidth = 36, ToolTip = action.Name };
            AutomationProperties.SetName(button, $"{action.Name}: {name}");
            button.Click += (_, _) => action.Run();
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(buttons);
        var rank = Muted(number);
        rank.Width = 34;
        rank.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(rank);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (open is { } id)
        {
            var link = new Button { Content = name, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 2, 0, 2), ToolTip = "Open " + name };
            link.SetResourceReference(StyleProperty, "TabLink");
            link.SetResourceReference(ForegroundProperty, "AppText");
            link.Click += (_, _) => GameRequested?.Invoke(this, id);
            text.Children.Add(link);
        }
        else text.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(detail)) text.Children.Add(Muted(detail));
        panel.Children.Add(text);
        return Boxed(panel, stretch: true);
    }

    private static Border Boxed(UIElement child, bool stretch = false)
    {
        var box = new Border { Child = child, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, stretch ? 0 : 10, 8) };
        box.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        box.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        return box;
    }

    private Button Tab(string label, Guid list)
    {
        var button = new Button { Content = label };
        button.SetResourceReference(StyleProperty, "TabLink");
        button.SetResourceReference(BorderBrushProperty, list == selected ? "AppAccent" : "AppLine");
        button.SetResourceReference(ForegroundProperty, list == selected ? "AppText" : "AppMuted");
        AutomationProperties.SetItemStatus(button, list == selected ? "Selected" : "");
        button.Click += (_, _) => { selected = list; Render(); };
        return button;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        return block;
    }
}
