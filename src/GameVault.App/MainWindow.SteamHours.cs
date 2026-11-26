using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Metadata;

namespace GameVault.App;

// Steam hours (issue #62): the game page shows the combined total and a small section to link or unlink the profile's Steam game.
public partial class MainWindow
{
    private SteamHoursService? steamHours;
    private SteamHoursState? steamState;
    private SteamOffer? steamOffer;
    private Guid steamFor;
    private int steamRequest;

    private SteamHoursService SteamHoursLinks => steamHours ??= new(store, new SteamLocalData(SteamRoot()), new StoreGameLauncher(), new SteamStoreProvider().SummaryAsync);

    private static string SteamTime(long seconds) => $"{seconds / 3600}h {seconds % 3600 / 60:00}m";

    private async Task RefreshSteamHoursAtStartAsync()
    {
        try { if (await SteamHoursLinks.RefreshAsync() > 0) await RefreshSteamAsync(GameList.SelectedItem as LibraryItemView); }
        catch (Exception error) when (IsExpected(error) || error is IOException) { }
    }

    private async Task RefreshSteamAsync(LibraryItemView? item)
    {
        var request = ++steamRequest;
        if (item?.Game.Profile is not { IsArchived: false })
        {
            steamState = null;
            SteamSection.Visibility = HeroPlaytimeDetail.Visibility = Visibility.Collapsed;
            return;
        }
        var id = item.Game.Id;
        // Another game: nothing of the previous one may stay on screen while this one is read.
        if (steamFor != id)
        {
            steamFor = id; steamState = null; ClearSteamOffer();
            SteamSection.Visibility = HeroPlaytimeDetail.Visibility = Visibility.Collapsed;
            SteamSection.IsExpanded = false;
        }
        try
        {
            var state = await SteamHoursLinks.GetAsync(id);
            var eligible = state.Link is not null || state.Message is not null || await SteamHoursLinks.CanBeLinkedAsync(id);
            if (request != steamRequest) return;
            steamState = state;
            SteamSection.Visibility = eligible ? Visibility.Visible : Visibility.Collapsed;
            SteamUnlinkButton.Visibility = state.Link is not null ? Visibility.Visible : Visibility.Collapsed;
            SteamFindButton.Visibility = state.Link is null && state.Message is null && steamOffer is null ? Visibility.Visible : Visibility.Collapsed;
            if (steamOffer is null)
                SteamStatus.Text = state.Message ?? (state.Link is { } link ? $"Linked to “{link.SteamName}” on Steam. Steam's hours are read again when GameVault starts." : "Add the hours Steam recorded for this game.");
            SteamSection.Header = state.Link is not null && !state.Active ? "Steam hours · needs attention" : "Steam hours";
            ApplySteamTotal();
        }
        catch (Exception error) when (IsExpected(error) || error is IOException)
        {
            if (request == steamRequest) { SteamSection.Visibility = Visibility.Visible; SteamStatus.Text = error.Message; }
        }
    }

    /// <summary>With an active link the top card shows the combined total, with both figures under it.</summary>
    private void ApplySteamTotal()
    {
        if (GameList.SelectedItem is not LibraryItemView selected || selected.Game.Id != steamFor || steamState?.Totals is not { } totals)
        {
            HeroPlaytimeDetail.Visibility = Visibility.Collapsed;
            if (GameList.SelectedItem is LibraryItemView item) DetailTime.Text = item.Played;
            return;
        }
        DetailTime.Text = LibraryItemView.FormatSeconds(totals.TotalSeconds);
        HeroPlaytimeDetail.Text = $"GameVault {SteamTime(totals.GameVaultSeconds)} · Steam {SteamTime(totals.SteamSeconds)}";
        HeroPlaytimeDetail.Visibility = Visibility.Visible;
    }

    private void ClearSteamOffer()
    {
        steamOffer = null;
        SteamCompare.Children.Clear();
        SteamAccountChoice.Items.Clear();
        SteamAccountChoice.Visibility = SteamLinkButton.Visibility = SteamCancelButton.Visibility = Visibility.Collapsed;
        SteamFindButton.IsEnabled = true;
    }

