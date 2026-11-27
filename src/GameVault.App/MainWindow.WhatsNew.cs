using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure.Updates;

namespace GameVault.App;

// "What's new" after an update (issue #100): shown once, when the app is free, so it never holds up the start. Settings > Updates opens it again.
public partial class MainWindow
{
    /// <summary>Whether this library was in use before this start; a first install is not told what changed.</summary>
    private bool usedBefore;

    private async void ShowWhatsNewAfterUpdate(object sender, RoutedEventArgs e)
    {
        Loaded -= ShowWhatsNewAfterUpdate;
        var seen = SeenVersion.Read(paths.Root);
        var notes = ReleaseNotes.Unseen(seen, AppVersionInfo.Current, usedBefore);
        if (notes.Length > 0)
        {
            // It waits for the library to load and for anything the player already opened.
            while (!stoppingObservation && (!IsVisible || !CanShowWhatsNew())) await Task.Delay(500);
            if (stoppingObservation) return;
        }
        // Remembered when it opens (or when there is nothing to show), so it appears once.
        SeenVersion.Write(paths.Root, ReleaseNotes.Newest(seen, AppVersionInfo.Current));
        if (notes.Length > 0) await ShowWhatsNewAsync(notes);
    }

    private bool CanShowWhatsNew() => !(busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this));

    private async void WhatsNewClicked(object sender, RoutedEventArgs e) => await ShowWhatsNewAsync(ReleaseNotes.UpTo(AppVersionInfo.Current));

    private async Task ShowWhatsNewAsync(ReleaseNote[] notes)
    {
        if (!CanShowWhatsNew()) return;
        editorOpen = true; UpdateButtons();
        try
        {
            var view = new WhatsNewView(settings.Theme, notes);
            await ShowInlineEditorAsync(view, h => view.Completed += h, h => view.Completed -= h);
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not show what's new", error.Message, true); }
        finally { editorOpen = false; CompleteInlineEditor(); UpdateButtons(); ContinueDeferredQuit(); }
    }
}
