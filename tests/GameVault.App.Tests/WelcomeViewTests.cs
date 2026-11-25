using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App.Tests;

public sealed class WelcomeViewTests
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

    [Fact]
    public void The_embedded_welcome_offers_the_five_ways_in_and_a_skip_and_each_button_raises_its_action() => InSta(() =>
    {
        var view = new WelcomeView("Midnight", embedded: true);
        var chosen = new List<WelcomeAction>();
        view.ActionChosen += (_, action) => chosen.Add(action);
        Assert.Equal(6, view.Buttons.Count);
        Assert.All(view.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
        foreach (var button in view.Buttons) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([WelcomeAction.AddGame, WelcomeAction.ImportFromStores, WelcomeAction.ChooseTheme, WelcomeAction.DownloadSaveDatabase, WelcomeAction.ImportOldFolder, WelcomeAction.Skip],
            chosen.OrderBy(a => a == WelcomeAction.Skip).ToArray());
        Assert.Contains(view.Buttons, b => (string)b.Content == "Skip for now");
    });

    [Fact]
    public void The_drawer_welcome_closes_after_an_action_and_never_offers_skip() => InSta(() =>
    {
        var view = new WelcomeView("Crimson", embedded: false);
        var completed = new List<bool>();
        var chosen = new List<WelcomeAction>();
        view.Completed += (_, saved) => completed.Add(saved);
        view.ActionChosen += (_, action) => chosen.Add(action);
        Assert.DoesNotContain(view.Buttons, b => (string)b.Content == "Skip for now");
        view.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([WelcomeAction.AddGame], chosen);
        Assert.Equal([true], completed);
        view.Buttons[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));      // Close
        Assert.Equal([true, false], completed);
        Assert.True(view.CanLeaveAsync().Result);
    });

    [Fact]
    public void The_library_uses_the_rule_the_skip_is_saved_and_the_palette_can_bring_it_back()
    {
        var welcome = Source("MainWindow.Welcome.cs");
        Assert.Contains("WelcomeRules.ShouldShow(items.Length, showingRemoved, settings.WelcomeDismissed)", welcome);
        Assert.Contains("settings with { WelcomeDismissed = true }", welcome);
        Assert.Contains("UpdateWelcome(removed);", Source("MainWindow.xaml.cs"));
        Assert.Contains("x:Name=\"WelcomeHost\"", Source("MainWindow.xaml"));
        Assert.Contains("Show the welcome", Source("MainWindow.Shell.cs"));
    }
}