    private async void SteamFindClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LibraryItemView item) return;
        var id = item.Game.Id;
        SteamFindButton.IsEnabled = false;
        SteamStatus.Text = "Looking for the Steam game…";
        try
        {
            var (offer, reason) = await SteamHoursLinks.FindOfferAsync(id);
            var facts = offer is { Proven: false } ? (await store.LoadMetadataAsync(id)).Value : null;
            if (steamFor != id) return;
            if (offer is null) { SteamStatus.Text = reason ?? ""; SteamFindButton.IsEnabled = true; return; }
            ShowSteamOffer(item, offer, facts);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { if (steamFor == id) { SteamStatus.Text = error.Message; SteamFindButton.IsEnabled = true; } }
    }

    private void ShowSteamOffer(LibraryItemView item, SteamOffer offer, GameMetadataDocument? facts)
    {
        ClearSteamOffer();
        steamOffer = offer;
        SteamStatus.Text = offer.Proven ? $"This program is installed by Steam as “{offer.SteamName}”." : "Is this the same game? The program is not in Steam's folder, so please check.";
        if (!offer.Proven)
        {
            SteamCompare.Children.Add(SteamCard("Your game", item.Name, item.Game.CoverFile, facts?.Get(MetadataField.Developers)?.Items,
                PartialDate.Parse(facts?.Get(MetadataField.ReleaseDate)?.Date)?.Year));
            SteamCompare.Children.Add(SteamCard("On Steam", offer.SteamName, offer.Summary?.ImageUrl, offer.Summary?.Developers, offer.Summary?.Year));
        }
        foreach (var choice in offer.Accounts)
            SteamAccountChoice.Items.Add(new ComboBoxItem { Content = $"{choice.Account.Name} · {SteamTime(choice.Minutes * 60)} on Steam", Tag = choice.Account.Id });
        // With several accounts the player picks one; nothing is chosen for them.
        SteamAccountChoice.SelectedIndex = offer.Accounts.Length == 1 ? 0 : -1;
        SteamAccountChoice.Visibility = SteamLinkButton.Visibility = SteamCancelButton.Visibility = Visibility.Visible;
        SteamLinkButton.Content = offer.Proven ? "Link" : "Yes, same game";
        SteamLinkButton.IsEnabled = SteamAccountChoice.SelectedIndex >= 0;
        SteamFindButton.Visibility = Visibility.Collapsed;
    }

    private static FrameworkElement SteamCard(string label, string name, string? picture, string[]? developers, int? year)
    {
        var panel = new StackPanel { Width = 240 };
        panel.Children.Add(Muted(label));
        var frame = new Border { Height = 112, Margin = new Thickness(0, 6, 0, 8), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        frame.SetResourceReference(Border.BackgroundProperty, "AppRaised");
        if (!string.IsNullOrEmpty(picture) && (HeroImages.IsAllowedUrl(picture) || File.Exists(picture)))
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(picture);
                image.DecodePixelWidth = 480;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                frame.Child = new Image { Source = image, Stretch = Stretch.Uniform };
            }
            catch (Exception error) when (error is UriFormatException or NotSupportedException or InvalidOperationException or IOException or UnauthorizedAccessException) { }
        panel.Children.Add(frame);
        panel.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Muted(developers is { Length: > 0 } ? string.Join(", ", developers.Take(2)) : "Developer unknown", new Thickness(0, 2, 0, 0)));
        panel.Children.Add(Muted(year?.ToString() ?? "Release year unknown", new Thickness(0, 2, 0, 0)));
        var card = new Border { Child = panel, Padding = new Thickness(12), Margin = new Thickness(0, 0, 12, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        card.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        return card;
    }

    private void SteamAccountChosen(object sender, SelectionChangedEventArgs e) => SteamLinkButton.IsEnabled = SteamAccountChoice.SelectedItem is not null;

    private async void SteamLinkClicked(object sender, RoutedEventArgs e)
    {
        if (steamOffer is not { } offer || SteamAccountChoice.SelectedItem is not ComboBoxItem { Tag: string account }) return;
        var id = steamFor;
        SteamLinkButton.IsEnabled = false;
        try
        {
            await SteamHoursLinks.LinkAsync(id, offer, account);
            if (steamFor != id) return;
            ClearSteamOffer();
            await RefreshSteamAsync(GameList.SelectedItem as LibraryItemView);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { if (steamFor == id) { SteamStatus.Text = error.Message; SteamLinkButton.IsEnabled = true; } }
    }

    private async void SteamCancelClicked(object sender, RoutedEventArgs e)
    {
        ClearSteamOffer();
        await RefreshSteamAsync(GameList.SelectedItem as LibraryItemView);
    }

    private async void SteamUnlinkClicked(object sender, RoutedEventArgs e)
    {
        var id = steamFor;
        SteamUnlinkButton.IsEnabled = false;
        try
        {
            await SteamHoursLinks.UnlinkAsync(id);
            ClearSteamOffer();
            await RefreshSteamAsync(GameList.SelectedItem as LibraryItemView);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { if (steamFor == id) SteamStatus.Text = error.Message; }
        finally { SteamUnlinkButton.IsEnabled = true; }
    }
}
