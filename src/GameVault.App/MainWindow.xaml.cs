using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.ComponentModel;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.App;

public partial class MainWindow : Window
{
    private readonly AppDataStore store;
    private AppSettings settings;
    private bool initializing = true;
    private readonly bool settingsReadOnly;
    private bool saving;
    private bool closeAfterSave;

    public MainWindow(ApplicationPaths paths, AppDataStore store, AppDataSnapshot snapshot)
    {
        this.store = store;
        settings = snapshot.Settings.Value ?? AppSettings.Default;
        settingsReadOnly = snapshot.Settings.IsReadOnly;
        InitializeComponent();
        DataContext = paths;
        ThemePalette.Apply(this, settings.Theme);
        ThemeChoice.SelectedIndex = settings.Theme == "Crimson" ? 1 : 0;
        ThemeChoice.IsEnabled = !settingsReadOnly;
        ProfileSummary.Text = $"{snapshot.Games.Length} stored game profiles. Adding games arrives in the next step.";
        StorageNotice.Text = snapshot.Warnings.Length > 0
            ? string.Join("\n", snapshot.Warnings.Distinct())
            : "Settings are saved locally. Your selected theme will be restored when you reopen GameVault.";
        initializing = false;
        Closing += BeforeClosing;
    }

    private async void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || settingsReadOnly || ThemeChoice.SelectedItem is not ComboBoxItem choice || choice.Tag is not string theme || theme == settings.Theme) return;
        ThemeChoice.IsEnabled = false;
        saving = true;
        try
        {
            var updated = settings with { Theme = theme };
            await store.SaveSettingsAsync(updated);
            settings = updated;
            ThemePalette.Apply(this, theme);
            StorageNotice.Text = "Theme saved. It will be restored next time you open GameVault.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            initializing = true;
            ThemeChoice.SelectedIndex = settings.Theme == "Crimson" ? 1 : 0;
            initializing = false;
            StorageNotice.Text = "The theme could not be saved. Your previous selection was kept. " + error.Message;
        }
        finally
        {
            saving = false;
            ThemeChoice.IsEnabled = !settingsReadOnly;
            if (closeAfterSave) Close();
        }
    }

    private void BeforeClosing(object? sender, CancelEventArgs e)
    {
        if (!saving) return;
        // Keep the cross-process workspace lease until the pending write ends.
        e.Cancel = true;
        closeAfterSave = true;
        StorageNotice.Text = "Finishing the settings save before closing…";
    }
}
