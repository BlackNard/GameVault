using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Launching;

namespace GameVault.App;

public partial class LaunchSettingsWindow : UserControl
{
    private readonly Guid gameId;
    private readonly LaunchSettingsStore store;
    private bool saving;
    private bool initialized;

    public LaunchSettingsWindow(string theme, GameProfile profile, LaunchSettingsStore store, StoredDocument<LaunchSettings> settings)
    {
        gameId = profile.Id; this.store = store;
        InitializeComponent(); ThemePalette.Apply(this, theme);
        GameTitle.Text = profile.Name;
        TargetPath.Text = settings.Value?.TargetExecutablePath ?? "";
        UseLaunchExecutable.IsChecked = settings.Value?.TargetExecutablePath is null;
        BuildActionsAndHooks(settings.Value);
        initialized = true;
        TargetFields.IsEnabled = UseLaunchExecutable.IsChecked != true;
        Notice.Text = settings.Message ?? "";
        Fields.IsEnabled = SaveButton.IsEnabled = !settings.IsReadOnly;
        navigation = new(Snapshot());
    }

    private void TrackingChoiceChanged(object sender, RoutedEventArgs e)
    { if (initialized) TargetFields.IsEnabled = UseLaunchExecutable.IsChecked != true; }

    private void BrowseTarget(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = NativePickers.File(Window.GetWindow(this)!, "Choose actual game executable", "Windows executables (*.exe)|*.exe", TargetPath.Text);
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
            if (!TryReadActionsAndHooks(out var actions, out var defaultId, out var beforeHook, out var afterHook, out var problem)) { Notice.Text = problem; return; }
            await store.SaveAsync(new(1, gameId, path, actions, defaultId, beforeHook, afterHook));
            navigation.AcceptSaved(Snapshot()); saving = false; Completed?.Invoke(this, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notice.Text = error.Message; }
        finally { saving = false; Fields.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = true; }
    }

    public bool IsBusy => saving;
    public event EventHandler<bool>? Completed;
    private readonly InlineDraftGuard navigation;
    private string Snapshot() => $"{UseLaunchExecutable.IsChecked}|{TargetPath.Text}|{ActionsSnapshot()}";
    public async Task<bool> CanLeaveAsync()
    {
        if (saving) { Notice.Text = "Finishing the launch settings save..."; return false; }
        return await navigation.CanLeaveAsync(IsBusy, Snapshot(), async () => await InlinePrompts.ShowAsync(this, "Discard changes to launch settings?", "Unsaved launch settings", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes);
    }
    private async void CancelEditing(object sender, RoutedEventArgs e)
    { if (await CanLeaveAsync()) Completed?.Invoke(this, false); }
}
