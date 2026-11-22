using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure.Artwork;
using GameVault.Infrastructure.Metadata;

namespace GameVault.App;

// Automatic game information and Steam covers for new games and games that have none yet (like Playnite's "download metadata on import").
public partial class MainWindow
{
    private CancellationTokenSource? autoInfo;
    private bool autoInfoRunning;
    private SteamPortraitCoverCatalogue? coverCatalogue;
    private HeroImageDownloader? heroDownloader;

    private void StopAutoInfo() { try { autoInfo?.Cancel(); } catch (ObjectDisposedException) { } }

    /// <summary>After the library has loaded: look up every game that has never been looked up. Runs quietly in the background.</summary>
    private async Task StartupInfoAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(4)); }
        catch (OperationCanceledException) { return; }
        if (Dispatcher.HasShutdownStarted) return;
        var games = items.Where(i => i.Game.Profile is { IsArchived: false } && !i.Game.IsReadOnly).Select(i => (i.Game.Id, i.Name)).ToArray();
        await AutoInfoAsync(games, true);
    }

    private async Task AutoInfoAsync(IReadOnlyList<(Guid Id, string Name)> games, bool onlyNeverLookedUp)
    {
        if (games.Count == 0 || autoInfoRunning || bulkInfo is not null || Dispatcher.HasShutdownStarted) return;
        MetadataSettings current;
        try { current = await Metadata.LoadSettingsAsync(); }
        catch (Exception error) when (IsExpected(error)) { return; }
        if (!current.AutoFetch) return;
        autoInfoRunning = true;
        autoInfo ??= new CancellationTokenSource();
        var token = autoInfo.Token;
        try
        {
            var result = await Task.Run(() => Metadata.FetchMissingAsync(games, null, token, onlyNeverLookedUp), token);
            var covers = await AutoCoversAsync(games, token);
            var heroes = await EnsureHeroesAsync(games, token);
            if (result.GamesUpdated > 0 || covers > 0 || heroes > 0)
            {
                var selected = (GameList.SelectedItem as LibraryItemView)?.Game.Id;
                if (covers > 0 || heroes > 0) await RefreshLibrary(selected);
                if (GameList.SelectedItem is LibraryItemView item) await RefreshAboutAsync(item);
                ShowToast("Game information added", $"{result.GamesUpdated} game(s) looked up" + (covers > 0 ? $", {covers} cover(s) added" : "") + (heroes > 0 ? $", {heroes} background(s) added." : "."));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (IsExpected(error)) { GameVault.Infrastructure.Diagnostics.AppLog.Warn("Automatic game information stopped: " + error.Message); }
        finally { autoInfoRunning = false; }
    }

    /// <summary>A game that has no background image gets the wide banner its store provides. A Steam game whose id is known needs no new lookup.</summary>
    private async Task<int> EnsureHeroesAsync(IReadOnlyList<(Guid Id, string Name)> games, CancellationToken token)
    {
        var added = 0;
        foreach (var (id, _) in games)
        {
            token.ThrowIfCancellationRequested();
            if (!libraryRows.TryGetValue(id, out var row) || row.Game.HeroFile is not null || row.Game.Profile is not { IsArchived: false } || row.Game.IsReadOnly) continue;
            try
            {
                var document = (await Metadata.LoadAsync(id)).Value;
                var url = document?.Get(MetadataField.Hero)?.Text ??
                    (document?.SteamAppId is { } steam ? $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{steam}/library_hero.jpg" : null);
                if (url is null) continue;
                heroDownloader ??= new HeroImageDownloader();
                var bytes = await heroDownloader.DownloadAsync(url, token);
                if (bytes is not null && await library.SetHeroIfMissingAsync(id, bytes)) added++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (IsExpected(error) || error is ArgumentException or System.Net.Http.HttpRequestException)
            { GameVault.Infrastructure.Diagnostics.AppLog.Info($"No background image for a game: {error.Message}"); }
        }
        return added;
    }

    /// <summary>A game from Steam that has no cover gets Steam's portrait cover. A cover the owner chose is never replaced.</summary>
    private async Task<int> AutoCoversAsync(IReadOnlyList<(Guid Id, string Name)> games, CancellationToken token)
    {
        var added = 0;
        foreach (var (id, name) in games)
        {
            token.ThrowIfCancellationRequested();
            if (!libraryRows.TryGetValue(id, out var row) || row.Game.CoverFile is not null) continue;
            try
            {
                if (await Metadata.SteamAppIdAsync(id) is not { } text || !int.TryParse(text, out var appId) || appId <= 0) continue;
                var page = new Uri($"https://store.steampowered.com/app/{appId}/");
                coverCatalogue ??= new SteamPortraitCoverCatalogue();
                var bytes = await coverCatalogue.DownloadAsync(new PortraitCoverCandidate(appId, name, "Steam", page), false, token);
                var png = GameVault.App.Artwork.PortraitCoverImages.Normalize(bytes, page);
                if (await library.SetCoverIfMissingAsync(id, png)) added++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (IsExpected(error) || error is ArtworkException or InvalidOperationException or System.Net.Http.HttpRequestException)
            { GameVault.Infrastructure.Diagnostics.AppLog.Info($"No automatic cover for a game: {error.Message}"); }
        }
        return added;
    }
}
