using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameVault.Core;
namespace GameVault.App.Tests;
public sealed class PortraitCoverPickerTests
{
    private static readonly PortraitCoverCandidate Match=new(10,"Game","Base game",new Uri("https://store.steampowered.com/app/10/"));
    [Fact] public void SearchRejectsUnsuitableDecodedArtworkAndKeepsSmallThumbnails()=>Sta(async()=>
    {
        var fake=new Catalogue { Search=(_,_)=>Task.FromResult<IReadOnlyList<PortraitCoverCandidate>>([Match,Match with { AppId=20 }]), Download=(c,_,_)=>Task.FromResult(PortraitCoverImageTests.Png(c.AppId==10 ? 600:900,c.AppId==10 ? 900:600)) };
        using var view=new CoverSearchView("Game",fake); await view.SearchAsync("Game");
        var result=Assert.Single(view.Results); Assert.Equal(600,result.Width); Assert.Equal(120,result.Thumbnail.PixelWidth); Assert.True(result.Thumbnail.IsFrozen); Assert.False(view.IsBusy);
    });
    [Fact] public void StaleSearchCannotReplaceNewResults()=>Sta(async()=>
    {
        var old=new TaskCompletionSource<IReadOnlyList<PortraitCoverCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake=new Catalogue { Search=(title,_)=>title=="old" ? old.Task : Task.FromResult<IReadOnlyList<PortraitCoverCandidate>>([Match]) };
        using var view=new CoverSearchView("Game",fake); var first=view.SearchAsync("old"); await view.SearchAsync("new");
        old.SetResult([Match with { AppId=30,Title="Stale" }]); await first; Assert.Equal("Game",Assert.Single(view.Results).Title); Assert.False(view.IsBusy);
    });
    [Fact] public void DownloadsAndDecodesAreBoundedToTwo()=>Sta(async()=>
    {
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var two=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active=0; var max=0; var started=0; var png=PortraitCoverImageTests.Png(600,900);
        var fake=new Catalogue { Search=(_,_)=>Task.FromResult<IReadOnlyList<PortraitCoverCandidate>>(Enumerable.Range(1,10).Select(id=>Match with { AppId=id }).ToArray()), Download=async(_,_,token)=>
        { var count=Interlocked.Increment(ref active); max=Math.Max(max,count); if(Interlocked.Increment(ref started)==2) two.SetResult(); try { await release.Task.WaitAsync(token); return png; } finally { Interlocked.Decrement(ref active); } } };
        using var view=new CoverSearchView("Game",fake); var search=view.SearchAsync("Game"); await two.Task; Assert.Equal(2,started); release.SetResult(); await search; Assert.Equal(2,max); Assert.Equal(10,view.Results.Count);
    });
    [Fact] public void SelectedDownloadRequiresExplicitConfirmationAndRejectsBadPreferredImage()=>Sta(async()=>
    {
        var preferred=false; var fake=new Catalogue { Download=(_,preview,_)=>{ preferred|=!preview; return Task.FromResult(PortraitCoverImageTests.Png(preview ? 600:100,preview ? 900:100)); } };
        using var view=new CoverSearchView("Game",fake); var chosen=0; view.CoverChosen+=(_,_)=>chosen++;
        await view.SearchAsync("Game"); await view.SelectAsync(Assert.Single(view.Results));
        Assert.True(preferred); Assert.False(((Button)view.FindName("UseButton")).IsEnabled); Assert.Equal(0,chosen);
        Assert.Contains("Could not load",((TextBlock)view.FindName("Notice")).Text);
    });
    [Fact] public void ConfirmedSelectionContainsNormalizedPngAndSourceCredit()=>Sta(async()=>
    {
        using var view=new CoverSearchView("Game",new Catalogue()); byte[]? chosen=null; string? credit=null; view.CoverChosen+=(bytes,text)=>{ chosen=bytes;credit=text; };
        await view.SearchAsync("Game"); await view.SelectAsync(Assert.Single(view.Results)); Assert.Null(chosen);
        ((Button)view.FindName("UseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.NotNull(chosen); Assert.Contains("https://store.steampowered.com/app/10/",credit);
        using var saved=new System.IO.MemoryStream(chosen!,false);
        var decoder=new System.Windows.Media.Imaging.PngBitmapDecoder(saved,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var metadata=Assert.IsType<System.Windows.Media.Imaging.BitmapMetadata>(decoder.Frames[0].Metadata);
        Assert.Contains(Match.SourcePage.AbsoluteUri,Assert.IsType<string>(metadata.GetQuery("/tEXt/{str=Description}")));
        Assert.Contains("publisher",Assert.IsType<string>(metadata.GetQuery("/tEXt/{str=Description}")));
    });
    [Fact] public void CancellationAndDisposalRejectLateDownloads()=>Sta(async()=>
    {
        var download=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var view=new CoverSearchView("Game",new Catalogue { Download=(_,_,_)=>download.Task });
        var search=view.SearchAsync("Game"); view.CancelPending(); view.Dispose(); download.SetResult(PortraitCoverImageTests.Png(600,900)); await search; Assert.Empty(view.Results); Assert.False(view.IsBusy);
    });
    [Fact] public void EditingTitleCancelsAnInFlightSearchBeforeItCanPublish()=>Sta(async()=>
    {
        var response=new TaskCompletionSource<IReadOnlyList<PortraitCoverCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var view=new CoverSearchView("Game",new Catalogue { Search=(_,_)=>response.Task });
        var search=view.SearchAsync("Game"); ((TextBox)view.FindName("Query")).Text="Different game";
        response.SetResult([Match]); await search; Assert.Empty(view.Results); Assert.False(view.IsBusy);
        Assert.Contains("Title changed",((TextBlock)view.FindName("Notice")).Text);
    });
    [Fact] public void RateLimitedImagesShowAnHonestProviderError()=>Sta(async()=>
    {
        using var view=new CoverSearchView("Game",new Catalogue { Download=(_,_,_)=>Task.FromException<byte[]>(new ArtworkException(ArtworkFailure.RateLimit,"Too many requests. Try again later.")) });
        await view.SearchAsync("Game"); Assert.Empty(view.Results); Assert.Contains("Too many requests",((TextBlock)view.FindName("Notice")).Text); Assert.False(view.IsBusy);
    });
    [Fact] public void FailedSelectionPreservesLocalPreviewAndWarnsForNonPortraitImport()=>Sta(async()=>
    {
        var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".png");
        try
        {
            await System.IO.File.WriteAllBytesAsync(path,PortraitCoverImageTests.Png(100,100));
            using var view=new CoverSearchView("Game",new Catalogue { Download=(_,_,_)=>Task.FromResult(PortraitCoverImageTests.Png(100,100)) });
            await view.ImportAsync(path); var preview=((Image)view.FindName("Preview")).Source;
            Assert.Contains("differs from a 2:3",((TextBlock)view.FindName("Notice")).Text);
            await view.SelectAsync(new CoverSearchView.CoverResult(Match,(System.Windows.Media.Imaging.BitmapSource)preview,100,100));
            Assert.Same(preview,((Image)view.FindName("Preview")).Source); Assert.True(((Button)view.FindName("UseButton")).IsEnabled);
        }
        finally { System.IO.File.Delete(path); }
    });
    private sealed class Catalogue : IPortraitCoverCatalogue
    {
        public Func<string,CancellationToken,Task<IReadOnlyList<PortraitCoverCandidate>>> Search=(_,_)=>Task.FromResult<IReadOnlyList<PortraitCoverCandidate>>([Match]);
        public Func<PortraitCoverCandidate,bool,CancellationToken,Task<byte[]>> Download=(_,_,_)=>Task.FromResult(PortraitCoverImageTests.Png(600,900));
        public Task<IReadOnlyList<PortraitCoverCandidate>> SearchAsync(string title,CancellationToken token)=>Search(title,token);
        public Task<byte[]> DownloadAsync(PortraitCoverCandidate item,bool preview,CancellationToken token)=>Download(item,preview,token);
    }
    private static void Sta(Func<Task> test)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            var dispatcher=Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async()=>{ try { await test(); } catch(Exception error) { failure=error; } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } });
            Dispatcher.Run();
        }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)),"Headless picker test timed out.");
        if(failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
