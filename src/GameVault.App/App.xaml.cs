using System.IO;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure;
using GameVault.Infrastructure.Storage;
using GameVault.Infrastructure.Diagnostics;
using GameVault.Infrastructure.Launching;
using System.Security.Principal;

namespace GameVault.App;

public partial class App : Application
{
    private ApplicationWorkspace? workspace;
    private string? markerRoot;

    /// <summary>What the previous run left behind (set once the application folder is ours); the main window reads it to show a notice.</summary>
    internal static CrashMarkerResult? PreviousRun { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && e.Args[0] == "--launch-helper")
        {
            // Helper mode never opens the UI, application workspace, settings, or game profiles.
            using var identity = WindowsIdentity.GetCurrent();
            var permitted = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            var result = 1;
            if (permitted && e.Args.Length == 3 && int.TryParse(e.Args[2], out var parentId))
                result = await ElevationBroker.RunHelperAsync(e.Args[1], parentId);
            Shutdown(result);
            return;
        }
        var paths = ApplicationPaths.FromRoot(AppContext.BaseDirectory);
        AppLog.Initialize(paths.Root);
        DispatcherUnhandledException += (_, args) => AppLog.Error("Unhandled UI error", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("Unhandled error", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { AppLog.Error("Unobserved background task error", args.Exception); args.SetObserved(); };
        SplashWindow? splash = null;
        var splashShownAt = DateTimeOffset.UtcNow;
        try
        {
            var preferences = SplashPreferences.Parse(ReadSettingsText(paths));
            if (preferences.Show)
            {
                splash = new SplashWindow(preferences.Theme, preferences.ReduceMotion);
                splash.Show();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);   // let it paint before the real work starts
                splashShownAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn("The startup window could not be shown: " + error.Message);
            splash = null;
        }
        try
        {
            workspace = await Task.Run(() => ApplicationWorkspace.Open(paths));
            // Only now is this process the owner of the folder, so only now may it read or write the "running" marker.
            PreviousRun = CrashMarker.Begin(paths.Root, typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown", DateTimeOffset.Now);
            markerRoot = paths.Root;
            if (PreviousRun.PreviousRunEndedUnexpectedly) AppLog.Warn("The previous run did not end normally" + (PreviousRun.PreviousStartedAt is { } at ? $" (it started {at:yyyy-MM-dd HH:mm})" : ""));
            var store = new AppDataStore(paths);
            var snapshot = await store.InitializeAsync();
            MainWindow = new MainWindow(paths, store, snapshot);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (splash is not null)
            {
                var wait = SplashTiming.RemainingDelay(splashShownAt, DateTimeOffset.UtcNow);
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                splash.CloseOnce();
            }
            MainWindow.Show();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            splash?.CloseOnce();
            workspace?.Dispose();
            MessageBox.Show(
                $"GameVault could not open its application folder:\n\n{paths.Root}\n\n" +
                "Close any other GameVault instance using this folder and try again. " +
                "If it still cannot open, move or reinstall GameVault into a writable folder.",
                "GameVault could not start", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    private static string? ReadSettingsText(ApplicationPaths paths)
    {
        try { return File.ReadAllText(Path.Combine(paths.Data, "settings.json")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if(MainWindow is MainWindow window){window.DisposeCaptureFeatures();window.DisposeTrayOnApplicationExit();}
        if (markerRoot is not null) CrashMarker.EndCleanly(markerRoot);
        workspace?.Dispose();
        base.OnExit(e);
    }
}
