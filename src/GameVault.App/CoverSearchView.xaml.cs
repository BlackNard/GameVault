using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GameVault.App.Artwork;
using GameVault.Core;
using GameVault.Infrastructure.Artwork;
namespace GameVault.App;

public partial class CoverSearchView : UserControl, IDisposable
{
    public sealed record CoverResult(PortraitCoverCandidate Candidate, BitmapSource Thumbnail, int Width, int Height)
    {
        public string Title => Candidate.Title;
        public string Context => Candidate.Context;
        public string Dimensions => $"{Width} × {Height}";
        public string SourcePage => Candidate.SourcePage.AbsoluteUri;
    }
    private readonly IPortraitCoverCatalogue catalogue;
    private readonly bool ownsCatalogue;
    private readonly SemaphoreSlim imageSlots = new(2,2);
    private CancellationTokenSource? pending;
    private byte[]? candidate;
    private string candidateName = "";
    private bool disposed;
    private long revision;
    private readonly InlineDraftGuard navigation = new("");
    public ObservableCollection<CoverResult> Results { get; } = [];
    public bool IsBusy { get; private set; }
    public event Action<byte[],string>? CoverChosen;
    public event Action? Cancelled;
    public CoverSearchView(string title) : this(title,new SteamPortraitCoverCatalogue(),true) { }
    public CoverSearchView(string title,IPortraitCoverCatalogue catalogue) : this(title,catalogue,false) { }
    private CoverSearchView(string title,IPortraitCoverCatalogue catalogue,bool ownsCatalogue)
    {
        this.catalogue=catalogue ?? throw new ArgumentNullException(nameof(catalogue)); this.ownsCatalogue=ownsCatalogue;
        InitializeComponent(); Query.Text=title; Loaded+=(_,_)=>Query.Focus();
    }
    private void QueryKeyDown(object sender,KeyEventArgs e)
    { if(e.Key==Key.Enter) { e.Handled=true; Search(sender,e); } }
    private async void Search(object sender,RoutedEventArgs e) => await SearchAsync(Query.Text);
    private void QueryChanged(object sender,TextChangedEventArgs e)
    { if(pending is not null) { CancelPending(); Notice.Text="Title changed. Choose Search covers to load matches for this title."; } }
    internal async Task SearchAsync(string title)
    {
        if(disposed) return;
        var (version,token)=Begin(); Results.Clear(); Gallery.SelectedItem=null;
        Notice.Text="Searching Steam for matching games and checking portrait artwork…";
        try
        {
            var matches=await catalogue.SearchAsync(title,token);
            token.ThrowIfCancellationRequested();
            var failures=new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var tasks=matches.Take(10).Select(async match=>
            {
                await imageSlots.WaitAsync(token);
                try
                {
                    var bytes=await catalogue.DownloadAsync(match,true,token);
                    var thumbnail=await Task.Run(()=>PortraitCoverImages.Thumbnail(bytes),token);
                    return new CoverResult(match,thumbnail.Image,thumbnail.Width,thumbnail.Height);
                }
                catch(Exception error) when(Expected(error) && error is not OperationCanceledException) { failures.Add(error); return null; }
                finally { imageSlots.Release(); }
            }).ToArray();
            var covers=await Task.WhenAll(tasks);
            if(!Current(version,token)) return;
            foreach(var cover in covers) if(cover is not null) Results.Add(cover);
            var failure=failures.OfType<ArtworkException>().FirstOrDefault(error=>error.Failure==ArtworkFailure.RateLimit) ?? failures.FirstOrDefault();
            Notice.Text=Results.Count==0 && failure is not null ? "Matching cover images could not be loaded or did not meet portrait requirements. "+failure.Message+" Retry or Browse an image." : Results.Count>0 ? "Choose the matching game and edition to download a full preview. Artwork belongs to its publisher." : "No suitable portrait covers were available for these matches. Refine the title, retry, or Browse an image.";
        }
        catch(OperationCanceledException) { if(Current(version)) Notice.Text="Search cancelled. Refine the title or Browse an image."; }
        catch(Exception error) when(Expected(error)) { if(Current(version)) Notice.Text="Cover search unavailable. "+error.Message+" Retry or Browse an image."; }
        finally { End(version); }
    }
    private async void SelectionChanged(object sender,SelectionChangedEventArgs e)
    { if(Gallery.SelectedItem is CoverResult result) await SelectAsync(result); }
    internal async Task SelectAsync(CoverResult result)
    {
        if(disposed) return;
        var (version,token)=Begin(); Notice.Text="Downloading and checking the selected portrait…";
        try
        {
            await imageSlots.WaitAsync(token);
            byte[] png;
            try
            {
                var bytes=await catalogue.DownloadAsync(result.Candidate,false,token);
                png=await Task.Run(()=>PortraitCoverImages.Normalize(bytes,result.Candidate.SourcePage),token);
            }
            finally { imageSlots.Release(); }
            if(!Current(version,token)) return;
            var image=ArtworkImageNormalizer.Preview(png);
            candidate=png; candidateName=$"{result.Title} · Steam · {result.SourcePage}"; Preview.Source=image;
            PreviewDetails.Text=$"{result.Title}\n{result.Context}\n{image.PixelWidth} × {image.PixelHeight}\nSteam · publisher artwork\n{result.SourcePage}";
            Notice.Text="Preview ready. Use this cover to confirm, or Back to keep your previous cover.";
        }
        catch(OperationCanceledException) { if(Current(version)) Notice.Text="Download cancelled. Your previous preview is unchanged."; }
        catch(Exception error) when(Expected(error)) { if(Current(version)) Notice.Text="Could not load this cover. "+error.Message+" Choose another cover or Browse an image."; }
        finally { End(version); }
    }
    private (long Version,CancellationToken Token) Begin()
    {
        CancelPending(); pending=new CancellationTokenSource(); IsBusy=true; UpdateButtons();
        return(revision,pending.Token);
    }
    private bool Current(long version) => !disposed && version==revision;
    private bool Current(long version,CancellationToken token) => Current(version) && !token.IsCancellationRequested;
    private void End(long version)
    { if(Current(version)) { pending?.Dispose(); pending=null; IsBusy=false; UpdateButtons(); } }
    internal void CancelPending()
    {
        revision++; pending?.Cancel(); pending?.Dispose(); pending=null; IsBusy=false;
        if(!disposed) UpdateButtons();
    }
    private void UpdateButtons()
    { UseButton.IsEnabled=!IsBusy && candidate is not null; StopButton.IsEnabled=IsBusy; }
    private void Stop(object sender,RoutedEventArgs e)
    { CancelPending(); Notice.Text="Loading cancelled. Your previous preview is unchanged. Search again or Browse an image."; }
    private async void Browse(object sender,RoutedEventArgs e)
    {
        if(disposed) return;
        try
        {
            var path=NativePickers.File(Window.GetWindow(this)!,"Choose cover image","Cover images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
            if(path is not null) await Import([path]);
        }
        catch(Exception error) when(Expected(error)) { if(!disposed) Notice.Text=error.Message; }
    }
    private void DragOverImage(object sender,DragEventArgs e)
    { e.Effects=!disposed && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled=true; }
    private async void DropImage(object sender,DragEventArgs e)
    { e.Handled=true; if(!disposed && e.Data.GetData(DataFormats.FileDrop) is string[] files) await Import(files); }
    public Task ImportAsync(string path) => Import([path]);
    private async Task Import(string[] files)
    {
        if(disposed) return;
        var (version,token)=Begin(); Notice.Text="Checking imported image…";
        try
        {
            var png=await CoverImport.ReadAsync(files);
            if(!Current(version,token)) return;
            var preview=ArtworkImageNormalizer.Preview(png);
            candidate=png; candidateName=Path.GetFileName(files[0]); Preview.Source=preview;
            PreviewDetails.Text=$"{candidateName}\n{preview.PixelWidth} × {preview.PixelHeight}\nLocal image";
            Notice.Text=PortraitCoverPolicy.IsSuitable(preview.PixelWidth,preview.PixelHeight)
                ? "Preview ready. Choose Use this cover to confirm."
                : "This local image is smaller than 600 × 900 or differs from a 2:3 portrait. It will keep its proportions and may leave space in the cover frame. Use this cover to confirm.";
        }
        catch(Exception error) when(Expected(error)) { if(Current(version)) Notice.Text="Could not import artwork. "+error.Message; }
        finally { End(version); }
    }
    private void UseCover(object sender,RoutedEventArgs e)
    { if(!disposed && !IsBusy && candidate is not null) CoverChosen?.Invoke(candidate,candidateName); }
    public Task<bool> CanLeaveAsync()
    {
        CancelPending();
        return navigation.CanLeaveAsync(false,candidate is null ? "" : "preview",async()=>
            await InlinePrompts.ShowAsync(this,"Discard this unconfirmed cover preview?","Cover preview",MessageBoxButton.YesNo,MessageBoxResult.No)==MessageBoxResult.Yes);
    }
    private async void Back(object sender,RoutedEventArgs e) { if(await CanLeaveAsync()) Cancelled?.Invoke(); }
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or FileFormatException or ArtworkException or System.Net.Http.HttpRequestException or OperationCanceledException;
    public void Dispose()
    {
        if(disposed) return;
        disposed=true; CancelPending(); candidate=null; Preview.Source=null; Results.Clear();
        if(ownsCatalogue && catalogue is IDisposable disposable) disposable.Dispose();
        // Slots are released by any in-flight operations; do not dispose the semaphore underneath them.
    }
}
