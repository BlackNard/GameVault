using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameVault.Core;

namespace GameVault.App;

/// <summary>A library game as the Lists page knows it.</summary>
public sealed record ListGame(Guid Id, string Name, bool Removed, string? CoverFile = null, string Played = "");

/// <summary>What was fetched for a list entry that is not a library game.</summary>
public sealed record ExternalGame(string? CoverFile, GameMetadataDocument? Info, bool Looking);

/// <summary>
/// The Lists page (ticket #97): the favourites and the player's lists as cards showing some of their covers; a list opens as a grid of game cards like the
/// library's; a game that is not in the library opens its own page here with the facts fetched for it.
/// </summary>
public sealed partial class ListsView : UserControl
{
    private const int PageSize = 60, Snippets = 5;
    private const double CoverWidth = 184, CoverHeight = 276;
    private readonly StackPanel page = new() { Margin = new Thickness(0, 0, 0, 32) };
    private readonly ScrollViewer scroll;
    private ListsDocument document = ListsDocument.Empty;
    private Dictionary<Guid, ListGame> games = [];
    private IReadOnlyDictionary<Guid, ExternalGame> outside = new Dictionary<Guid, ExternalGame>();
    private Guid? opened;           // null: all lists; empty: the favourites
    private Guid? detail;           // an entry outside the library, inside the opened list
    private int shown = PageSize;
    private string? problem;

    public event EventHandler? NewListRequested;
    public event EventHandler<GameList>? EditListRequested;
    public event EventHandler<GameList>? AddEntryRequested;
    public event EventHandler<(GameList List, ListEntry Entry)>? EditEntryRequested;
    public event EventHandler<Func<ListsDocument, ListsDocument>>? ChangeRequested;
    public event EventHandler<Guid>? GameRequested;
    public event EventHandler<ListEntry>? RefreshRequested;
    public event EventHandler<ListEntry>? MatchRequested;
    public event EventHandler<ListEntry>? CoverRequested;

    public ListsView()
    {
        scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var layers = new Grid();
        layers.Children.Add(scroll);
        layers.Children.Add(overlay);       // the card being dragged is drawn above everything
        Content = layers;
        PrepareDragging();
    }

    public void ShowProblem(string message) { problem = "The lists could not be read. " + message; Render(); }

    /// <summary>Opens a list the next time the lists are shown (a new one is not here yet).</summary>
    public void Select(Guid list) { opened = list; detail = null; shown = PageSize; }

    /// <summary>Back to the cards of all lists.</summary>
    public void ShowAll() { opened = null; detail = null; Render(); }

    public void Show(ListsDocument lists, IEnumerable<ListGame> library, IReadOnlyDictionary<Guid, ExternalGame>? external = null)
    {
        document = lists;
        games = library.ToDictionary(g => g.Id);
        outside = external ?? new Dictionary<Guid, ExternalGame>();
        problem = null;
        // A list that is gone leaves nothing to show.
        if (opened is { } id && id != Guid.Empty && Opened is null) opened = null;
        Render();
    }

    private GameList? Opened => opened is { } id ? document.Lists.FirstOrDefault(l => l.Id == id) : null;

    private void Render()
    {
        // The page is not rebuilt under a card that is being dragged.
        if (drag is not null) { renderPending = true; return; }
        var offset = scroll.VerticalOffset;
        page.Children.Clear();
        if (problem is not null) page.Children.Add(Muted(problem));
        if (Opened is { } list && detail is { } entry && list.Entries.FirstOrDefault(e => e.Id == entry && e.GameId is null) is { } chosen) RenderGame(list, chosen);
        else if (Opened is { } open) RenderList(open);
        else if (opened == Guid.Empty) RenderFavourites();
        else RenderAll();
        scroll.ScrollToVerticalOffset(offset);
    }

    private void Open(Guid? list, Guid? entry = null)
    {
        opened = list; detail = entry; shown = PageSize;
        Render();
        scroll.ScrollToTop();
    }

    // ---------- all lists ----------
    private void RenderAll()
    {
        var header = Header("Lists", null);
        var add = new Button { Content = "New list…", ToolTip = "Create a list of games", VerticalAlignment = VerticalAlignment.Center };
        add.SetResourceReference(StyleProperty, "PrimaryButton");
        add.Click += (_, _) => NewListRequested?.Invoke(this, EventArgs.Empty);
        header.Children.Add(add);
        page.Children.Add(header);
        var cards = new WrapPanel();
        var favourites = document.Favourites.Where(games.ContainsKey).Select(f => games[f]).ToArray();
        cards.Children.Add(ListCard("♥ Favourites", favourites.Length, [.. favourites.Take(Snippets).Select(g => (g.Name, g.CoverFile))], () => Open(Guid.Empty)));
        foreach (var list in document.Lists)
        {
            var id = list.Id;
            cards.Children.Add(ListCard(list.Name, list.Entries.Length, [.. list.Entries.Take(Snippets).Select(e => (NameOf(e), CoverOf(e)))], () => Open(id)));
        }
        page.Children.Add(cards);
    }

