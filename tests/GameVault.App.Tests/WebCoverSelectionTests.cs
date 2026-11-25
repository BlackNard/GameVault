using System.Text.Json;
using System.Windows.Controls;
using GameVault.App.Artwork;

namespace GameVault.App.Tests;

public sealed class WebCoverSelectionTests
{
    private static string Payload(string source, string? link = null) => JsonSerializer.Serialize(new { source, link });

    [Fact]
    public void GoogleImageLinkSelectsOriginalInsteadOfThumbnail()
    {
        var candidate = WebCoverSelection.Parse(Payload("https://encrypted-tbn0.gstatic.com/thumb", "https://www.google.com/imgres?imgurl=https%3A%2F%2Fimages.example.com%2Fcover.jpg&imgrefurl=https%3A%2F%2Fexample.com"));
        Assert.Equal("https://images.example.com/cover.jpg", candidate!.AbsoluteUri);
    }

    [Fact]
    public void DroppedOversizedThumbnailRequiresAValidOriginalLink()
    {
        Assert.Equal("https://images.example.com/cover.jpg", WebCoverSelection.Parse(Payload("", "https://www.google.com/imgres?imgurl=https%3A%2F%2Fimages.example.com%2Fcover.jpg"))!.AbsoluteUri);
        Assert.Null(WebCoverSelection.Parse(Payload("")));
        Assert.Null(WebCoverSelection.Parse(Payload("", "https://example.com/page")));
    }

    [Fact]
    public void DirectImageIsAcceptedWithoutTrustingPageDimensions()
    {
        Assert.Equal("https://images.example.com/a.png", WebCoverSelection.Parse("{\"source\":\"https://images.example.com/a.png#fragment\",\"width\":9999999,\"height\":-1}")!.AbsoluteUri);
    }

    [Theory]
    [InlineData("data:image/png;base64,AAA")]
    [InlineData("blob:https://example.com/id")]
    [InlineData("http://example.com/a.png")]
    [InlineData("https://127.0.0.1/a.png")]
    public void UnsafeOriginalDoesNotFallBackToThumbnail(string address)
    {
        Assert.Null(WebCoverSelection.Parse(Payload("https://images.example.com/thumb", "https://google.com/imgres?imgurl=" + Uri.EscapeDataString(address))));
        Assert.Null(WebCoverSelection.Parse(Payload(address)));
    }

    [Fact]
    public void UnrelatedAnchorCannotSupplyAnOriginal()
    {
        Assert.Equal("https://images.example.com/thumb", WebCoverSelection.Parse(Payload("https://images.example.com/thumb", "https://evil.example/imgres?imgurl=https%3A%2F%2Fimages.example.com%2Fother"))!.AbsoluteUri);
    }

    [Theory]
    [InlineData("null")][InlineData("[]")][InlineData("{}")][InlineData("garbage")][InlineData("{\"source\":42}")]
    public void MissingOrMalformedPayloadIsRejected(string json) => Assert.Null(WebCoverSelection.Parse(json));

    [Fact]
    public void PayloadAndAddressAreBounded()
    {
        Assert.Null(WebCoverSelection.Parse(Payload("https://images.example.com/" + new string('a', 8192))));
        Assert.Null(WebCoverSelection.Parse("{\"source\":\"https://images.example.com/a\",\"ignored\":\"" + new string('x', 16384) + "\"}"));
        Assert.Null(WebCoverSelection.Parse(Payload("https://images.example.com/a", "https://google.com/imgres?imgurl=")));
    }

    [Theory]
    [InlineData(100, 60, 2, 50, 30)]
    [InlineData(100, 60, 1.25, 80, 48)]
    public void ClickCoordinatesUseDeviceIndependentPixelsAndBrowserZoom(double x, double y, double zoom, double expectedX, double expectedY)
    {
        var point = WebCoverSelection.CssPoint(x, y, zoom);
        Assert.Equal(expectedX, point.X); Assert.Equal(expectedY, point.Y);
        var script = WebCoverSelection.HitTestScript(x, y, zoom);
        Assert.Contains("document.elementFromPoint(", script);
        Assert.DoesNotContain("querySelectorAll", script); Assert.DoesNotContain("addEventListener", script);
        Assert.Contains("closest('div[data-docid][data-lpage]')", script);        // only the clicked tile is looked up
    }

    [Fact]
    public void NewClickNavigationAndCancelInvalidateOlderResults()
    {
        var selection = new WebCoverSelection();
        var first = selection.Begin(); var second = selection.Begin();
        Assert.False(selection.Complete(first, Payload("https://images.example.com/old")));
        Assert.True(selection.Complete(second, Payload("https://images.example.com/current")));
        Assert.NotNull(selection.Source);
        var navigation = selection.Begin(); Assert.Null(selection.Source);
        selection.Reset(); Assert.False(selection.Complete(navigation, Payload("https://images.example.com/stale")));
        Assert.Null(selection.Source);
    }

    [Fact]
    public void ControlHasExplicitDisabledPreviewWithoutCreatingBrowser()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var view = new WebCoverSearchView("Game");
                var preview = Assert.IsType<Button>(view.FindName("PreviewButton"));
                Assert.Equal("Preview selected image", preview.Content);
                Assert.False(preview.IsEnabled);
                Assert.Empty(Assert.IsType<Grid>(view.FindName("BrowserHost")).Children);
                Assert.False(view.IsBusy);
                Assert.True(view.CanLeaveAsync().GetAwaiter().GetResult());
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
