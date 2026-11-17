using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App.Tests;

public sealed class CaptureRecorderChoiceTests
{
    [Fact]
    public void Nvidia_recorder_turns_off_gamevaults_own_capture_buttons_and_shows_the_hotkey_hint() => InSta(() =>
    {
        var controls = new CaptureControls();
        var info = new NvidiaAppInfo(true, true, @"D:\Videos\NVIDIA");
        controls.SetRecorder(CaptureSource.Auto, info, true, true);
        controls.Update("TEKKEN 8", "status", running: true, active: false, busy: false);
        foreach (var text in new[] { "Screenshot", "Record video", "Show controls" }) Assert.False(Find<Button>(controls, b => b.Content as string == text).IsEnabled, text);
        var import = Find<Button>(controls, b => b.Content as string == "Copy NVIDIA captures now");
        Assert.Equal(Visibility.Visible, import.Visibility);
        Assert.True(import.IsEnabled);
        var texts = All<TextBlock>(controls).Select(t => t.Text).ToArray();
        Assert.Contains(texts, t => t.Contains("NVIDIA App detected and running") && t.Contains(@"D:\Videos\NVIDIA") && t.Contains("Alt+F9"));
    });

    [Fact]
    public void Gamevault_recorder_keeps_its_buttons_and_hides_the_import_button_without_nvidia() => InSta(() =>
    {
        var controls = new CaptureControls();
        controls.SetRecorder(CaptureSource.GameVault, NvidiaAppInfo.NotFound, false, true);
        controls.Update("Game", "status", running: true, active: false, busy: false);
        Assert.True(Find<Button>(controls, b => b.Content as string == "Screenshot").IsEnabled);
        Assert.True(Find<Button>(controls, b => b.Content as string == "Record video").IsEnabled);
        Assert.Equal(Visibility.Collapsed, Find<Button>(controls, b => b.Content as string == "Copy NVIDIA captures now").Visibility);
    });

    [Fact]
    public void Only_a_user_choice_raises_the_change_event_and_the_buttons_raise_import() => InSta(() =>
    {
        var controls = new CaptureControls();
        var chosen = new List<CaptureSource>(); var imports = 0;
        controls.SourceChanged += (_, source) => chosen.Add(source);
        controls.ImportRequested += (_, _) => imports++;
        controls.SetRecorder(CaptureSource.NvidiaApp, new NvidiaAppInfo(true, false, null), true, true);
        Assert.Empty(chosen); // showing the saved choice is not a change
        var combo = Find<ComboBox>(controls, _ => true);
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal(["Automatic (NVIDIA App if installed)", "GameVault recorder", "NVIDIA App"], combo.Items.Cast<string>().ToArray());
        combo.SelectedIndex = 1;
        Assert.Equal([CaptureSource.GameVault], chosen);
        combo.SelectedIndex = 2;
        Assert.Equal([CaptureSource.GameVault, CaptureSource.NvidiaApp], chosen);
        Find<Button>(controls, b => b.Content as string == "Copy NVIDIA captures now").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, imports);
    });

    [Fact]
    public void The_choice_cannot_be_edited_while_recording_or_without_a_game() => InSta(() =>
    {
        var controls = new CaptureControls();
        controls.SetRecorder(CaptureSource.Auto, new NvidiaAppInfo(true, false, null), false, false);
        Assert.False(Find<ComboBox>(controls, _ => true).IsEnabled);
        Assert.False(Find<Button>(controls, b => b.Content as string == "Copy NVIDIA captures now").IsEnabled);
    });

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in All<T>(child)) yield return found;
        if (root is ContentControl { Content: DependencyObject content })
            foreach (var found in All<T>(content)) yield return found;
    }
    private static T Find<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject => All<T>(root).First(predicate);

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Headless control test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
