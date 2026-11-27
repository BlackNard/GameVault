using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GameVault.Core;

namespace GameVault.App;

// Changing a game's status where it is shown: the button on its page and the Status submenu of its card (issue #94).
public partial class MainWindow
{
    private static bool CanChangeStatus(LibraryItemView? item) => item?.Game is { IsReadOnly: false, Profile.IsArchived: false };

    private void ShowHeroStatus(LibraryItemView? item)
    {
        HeroStatusButton.Visibility = item?.Playing is null ? Visibility.Collapsed : Visibility.Visible;
        if (item?.Playing is { } status) HeroStatusButton.Content = PlayStatuses.Label(status) + " ▾";
    }

    private void HeroStatusClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LibraryItemView item || !CanChangeStatus(item)) return;
        var menu = new ContextMenu { PlacementTarget = HeroStatusButton, Placement = PlacementMode.Bottom, StaysOpen = false };
        FillStatusMenu(menu.Items, item);
        menu.IsOpen = true;
    }

    /// <summary>Every status, with the current one ticked.</summary>
    private void FillStatusMenu(ItemCollection entries, LibraryItemView item)
    {
        entries.Clear();
        foreach (var status in PlayStatuses.All)
        {
            var chosen = status;
            var entry = new MenuItem { Header = PlayStatuses.Label(status), IsChecked = item.Playing == status };
            entry.Click += async (_, e) => { e.Handled = true; await ChangeStatusAsync(item.Game.Id, chosen); };
            entries.Add(entry);
        }
    }

    private async Task ChangeStatusAsync(Guid id, PlayStatus status)
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation || items.FirstOrDefault(i => i.Game.Id == id) is not { } item || !CanChangeStatus(item) || item.Playing == status) return;
        if (launches.GetState(id).IsActive || backups.IsWorking(id)) { ShowToast("The status was not changed", "Wait until the game and its backup have finished.", true); return; }
        busy = true; UpdateButtons();
        try
        {
            await store.SetPlayStatusAsync(id, status);
            // On the game's own page a filter that no longer matches would switch to another game, so it is cleared.
            var filter = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
            if (GamePage.Visibility == Visibility.Visible && filter != "All" && filter != status.ToString()) StatusFilter.SelectedIndex = 0;
            await RefreshLibrary(id);
            if (notesView is not null) await notesView.RefreshAsync();
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { ShowToast("The status was not changed", error.Message, true); }
        finally { EndOperation(); }
    }
}
