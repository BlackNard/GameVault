using System.IO;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure;
using GameVault.Infrastructure.Storage;

namespace GameVault.App;

public partial class App : Application
{
    private ApplicationWorkspace? workspace;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = ApplicationPaths.FromRoot(AppContext.BaseDirectory);
        try
        {
            workspace = await Task.Run(() => ApplicationWorkspace.Open(paths));
            var store = new AppDataStore(paths);
            var snapshot = await store.InitializeAsync();
            MainWindow = new MainWindow(paths, store, snapshot);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            workspace?.Dispose();
            MessageBox.Show(
                $"GameVault could not open its application folder:\n\n{paths.Root}\n\n" +
                "Close any other GameVault instance using this folder and try again. " +
                "If it still cannot open, move or reinstall GameVault into a writable folder.",
                "GameVault could not start", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        workspace?.Dispose();
        base.OnExit(e);
    }
}
