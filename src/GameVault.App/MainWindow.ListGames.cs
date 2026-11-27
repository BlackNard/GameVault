using System.IO;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Artwork;

namespace GameVault.App;

// Cover and details for list entries that are not in the library (issue #97), fetched the same way as a library game's.
public partial class MainWindow
{
    private readonly Dictionary<Guid, ExternalGame> listGames = [];
    /// <summary>Entries being looked up now (UI thread only).</summary>
    private readonly HashSet<Guid> listLookups = [];
    /// <summary>Entries already tried since the start, so a failing lookup is not repeated every time the page is shown.</summary>
    private readonly HashSet<Guid> listTried = [];
    private bool listLookingUp;

    private ListEntry[] OutsideEntries() => [.. lists.Lists.SelectMany(l => l.Entries).Where(e => e.GameId is null)];

    private async Task LoadListGamesAsync()
    {
        var entries = OutsideEntries();
        foreach (var gone in listGames.Keys.Except(entries.Select(e => e.Id)).ToArray()) listGames.Remove(gone);
        foreach (var entry in entries) await LoadListGameAsync(entry.Id);
    }

    private async Task LoadListGameAsync(Guid entry)
    {
        var info = (await store.LoadListGameInfoAsync(entry)).Value;
        listGames[entry] = new(store.ListGameCover(entry), info, false);
    }

    /// <summary>Never looked up, or looked up before GameVault knew how to find as much as it does now.</summary>
    private static bool NeverLookedUp(GameMetadataDocument? info) =>
        info?.LastRefresh is null || (info.LookupVersion ?? 0) < GameVault.Infrastructure.Metadata.GameMetadataService.CurrentLookupVersion;

    /// <summary>A new name means another game may be meant: what was fetched goes, and it is looked up again. A cover the player chose stays.</summary>
    private async Task RenameListGameAsync(ListEntry entry)
    {
        TextBox name = null!;
        var changed = false;
        var form = new FormView(settings.Theme, "Rename " + entry.Title, async () =>
        {
            // Read here: the change itself runs on the store's thread.
            var title = name.Text.Trim();
            if (title.Length == 0) throw new ArgumentException("Enter the game's name.");
            if (title == entry.Title) return;
            await store.ChangeListsAsync(d => GameListRules.Rename(d, entry.Id, title));
            await store.ForgetListGameAsync(entry.Id);
            changed = true;
        });
        name = form.Text("Name", entry.Title, 200, help: "Its cover and details are looked up again by this name.");
        if (!await ShowFormAsync(form)) return;
        if (changed) { listTried.Remove(entry.Id); listGames.Remove(entry.Id); }
        await LoadListsAsync();
    }

    /// <summary>Looks up, one at a time, the entries that never were. Follows the "fetch game information automatically" setting.</summary>
    private async Task LookUpNewListGamesAsync()
    {
        if (listLookingUp) return;
        listLookingUp = true;
        try
        {
            if (!(await Metadata.LoadSettingsAsync()).AutoFetch) return;
            while (!Dispatcher.HasShutdownStarted &&
                   OutsideEntries().FirstOrDefault(e => !listTried.Contains(e.Id) && NeverLookedUp(listGames.GetValueOrDefault(e.Id)?.Info)) is { } next)
                await LookUpListGameAsync(next, () => Metadata.RefreshTitleAsync(next.Id, next.Title, false, CancellationToken.None), false);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { GameVault.Infrastructure.Diagnostics.AppLog.Warn("List games were not looked up: " + error.Message); }
        finally { listLookingUp = false; }
    }

    private async Task LookUpListGameAsync(ListEntry entry, Func<Task> lookUp, bool tell)
    {
        if (!listLookups.Add(entry.Id)) return;
        listTried.Add(entry.Id);
        ShowListsState();
        try
        {
            await Task.Run(lookUp);
            await FetchListCoverAsync(entry);
        }
        catch (Exception error) when (IsExpected(error) || error is IOException or ArgumentException or ArtworkException or System.Net.Http.HttpRequestException)
        {
            if (tell) ShowToast("Could not look up " + entry.Title, error.Message, true);
            else GameVault.Infrastructure.Diagnostics.AppLog.Info("A list game was not looked up: " + error.Message);
        }
        finally
        {
            listLookups.Remove(entry.Id);
            try { if (OutsideEntries().Any(e => e.Id == entry.Id)) await LoadListGameAsync(entry.Id); }
            catch (Exception error) when (IsExpected(error) || error is IOException) { GameVault.Infrastructure.Diagnostics.AppLog.Info(error.Message); }
            ShowListsState();
        }
    }

    /// <summary>Steam's portrait cover, when the lookup found the game there and it has no cover yet.</summary>
    private async Task FetchListCoverAsync(ListEntry entry)
    {
        if (store.ListGameCover(entry.Id) is not null) return;
        var info = (await store.LoadListGameInfoAsync(entry.Id)).Value;
        if (info?.SteamAppId is not { } text || !int.TryParse(text, out var appId) || appId <= 0) return;
        var page = new Uri($"https://store.steampowered.com/app/{appId}/");
        coverCatalogue ??= new SteamPortraitCoverCatalogue();
        try
        {
            var bytes = await coverCatalogue.DownloadAsync(new PortraitCoverCandidate(appId, entry.Title, "Steam", page), false, CancellationToken.None);
            await store.SaveListGameCoverAsync(entry.Id, GameVault.App.Artwork.PortraitCoverImages.Normalize(bytes, page), own: false);
        }
        catch (Exception error) when (error is ArtworkException or InvalidOperationException or System.Net.Http.HttpRequestException)
        { GameVault.Infrastructure.Diagnostics.AppLog.Info("No cover for a list game: " + error.Message); }
    }

    private Task RefreshListGameAsync(ListEntry entry) =>
        LookUpListGameAsync(entry, () => Metadata.RefreshTitleAsync(entry.Id, entry.Title, true, CancellationToken.None), true);

    private async Task MatchListGameAsync(ListEntry entry)
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation) return;
        try
        {
            var view = new MatchPickerView(settings.Theme, entry.Title, (title, token) => Metadata.SearchMatchesAsync(title, token), async candidate =>
            {
                var result = await Task.Run(() => Metadata.ApplyTitleMatchAsync(entry.Id, entry.Title, candidate, CancellationToken.None));
                await FetchListCoverAsync(entry);
                return candidate is null ? "Saved." : result.FieldsFilled > 0 ? $"Found {result.FieldsFilled} fact(s) for {candidate.Title}." : $"Saved {candidate.Title}. No facts were available for it yet.";
            });
            busy = editorOpen = true;
            UpdateButtons();
            try { await ShowInlineEditorAsync(view, h => view.Completed += h, h => view.Completed -= h); }
            finally
            {
                busy = editorOpen = false;
                try { UpdateButtons(); ContinueDeferredQuit(); }
                finally { CompleteInlineEditor(); }
            }
            await LoadListsAsync();
        }
        catch (Exception error) when (IsExpected(error) || error is IOException) { ShowToast("Could not change the match", error.Message, true); }
    }

    private async Task ChooseListCoverAsync(ListEntry entry)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a cover for " + entry.Title, Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var png = CoverImages.Normalize(dialog.FileName);
            await store.SaveListGameCoverAsync(entry.Id, png, own: true);
            CoverImages.InvalidateThumbnail(store.ListGameCover(entry.Id));
            await LoadListsAsync();
        }
        catch (Exception error) when (IsExpected(error) || error is IOException or ArgumentException or NotSupportedException) { ShowToast("The cover was not changed", error.Message, true); }
    }
}
