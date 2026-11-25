using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Infrastructure.Library;

namespace GameVault.App.Tests;

public sealed class ProfileImportViewTests
{
    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static ImportCandidate Game(Guid id, string name, ImportStatus status) =>
        new(id, name, name, status, status == ImportStatus.Ready ? null : "Already in your library.", @"C:\x\game.exe", true, 60, 1, 0, 10);

    [Fact]
    public void Only_importable_games_can_be_ticked_and_only_those_are_imported() => InSta(() =>
    {
        var fresh = Guid.NewGuid();
        var scan = new ImportScan(@"C:\old\Profiles", [Game(fresh, "New one", ImportStatus.Ready), Game(Guid.NewGuid(), "Mine", ImportStatus.AlreadyInLibrary)]);
        IReadOnlyCollection<Guid>? asked = null;
        var view = new ProfileImportView("Midnight", scan, ids => { asked = ids; return Task.FromResult(new ImportResult([.. ids], [])); });
        var completed = new List<bool>();
        view.Completed += (_, changed) => completed.Add(changed);
        Assert.True(view.Choices[0] is { IsEnabled: true, IsChecked: true });
        Assert.True(view.Choices[1] is { IsEnabled: false, IsChecked: false });
        Assert.True(view.ImportButton.IsEnabled);
        view.ImportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([fresh], asked);
        Assert.Equal([fresh], view.ImportedIds);
        Assert.Equal([true], completed);
    });

    [Fact]
    public void Settings_the_guide_and_the_welcome_lead_to_the_import()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        string Source(string name) => File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", name));
        Assert.Contains("x:Name=\"ImportOldFolderButton\"", Source("MainWindow.xaml"));
        Assert.Contains("Old GameVault folder left behind?", Source("MainWindow.xaml"));
        Assert.Contains("case WelcomeAction.ImportOldFolder: ImportOldFolderClicked", Source("MainWindow.Welcome.cs"));
        Assert.Contains("ProfileImportView editor=>editor.CanLeaveAsync", Source("MainWindow.Navigation.cs"));
        var window = Source("MainWindow.ProfileImport.cs");
        Assert.Contains("if (DataBackupBlocked()) return;", window);      // never while a game or backup runs
    }
}
