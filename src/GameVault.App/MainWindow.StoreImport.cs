using System.IO;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using Microsoft.Win32;

namespace GameVault.App;

// "Add from Steam / Epic": installed games in one step (ticket #85).
public partial class MainWindow
{
    private static string? SteamRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path && Directory.Exists(path)) return Path.GetFullPath(path);
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException or ArgumentException) { }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private async void AddFromStoresClicked(object sender, RoutedEventArgs e)
    {
        GameOptionsPopup.IsOpen = false;
        if (busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this)) return;
        editorOpen = true;
        UpdateButtons();
        var changed = new List<Guid>();
        try
        {
            var view = new StoreImportView(settings.Theme, new StoreLibraryScanner(SteamRoot()),
                () => [.. items.Where(i => i.Game.Profile is not null).Select(i => new LibraryExecutable(i.Game.Id, i.Game.Profile!.ExecutablePath, i.Game.Profile.IsArchived))],
                async draft => { busy = true; try { return await library.SaveAsync(draft); } finally { busy = false; } },
                async id => { busy = true; try { return await library.RestoreAsync(id); } finally { busy = false; } });
            await ShowInlineEditorAsync(view, h => view.Completed += h, h => view.Completed -= h);
            changed.AddRange(view.ChangedGameIds);
            if (changed.Count > 0)
            {
                busy = true;
                initializing = true;
                Search.Text = ""; StatusFilter.SelectedIndex = 0; ratingFilter = GameVault.Core.RatingFilters.Any; RemovedFilter.IsChecked = false;
                initializing = false;
                foreach (var id in changed) await discovery.ScanAsync(id);
                await RefreshLibrary(changed[^1]);
                QueueAutoInfo(changed.Where(libraryRows.ContainsKey).Select(id => (id, libraryRows[id].Name)).ToArray(), urgent: true);
                ShowToast($"{changed.Count} game(s) added", "Check each game's save locations on its Saves tab.");
            }
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not add the games", error.Message, true); }
        finally
        {
            busy = editorOpen = false;
            try { UpdateButtons(); ContinueDeferredQuit(); }
            finally { CompleteInlineEditor(); }
        }
    }
}
