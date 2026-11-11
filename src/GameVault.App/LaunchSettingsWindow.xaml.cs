using System.ComponentModel;
using System.IO;
using System.Windows;
using GameVault.Core;
using GameVault.Infrastructure.Launching;

namespace GameVault.App;

public partial class LaunchSettingsWindow : Window
{
    private readonly Guid gameId;
    private readonly LaunchSettingsStore store;
    private bool saving;
    private bool initialized;

    public LaunchSettingsWindow(string theme, GameProfile profile, LaunchSettingsStore store, StoredDocument<LaunchSettings> settings)
    {
        gameId = profile.Id; this.store = store;
        InitializeComponent(); ThemePalette.Apply(this, theme);
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        MinHeight = Math.Min(MinHeight, MaxHeight); MinWidth = Math.Min(MinWidth, MaxWidth);
        GameTitle.Text = profile.Name;
        TargetPath.Text = settings.Value?.TargetExecutablePath ?? "";
        UseLaunchExecutable.IsChecked = settings.Value?.TargetExecutablePath is null;
        initialized = true;
        TargetFields.IsEnabled = UseLaunchExecutable.IsChecked != true;
        Notice.Text = settings.Message ?? "";
        Fields.IsEnabled = SaveButton.IsEnabled = !settings.IsReadOnly;
        Closing += BeforeClosing;
    }

    private void TrackingChoiceChanged(object sender, RoutedEventArgs e)
    { if (initialized) TargetFields.IsEnabled = UseLaunchExecutable.IsChecked != true; }

    private void BrowseTarget(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = NativePickers.File(this, "Choose actual game executable", "Windows executables (*.exe)|*.exe", TargetPath.Text);
            if (path is not null) TargetPath.Text = path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { Notice.Text = "The browser could not open. Paste a full executable path instead. " + error.Message; }
    }

    private async void SaveSettings(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        saving = true; Fields.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = false;
        try
        {
            var path = UseLaunchExecutable.IsChecked == true ? null : TargetPath.Text.Trim().Trim('"');
            await store.SaveAsync(new(1, gameId, path));
            saving = false; DialogResult = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notice.Text = error.Message; }
        finally { saving = false; Fields.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = true; }
    }

    private void BeforeClosing(object? sender, CancelEventArgs e)
    { if (saving) { e.Cancel = true; Notice.Text = "Finishing the launch settings save…"; } }
}
