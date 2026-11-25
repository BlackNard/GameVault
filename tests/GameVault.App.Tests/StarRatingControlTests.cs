using System.IO;
using System.Windows.Automation;
using System.Windows.Input;

namespace GameVault.App.Tests;

public sealed class StarRatingControlTests
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

    [Fact]
    public void The_stars_show_their_value_to_screen_readers_and_ask_before_changing_it() => InSta(() =>
    {
        var control = new StarRatingControl { Value = 7 };
        Assert.Equal("Game rating: 3.5 of 5 stars", AutomationProperties.GetName(control));
        var chosen = new List<int>();
        control.Chosen += (_, value) => chosen.Add(value);
        control.Choose(7);                       // unchanged: nothing to save
        control.Choose(8);
        Assert.Equal([8], chosen);
        Assert.Equal(7, control.Value);          // the owner sets it once it is saved
        control.IsEnabled = false;
        control.Choose(2);
        Assert.Equal([8], chosen);
    });

    [Fact]
    public void Keys_move_in_halves_pick_whole_stars_and_clear()
    {
        Assert.Equal(6, StarRatingControl.ForKey(Key.Right, 5));
        Assert.Equal(10, StarRatingControl.ForKey(Key.Up, 10));
        Assert.Equal(0, StarRatingControl.ForKey(Key.Left, 0));
        Assert.Equal(8, StarRatingControl.ForKey(Key.D4, 1));
        Assert.Equal(10, StarRatingControl.ForKey(Key.NumPad5, 1));
        Assert.Equal(0, StarRatingControl.ForKey(Key.Delete, 9));
        Assert.Null(StarRatingControl.ForKey(Key.Tab, 9));
    }

    [Fact]
    public void The_review_tab_the_sort_menu_and_the_filter_are_wired()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        string Source(string name) => File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", name));
        Assert.Contains("x:Name=\"Rating\"", Source("NotesView.xaml"));
        Assert.Contains("RatingPanel.Visibility=review?", Source("NotesView.xaml.cs"));
        Assert.Contains("x:Name=\"RatingButton\"", Source("MainWindow.xaml"));
        Assert.Contains("RatingFilters.Matches(ratingFilter, i.Game.Rating)", Source("MainWindow.xaml.cs"));
        foreach (var file in new[] { "MainWindow.ProfileImport.cs", "MainWindow.StoreImport.cs", "MainWindow.Shell.cs", "MainWindow.Tray.cs", "MainWindow.xaml.cs" })
            Assert.Contains("ratingFilter = ", Source(file));
    }
}
