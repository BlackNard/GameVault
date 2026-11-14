using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.App;
using GameVault.Core;
namespace GameVault.App.Tests;
public sealed class ThumbnailRowTests
{
    private static BitmapImage Image()
    {
        var source = BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[4],4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream(); encoder.Save(stream); stream.Position=0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream; image.EndInit(); image.Freeze(); return image;
    }
    [Fact]
    public async Task ReleasedRowRejectsLateCoverAndCanLoadAgain()
    {
        var completion = new TaskCompletionSource<BitmapImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var row = new LibraryItemView(new LibraryGame(Guid.NewGuid(),null,true,false,"fixture.png",0,null), (_,_) => completion.Task);
        var loading = row.LoadCoverAsync(); row.ReleaseCover();
        completion.SetResult(Image()); await loading;
        Assert.Null(row.Cover);
        await row.LoadCoverAsync(); Assert.NotNull(row.Cover);
        row.ReleaseCover(); Assert.Null(row.Cover);
    }
    [Fact]
    public void RefreshGamePreservesIdentityAndLiveProgress()
    {
        var game = new LibraryGame(Guid.NewGuid(),null,true,false,null,0,null);
        var row = new LibraryItemView(game);
        row.RefreshGame(game with { PlayedSeconds=3600, Warning="fresh" });
        Assert.Equal("1h 00m 00s",row.Played);
        Assert.Equal("fresh",row.Game.Warning);
        Assert.Throws<ArgumentException>(() => row.RefreshGame(game with { Id=Guid.NewGuid() }));
    }
    [Fact]
    public void RefreshGameKeepsActiveLaunchAndSessionProgress()
    {
        var game = new LibraryGame(Guid.NewGuid(),null,true,false,null,0,null);
        var row = new LibraryItemView(game);
        row.SetLaunchState(new(game.Id, LaunchPhase.Running, "running"));
        row.SetSessionProgress(new(game.Id, Guid.NewGuid(), 60, 3660, "session warning"));
        row.RefreshGame(game with { PlayedSeconds=900 });
        Assert.Equal("Running",row.Availability);
        Assert.Equal("1h 01m 00s",row.Played);
        Assert.Equal("This session: 0h 01m 00s",row.SessionTime);
        Assert.Equal("session warning",row.SessionWarning);
    }}
