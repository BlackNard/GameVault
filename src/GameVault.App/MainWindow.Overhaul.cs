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
        Mark(LibraryNav, route.Kind != NavKind.Settings);
        Mark(SettingsNav, route.Kind == NavKind.Settings);
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

    // ---------- settings ----------
    private void SettingsSectionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && FindName(name) is FrameworkElement target) target.BringIntoView();
    }
}
