using System.IO;

namespace GameVault.App.Tests;

/// <summary>Source-contract checks that the crash capture is wired into the real startup and quit paths.</summary>
public sealed class CrashCaptureWiringTests
{
    private static string Source(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", name));
    }

    [Fact]
    public void Startup_writes_the_marker_only_after_the_workspace_is_owned_and_quit_removes_only_its_own_marker()
    {
        var app = Source("App.xaml.cs");
        Assert.True(app.IndexOf("ApplicationWorkspace.Open", StringComparison.Ordinal) < app.IndexOf("CrashMarker.Begin", StringComparison.Ordinal),
            "A second instance that fails to open the folder must not touch the running instance's marker.");
        Assert.Contains("markerRoot = paths.Root", app);
        Assert.Contains("if (markerRoot is not null) CrashMarker.EndCleanly(markerRoot)", app);
        Assert.Contains("TaskScheduler.UnobservedTaskException", app);
        Assert.Contains("DispatcherUnhandledException", app);
    }

    [Fact]
    public void The_main_window_offers_diagnostics_after_an_unclean_end()
    {
        var window = Source("MainWindow.xaml.cs");
        var diagnostics = Source("MainWindow.Diagnostics.cs");
        Assert.Contains("Loaded += ShowPreviousRunNotice", window);
        Assert.Contains("PreviousRunEndedUnexpectedly: true", diagnostics);
        Assert.Contains("Save diagnostics", diagnostics);
    }
}
