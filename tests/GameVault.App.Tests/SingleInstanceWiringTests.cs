using System.IO;

namespace GameVault.App.Tests;

public sealed class SingleInstanceWiringTests
{
    [Fact]
    public void A_second_start_wakes_the_running_window_before_it_touches_the_folder_and_a_closing_copy_stops_answering()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        var app = File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", "App.xaml.cs"));
        var startup = app[app.IndexOf("protected override async void OnStartup", StringComparison.Ordinal)..];
        var activate = startup.IndexOf("if (ActivateRunningCopy(paths))", StringComparison.Ordinal);
        Assert.True(activate > startup.IndexOf("--launch-helper", StringComparison.Ordinal), "The elevated helper is not a second start.");
        foreach (var later in new[] { "AppLog.Initialize", "TryInstallWaitingUpdateAtStart()", "LibraryIsSafeAfterUpdate(paths)", "new SplashWindow", "ApplicationWorkspace.Open" })
            Assert.True(activate < startup.IndexOf(later, StringComparison.Ordinal), later);
        Assert.True(startup.IndexOf("ApplicationWorkspace.Open", StringComparison.Ordinal) < startup.IndexOf("SingleInstance.Listen", StringComparison.Ordinal), "Only the owner of the folder listens.");
        var exit = app[app.IndexOf("protected override void OnExit", StringComparison.Ordinal)..];
        Assert.True(exit.IndexOf("instance?.Dispose();", StringComparison.Ordinal) < exit.IndexOf("workspace?.Dispose();", StringComparison.Ordinal));
        var tray = File.ReadAllText(Path.Combine(dir.FullName, "src", "GameVault.App", "MainWindow.Tray.cs"));
        Assert.Contains("internal void ShowForAnotherStart()", tray);
    }
}
