using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameVault.App.Tests;

public sealed class SplashWindowTests
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

    private static string Source(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", name));
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    [Fact]
    public void The_splash_is_a_small_borderless_window_that_does_not_take_focus_or_a_taskbar_button() => InSta(() =>
    {
        var splash = new SplashWindow("Crimson", reduceMotion: false);
        Assert.Equal(WindowStyle.None, splash.WindowStyle);
        Assert.False(splash.ShowInTaskbar);
        Assert.False(splash.ShowActivated);
        Assert.Equal(WindowStartupLocation.CenterScreen, splash.WindowStartupLocation);
        Assert.InRange(splash.Width, 300, 600);
        Assert.InRange(splash.Height, 180, 400);
        Assert.Equal("GameVault is starting", AutomationProperties.GetName(splash));
        Assert.Contains(Find<TextBlock>(splash), t => t.Text == "GameVault");
        Assert.Contains(Find<TextBlock>(splash), t => t.Text.StartsWith("Loading"));
        Assert.True(splash.Resources.Contains("AppBackground"));      // coloured by the saved theme
    });

    [Fact]
    public void Reduce_motion_turns_the_progress_animation_off_and_closing_twice_is_harmless() => InSta(() =>
    {
        var animated = new SplashWindow("Midnight", reduceMotion: false);
        var still = new SplashWindow("Midnight", reduceMotion: true);
        Assert.True(Find<ProgressBar>(animated).Single().IsIndeterminate);
        Assert.False(Find<ProgressBar>(still).Single().IsIndeterminate);
        animated.CloseOnce();
        animated.CloseOnce();                                         // a failure path may close it again
        Assert.True(animated.IsClosed);
        var neverShown = new SplashWindow("Midnight", reduceMotion: false);
        neverShown.CloseOnce();
        Assert.True(neverShown.IsClosed);
    });

    [Fact]
    public void Startup_shows_the_splash_only_in_normal_mode_waits_the_minimum_and_closes_it_before_an_error_dialog_or_the_main_window()
    {
        var app = Source("App.xaml.cs");
        var helper = app.IndexOf("--launch-helper", StringComparison.Ordinal);
        var show = app.IndexOf("splash.Show()", StringComparison.Ordinal);
        Assert.True(helper >= 0 && show > helper, "The elevated helper must never show a splash.");
        Assert.True(show < app.IndexOf("ApplicationWorkspace.Open", StringComparison.Ordinal), "Show it before the slow work starts.");
        Assert.Contains("SplashPreferences.Parse(ReadSettingsText(paths))", app);
        Assert.Contains("SplashTiming.RemainingDelay(", app);
        Assert.True(app.IndexOf("splash.CloseOnce()", StringComparison.Ordinal) < app.IndexOf("MainWindow.Show()", StringComparison.Ordinal));
        Assert.True(app.IndexOf("splash?.CloseOnce()", StringComparison.Ordinal) < app.IndexOf("GameVault could not start", StringComparison.Ordinal), "Close it before the error dialog.");
        Assert.DoesNotContain("preferences.Show", app);                                  // the startup window is always shown
        Assert.DoesNotContain("StartupSplashChoice", Source("MainWindow.xaml"));          // and there is no switch for it
    }
}
