using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GameVault.Core;

namespace GameVault.App;

// Library chips and layout switch, card and context-menu actions, the left rail and the settings section menu (UI overhaul, ticket #90).
public partial class MainWindow
{
    private bool syncingChips;
    private readonly List<Guid> recentGameIds = [];

    // ---------- library layout and filters ----------
    private void LibraryModeChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || GameList is null) return;
        var grid = GridModeButton.IsChecked == true;
        GameList.ItemTemplate = (DataTemplate)GameList.FindResource(grid ? "CardTemplate" : "RowTemplate");
        GameList.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(grid ? typeof(VirtualizingCoverPanel) : typeof(VirtualizingStackPanel)));
        ReloadVisibleCovers();
    }

    private void StatusChipChecked(object sender, RoutedEventArgs e)
    {
        if (initializing || syncingChips || sender is not RadioButton { Tag: string tag }) return;
        foreach (ComboBoxItem item in StatusFilter.Items)
            if ((string?)item.Tag == tag) { StatusFilter.SelectedItem = item; break; }
    }

    private void RemovedChipChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || syncingChips) return;
        RemovedFilter.IsChecked = ChipRemoved.IsChecked;
    }

    /// <summary>Keeps the visible chips in step with the hidden status filter and removed checkbox that the filtering code uses.</summary>
    private void SyncChips()
    {
        if (ChipAll?.Parent is not Panel chips) return;
        syncingChips = true;
        try
        {
            var tag = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
            foreach (var chip in chips.Children.OfType<RadioButton>()) chip.IsChecked = (string?)chip.Tag == tag;
            ChipRemoved.IsChecked = RemovedFilter.IsChecked == true;
        }
        finally { syncingChips = false; }
    }

    // ---------- card, row and context menu actions ----------
    private static LibraryItemView? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as LibraryItemView;

    private void GameContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ItemOf(sender) is { } item) GameList.SelectedItem = item;
    }

    private void GameContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var entry in menu.Items.OfType<MenuItem>())
            entry.IsEnabled = (entry.Header as string) switch
            {
                "Play" => PlayButton.IsEnabled,
                "Back up now" => BackupButton.IsEnabled,
                "Edit game…" => EditButton.IsEnabled,
                "Remove from library" => ArchiveButton.IsEnabled,
                _ => GameList.SelectedItem is not null
            };
    }

    private void CardPlayClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;   // do not also open the game page
        if (ItemOf(sender) is not { } item) return;
        GameList.SelectedItem = item;
        PlayGame(sender, e);
    }

    private void CardMoreClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ItemOf(sender) is not { } item || sender is not UIElement target) return;
        GameList.SelectedItem = item;
        var menu = (ContextMenu)GameList.FindResource("GameContextMenu");
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void MenuPlayClicked(object sender, RoutedEventArgs e) => PlayGame(sender, e);
    private void MenuBackUpClicked(object sender, RoutedEventArgs e) => BackUpNow(sender, e);

    private async void MenuNotesClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is LibraryItemView item) await NavigateToAsync(NavRoute.Game(item.Game.Id, "Sessions"));
    }

    private void OpenInstallFolderClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LibraryItemView { Game.Profile: { } profile }) return;
        try
        {
            if (File.Exists(profile.ExecutablePath))
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + profile.ExecutablePath + "\"") { UseShellExecute = false });
            else if (Directory.Exists(profile.WorkingDirectory))
                Process.Start(new ProcessStartInfo(profile.WorkingDirectory) { UseShellExecute = true });
            else
                ShowToast("Install folder not found", "Edit the game to fix its location.", true);
        }
        catch (Exception error) when (IsExpected(error) || error is Win32Exception)
        {
            ShowToast("Could not open the folder", error.Message, true);
        }
    }

    // ---------- left rail ----------
    private void UpdateRecent(NavRoute? route = null)
    {
        if (route is { Kind: NavKind.Game, GameId: Guid id })
        {
            recentGameIds.Remove(id);
            recentGameIds.Insert(0, id);
            if (recentGameIds.Count > 4) recentGameIds.RemoveRange(4, recentGameIds.Count - 4);
        }
        var shown = recentGameIds.Select(game => items.FirstOrDefault(i => i.Game.Id == game)).Where(i => i is not null).ToArray();
        RecentList.ItemsSource = shown;
        foreach (var item in shown) _ = item!.LoadRailIconAsync();
        RecentHeader.Visibility = shown.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateRailSelection(NavRoute route)
    {
        void Mark(Button button, bool on)
        {
            if (on) { button.SetResourceReference(Control.BackgroundProperty, "AppRaised"); button.SetResourceReference(Control.ForegroundProperty, "AppText"); }
            else { button.ClearValue(Control.BackgroundProperty); button.ClearValue(Control.ForegroundProperty); }
        }
        Mark(LibraryNav, route.Kind is NavKind.Library or NavKind.Game);
        Mark(SettingsNav, route.Kind == NavKind.Settings);
        Mark(BackupsNav, route.Kind == NavKind.Backups);
        Mark(CapturesNav, route.Kind == NavKind.Captures);
    }

    private async void RecentClicked(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) await NavigateToAsync(NavRoute.Game(item.Game.Id, "Overview"));
    }

    // ---------- drawer ----------
    /// <summary>The cover search needs room: while it is open the drawer fills the whole window, otherwise it is a 700 px side panel.</summary>
    private void SetDrawerFullWidth(bool full)
    {
        DrawerPanel.HorizontalAlignment = full ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        DrawerPanel.Width = full ? double.NaN : 700;
    }
}

