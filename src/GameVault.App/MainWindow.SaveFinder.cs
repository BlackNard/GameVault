using System.IO;
using GameVault.Core;
using GameVault.Infrastructure.Saves;

namespace GameVault.App;

// What the player sees when GameVault finds, or cannot find, a game's saves (issue #111).
public partial class MainWindow
{
    private const string SaveNoticeIdPrefix = "saves:";
    private static readonly TimeSpan SaveScanGap = TimeSpan.FromSeconds(2);

    private void SaveSelectionChanged(object? sender, SaveSelectionChanged change)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (stoppingObservation) return;
            if (change.NewlySelected.Length > 0) await SavesFoundAsync(change.GameId, change.GameName, change.NewlySelected);
            else AskAboutSaves(change.GameId, change.GameName, change.NewlyToConfirm);
        });
    }

    /// <summary>A confident find is backed up at once (at the end of the session when the game is running) and announced.</summary>
    private async Task SavesFoundAsync(Guid id, string name, SaveSourceMapping[] found)
    {
        try
        {
            var outcome = SaveBackupOutcome.WhenClosed;
            if (!launches.GetState(id).IsActive)
            {
                // The scan may be part of a backup already running; wait for it instead of competing with it.
                for (var waited = 0; backups.IsWorking(id) && waited < 120; waited++) await Task.Delay(500);
                if (stoppingObservation) return;
                var result = await backups.BackUpAsync(id, true);
                outcome = result.Phase is BackupPhase.Complete or BackupPhase.Unchanged ? SaveBackupOutcome.BackedUp : SaveBackupOutcome.NotYet;
            }
            if (stoppingObservation) return;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var detail = SaveFinderText.FoundDetail(found[0].Path, home, outcome) + (found.Length > 1 ? $" (+{found.Length - 1} more)" : "");
            ShowToast(SaveFinderText.FoundTitle(name), detail, false, "Not this folder", () => _ = RejectSavesAsync(id, name, found));
            PostNotice(new Notice(SaveNoticeIdPrefix + id, NoticeKind.SaveFound, SaveFinderText.FoundTitle(name), detail, DateTimeOffset.UtcNow, false, "Show"),
                () => ShowSaveSources(id));
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = name + ": the saves were found but could not be backed up yet. " + error.Message; }
    }

    private void AskAboutSaves(Guid id, string name, SaveSourceMapping[] candidates)
    {
        if (candidates.Length == 0) return;
        var source = candidates[0];
        var detail = SaveFinderText.Short(source.Path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var noticeId = SaveNoticeIdPrefix + id;
        var isNew = !notices.Items.Any(n => n.Id == noticeId);
        PostNotice(new Notice(noticeId, NoticeKind.SaveFound, SaveFinderText.AskTitle(name), detail, DateTimeOffset.UtcNow, false, "Yes, back up"),
            () => _ = ConfirmSavesAsync(id, name, source.Id));
        if (isNew) ShowToast(SaveFinderText.AskTitle(name), detail, false, "Yes, back up", () => _ = ConfirmSavesAsync(id, name, source.Id));
    }

    private async Task ConfirmSavesAsync(Guid id, string name, Guid sourceId)
    {
        try
        {
            notices.Remove(SaveNoticeIdPrefix + id);
            await discovery.ConfirmAsync(id, sourceId);
            if (launches.GetState(id).IsActive) { ShowToast(name + ": saves chosen", "Backs up when you close the game."); return; }
            var result = await backups.BackUpAsync(id, true);
            ShowToast(name + ": " + result.Message, isError: result.Phase is BackupPhase.Failed or BackupPhase.Blocked);
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("The save folder could not be confirmed", error.Message, true); ShowSaveSources(id); }
    }

    private async Task RejectSavesAsync(Guid id, string name, SaveSourceMapping[] sources)
    {
        try
        {
            notices.Remove(SaveNoticeIdPrefix + id);
            foreach (var source in sources) await discovery.RejectAsync(id, source.Id);
            ShowToast("Got it", name + " won't use that folder.");
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("The folder could not be removed", error.Message, true); }
    }

    private async Task ChooseSaveFolderAsync(Guid id)
    {
        if (stoppingObservation || quitPreparing || busy || editorOpen) return;
        lifetime.Reopen();
        try
        {
            if (NativePickers.Folder(this, "Choose game save folder") is not { } folder) return;
            await discovery.ChooseAsync(id, folder);
            if (!launches.GetState(id).IsActive)
            {
                var result = await backups.BackUpAsync(id, true);
                var name = items.FirstOrDefault(i => i.Game.Id == id)?.Name ?? "Game";
                ShowToast(name + ": " + result.Message, isError: result.Phase is BackupPhase.Failed or BackupPhase.Blocked);
            }
            await RefreshLibrary();
        }
        catch (Exception error) when (IsTrayError(error)) { ShowToast("That folder could not be used", error.Message, true); }
    }

    /// <summary>Opens a game's save sources from a notice.</summary>
    private async void ShowSaveSources(Guid id)
    {
        if (stoppingObservation || quitPreparing || busy || editorOpen) return;
        lifetime.Reopen();
        if (!await LeaveNotes()) return;
        try
        {
            await RefreshLibrary();
            var item = items.FirstOrDefault(i => i.Game.Id == id);
            if (item is null) return;
            Search.Clear(); StatusFilter.SelectedIndex = 0; ratingFilter = RatingFilters.Any; RemovedFilter.IsChecked = item.Game.Profile?.IsArchived == true;
            ApplyFilters(id); SetNavigation(true);
            ShowGamePage(); ShowGameSection("Saves"); OpenSaveSources(this, new System.Windows.RoutedEventArgs());
        }
        catch (Exception error) when (IsTrayError(error)) { OperationNotice.Text = "Could not open the save folders. " + error.Message; }
    }

    /// <summary>After start-up, one game at a time: games that have no save folder yet get a look, and unsure finds are asked about.</summary>
    private async Task ScanGamesWithoutSavesAsync()
    {
        foreach (var item in items.ToArray())
        {
            await Task.Delay(SaveScanGap);
            if (stoppingObservation || quitPreparing) return;
            var id = item.Game.Id;
            if (item.Game.IsReadOnly || item.Game.Profile is not { IsArchived: false } || launches.GetState(id).IsActive || backups.IsWorking(id)) continue;
            try
            {
                var known = await discovery.ReadAsync(id);
                if (known.Document.Value is { } document && (!document.Automatic || document.Sources.Any(s => s.Selected))) continue;
                var result = await discovery.ScanAsync(id);
                if (result.Phase == SaveDiscoveryPhase.NeedsConfirmation && result.Document.Value is { } found)
                    AskAboutSaves(id, item.Name, found.Sources.Where(s => s.CanConfirm).ToArray());
            }
            catch (Exception error) when (IsExpected(error)) { Infrastructure.Diagnostics.AppLog.Warn("A start-up save check did not finish: " + error.GetType().Name); }
        }
    }
}
