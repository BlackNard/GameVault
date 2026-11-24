using System.IO;

namespace GameVault.App.Tests;

public sealed class DragDropWiringTests
{
    private static string Source(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", name));
    }

    [Fact]
    public void The_library_accepts_dropped_files_with_a_visible_target_and_ignores_them_while_busy()
    {
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("x:Name=\"LibraryPanel\" AllowDrop=\"True\"", xaml);
        Assert.Contains("PreviewDragOver=\"LibraryDragOver\"", xaml);
        Assert.Contains("PreviewDrop=\"LibraryDropped\"", xaml);
        Assert.Contains("x:Name=\"DropOverlay\"", xaml);
        var code = Source("MainWindow.DragDrop.cs");
        Assert.Contains("DataFormats.FileDrop", code);
        Assert.Contains("busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this)", code);
        Assert.Contains("DroppedGameResolver.Resolve(", code);
        Assert.Contains("OpenEditor(null, first.Result.ExecutablePath)", code);
        Assert.DoesNotContain("Process.Start", code);                    // a drop only reads paths: nothing is run or opened
    }

    [Fact]
    public void The_editor_is_prefilled_through_the_same_suggestions_as_browse_without_counting_as_an_unsaved_edit()
    {
        var editor = Source("GameEditor.xaml.cs");
        var prefill = editor[editor.IndexOf("public void Prefill", StringComparison.Ordinal)..];
        prefill = prefill[..prefill.IndexOf("private void BrowseExecutable", StringComparison.Ordinal)];
        Assert.Contains("GamePathSuggestions.WorkingDirectory(", prefill);
        Assert.Contains("GamePathSuggestions.GameName(", prefill);
        Assert.Contains("navigation.AcceptSaved(Snapshot())", prefill);
        Assert.Contains("prefillExecutable", Source("MainWindow.xaml.cs"));
    }

    [Fact]
    public void A_real_shortcut_is_read_back_to_its_target_and_a_non_shortcut_gives_nothing()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return;                                         // not a normal Windows shell: nothing to check
        var folder = Path.Combine(Path.GetTempPath(), "GameVault shortcut", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var target = Path.Combine(folder, "Game.exe");
            File.WriteAllBytes(target, [0x4d, 0x5a]);
            var lnk = Path.Combine(folder, "Game.lnk");
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(lnk);
            link.TargetPath = target;
            link.Save();
            Assert.Equal(target, ShortcutReader.Target(lnk), ignoreCase: true);
            var notShortcut = Path.Combine(folder, "plain.lnk");
            File.WriteAllText(notShortcut, "not a shortcut");
            Assert.True(string.IsNullOrEmpty(ShortcutReader.Target(notShortcut)));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
