using GameVault.Core;

namespace GameVault.Tests;

public sealed class CaptureGalleryRulesTests
{
    [Fact]
    public void TheGridFitsAsManyTilesAsTheWidthAllowsAndSplitsIntoRows()
    {
        Assert.Equal(1, CaptureGalleryRules.Columns(0));
        Assert.Equal(1, CaptureGalleryRules.Columns(double.NaN));
        Assert.Equal(1, CaptureGalleryRules.Columns(495));
        Assert.Equal(2, CaptureGalleryRules.Columns(496));
        Assert.Equal(4, CaptureGalleryRules.Columns(1000));
        var rows = CaptureGalleryRules.Rows(Enumerable.Range(1, 5).ToArray(), 2);
        Assert.Equal(3, rows.Length);
        Assert.Equal([5], rows[^1]);
        Assert.Empty(CaptureGalleryRules.Rows(Array.Empty<int>(), 3));
        Assert.Equal(2, CaptureGalleryRules.Rows([1, 2], 0).Length);
    }

    [Fact]
    public void LengthsAndSizesReadNaturally()
    {
        Assert.Equal("0:07", CaptureGalleryRules.Duration(TimeSpan.FromSeconds(7)));
        Assert.Equal("12:05", CaptureGalleryRules.Duration(TimeSpan.FromSeconds(725)));
        Assert.Equal("1:02:03", CaptureGalleryRules.Duration(new TimeSpan(1, 2, 3)));
        Assert.Equal("", CaptureGalleryRules.Duration(null));
        Assert.Equal("1 KB", CaptureGalleryRules.Size(10));
        Assert.EndsWith("MB", CaptureGalleryRules.Size(3 * 1024 * 1024));
        Assert.EndsWith("GB", CaptureGalleryRules.Size(2L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void AnOpenedCaptureFolderIsItsOwnPlaceInTheHistory()
    {
        var game = Guid.NewGuid();
        Assert.Equal("Captures", NavBreadcrumb.Text(NavRoute.Captures, null));
        Assert.Equal("Captures › TEKKEN 8", NavBreadcrumb.Text(NavRoute.CapturesOf(game), "TEKKEN 8"));
        var history = new NavigationHistory();
        history.Record(NavRoute.Captures);
        history.Record(NavRoute.CapturesOf(game));
        Assert.Equal(NavRoute.Captures, history.Back(_ => true));
        Assert.Equal(NavRoute.CapturesOf(game), history.Forward(_ => true));
    }

    [Fact]
    public void PreviousAndNextMoveThroughFinishedScreenshotsOnly()
    {
        static CaptureMedia Media(CaptureMediaKind kind, CaptureMediaStatus status = CaptureMediaStatus.Complete) =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, kind, status, "x");
        var first = Media(CaptureMediaKind.Screenshot);
        var video = Media(CaptureMediaKind.Video);
        var broken = Media(CaptureMediaKind.Screenshot, CaptureMediaStatus.Incomplete);
        var second = Media(CaptureMediaKind.Screenshot);
        CaptureMedia[] all = [first, video, broken, second];
        Assert.Equal(second, CaptureGalleryRules.Neighbour(all, first.Id, 1));
        Assert.Equal(first, CaptureGalleryRules.Neighbour(all, second.Id, -1));
        Assert.Null(CaptureGalleryRules.Neighbour(all, first.Id, -1));
        Assert.Null(CaptureGalleryRules.Neighbour(all, second.Id, 1));
        Assert.Null(CaptureGalleryRules.Neighbour(all, video.Id, 1));
        Assert.Equal("Screenshot · incomplete", CaptureGalleryRules.Title(broken));
        Assert.Equal("Recording · NVIDIA App", CaptureGalleryRules.Title(video with { SourceFingerprint = "f" }));
    }
}
