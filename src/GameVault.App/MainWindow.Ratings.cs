using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GameVault.Core;

namespace GameVault.App;

// Filtering the library by the player's own rating (issue #59). The filter is for this run only; saved views keep their sort, including "Highest rated".
public partial class MainWindow
{
    private string ratingFilter = RatingFilters.Any;

    /// <summary>The "Your rating" card at the top of a game page: saved as soon as it is chosen.</summary>
    private async void HeroRatingChosen(object? sender, int halfStars)
    {
        if (GameList.SelectedItem is not LibraryItemView { Game.Profile: { IsArchived: false } profile }) return;
        HeroRating.IsEnabled = false;
        try
        {
            await store.SaveRatingAsync(profile.Id, halfStars);
            HeroRating.Value = halfStars;
            await RefreshLibrary(profile.Id);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { ShowToast("The rating could not be saved", error.Message, true); }
        finally { HeroRating.IsEnabled = GameList.SelectedItem is LibraryItemView { Game: { IsReadOnly: false, Profile.IsArchived: false } }; }
    }

    private void RatingButtonClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RatingButton, Placement = PlacementMode.Bottom, StaysOpen = false };
        foreach (var filter in RatingFilters.All)
        {
            var captured = filter;
            var entry = new MenuItem { Header = RatingFilters.Label(filter), IsChecked = filter == ratingFilter };
            entry.Click += (_, _) => { ratingFilter = captured; ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id); };
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
    }
}
