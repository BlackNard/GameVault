using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GameVault.Core;

namespace GameVault.App;

// Tags, categories, sort order and saved-view management (ticket #88).
public partial class MainWindow
{
    private readonly Dictionary<Guid, string[]> gameTags = [];
    private readonly HashSet<string> selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private string sortOrder = LibrarySortOrder.Name;
    private bool tagsLoaded;
    private bool tagsReadOnly;
    private readonly List<Button> presetChips = [];

    private string[] TagsOf(Guid id) => gameTags.TryGetValue(id, out var tags) ? tags : [];

    private async Task EnsureTagsLoadedAsync()
    {
        if (tagsLoaded) return;
        tagsLoaded = true;
        try
        {
            var stored = await store.LoadLibraryTagsAsync();
            tagsReadOnly = stored.IsReadOnly;
            if (stored.Value is { } document) foreach (var (id, tags) in document.Tags) gameTags[id] = TagRules.Normalize(tags);
        }
        catch (Exception error) when (IsExpected(error)) { tagsReadOnly = true; }
        await LoadSavedViewsAsync();
        RebuildPresetChips();
    }

    private async Task PersistTagsAsync()
    {
        try { await store.SaveLibraryTagsAsync(new LibraryTagsDocument(1, gameTags.Where(p => p.Value.Length > 0).ToDictionary(p => p.Key, p => p.Value))); }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not save the tags", error.Message, true); }
    }

