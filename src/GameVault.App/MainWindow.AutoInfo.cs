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
    private readonly AutoLookupQueue lookupQueue = new();
    private SteamPortraitCoverCatalogue? coverCatalogue;
    private HeroImageDownloader? heroDownloader;

    private void StopAutoInfo() { try { autoInfo?.Cancel(); } catch (ObjectDisposedException) { } lookupQueue.Clear(); }

    /// <summary>After the library has loaded: look up every game that has never been looked up. Runs quietly in the background, one game at a time.</summary>
    private async Task StartupInfoAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(4)); }
        catch (OperationCanceledException) { return; }
        if (Dispatcher.HasShutdownStarted) return;
        QueueAutoInfo(items.Where(i => i.Game.Profile is { IsArchived: false } && !i.Game.IsReadOnly).Select(i => (i.Game.Id, i.Name)), urgent: false);
    }

    /// <summary>
    /// Puts games in line for their automatic lookup. A game you just added or imported is urgent: it goes to the front and is looked up right away,
    /// even while the background pass over the library is still running. Nothing is dropped because something else is busy.
    /// </summary>
    private void QueueAutoInfo(IEnumerable<(Guid Id, string Name)> games, bool urgent)
    {
        var jobs = games.Select(g => new AutoLookupQueue.Job(g.Id, g.Name, OnlyNeverLookedUp: !urgent, Urgent: urgent)).ToArray();
        if (jobs.Length == 0 || Dispatcher.HasShutdownStarted) return;
        lookupQueue.Enqueue(jobs);
        if (urgent) ShowToast("Looking up game information", jobs.Length == 1 ? jobs[0].Name : $"{jobs.Length} new games");
        if (!autoInfoRunning) _ = RunAutoInfoAsync();
    }

    private async Task RunAutoInfoAsync()
    {
        if (autoInfoRunning || Dispatcher.HasShutdownStarted) return;
        autoInfoRunning = true;
        autoInfo ??= new CancellationTokenSource();
        var token = autoInfo.Token;
        int looked = 0, coversAdded = 0, heroesAdded = 0;
        try
        {
            while (lookupQueue.TryDequeue(out var job))
            {
                token.ThrowIfCancellationRequested();
                MetadataSettings current;
                try { current = await Metadata.LoadSettingsAsync(); }
                catch (Exception error) when (IsExpected(error)) { continue; }
                if (!current.AutoFetch) continue;
                var one = new[] { (job.Id, job.Name) };
                try
                {
                    var result = await Task.Run(() => Metadata.FetchMissingAsync(one, null, token, job.OnlyNeverLookedUp), token);
                    var covers = await AutoCoversAsync(one, token);
                    var heroes = await EnsureHeroesAsync(one, token);
                    looked += result.GamesUpdated;
                    coversAdded += covers;
                    heroesAdded += heroes;
                    if (result.GamesUpdated > 0 || covers > 0 || heroes > 0)
                    {
                        // Show what was found right away if that game is open, without waiting for the rest of the queue.
                        var selected = (GameList.SelectedItem as LibraryItemView)?.Game.Id;
                        if (covers > 0 || heroes > 0) await RefreshLibrary(selected);
                        if (GameList.SelectedItem is LibraryItemView open && open.Game.Id == job.Id) await RefreshAboutAsync(open);
                        if (job.Urgent) ShowToast("Game information added", job.Name);
                    }
                    else if (job.Urgent && result.Problems.Length > 0)
                        ShowToast("Could not look up " + job.Name, "Check your connection, or use Refresh info later. " + result.Problems[0], true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) when (IsExpected(error)) { GameVault.Infrastructure.Diagnostics.AppLog.Warn("Automatic game information stopped for one game: " + error.Message); }
            }
            if (looked > 0 && lookupQueue.Count == 0 && coversAdded + heroesAdded + looked > 1)
                ShowToast("Game information added", $"{looked} game(s) looked up" + (coversAdded > 0 ? $", {coversAdded} cover(s)" : "") + (heroesAdded > 0 ? $", {heroesAdded} background(s)." : "."));
        }
        catch (OperationCanceledException) { }
        finally { autoInfoRunning = false; }
        // Something may have been queued while the last game was finishing.
        if (lookupQueue.Count > 0 && !Dispatcher.HasShutdownStarted && !token.IsCancellationRequested) _ = RunAutoInfoAsync();
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