// Continue-playing row, saved filter views and settings search (UI overhaul part two, ticket #90).
public partial class MainWindow
{
    private readonly SavedViewList savedViews = new();
    private bool savedViewsReadOnly;
    private bool savedViewsLoaded;

    // ---------- continue playing ----------
    private void UpdateContinueRow()
    {
        if (ContinueRow is null) return;
        var plain = Search.Text.Trim().Length == 0 && RemovedFilter.IsChecked != true && ratingFilter == RatingFilters.Any && ((StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All") == "All";
        var picked = plain
            ? ContinuePlaying.Pick(items.Where(i => i.Game.Profile is { IsArchived: false }).Select(i => i.LastPlayedAt is { } at ? new ContinuePlaying.Entry(i.Game.Id, at) : null)).Select(e => libraryRows[e.GameId]).ToArray()
            : [];
        ContinueList.ItemsSource = picked;
        ContinueRow.Visibility = picked.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in picked) _ = item.LoadRailIconAsync();
    }

    private async void ContinueOpenClicked(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) await NavigateToAsync(NavRoute.Game(item.Game.Id, "Overview"));
    }

    // ---------- saved views ----------
    private async Task LoadSavedViewsAsync()
    {
        if (savedViewsLoaded) return;
        savedViewsLoaded = true;
        try
        {
            var stored = await store.LoadLibraryViewsAsync();
            savedViewsReadOnly = stored.IsReadOnly;
            if (stored.Value is { } document) foreach (var view in document.Views) savedViews.Save(view);
        }
        catch (Exception error) when (IsExpected(error)) { savedViewsReadOnly = true; }
    }

    private async Task PersistSavedViewsAsync()
    {
        try { await store.SaveLibraryViewsAsync(new LibraryViewsDocument(1, [.. savedViews.Views])); }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not save the view", error.Message, true); }
    }

    private SavedLibraryView CurrentView()
    {
        var status = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        var removed = RemovedFilter.IsChecked == true;
        var search = Search.Text.Trim();
        var parts = new List<string>();
        if (status != "All") parts.Add(status switch { "NotStarted" => "Not started", "OnHold" => "On hold", _ => status });
        if (removed) parts.Add("Removed");
        if (search.Length > 0) parts.Add("“" + search + "”");
        if (selectedTags.Count > 0) parts.Add(string.Join(" + ", selectedTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)));
        if (sortOrder != LibrarySortOrder.Name) parts.Add(LibrarySortOrder.Label(sortOrder));
        return new SavedLibraryView(string.Join(" · ", parts), status, removed, search, [.. selectedTags], sortOrder);
    }

    private async void ViewsButtonClicked(object sender, RoutedEventArgs e)
    {
        await LoadSavedViewsAsync();
        var menu = new ContextMenu { PlacementTarget = ViewsButton, Placement = PlacementMode.Bottom };
        var current = CurrentView();
        foreach (var view in savedViews.Views)
        {
            var captured = view;
            var entry = new MenuItem { Header = view.Name, IsChecked = view.Status == current.Status && view.Removed == current.Removed && view.Search == current.Search && LibrarySortOrder.Normalize(view.Sort) == current.Sort && TagRules.Normalize(view.Tags).SequenceEqual(TagRules.Normalize(current.Tags), StringComparer.OrdinalIgnoreCase) };
            entry.Click += (_, _) => ApplySavedView(captured);
            menu.Items.Add(entry);
        }
        var save = new MenuItem { Header = current.Name.Length == 0 ? "Save current view (choose a filter first)" : "Save view “" + current.Name + "”", IsEnabled = current.Name.Length > 0 && !savedViewsReadOnly };
        save.Click += async (_, _) =>
        {
            if (!savedViews.Save(current)) { ShowToast("Could not save the view", "You can keep up to " + SavedViewList.MaxViews + " saved views. Delete one first.", true); return; }
            await PersistSavedViewsAsync();
            RebuildPresetChips();
            ShowToast("View saved", current.Name);
        };
        menu.Items.Add(save);
        if (savedViews.Views.Count > 0)
        {
            var delete = new MenuItem { Header = "Delete a view", IsEnabled = !savedViewsReadOnly };
            foreach (var view in savedViews.Views)
            {
                var name = view.Name;
                var item = new MenuItem { Header = name };
                item.Click += async (_, _) => { savedViews.Remove(name); await PersistSavedViewsAsync(); RebuildPresetChips(); ShowToast("View deleted", name); };
                delete.Items.Add(item);
            }
            menu.Items.Add(delete);
        }
        menu.IsOpen = true;
    }

    private void ApplySavedView(SavedLibraryView view)
    {
        initializing = true;
        try
        {
            Search.Text = view.Search;
            RemovedFilter.IsChecked = view.Removed;
            ratingFilter = RatingFilters.Any;
            selectedTags.Clear();
            foreach (var tag in TagRules.Normalize(view.Tags)) selectedTags.Add(tag);
            sortOrder = LibrarySortOrder.Normalize(view.Sort);
            StatusFilter.SelectedItem = StatusFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == view.Status) ?? StatusFilter.Items[0];
        }
        finally { initializing = false; }
        ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id);
    }
}
