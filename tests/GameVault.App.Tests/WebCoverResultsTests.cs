using System.IO;
using GameVault.App.Artwork;

namespace GameVault.App.Tests;

public sealed class WebCoverResultsTests
{
    [Theory]
    [InlineData(600, 900, true, true)]
    [InlineData(1000, 1500, true, true)]
    [InlineData(801, 1000, true, false)]        // big enough, but too wide for a cover
    [InlineData(1248, 2212, true, false)]       // too narrow
    [InlineData(599, 900, false, false)]
    [InlineData(600, 899, false, false)]
    [InlineData(1920, 1080, false, false)]
    public void ResultsAreJudgedBySizeAndShape(int width, int height, bool bigEnough, bool coverShape)
    {
        Assert.Equal(bigEnough, WebCoverResults.Fits(width, height));
        Assert.Equal(coverShape, WebCoverResults.IsCoverShape(width, height));
        Assert.Equal(coverShape, WebCoverResults.Shows(WebCoverFilter.CoverShape, width, height));
        Assert.Equal(bigEnough, WebCoverResults.Shows(WebCoverFilter.BigEnough, width, height));
        Assert.True(WebCoverResults.Shows(WebCoverFilter.Everything, width, height));
    }

    [Fact]
    public void AResultClickCarriesTheOriginalPictureAndItsSize()
    {
        var selection = new WebCoverSelection();
        var click = selection.Begin();
        Assert.True(selection.Complete(click, "{\"source\":\"https://images.example.com/full.jpg\",\"width\":1000,\"height\":1500}"));
        Assert.Equal("https://images.example.com/full.jpg", selection.Source!.AbsoluteUri);
        Assert.Equal((1000, 1500), selection.Size);
        selection.Reset();
        Assert.Null(selection.Size);
        Assert.Null(WebCoverSelection.ParseSize("{\"source\":\"https://images.example.com/a\"}"));
        Assert.Null(WebCoverSelection.ParseSize("{\"width\":-1,\"height\":10}"));
        Assert.Null(WebCoverSelection.ParseSize("{\"width\":\"big\",\"height\":10}"));
        Assert.Null(WebCoverSelection.ParseSize("not json"));
        var unsafeClick = selection.Begin();
        selection.Complete(unsafeClick, "{\"source\":\"https://127.0.0.1/a.jpg\",\"width\":1000,\"height\":1500}");
        Assert.Null(selection.Source);          // a private address is never used, whatever the page says
        Assert.Null(selection.Size);
    }

    [Fact]
    public void OpeningThePageSearchesAndTheScriptsOnlyReadTheResults()
    {
        var script = WebCoverResults.FilterScript(WebCoverFilter.CoverShape);
        Assert.Contains("data-docid", script);
        Assert.DoesNotContain("fetch(", script);
        Assert.DoesNotContain("XMLHttpRequest", script);
        Assert.DoesNotContain("postMessage", script);
        Assert.Contains("const shows = (w, h) => true", WebCoverResults.FilterScript(WebCoverFilter.Everything));
        Assert.Contains("if (true || fitting.length === 0)", WebCoverResults.FilterScript(WebCoverFilter.Everything));    // Google's own page
        Assert.DoesNotContain("innerHTML", script);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        var code = File.ReadAllText(Path.Combine(dir!.FullName, "src", "GameVault.App", "WebCoverSearchView.xaml.cs"));
        Assert.Contains("if (SearchOnOpen && !string.IsNullOrWhiteSpace(Query.Text)) Search(this, new RoutedEventArgs());", code);
    }
}
