using System.IO;
using System.Windows;
using GameVault.Core;

namespace GameVault.App;

// Drag a game (.exe, a .lnk shortcut or a folder) onto the library to open the add-game form pre-filled (issue #35).
public partial class MainWindow
{
    private bool DropAllowedNow => !(busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this)) && libraryVisible;

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void LibraryDragOver(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        var allowed = DropAllowedNow;
        e.Effects = allowed ? DragDropEffects.Link : DragDropEffects.None;
        DropOverlay.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void LibraryDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private void LibraryDropped(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!HasFiles(e)) return;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        if (!DropAllowedNow) { ShowToast("GameVault is busy", "Finish what you are doing, then drop the game again.", true); return; }
        HandleDroppedItems(paths);
    }

    private DropResolution ResolveDropped(string path) => DroppedGameResolver.Resolve(path,
        ShortcutReader.Target, File.Exists, Directory.Exists,
        folder => Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly),
        folder => Directory.EnumerateDirectories(folder).Take(60),
        exe => items.Any(i => i.Game.Profile is { } p && string.Equals(p.ExecutablePath, exe, StringComparison.OrdinalIgnoreCase)));

    private void HandleDroppedItems(string[] paths)
    {
        var results = paths.Select(p => (Path: p, Result: ResolveDropped(p))).ToArray();
        var first = results.FirstOrDefault(r => r.Result.Outcome == DropOutcome.Accepted);
        var skipped = new List<string>();
        foreach (var (path, result) in results)
        {
            if (ReferenceEquals(result, first.Result) && first.Result is not null) continue;
            skipped.Add(DropMessage(path, result));
        }
        if (first.Result is null)
        {
            // nothing can be added: say why for the first item (and mention the rest) and, for a duplicate, take the player to that game
            var head = results[0];
            ShowToast(DropTitle(head.Result.Outcome), DropMessage(head.Path, head.Result) + (results.Length > 1 ? $" ({results.Length - 1} more item(s) were not added either.)" : ""), true);
            if (results.FirstOrDefault(r => r.Result.Outcome == DropOutcome.AlreadyInLibrary) is { Result: not null } dup) _ = OpenDroppedDuplicateAsync(dup.Result.ExecutablePath);
            return;
        }
        if (skipped.Count > 0)
            ShowToast($"{skipped.Count} dropped item(s) were not added", string.Join("\n", skipped.Take(4)) + (skipped.Count > 4 ? $"\n…and {skipped.Count - 4} more" : ""), false);
        OpenEditor(null, first.Result.ExecutablePath);
    }

    private static string DropTitle(DropOutcome outcome) => outcome switch
    {
        DropOutcome.AlreadyInLibrary => "Already in your library",
        DropOutcome.NoExecutableFound => "No game found in that folder",
        DropOutcome.BrokenShortcut => "That shortcut does not point to a game",
        _ => "That cannot be added as a game"
    };

    private static string DropMessage(string path, DropResolution result)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return result.Outcome switch
        {
            DropOutcome.Accepted => name + " is ready to add.",
            DropOutcome.AlreadyInLibrary => name + " is already in your library.",
            DropOutcome.NoExecutableFound => name + ": no game executable found in this folder.",
            DropOutcome.BrokenShortcut => name + ": the shortcut does not point to an existing .exe.",
            _ => name + ": drop a game's .exe, a shortcut to it, or its folder."
        };
    }

    private async Task OpenDroppedDuplicateAsync(string? executable)
    {
        var match = items.FirstOrDefault(i => i.Game.Profile is { } p && string.Equals(p.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        if (match.Game.Profile?.IsArchived == true) { ShowToast("It is in Removed profiles", "Open Removed profiles in the library to restore it.", false); return; }
        await NavigateToAsync(NavRoute.Game(match.Game.Id, "Overview"));
    }
}
