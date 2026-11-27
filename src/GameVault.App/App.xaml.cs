using System.IO;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure;
using GameVault.Infrastructure.Storage;
using GameVault.Infrastructure.Diagnostics;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Updates;
using System.Security.Principal;

namespace GameVault.App;

public partial class App : Application
{
    private ApplicationWorkspace? workspace;
    private string? markerRoot;
    private SingleInstance? instance;
    private bool showRequested;      // another start asked for the window while it was still being created

    /// <summary>What the previous run left behind (set once the application folder is ours); the main window reads it to show a notice.</summary>
    internal static CrashMarkerResult? PreviousRun { get; private set; }

    /// <summary>Where downloaded update installers are kept (outside the application folder, so the library is never touched).</summary>
    internal static string UpdatesFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameVault-Updates");

    /// <summary>A verified installer the player asked for. It is started only after this process released the application folder, and only when GameVault really quits.</summary>
    internal static string? PendingUpdateInstaller { get; set; }

    /// <summary>True when the player asked to restart and update, so the new version opens when setup finishes. A normal quit installs without reopening.</summary>
    internal static bool RelaunchAfterUpdate { get; set; }

    /// <summary>Set by the main window while automatic updates are on: a verified update that is waiting installs when GameVault quits.</summary>
    internal static bool AutoUpdateEnabled { get; set; }

    /// <summary>
    /// Starts the installer over the installed copy once this process has released the application folder. It runs for the update the player asked for,
    /// or (automatic updates on) for a verified one that is waiting. The file is checked again here, right before it is started; nothing runs for an
    /// update that failed too often. A player who simply closes GameVault gets the new version at the next start without a surprise window.
    /// </summary>
    private static void StartPendingUpdate(bool relaunch)
    {
        var store = new PendingUpdateStore(UpdatesFolder);
        var pending = store.Read();
        if (pending is null || !ReleaseVersion.TryParse(AppVersionInfo.Current, out var running)) return;
        if (!AutoUpdateRules.ShouldInstall(true, pending, running) || !store.IsIntact(pending)) return;
        if (!StartWithSafetyCopy(store, pending, relaunch)) AppLog.Warn("The update installer was not started.");
    }

