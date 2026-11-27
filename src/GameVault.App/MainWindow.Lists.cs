using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

// The Lists page and favourites (issue #97).
public partial class MainWindow
{
    private ListsView? listsView;
    private ListsDocument lists = ListsDocument.Empty;

    private bool ListsVisible => ListsPanel.Visibility == Visibility.Visible;

    private async void ShowLists(object sender, RoutedEventArgs e)
    {
        // The sidebar goes to all lists, also from inside one.
        if (ListsVisible) listsView?.ShowAll();
        else if (await NavigateToAsync(NavRoute.Lists)) listsView?.ShowAll();
    }

    private async Task ShowListsAsync()
    {
        if (listsView is null)
        {
            listsView = new ListsView();
            listsView.ChangeRequested += async (_, change) => await ChangeListsAsync(change);
            listsView.NewListRequested += async (_, _) => await EditListAsync(null);
            listsView.EditListRequested += async (_, list) => await EditListAsync(list);
            listsView.AddEntryRequested += async (_, list) => await AddListEntryAsync(list);
            listsView.EditEntryRequested += async (_, which) => await EditListNoteAsync(which.List, which.Entry);
            listsView.GameRequested += async (_, id) => await NavigateToAsync(NavRoute.Game(id, "Overview"));
            listsView.RefreshRequested += async (_, entry) => await RefreshListGameAsync(entry);
            listsView.MatchRequested += async (_, entry) => await MatchListGameAsync(entry);
            listsView.CoverRequested += async (_, entry) => await ChooseListCoverAsync(entry);
            ListsPanel.Content = listsView;
        }
        libraryVisible = false;
        LibraryPanel.Visibility = SettingsPanel.Visibility = GamePage.Visibility = OverviewPanel.Visibility = StatisticsPanel.Visibility = JournalPanel.Visibility = Visibility.Collapsed;
        ListsPanel.Visibility = Visibility.Visible;
        RecordRoute();
        await LoadListsAsync();
    }

    /// <summary>Reads the lists and shows them where they are used.</summary>
    private async Task LoadListsAsync()
    {
        try
        {
            var stored = await store.LoadListsAsync();
            lists = stored.Value ?? ListsDocument.Empty;
            await LoadListGamesAsync();
            ShowListsState();
            if (stored.Message is { } message && ListsVisible) listsView?.ShowProblem(message);
            if (ListsVisible) _ = LookUpNewListGamesAsync();
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { listsView?.ShowProblem(error.Message); }
    }

    private void ShowListsState()
    {
        if (ListsVisible)
            listsView?.Show(lists, items.Where(i => i.Game.Profile is not null).Select(i => new ListGame(i.Game.Id, i.Name, i.Game.Profile!.IsArchived, i.Game.CoverFile, i.Played)),
                listGames.ToDictionary(p => p.Key, p => p.Value with { Looking = listLookups.Contains(p.Key) }));
        ShowFavourite(GameList.SelectedItem as LibraryItemView);
    }

    private async Task ChangeListsAsync(Func<ListsDocument, ListsDocument> change)
    {
        try { lists = await store.ChangeListsAsync(change); await LoadListGamesAsync(); ShowListsState(); }
        catch (Exception error) when (IsExpected(error) || error is IOException or ArgumentException) { ShowToast("The list was not changed", error.Message, true); }
    }

    private void ShowFavourite(LibraryItemView? item)
    {
        var show = item?.Game.Profile is { IsArchived: false };
        HeroFavouriteButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) HeroFavouriteButton.Content = lists.Favourites.Contains(item!.Game.Id) ? "♥ Favourite" : "♡ Favourite";
    }