    private Button ListCard(string name, int count, (string Name, string? Cover)[] covers, Action open)
    {
        const double width = 100, height = 150, step = 50;
        var strip = new Canvas { Width = width + step * (Snippets - 1), Height = height, ClipToBounds = true };
        for (var i = 0; i < Snippets; i++)
        {
            var tile = i < covers.Length ? Cover(covers[i].Name, covers[i].Cover, width, height, 30) : Cover("", null, width, height, 30);
            tile.BorderThickness = new Thickness(0, 0, 1, 0);
            tile.SetResourceReference(Border.BorderBrushProperty, "AppBackground");
            Canvas.SetLeft(tile, i * step);
            Panel.SetZIndex(tile, Snippets - i);
            strip.Children.Add(tile);
        }
        var panel = new StackPanel { Width = strip.Width };
        panel.Children.Add(new Border { Child = strip, CornerRadius = new CornerRadius(8), ClipToBounds = true });
        panel.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 10, 0, 2), ToolTip = name });
        panel.Children.Add(Muted(count == 1 ? "1 game" : count.ToString(CultureInfo.CurrentCulture) + " games"));
        var card = Card(panel, "Open " + name);
        card.Margin = new Thickness(0, 0, 14, 14);
        card.Click += (_, _) => open();
        return card;
    }

    // ---------- one list ----------
    private void RenderFavourites()
    {
        page.Children.Add(Header("♥ Favourites", ("‹ All lists", () => Open(null))));
        var favourites = document.Favourites.Where(games.ContainsKey).Select(f => games[f]).ToArray();
        if (favourites.Length == 0) { page.Children.Add(Muted("No favourites yet. Mark a game with Favourite on its page or in its menu.")); return; }
        var grid = new WrapPanel();
        for (var i = 0; i < Math.Min(shown, favourites.Length); i++)
        {
            var game = favourites[i];
            var actions = new List<(string, bool, Action)>
            {
                ("Move earlier", i > 0, () => Change(d => Swap(d, game.Id, -1))),
                ("Move later", i < favourites.Length - 1, () => Change(d => Swap(d, game.Id, 1))),
                ("Remove from favourites", true, () => Change(d => GameListRules.ToggleFavourite(d, game.Id)))
            };
            Action? open = game.Removed ? null : () => GameRequested?.Invoke(this, game.Id);
            var card = GameCard(game.Name, game.Removed ? "Removed from the library" : game.Played, game.CoverFile, "#" + (i + 1), null, open, actions);
            Draggable(card, game.Id, (dragged, onto) => Change(d => d with { Favourites = GameListRules.MoveOnto(d.Favourites, f => f == dragged, f => f == onto) }));
            grid.Children.Add(card);
        }
        page.Children.Add(grid);
        AddMore(favourites.Length);
    }

    private void RenderList(GameList list)
    {
        var header = Header(list.Name, ("‹ All lists", () => Open(null)));
        var add = new Button { Content = "Add a game…", ToolTip = "From your library, or any game by its name", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        add.SetResourceReference(StyleProperty, "PrimaryButton");
        add.Click += (_, _) => AddEntryRequested?.Invoke(this, list);
        var edit = new Button { Content = "Edit list…", ToolTip = "Name, description, ranking, or delete the list", VerticalAlignment = VerticalAlignment.Center };
        edit.Click += (_, _) => EditListRequested?.Invoke(this, list);
        header.Children.Add(add);
        header.Children.Add(edit);
        page.Children.Add(header);
        if (list.Description.Length > 0) { var text = Muted(list.Description); text.Margin = new Thickness(0, 0, 0, 12); page.Children.Add(text); }
        if (list.Entries.Length == 0) { page.Children.Add(Muted("This list is empty.")); return; }
        var grid = new WrapPanel();
        for (var i = 0; i < Math.Min(shown, list.Entries.Length); i++)
        {
            var entry = list.Entries[i];
            var game = entry.GameId is { } id && games.TryGetValue(id, out var found) ? found : null;
            Action? open = game is { Removed: false } ? () => GameRequested?.Invoke(this, game.Id) : entry.GameId is null ? () => Open(list.Id, entry.Id) : null;
            var actions = new List<(string, bool, Action)>
            {
                ("Move earlier", i > 0, () => Change(d => Replace(d, list.Id, e => GameListRules.Move(e, entry.Id, -1)))),
                ("Move later", i < list.Entries.Length - 1, () => Change(d => Replace(d, list.Id, e => GameListRules.Move(e, entry.Id, 1)))),
                ("Note…", true, () => EditEntryRequested?.Invoke(this, (list, entry))),
                ("Remove from the list", true, () => Change(d => Replace(d, list.Id, e => [.. e.Where(x => x.Id != entry.Id)])))
            };
            var under = game is not null ? game.Removed ? "Removed from the library" : game.Played :
                entry.GameId is null && outside.GetValueOrDefault(entry.Id) is { Looking: true } ? "Looking up…" : "Not in the library";
            var card = GameCard(NameOf(entry), under, CoverOf(entry), list.Ranked ? "#" + (i + 1) : null, entry.Note, open, actions);
            Draggable(card, entry.Id, (dragged, onto) => Change(d => Replace(d, list.Id, e => GameListRules.MoveOnto(e, x => x.Id == dragged, x => x.Id == onto))));
            grid.Children.Add(card);
        }
        page.Children.Add(grid);
        AddMore(list.Entries.Length);
    }

    private void AddMore(int count)
    {
        if (count <= shown) return;
        var more = new Button { Content = "Show more", HorizontalAlignment = HorizontalAlignment.Left };
        more.Click += (_, _) => { shown += PageSize; Render(); };
        page.Children.Add(more);
    }

    private Button GameCard(string name, string under, string? cover, string? rank, string? note, Action? open, List<(string Text, bool Enabled, Action Run)> actions)
    {
        var top = new Grid { Width = CoverWidth, Height = CoverHeight };
        top.Children.Add(Cover(name, cover, CoverWidth, CoverHeight, 48));
        if (rank is not null) top.Children.Add(Badge(rank, HorizontalAlignment.Left, VerticalAlignment.Top));
        if (note is { Length: > 0 }) { var badge = Badge("Note", HorizontalAlignment.Left, VerticalAlignment.Bottom); badge.ToolTip = note; top.Children.Add(badge); }
        var menu = new ContextMenu();
        foreach (var action in actions)
        {
            var item = new MenuItem { Header = action.Text, IsEnabled = action.Enabled };
            item.Click += (_, e) => { e.Handled = true; action.Run(); };
            menu.Items.Add(item);
        }
        var more = new Button { Content = "⋯", Width = 36, Height = 36, MinHeight = 36, Padding = new Thickness(0), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Hidden, ToolTip = "More actions" };
        AutomationProperties.SetName(more, "More actions for " + name);
        more.Click += (_, e) => { e.Handled = true; menu.PlacementTarget = more; menu.IsOpen = true; };
        top.Children.Add(more);
        var panel = new StackPanel { Width = CoverWidth };
        panel.Children.Add(top);
        panel.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 12, 0, 3), ToolTip = name });
        var line = Muted(under);
        line.TextWrapping = TextWrapping.NoWrap;
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        line.Margin = new Thickness(2, 0, 0, 0);
        panel.Children.Add(line);
        var card = Card(panel, name);
        card.Margin = new Thickness(0, 0, 8, 12);
        card.ContextMenu = menu;
        void ShowMore() => more.Visibility = card.IsMouseOver || card.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Hidden;
        card.MouseEnter += (_, _) => ShowMore();
        card.MouseLeave += (_, _) => ShowMore();
        card.IsKeyboardFocusWithinChanged += (_, _) => ShowMore();
        card.Click += (_, e) => { if (!ReferenceEquals(e.OriginalSource, more)) open?.Invoke(); };
        return card;
    }

    // ---------- a game that is not in the library ----------
    private void RenderGame(GameList list, ListEntry entry)
    {
        var known = outside.GetValueOrDefault(entry.Id) ?? new ExternalGame(null, null, false);
        page.Children.Add(Header(entry.Title, ("‹ " + list.Name, () => Open(list.Id))));
        var columns = new DockPanel();
        var cover = Cover(entry.Title, known.CoverFile, 220, 330, 56);
        cover.VerticalAlignment = VerticalAlignment.Top;
        cover.Margin = new Thickness(0, 0, 24, 0);
        DockPanel.SetDock(cover, Dock.Left);
        columns.Children.Add(cover);
        var text = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        void Add(string label, string help, bool enabled, Action run)
        {
            var button = new Button { Content = label, IsEnabled = enabled, Margin = new Thickness(0, 0, 8, 8) };
            SettingHelp.SetText(button, help);
            button.Click += (_, _) => run();
            buttons.Children.Add(button);
        }
        Add("Refresh info", "Looks the details up again.", !known.Looking, () => RefreshRequested?.Invoke(this, entry));
        Add("Not the right game?", "Search and choose the correct game.", !known.Looking, () => MatchRequested?.Invoke(this, entry));
        Add("Choose cover…", "Use a picture from this PC.", true, () => CoverRequested?.Invoke(this, entry));
        Add("Note…", "Your note about this game in the list.", true, () => EditEntryRequested?.Invoke(this, (list, entry)));
        text.Children.Add(buttons);
        if (entry.Note is { Length: > 0 }) text.Children.Add(Paragraph(entry.Note));
        if (known.Looking) text.Children.Add(Muted("Looking up…"));
        var info = known.Info;
        string? Joined(MetadataField field) => info?.Get(field)?.Items is { Length: > 0 } items ? string.Join(", ", items) : null;
        var facts = new (string Label, string? Value)[]
        {
            ("Released", PartialDate.Parse(info?.Get(MetadataField.ReleaseDate)?.Date)?.Display()),
            ("Developer", Joined(MetadataField.Developers)),
            ("Publisher", Joined(MetadataField.Publishers)),
            ("Genres", Joined(MetadataField.Genres)),
            ("Platforms", Joined(MetadataField.Platforms)),
            ("Time to beat", info?.Get(MetadataField.TimeToBeat)?.Times is { MainSeconds: > 0 and var main } ? StatisticsView.Duration(main) + " main story" : null),
            ("Critic score", info?.Get(MetadataField.CriticScore)?.Number?.ToString(CultureInfo.CurrentCulture))
        };
        foreach (var (label, value) in facts.Where(f => !string.IsNullOrEmpty(f.Value)))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var caption = Muted(label);
            caption.Width = 120;
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(row);
        }
        if (info?.Get(MetadataField.Description)?.Text is { } description) { var body = Paragraph(description); body.Margin = new Thickness(0, 12, 0, 0); text.Children.Add(body); }
        else if (!known.Looking && facts.All(f => string.IsNullOrEmpty(f.Value))) text.Children.Add(Muted("Nothing was found for this title. Try Not the right game? to search with other words."));
        columns.Children.Add(text);
        page.Children.Add(columns);
    }

    // ---------- pieces ----------
    private string NameOf(ListEntry entry) => entry.GameId is { } id && games.TryGetValue(id, out var game) ? game.Name : entry.Title;

    private string? CoverOf(ListEntry entry) => entry.GameId is { } id ? games.GetValueOrDefault(id)?.CoverFile : outside.GetValueOrDefault(entry.Id)?.CoverFile;

    private static ListsDocument Replace(ListsDocument d, Guid list, Func<ListEntry[], ListEntry[]> change) =>
        d.Lists.FirstOrDefault(l => l.Id == list) is { } current ? GameListRules.Upsert(d, current with { Entries = change(current.Entries) }) : d;

    private static ListsDocument Swap(ListsDocument document, Guid game, int by)
    {
        var order = document.Favourites.ToList();
        var from = order.IndexOf(game);
        var to = from + by;
        if (from < 0 || to < 0 || to >= order.Count) return document;
        (order[from], order[to]) = (order[to], order[from]);
        return document with { Favourites = [.. order] };
    }

    private void Change(Func<ListsDocument, ListsDocument> change) => ChangeRequested?.Invoke(this, change);

    private static WrapPanel Header(string title, (string Text, Action Run)? back)
    {
        var header = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (back is { } to)
        {
            var button = new Button { Content = to.Text, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            button.Click += (_, _) => to.Run();
            header.Children.Add(button);
        }
        header.Children.Add(new TextBlock { Text = title, FontSize = 30, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 700,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) });
        return header;
    }

    private static Button Card(UIElement content, string name)
    {
        var card = new Button { Content = content, VerticalAlignment = VerticalAlignment.Top };
        card.SetResourceReference(StyleProperty, "CardButton");
        AutomationProperties.SetName(card, name);
        return card;
    }

    /// <summary>A cover, or the title's first letter while there is none.</summary>
    private static Border Cover(string name, string? file, double width, double height, double letter)
    {
        var initial = Muted(name.Length > 0 ? name[..1].ToUpperInvariant() : "");
        initial.FontSize = letter;
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        var image = new Image { Stretch = Stretch.UniformToFill };
        var layers = new Grid();
        layers.Children.Add(initial);
        layers.Children.Add(image);
        var border = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = layers };
        border.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        border.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 10, 10);
        if (file is not null) LoadInto(image, initial, file);
        return border;
    }

    private static async void LoadInto(Image image, TextBlock initial, string file)
    {
        try { image.Source = await CoverImages.LoadThumbnailAsync(file); initial.Visibility = Visibility.Collapsed; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    private static Border Badge(string text, HorizontalAlignment across, VerticalAlignment down) => new()
    {
        HorizontalAlignment = across, VerticalAlignment = down, Margin = new Thickness(10), Padding = new Thickness(9, 3, 9, 3), CornerRadius = new CornerRadius(11),
        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0E, 0x14, 0x1D)),
        Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xF2, 0xF8)) }
    };

    private static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        return block;
    }
}