    /// <summary>
    /// The one place an installer is started. First a verified safety copy of the library's data and its fingerprint are made (see <see cref="UpdateSafety"/>);
    /// when that fails the installer is not started at all, and the attempt still counts so a permanently failing copy does not loop.
    /// </summary>
    private static bool StartWithSafetyCopy(PendingUpdateStore store, PendingUpdate pending, bool relaunch)
    {
        var counted = store.RecordAttempt(pending, DateTimeOffset.UtcNow);
        var paths = ApplicationPaths.FromRoot(AppContext.BaseDirectory);
        SafetyCopy copy;
        try { copy = Task.Run(() => UpdateSafety.PrepareAsync(paths, UpdatesFolder, AppVersionInfo.Current)).GetAwaiter().GetResult(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Warn("The update was not started because the safety copy of the library could not be made: " + error.Message);
            return false;
        }
        counted = counted with { SafetyArchive = Path.GetFileName(copy.ArchivePath), FingerprintFile = copy.FingerprintFile };
        store.Write(counted);
        AppLog.Info($"Safety copy made before the update ({copy.Fingerprint.FileCount} files in the library).");
        return UpdateLauncher.Start(store.InstallerPath(counted), UpdatesFolder, relaunch, Environment.ProcessId);
    }

    /// <summary>
    /// Right after an update, before the library is opened: it must be exactly as it was when the installer started. Returns false when the player chose to quit.
    /// </summary>
    private static bool LibraryIsSafeAfterUpdate(ApplicationPaths paths)
    {
        if (!ReleaseVersion.TryParse(AppVersionInfo.Current, out var running)) return true;
        var check = UpdateSafety.VerifyAfterUpdate(paths, UpdatesFolder, running);
        if (check.Status == AfterUpdateStatus.NotApplicable) return true;
        if (check.Status == AfterUpdateStatus.Intact)
        {
            AppLog.Info($"After the update all {check.FileCount} files of the library were checked and are unchanged.");
            new PendingUpdateStore(UpdatesFolder).Clear();
            return true;
        }
        var difference = check.Difference!;
        AppLog.Warn("After the update the library differs from before: " + difference.Summary(5));
        var answer = MessageBox.Show(
            "GameVault was updated, but your library is not exactly as it was before the update:\n\n" + difference.Summary(3) + "\n\n" +
            (check.SafetyArchive is { } archive ? "A safety copy of your data was made before the update:\n" + archive + "\n\n" : "") +
            "You can restore it in Settings, under GameVault data (Restore from a backup…). Open GameVault anyway?\n\nYes opens GameVault. No closes it without changing anything.",
            "GameVault update check", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return false;
        new PendingUpdateStore(UpdatesFolder).Clear();
        return true;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && e.Args[0] == "--stop-helper")
        {
            // Stops an administrator game and ends; like the launch helper it opens no window or data.
            using var current = WindowsIdentity.GetCurrent();
            Shutdown(new WindowsPrincipal(current).IsInRole(WindowsBuiltInRole.Administrator) ? ElevationBroker.RunStopHelper(e.Args) : 1);
            return;
        }
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
        // GameVault already runs from this folder (often just in the tray): bring its window up and end quietly. This comes before anything else, so a second
        // start never installs an update, checks the library or writes into the folder the running copy owns.
        if (ActivateRunningCopy(paths))
        {
            Shutdown(0);
            return;
        }
        AppLog.Initialize(paths.Root);
        // A verified update that is still waiting (GameVault was not closed normally, or it lives in the tray) installs now, before anything else is opened.
        if (UpdatePreferences.ParseAutoUpdate(ReadSettingsText(paths)) && TryInstallWaitingUpdateAtStart())
        {
            Shutdown(0);
            return;
        }
        if (!LibraryIsSafeAfterUpdate(paths))
        {
            Shutdown(0);
            return;
        }
        DispatcherUnhandledException += (_, args) => AppLog.Error("Unhandled UI error", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("Unhandled error", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { AppLog.Error("Unobserved background task error", args.Exception); args.SetObserved(); };
        SplashWindow? splash = null;
        var splashShownAt = DateTimeOffset.UtcNow;
        try
        {
            var preferences = SplashPreferences.Parse(ReadSettingsText(paths));
            splash = new SplashWindow(preferences.Theme, preferences.ReduceMotion);
            splash.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);   // let it paint before the real work starts
            splashShownAt = DateTimeOffset.UtcNow;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn("The startup window could not be shown: " + error.Message);
            splash = null;
        }
        try
        {
            workspace = await Task.Run(() => ApplicationWorkspace.Open(paths));
            instance = SingleInstance.Listen(paths.Root, ShowForAnotherStart);
            // Only now is this process the owner of the folder, so only now may it read or write the "running" marker.
            PreviousRun = CrashMarker.Begin(paths.Root, typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown", DateTimeOffset.Now);
            markerRoot = paths.Root;
            if (PreviousRun.PreviousRunEndedUnexpectedly) AppLog.Warn("The previous run did not end normally" + (PreviousRun.PreviousStartedAt is { } at ? $" (it started {at:yyyy-MM-dd HH:mm})" : ""));
            // Every start looks for a newer release before the window opens, so it is in the bell right away (issue #100). It runs beside the loading.
            var checkStarted = DateTimeOffset.UtcNow;
            var updateCheck = UpdatePreferences.ParseCheckForUpdates(ReadSettingsText(paths)) ? GameVault.App.MainWindow.AskForUpdateAsync() : null;
            // Capture folders from older versions get readable date names, before anything lists or records captures.
            var renamed = await Task.Run(() => GameVault.Infrastructure.Capture.MediaFolders.MigrateAll(paths));
            if (renamed > 0) AppLog.Info($"Renamed {renamed} capture folder(s) to date names.");
            var store = new AppDataStore(paths);
            var snapshot = await store.InitializeAsync();
            var window = new MainWindow(paths, store, snapshot) { StartupCheck = updateCheck };
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (updateCheck is not null && !updateCheck.IsCompleted)
            {
                // Offline or slow: the window does not wait longer than this, and the notice arrives when the answer does.
                var left = UpdateCheckRules.StartWait - (DateTimeOffset.UtcNow - checkStarted);
                splash?.ShowStatus("Checking for updates…");
                if (left > TimeSpan.Zero) await Task.WhenAny(updateCheck, Task.Delay(left));
            }
            if (splash is not null)
            {
                var wait = SplashTiming.RemainingDelay(splashShownAt, DateTimeOffset.UtcNow);
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                splash.CloseOnce();
            }
            MainWindow.Show();
            if (showRequested) ((MainWindow)MainWindow).ShowForAnotherStart();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            splash?.CloseOnce();
            workspace?.Dispose();
            // Two starts at the same moment: the other one may only just have begun listening.
            if (SingleInstance.TryActivateExisting(paths.Root, TimeSpan.FromSeconds(5), AllowAnyForeground))
            {
                Shutdown(0);
                return;
            }
            MessageBox.Show(
                $"GameVault could not open its application folder:\n\n{paths.Root}\n\n" +
                "Close any other GameVault instance using this folder and try again. " +
                "If it still cannot open, move or reinstall GameVault into a writable folder.",
                "GameVault could not start", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    private static bool ActivateRunningCopy(ApplicationPaths paths) =>
        SingleInstance.TryActivateExisting(paths.Root, TimeSpan.FromSeconds(5), AllowAnyForeground);

    /// <summary>The start the player just made has the right to take the foreground; it passes that to the running copy.</summary>
    private static void AllowAnyForeground() => AllowSetForegroundWindow(-1);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Called on a background thread when GameVault is started again.</summary>
    private bool ShowForAnotherStart() => Dispatcher.Invoke(() =>
    {
        if (Dispatcher.HasShutdownStarted) return false;
        if (MainWindow is MainWindow window && window.HasBeenShown) window.ShowForAnotherStart();
        else showRequested = true;
        return true;
    });

    private static bool TryInstallWaitingUpdateAtStart()
    {
        var store = new PendingUpdateStore(UpdatesFolder);
        var pending = store.Read();
        if (pending is null || !ReleaseVersion.TryParse(AppVersionInfo.Current, out var running) || !AutoUpdateRules.ShouldInstall(true, pending, running) || !store.IsIntact(pending)) return false;
        return StartWithSafetyCopy(store, pending, relaunch: true);
    }

    private static string? ReadSettingsText(ApplicationPaths paths)
    {
        try { return File.ReadAllText(Path.Combine(paths.Data, "settings.json")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();      // first: a closing copy must not answer a new start
        instance = null;
        if(MainWindow is MainWindow window){window.DisposeCaptureFeatures();window.DisposeTrayOnApplicationExit();}
        if (markerRoot is not null) CrashMarker.EndCleanly(markerRoot);
        workspace?.Dispose();
        // The application folder lock is released: now the installer can replace the files in place (the player's Data and Profiles are not part of it).
        if (PendingUpdateInstaller is not null) StartPendingUpdate(RelaunchAfterUpdate);
        else if (AutoUpdateEnabled) StartPendingUpdate(relaunch: false);
        base.OnExit(e);
    }
}
