using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.App;

public partial class MainWindow : Window
{
    private readonly AppDataStore store;
    private readonly LibraryService library;
    private AppSettings settings;
    private readonly bool settingsReadOnly;
    private bool initializing = true;
    private bool busy;
    private bool closeAfterSave;
    private bool editorOpen;
    private LibraryItemView[] items = [];

    public MainWindow(ApplicationPaths paths, AppDataStore store, AppDataSnapshot snapshot)
    {
        this.store = store;
        library = new(paths, store);
        settings = snapshot.Settings.Value ?? AppSettings.Default;
        settingsReadOnly = snapshot.Settings.IsReadOnly;
        InitializeComponent();
        ThemePalette.Apply(this, settings.Theme);
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        MinHeight = Math.Min(MinHeight, MaxHeight);
        MinWidth = Math.Min(MinWidth, MaxWidth);
        ThemeChoice.SelectedIndex = settings.Theme == "Crimson" ? 1 : 0;
        ProfilesPath.Text = paths.Profiles;
        StorageNotice.Text = string.Join("\n", snapshot.Warnings.Distinct());
        initializing = false;
        SetNavigation(true);
        UpdateButtons();
        Closing += BeforeClosing;
        Loaded += LoadLibrary;
    }

    private async void LoadLibrary(object sender, RoutedEventArgs e)
    {
        busy = true; UpdateButtons();
        try { await RefreshLibrary(); }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = "Could not load the library. " + error.Message; }
        finally { EndOperation(); }
    }

    private async Task RefreshLibrary(Guid? select = null)
    {
        var previous = select ?? (GameList.SelectedItem as LibraryItemView)?.Game.Id;
        var snapshot = await library.ListAsync();
        items = snapshot.Games.Select(g => new LibraryItemView(g)).ToArray();
        StorageNotice.Text = string.Join("\n", snapshot.Warnings.Distinct());
        ApplyFilters(previous);
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (!initializing) ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id);
    }

    private void ApplyFilters(Guid? selected)
    {
        var removed = RemovedFilter.IsChecked == true;
        var filter = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        var search = Search.Text.Trim();
        var visible = items.Where(i => (i.Game.Profile?.IsArchived ?? false) == removed &&
            (search.Length == 0 || i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (filter == "All" || i.Game.Profile?.Status.ToString() == filter)).ToArray();
        GameList.ItemsSource = visible;
        GameList.SelectedItem = visible.FirstOrDefault(i => i.Game.Id == selected) ?? visible.FirstOrDefault();
        LibrarySummary.Text = $"{visible.Length} {(removed ? "removed profiles" : "games")} · stored on your device";
        EmptyState.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHeading.Text = removed ? "No removed profiles here" : items.Length == 0 ? "Make room for your favorites" : "No matching games";
        EmptyDescription.Text = removed ? "Removed games keep their data here so you can restore them." : items.Length == 0 ? "Choose Add game and browse to its executable to start your library." : "Try another search or status. You can also check Removed profiles.";
        ShowSelection();
    }

    private void SelectedGameChanged(object sender, SelectionChangedEventArgs e) { if (!initializing) ShowSelection(); }

    private void ShowSelection()
    {
        var item = GameList.SelectedItem as LibraryItemView;
        DetailTitle.Text = item?.Name ?? "A home for every game";
        DetailState.Text = item is null ? "Select a game to see its details or choose Add game to get started." :
            string.Join("\n", new[] { item.Status, item.Availability, item.Game.Warning }.Where(s => !string.IsNullOrWhiteSpace(s)));
        DetailFields.Visibility = item?.Game.Profile is not null ? Visibility.Visible : Visibility.Collapsed;
        if (item?.Game.Profile is GameProfile profile)
        {
            DetailTime.Text = item.Played;
            DetailExecutable.Text = profile.ExecutablePath;
            DetailWorking.Text = profile.WorkingDirectory;
            DetailSaves.Text = profile.SaveSources.Length == 0 ? "Save location needed. Add it through Edit game when you know where the game saves." : string.Join("\n", profile.SaveSources);
            ArchiveButton.Content = profile.IsArchived ? "Restore to library" : "Remove from library";
        }
        UpdateButtons();
    }

    private async void CoverRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryItemView item }) await item.LoadCoverAsync();
    }

    private async void CoverRowChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is LibraryItemView item) await item.LoadCoverAsync();
    }

    private void ShowLibrary(object sender, RoutedEventArgs e) => SetNavigation(true);
    private void ShowSettings(object sender, RoutedEventArgs e) => SetNavigation(false);

    private void SetNavigation(bool showLibrary)
    {
        LibraryPanel.Visibility = showLibrary ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = showLibrary ? Visibility.Collapsed : Visibility.Visible;
        LibraryNav.SetResourceReference(Control.BorderBrushProperty, showLibrary ? "AppAccent" : "AppLine");
        SettingsNav.SetResourceReference(Control.BorderBrushProperty, showLibrary ? "AppLine" : "AppAccent");
    }
    private void AddGame(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void EditGame(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is LibraryItemView { Game.IsReadOnly: false } item) OpenEditor(item.Game.Profile);
    }

    private async void OpenEditor(GameProfile? profile)
    {
        if (busy || editorOpen) return;
        editorOpen = true;
        UpdateButtons();
        try
        {
            var editor = new GameEditor(settings.Theme, profile, async (draft, cover) =>
            {
                busy = true;
                try { return await library.SaveAsync(draft, cover); }
                finally { busy = false; }
            }) { Owner = this };
            if (editor.ShowDialog() == true && editor.Result is GameSaveResult result)
            {
                busy = true;
                // A new game should be visible immediately; editing retains the current library filters.
                if (profile is null)
                {
                    initializing = true;
                    Search.Text = ""; StatusFilter.SelectedIndex = 0; RemovedFilter.IsChecked = false;
                    initializing = false;
                }
                await RefreshLibrary(result.Profile.Id);
                OperationNotice.Text = result.Warning ?? $"{result.Profile.Name} saved.";
            }
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = "Could not refresh the library. " + error.Message; }
        finally
        {
            busy = editorOpen = false;
            UpdateButtons();
            if (closeAfterSave) Close();
        }
    }

    private async void ArchiveGame(object sender, RoutedEventArgs e)
    {
        if (busy || GameList.SelectedItem is not LibraryItemView { Game.IsReadOnly: false, Game.Profile: not null } item) return;
        var profile = item.Game.Profile;
        var remove = !profile.IsArchived;
        if (remove && MessageBox.Show(this, $"Remove {profile.Name} from your library?\n\nIts profile, statistics, and any existing backups will be kept. You can restore it from Removed profiles. The installed game is untouched.",
            "Remove from library", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        busy = true; UpdateButtons();
        try
        {
            await library.SetArchivedAsync(item.Game.Id, remove);
            await RefreshLibrary();
            OperationNotice.Text = remove ? "Game removed. Its data is kept in Removed profiles." : "Game restored to your library. Uncheck Removed profiles to see it.";
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = error.Message; }
        finally { EndOperation(); }
    }

    private async void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || busy || settingsReadOnly || ThemeChoice.SelectedItem is not ComboBoxItem { Tag: string theme } || theme == settings.Theme) return;
        busy = true; UpdateButtons();
        try
        {
            var updated = settings with { Theme = theme };
            await store.SaveSettingsAsync(updated);
            settings = updated;
            ThemePalette.Apply(this, theme);
            OperationNotice.Text = "Theme saved. It will be restored next time you open GameVault.";
        }
        catch (Exception error) when (IsExpected(error))
        {
            initializing = true; ThemeChoice.SelectedIndex = settings.Theme == "Crimson" ? 1 : 0; initializing = false;
            OperationNotice.Text = "Could not save the theme. " + error.Message;
        }
        finally { EndOperation(); }
    }

    private void UpdateButtons()
    {
        AddButton.IsEnabled = !busy && !editorOpen && !settingsReadOnly;
        ThemeChoice.IsEnabled = !busy && !editorOpen && !settingsReadOnly;
        var editable = !busy && !editorOpen && GameList.SelectedItem is LibraryItemView { Game.IsReadOnly: false, Game.Profile: not null };
        EditButton.IsEnabled = ArchiveButton.IsEnabled = editable;
    }

    private void EndOperation() { busy = false; UpdateButtons(); if (closeAfterSave) Close(); }
    private static bool IsExpected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException;

    private void BeforeClosing(object? sender, CancelEventArgs e)
    {
        if (!busy && !editorOpen) return;
        e.Cancel = true;
        closeAfterSave = true;
        OperationNotice.Text = editorOpen ? "Finish or cancel the game editor before closing." : "Finishing the current operation before closing…";
    }
}
