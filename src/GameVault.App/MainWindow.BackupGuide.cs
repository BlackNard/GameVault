using System.Windows;
using System.Windows.Threading;

namespace GameVault.App;

// The in-app guide for backing up and restoring (issue #47): a collapsible section in Settings, also reachable from a game's Saves tab.
public partial class MainWindow
{
    private async void OpenBackupGuideClicked(object sender, RoutedEventArgs e)
    {
        if (editorOpen || InlinePrompts.IsOpen(this) || !await LeaveNotes()) return;
        settingsCategory = GameVault.Core.SettingsCatalog.Backups;
        ApplySettingsCategory();
        SetNavigation(false);
        BackupGuide.IsExpanded = true;
        await Dispatcher.InvokeAsync(() => BackupGuide.BringIntoView(), DispatcherPriority.Loaded);
    }

    private void GuideGoToDataClicked(object sender, RoutedEventArgs e) => SetData.BringIntoView();      // same page as the guide
}