    // ---------- tag and sort filters ----------
    private void UpdateFilterButtons()
    {
        TagsButton.Content = selectedTags.Count == 0 ? "Tags ▾" : $"Tags ({selectedTags.Count}) ▾";
        SortButton.Content = "Sort: " + LibrarySortOrder.Label(sortOrder) + " ▾";
        RatingButton.Content = (ratingFilter == RatingFilters.Any ? "Rating" : RatingFilters.Label(ratingFilter)) + " ▾";
        RatingButton.Visibility = items.Any(i => i.Game.Rating > 0) || ratingFilter != RatingFilters.Any ? Visibility.Visible : Visibility.Collapsed;
        TagsButton.Visibility = TagRules.AllTags(gameTags.Values).Length > 0 || selectedTags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TagsButtonClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = TagsButton, Placement = PlacementMode.Bottom, StaysOpen = false };
        foreach (var tag in TagRules.AllTags(gameTags.Values).Union(selectedTags, StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            var captured = tag;
            var entry = new MenuItem { Header = tag, IsCheckable = true, IsChecked = selectedTags.Contains(tag), StaysOpenOnClick = true };
            entry.Click += (_, _) =>
            {
                if (entry.IsChecked) selectedTags.Add(captured); else selectedTags.Remove(captured);
                ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id);
            };
            menu.Items.Add(entry);
        }
        var clear = new MenuItem { Header = "Clear tag filter", IsEnabled = selectedTags.Count > 0 };
        clear.Click += (_, _) => { selectedTags.Clear(); ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id); };
        menu.Items.Add(clear);
        menu.IsOpen = true;
    }

    private void SortButtonClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = PlacementMode.Bottom };
        foreach (var sort in LibrarySortOrder.All)
        {
            var captured = sort;
            var entry = new MenuItem { Header = LibrarySortOrder.Label(sort), IsChecked = sort == sortOrder };
            entry.Click += (_, _) => { sortOrder = captured; ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id); };
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
    }

    // ---------- tags on the game page ----------
    private void UpdateTagsUi(LibraryItemView? item)
    {
        if (item?.Game.Profile is null) { TagsBlock.Visibility = Visibility.Collapsed; return; }
        TagsBlock.Visibility = Visibility.Visible;
        var tags = TagsOf(item.Game.Id);
        TagsChips.Children.Clear();
        foreach (var tag in tags) TagsChips.Children.Add(Chip(tag));
        if (tags.Length == 0) TagsChips.Children.Add(Muted("None yet", new Thickness(0, 0, 0, 6)));
        EditTagsButton.IsEnabled = !tagsReadOnly;
    }

    private void EditTagsClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LibraryItemView { Game.Profile: not null } item || tagsReadOnly) return;
        TagsInput.Text = string.Join(", ", TagsOf(item.Game.Id));
        TagsEditor.Visibility = Visibility.Visible;
        TagsInput.Focus();
        TagsInput.CaretIndex = TagsInput.Text.Length;
    }

    private void CancelTagsClicked(object sender, RoutedEventArgs e) => TagsEditor.Visibility = Visibility.Collapsed;

    private async void SaveTagsClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LibraryItemView { Game.Profile: not null } item) return;
        var tags = TagRules.Parse(TagsInput.Text);
        if (tags.Length == 0) gameTags.Remove(item.Game.Id); else gameTags[item.Game.Id] = tags;
        TagsEditor.Visibility = Visibility.Collapsed;
        await PersistTagsAsync();
        ApplyFilters(item.Game.Id);
        ShowToast("Your tags were saved", tags.Length == 0 ? "No tags" : string.Join(", ", tags));
    }

    private void TagsInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { SaveTagsClicked(sender, e); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Escape) { CancelTagsClicked(sender, e); e.Handled = true; }
    }

    // ---------- preset chips above the library ----------
    private void RebuildPresetChips()
    {
        if (ChipsPanel is null) return;
        foreach (var chip in presetChips) ChipsPanel.Children.Remove(chip);
        presetChips.Clear();
        var insertAt = ChipsPanel.Children.IndexOf(ViewsButton);
        foreach (var view in savedViews.Views)
        {
            var captured = view;
            var chip = new Button { Content = view.Name, Style = (Style)FindResource("ChipButton"), ToolTip = "Saved view: " + view.Name };
            chip.Click += (_, _) => ApplySavedView(captured);
            ChipsPanel.Children.Insert(insertAt++, chip);
            presetChips.Add(chip);
        }
    }

    // ---------- manage saved views in Settings ----------
    private async void RefreshSavedViewsManager()
    {
        await EnsureTagsLoadedAsync();
        SavedViewsManager.Children.Clear();
        if (savedViews.Views.Count == 0)
        {
            SavedViewsManager.Children.Add(new TextBlock { Text = "No saved views yet. Set a search, status, tags or sort in the library, then choose Views ▾ → Save view.", TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)FindResource("AppMuted") });
            return;
        }
        foreach (var view in savedViews.Views.ToArray())
        {
            var name = view.Name;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var input = new TextBox { Text = name, Width = 220, MaxLength = SavedViewList.MaxNameLength, VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(input, "Rename saved view " + name);
            async void Commit()
            {
                if (input.Text.Trim() == name) return;
                if (!savedViews.Rename(name, input.Text)) { ShowToast("Could not rename the view", "Names must be unique and not empty.", true); input.Text = name; return; }
                await PersistSavedViewsAsync(); RebuildPresetChips(); RefreshSavedViewsManager();
            }
            input.LostFocus += (_, _) => Commit();
            input.KeyDown += (_, a) => { if (a.Key == System.Windows.Input.Key.Enter) { Commit(); a.Handled = true; } };
            Button Action(string text, string tip, Func<Task> run) { var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(8, 0, 0, 0), MinWidth = 36, IsEnabled = !savedViewsReadOnly }; System.Windows.Automation.AutomationProperties.SetName(b, tip + " " + name); b.Click += async (_, _) => await run(); return b; }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Action("↑", "Move up", async () => { if (savedViews.Move(name, -1)) { await PersistSavedViewsAsync(); RebuildPresetChips(); RefreshSavedViewsManager(); } }));
            buttons.Children.Add(Action("↓", "Move down", async () => { if (savedViews.Move(name, 1)) { await PersistSavedViewsAsync(); RebuildPresetChips(); RefreshSavedViewsManager(); } }));
            buttons.Children.Add(Action("Delete", "Delete", async () => { savedViews.Remove(name); await PersistSavedViewsAsync(); RebuildPresetChips(); RefreshSavedViewsManager(); }));
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(input);
            SavedViewsManager.Children.Add(row);
        }
    }
}
