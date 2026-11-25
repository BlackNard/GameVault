using System.Diagnostics;
using System.IO;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.App;

// What the capture tiles do (issue #60). Every action lists the game's captures again first, so only a file that is still inside the profile's media folder is used.
public partial class MainWindow
{
    private CaptureCard CardFor(CaptureMedia media) => new(media, OpenCapture, ShowCaptureInExplorer, CopyCapturePath);

    private async Task<(CaptureMedia Current, CaptureMedia[] All)> FindCaptureAsync(CaptureMedia media)
    {
        if (capture is null) throw new IOException("Captures are not available.");
        var all = await capture.ListMediaAsync(media.ProfileId);
        return (all.SingleOrDefault(m => m.Id == media.Id) ?? throw new IOException("This capture is no longer available."), all);
    }

    private static bool IsCaptureError(Exception error) => IsExpected(error) || error is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException;

    /// <summary>A screenshot is previewed inside GameVault; a recording opens in the Windows default video player.</summary>
    private async void OpenCapture(CaptureMedia media)
    {
        try
        {
            var (current, all) = await FindCaptureAsync(media);
            if (current.Status != CaptureMediaStatus.Complete) throw new IOException("This capture did not finish, so it cannot be opened.");
            if (current.Kind == CaptureMediaKind.Video) OpenInDefaultApp(current);
            else await PreviewScreenshotAsync(current, all);
        }
        catch (Exception error) when (IsCaptureError(error)) { ShowToast("Could not open the capture", error.Message, true); }
    }

    private async Task PreviewScreenshotAsync(CaptureMedia first, CaptureMedia[] all)
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this)) { ShowToast("GameVault is busy", "Finish what you are doing first, then try again.", true); return; }
        editorOpen = true; UpdateButtons();
        try
        {
            var view = new CapturePreviewView(settings.Theme, all, first,
                async shown => await WithCaptureAsync(shown, OpenInDefaultApp, "Could not open the capture"),
                ShowCaptureInExplorer);
            await ShowInlineEditorAsync(view, h => view.Completed += h, h => view.Completed -= h);
        }
        finally { editorOpen = false; CompleteInlineEditor(); UpdateButtons(); ContinueDeferredQuit(); }
    }

    private async Task WithCaptureAsync(CaptureMedia media, Action<CaptureMedia> action, string failure)
    {
        try { action((await FindCaptureAsync(media)).Current); }
        catch (Exception error) when (IsCaptureError(error)) { ShowToast(failure, error.Message, true); }
    }

    private static void OpenInDefaultApp(CaptureMedia media)
    {
        if (media.Status != CaptureMediaStatus.Complete) throw new IOException("This capture did not finish, so it cannot be opened.");
        Process.Start(new ProcessStartInfo(media.FilePath) { UseShellExecute = true });
    }

    private async void ShowCaptureInExplorer(CaptureMedia media) => await WithCaptureAsync(media,
        current => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{current.FilePath}\"") { UseShellExecute = true }), "Could not show the file");

    private async void CopyCapturePath(CaptureMedia media) => await WithCaptureAsync(media,
        current => { Clipboard.SetText(current.FilePath); ShowToast("Path copied", Path.GetFileName(current.FilePath)); }, "Could not copy the path");

    private void OpenCaptureFolderClicked(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is LibraryItemView item) OpenCaptureFolder(item.Game.Id);
    }

    private void OpenCaptureFolder(Guid game)
    {
        try
        {
            var folder = Path.Combine(ProfileFolders.Resolve(paths, game), "Media");
            if (!Directory.Exists(folder)) { ShowToast("No captures yet", "The folder is created with the first screenshot or recording."); return; }
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders are not opened.");
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception error) when (IsCaptureError(error)) { ShowToast("Could not open the folder", error.Message, true); }
    }
}
