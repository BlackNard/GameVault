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
            // Capture folders from older versions get readable date names, before anything lists or records captures.
            var renamed = await Task.Run(() => GameVault.Infrastructure.Capture.MediaFolders.MigrateAll(paths));
            if (renamed > 0) AppLog.Info($"Renamed {renamed} capture folder(s) to date names.");
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
        if(MainWindow is MainWindow window){window.DisposeCaptureFeatures();window.DisposeTrayOnApplicationExit();}
        if (markerRoot is not null) CrashMarker.EndCleanly(markerRoot);
        workspace?.Dispose();
        // The application folder lock is released: now the installer can replace the files in place (the player's Data and Profiles are not part of it).
        if (PendingUpdateInstaller is not null) StartPendingUpdate(RelaunchAfterUpdate);
        else if (AutoUpdateEnabled) StartPendingUpdate(relaunch: false);
        base.OnExit(e);
    }
}
