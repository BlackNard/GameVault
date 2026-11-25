using System.IO;
using System.Windows;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

// "Import from an old GameVault folder" (issue #53): copy chosen game profiles from an old copy into this library.
public partial class MainWindow
{
    private async void ImportOldFolderClicked(object sender, RoutedEventArgs e)
    {
        if (DataBackupBlocked()) return;
        var folder = NativePickers.Folder(this, "Choose the old GameVault folder (the one with a Profiles folder inside)");
        if (folder is null) return;
        var importer = new ProfileImporter(paths, store);
        busy = editorOpen = true; UpdateButtons();
        try
        {
            var scan = await Task.Run(() => importer.ScanAsync(folder));
            var view = new ProfileImportView(settings.Theme, scan, ids => Task.Run(() => importer.ImportAsync(folder, ids)));
            await ShowInlineEditorAsync(view, h => view.Completed += h, h => view.Completed -= h);
            if (view.ImportedIds.Count > 0)
            {
                initializing = true;
                Search.Text = ""; StatusFilter.SelectedIndex = 0; RemovedFilter.IsChecked = false;
                initializing = false;
                await RefreshLibrary(view.ImportedIds[^1]);
                ShowToast($"{view.ImportedIds.Count} game(s) imported", "Check that each game's program path is still right.");
            }
        }
        catch (ArgumentException error) { ShowToast("That folder cannot be used", error.Message + " Nothing was changed.", true); }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not import the games", error.Message, true); }
        finally
        {
            busy = editorOpen = false;
            try { UpdateButtons(); ContinueDeferredQuit(); }
            finally { CompleteInlineEditor(); }
        }
    }
}