    private async void HeroFavouriteClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is LibraryItemView item) await ChangeListsAsync(d => GameListRules.ToggleFavourite(d, item.Game.Id));
    }

    /// <summary>The favourite entry and the "Add to list" submenu of a card's menu.</summary>
    private void FillListMenu(MenuItem favourite, MenuItem addTo, LibraryItemView item)
    {
        favourite.IsChecked = lists.Favourites.Contains(item.Game.Id);
        addTo.Items.Clear();
        foreach (var list in lists.Lists)
        {
            var target = list.Id;
            var entry = new MenuItem { Header = list.Name, IsChecked = list.Entries.Any(x => x.GameId == item.Game.Id) };
            entry.Click += async (_, e) =>
            {
                e.Handled = true;
                await ChangeListsAsync(d => d.Lists.FirstOrDefault(l => l.Id == target) is { } current
                    ? GameListRules.Upsert(d, current with { Entries = GameListRules.Add(current.Entries, new(Guid.NewGuid(), item.Game.Id, item.Name, null)) }) : d);
            };
            addTo.Items.Add(entry);
        }
        if (lists.Lists.Length == 0) addTo.Items.Add(new MenuItem { Header = "Create a list on the Lists page", IsEnabled = false });
    }

    private async void MenuFavouriteClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is LibraryItemView item) await ChangeListsAsync(d => GameListRules.ToggleFavourite(d, item.Game.Id));
    }

    private async Task EditListAsync(GameList? existing)
    {
        var list = existing ?? new GameList(Guid.NewGuid(), "", "", false, []);
        TextBox name = null!, description = null!;
        CheckBox ranked = null!;
        var form = new FormView(settings.Theme, existing is null ? "New list" : "Edit list", async () =>
        {
            // The boxes are read here: the change itself runs on the store's thread, which may not touch them.
            var (title, about, numbered) = (name.Text, description.Text.Trim(), ranked.IsChecked == true);
            // Entries are taken from the stored list, so nothing added meanwhile is lost.
            await store.ChangeListsAsync(d => GameListRules.Upsert(d, (d.Lists.FirstOrDefault(l => l.Id == list.Id) ?? list) with
            { Name = title, Description = about, Ranked = numbered }));
            listsView?.Select(list.Id);
        }, existing is null ? null : () => store.ChangeListsAsync(d => GameListRules.Remove(d, existing.Id)));
        name = form.Text("Name", list.Name, 80);
        description = form.Text("Description", list.Description, 1000, lines: true);
        ranked = form.Check("Ranked", list.Ranked, "Numbers the games in their order.");
        if (await ShowFormAsync(form)) await LoadListsAsync();
    }

    private async Task AddListEntryAsync(GameList list)
    {
        var library = items.Where(i => i.Game.Profile is { IsArchived: false }).ToArray();
        ComboBox game = null!;
        TextBox title = null!, note = null!;
        var form = new FormView(settings.Theme, "Add to " + list.Name, () =>
        {
            var chosen = FormView.Chosen<Guid>(game);
            var typed = title.Text.Trim();
            // A typed name that is a library game's is that game.
            var item = library.FirstOrDefault(i => i.Game.Id == chosen) ?? library.FirstOrDefault(i => string.Equals(i.Name, typed, StringComparison.CurrentCultureIgnoreCase));
            var text = item?.Name ?? typed;
            if (text.Length == 0) throw new ArgumentException("Choose a game from your library, or type its name.");
            var comment = note.Text.Trim() is { Length: > 0 } n ? n : null;
            return store.ChangeListsAsync(d => d.Lists.FirstOrDefault(l => l.Id == list.Id) is { } current
                ? GameListRules.Upsert(d, current with { Entries = GameListRules.Add(current.Entries, new(Guid.NewGuid(), item?.Game.Id, text, comment)) }) : d);
        });
        game = form.Choice("Game from your library", library.Select(i => (i.Game.Id, i.Name)).Prepend((Guid.Empty, "Another game")), Guid.Empty);
        title = form.Text("Another game's name (looked up online)", "", 200, help: "Its cover and details are fetched by this name.");
        note = form.Text("Note", "", 1000, lines: true, help: "Optional.");
        if (await ShowFormAsync(form)) await LoadListsAsync();
    }

    private async Task EditListNoteAsync(GameList list, ListEntry entry)
    {
        TextBox note = null!;
        var form = new FormView(settings.Theme, "Note for " + entry.Title, () =>
        {
            var comment = note.Text.Trim() is { Length: > 0 } n ? n : null;
            return store.ChangeListsAsync(d => d.Lists.FirstOrDefault(l => l.Id == list.Id) is { } current
                ? GameListRules.Upsert(d, current with { Entries = [.. current.Entries.Select(e => e.Id == entry.Id ? e with { Note = comment } : e)] }) : d);
        });
        note = form.Text("Note", entry.Note, 1000, lines: true);
        if (await ShowFormAsync(form)) await LoadListsAsync();
    }
}
