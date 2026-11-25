using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App.Tests;

public sealed class CaptureGalleryTests
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

    private static CaptureMedia Media(CaptureMediaKind kind, CaptureMediaStatus status = CaptureMediaStatus.Complete) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddDays(100), kind, status, Path.Combine(Path.GetTempPath(), "missing-capture-file"));

    [Fact]
    public void A_tile_opens_and_shows_its_own_capture_and_an_unfinished_one_cannot_be_opened() => InSta(() =>
    {
        CaptureTile.LoadThumbnail = (_, _) => Task.FromResult<BitmapSource?>(null);      // nothing is handed to Windows
        var opened = new List<CaptureMedia>();
        var shown = new List<CaptureMedia>();
        var picture = Media(CaptureMediaKind.Screenshot);
        var tile = new CaptureTile { DataContext = new CaptureCard(picture, opened.Add, shown.Add, _ => { }) };
        tile.Picture!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        tile.ExplorerButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([picture], opened);
        Assert.Equal([picture], shown);
        Assert.Equal(3, tile.ContextMenu!.Items.Count);
        tile.DataContext = new CaptureCard(Media(CaptureMediaKind.Video, CaptureMediaStatus.Incomplete), opened.Add, shown.Add, _ => { });      // recycled
        Assert.False(tile.Picture!.IsEnabled);
        Assert.False(((MenuItem)tile.ContextMenu!.Items[0]!).IsEnabled);
    });

    [Fact]
    public void The_gallery_keeps_every_capture_and_regroups_rows_when_the_width_changes() => InSta(() =>
    {
        var gallery = new CaptureGallery();
        gallery.Show(Enumerable.Range(0, 7).Select(_ => new CaptureCard(Media(CaptureMediaKind.Screenshot), _ => { }, _ => { }, _ => { })));
        Assert.Equal(7, gallery.Cards.Count);
        Assert.Equal(7, gallery.Items.Count);                 // one per row until a width is known
        gallery.Layout(3, false);
        Assert.Equal(3, gallery.Items.Count);
        gallery.Show([]);
        Assert.Equal(Visibility.Collapsed, gallery.Visibility);
    });

    [Fact]
    public void The_preview_moves_between_screenshots_and_survives_a_file_it_cannot_read() => InSta(() =>
    {
        var first = Media(CaptureMediaKind.Screenshot);
        var second = Media(CaptureMediaKind.Screenshot);
        var view = new CapturePreviewView("Midnight", [first, Media(CaptureMediaKind.Video), second], first, _ => { }, _ => { },
            _ => throw new IOException("gone"));
        Assert.Equal(first, view.Current);
        view.Move(1);
        Assert.Equal(second, view.Current);
        view.Move(1);
        Assert.Equal(second, view.Current);
        view.Move(-1);
        Assert.Equal(first, view.Current);
    });

    [Fact]
    public void The_media_tab_and_the_captures_page_use_the_tiles_and_files_are_rechecked_before_use()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        string Source(string name) => File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", name));
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("x:Name=\"CaptureGalleryList\"", xaml);
        Assert.Contains("x:Name=\"CaptureFolders\"", xaml);                 // the Captures page lists one folder per game
        Assert.Contains("x:Name=\"CaptureGalleryPage\"", xaml);             // and opens a folder as the same virtualised gallery
        var overview = Source("MainWindow.Overview.cs");
        Assert.Contains("NavigateToAsync(NavRoute.CapturesOf(folder.Item.Game.Id))", overview);
        Assert.Contains("CaptureGalleryPage.Show(cards);", overview);
        Assert.Contains("NavRoute.CapturesOf(folder)", Source("MainWindow.Shell.cs"));   // Back and Forward know the opened folder
        Assert.DoesNotContain("CaptureMediaList", xaml);
        var actions = Source("MainWindow.CaptureGallery.cs");
        Assert.Contains("await FindCaptureAsync(media)", actions);
        Assert.Contains("current.Kind == CaptureMediaKind.Video) OpenInDefaultApp(current)", actions);
        Assert.Contains("/select,", actions);
        Assert.Contains("CapturePreviewView editor=>editor.CanLeaveAsync", Source("MainWindow.Navigation.cs"));
    }
}
