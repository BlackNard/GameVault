using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GameVault.Core;

namespace GameVault.App;

// Settings in categories with a search (issue #56). The last page is remembered while GameVault runs; Back, Forward and the command palette can open one.
public partial class MainWindow
{
    private string settingsCategory = SettingsCatalog.Appearance;
    private bool selectingCategory;

    private void InitializeSettingsPages()
    {
        selectingCategory = true;
        SettingsMenu.ItemsSource = SettingsCatalog.Categories;
        selectingCategory = false;
        ApplySettingsCategory();
    }

    /// <summary>Shows one page of Settings. It does not switch to Settings by itself.</summary>
    private void ApplySettingsCategory()
    {
        var pages = new (string Key, FrameworkElement Page)[]
        {
            (SettingsCatalog.Appearance, CatAppearance), (SettingsCatalog.General, CatGeneral), (SettingsCatalog.Library, CatLibrary),
            (SettingsCatalog.Backups, CatBackups), (SettingsCatalog.Capture, CatCapture), (SettingsCatalog.Updates, CatUpdates), (SettingsCatalog.Advanced, CatAdvanced)
        };
        foreach (var (key, page) in pages) page.Visibility = key == settingsCategory ? Visibility.Visible : Visibility.Collapsed;
        var category = SettingsCatalog.Categories.First(c => c.Key == settingsCategory);
        CategoryDescription.Text = category.Description;
        selectingCategory = true;
        SettingsMenu.SelectedItem = category;
        selectingCategory = false;
        SettingsScroll.ScrollToTop();
    }

    private void SelectSettingsCategory(string key)
    {
        if (!SettingsCatalog.IsKnown(key)) return;
        settingsCategory = key;
        ApplySettingsCategory();
        if (SettingsPanel.Visibility == Visibility.Visible) RecordRoute();
    }

    private void SettingsCategorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (!selectingCategory && SettingsMenu.SelectedItem is SettingsCategory category) SelectSettingsCategory(category.Key);
    }

    private void SettingsSearchChanged(object sender, TextChangedEventArgs e)
    {
        var searching = SettingsSearch.Text.Trim().Length > 0;
        var found = SettingsCatalog.Search(SettingsSearch.Text);
        SettingsResults.ItemsSource = found;
        SettingsResults.Visibility = searching && found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsNoResults.Visibility = searching && found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsMenu.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SettingsSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsSearch.Text.Length > 0) { e.Handled = true; SettingsSearch.Clear(); }
        else if (e.Key == Key.Enter && SettingsResults.Items.Count > 0) { e.Handled = true; OpenSettingsEntry((SettingsEntry)SettingsResults.Items[0]!); }
        else if (e.Key == Key.Down && SettingsResults.Items.Count > 0) { e.Handled = true; SettingsResults.Focus(); SettingsResults.SelectedIndex = 0; }
    }

    private void SettingsResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (SettingsResults.SelectedItem is SettingsEntry entry) OpenSettingsEntry(entry);
    }

    private void SettingsResultKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SettingsResults.SelectedItem is SettingsEntry entry) { e.Handled = true; OpenSettingsEntry(entry); }
    }

    /// <summary>Opens the page of a found option, scrolls to it and puts the keyboard on it.</summary>
    private async void OpenSettingsEntry(SettingsEntry entry)
    {
        SettingsSearch.Clear();
        SelectSettingsCategory(entry.Category);
        if (FindName(entry.Target) is not FrameworkElement target) return;
        if (target is Expander expander) expander.IsExpanded = true;
        await Dispatcher.InvokeAsync(() =>
        {
            target.BringIntoView();
            if (!target.Focus()) target.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }, DispatcherPriority.Loaded);
    }
}
