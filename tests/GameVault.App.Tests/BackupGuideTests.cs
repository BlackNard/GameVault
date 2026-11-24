using System.IO;

namespace GameVault.App.Tests;

public sealed class BackupGuideTests
{
    private static string Source(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GameVault.slnx"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir!, "src", "GameVault.App", name));
    }

    [Fact]
    public void The_guide_is_in_Settings_is_reachable_from_the_Saves_tab_and_names_the_real_buttons()
    {
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("x:Name=\"BackupGuide\"", xaml);
        Assert.Contains("Click=\"OpenBackupGuideClicked\"", xaml);
        Assert.Contains("Click=\"GuideGoToDataClicked\"", xaml);
        var guide = xaml[xaml.IndexOf("x:Name=\"BackupGuide\"", StringComparison.Ordinal)..xaml.IndexOf("x:Name=\"SetBackups\"", StringComparison.Ordinal)];
        foreach (var name in new[] { "Back up now", "Backup history", "Back up GameVault data", "Restore from a backup", "Include game backups", "Backup storage" })
            Assert.Contains(name, guide);
        // every button the guide names exists in the app
        foreach (var name in new[] { "Content=\"Back up now\"", "Content=\"Backup history\"", "Content=\"Back up GameVault data…\"", "Content=\"Restore from a backup…\"", "Include game backups and recorded media" })
            Assert.Contains(name, xaml);
        Assert.Contains("BackupGuide.IsExpanded = true", Source("MainWindow.BackupGuide.cs"));
    }

    [Fact]
    public void Back_and_Forward_close_an_open_full_window_view_first_through_its_own_guard()
    {
        var shell = Source("MainWindow.Shell.cs");
        var method = shell[shell.IndexOf("private async Task NavigateHistoryAsync", StringComparison.Ordinal)..];
        var leave = method.IndexOf("activeInlineEditor is { } open && !await open.TryLeaveAsync()", StringComparison.Ordinal);
        Assert.True(leave > 0 && leave < method.IndexOf("navHistory.Back(", StringComparison.Ordinal), "Leave the open view before the history moves.");
    }
}
