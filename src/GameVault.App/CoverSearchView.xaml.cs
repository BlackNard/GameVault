using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameVault.App.Artwork;
using GameVault.Core;
using GameVault.Infrastructure.Artwork;
namespace GameVault.App;
public partial class CoverSearchView : System.Windows.Controls.UserControl, IDisposable
{
    private readonly IArtworkCredentialStore credentials;
    private readonly SteamGridDbProvider provider;
    private readonly SemaphoreSlim thumbnails = new(2);
    private readonly Dictionary<System.Windows.Controls.Image,CancellationTokenSource> visibleImages=[];
    private CancellationTokenSource pending=new();
    private CancellationTokenSource artwork=new();
    private bool disposed;
    public event Action<byte[],string>? CoverChosen;
    public event Action? Cancelled;
    public CoverSearchView(string title)
    {
        credentials=new WindowsArtworkCredentialStore();provider=new(credentials);InitializeComponent();Query.Text=title;
        Loaded+=(_,_)=>Query.Focus();Unloaded+=(_,_)=>CancelRequests();
    }
    private void CancelRequests(){pending.Cancel();artwork.Cancel();foreach(var cancellation in visibleImages.Values)cancellation.Cancel();}
    private void QueryChanged(object sender,TextChangedEventArgs e)
    {if(Games is null)return;CancelRequests();Games.ItemsSource=null;Covers.ItemsSource=null;Notice.Text="Press Search to send this title to SteamGridDB.";}
    private void QueryKeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;Search(sender,e);}}
    private void SaveKey(object sender,RoutedEventArgs e)
    {try{credentials.Write(ApiKey.Password);Notice.Text="Personal key saved in Windows Credential Manager.";}catch(Exception){Notice.Text="The key could not be saved. Check the personal API key or use local Browse.";}finally{ApiKey.Clear();}}
    private void ForgetKey(object sender,RoutedEventArgs e)
    {CancelRequests();ApiKey.Clear();try{credentials.Delete();Notice.Text="Saved key removed. Local Browse remains available.";}catch(Exception){Notice.Text="Windows could not remove the saved key. Try again in Credential Manager.";}}
    private async void Search(object sender,RoutedEventArgs e)
    {
        if(disposed)return;CancelRequests();pending.Dispose();pending=new();var token=pending.Token;Games.ItemsSource=null;Covers.ItemsSource=null;Notice.Text="Searching SteamGridDB…";
        try{var games=await provider.SearchAsync(Query.Text,token);if(token.IsCancellationRequested)return;Games.ItemsSource=games;Notice.Text=games.Count==0?"No matching games. Refine the title and Search again, or use Browse.":"Choose the matching game to see covers.";}
        catch(OperationCanceledException){}catch(Exception error){if(!token.IsCancellationRequested)ShowError(error);}
    }
    private async void ChooseGame(object sender,SelectionChangedEventArgs e)
    {
        if(disposed||Games.SelectedItem is not ArtworkGame game)return;artwork.Cancel();artwork.Dispose();artwork=new();var token=artwork.Token;Covers.ItemsSource=null;Notice.Text="Loading cover choices…";
        try{var covers=await provider.GetCoversAsync(game.Id,token);if(token.IsCancellationRequested)return;Covers.ItemsSource=covers;Notice.Text=covers.Count==0?"No supported covers. Choose another game, refine the title, or use Browse.":"Choose artwork, then Use selected cover. Credits and source pages appear beneath previews.";}
        catch(OperationCanceledException){}catch(Exception error){if(!token.IsCancellationRequested)ShowError(error);}
    }
    private async void ThumbnailLoaded(object sender,RoutedEventArgs e)
    {
        if(disposed||sender is not System.Windows.Controls.Image image||image.DataContext is not ArtworkCover cover)return;image.Source=null;
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(artwork.Token);if(visibleImages.Remove(image,out var previous))previous.Cancel();visibleImages[image]=cancellation;var token=cancellation.Token;var entered=false;
        try{await thumbnails.WaitAsync(token);entered=true;var bytes=await provider.DownloadAsync(cover.Thumbnail,token);var source=await Task.Run(()=>ArtworkImageNormalizer.Preview(bytes),token);if(!token.IsCancellationRequested&&Equals(image.DataContext,cover))image.Source=source;}
        catch(OperationCanceledException){}catch(Exception){if(!token.IsCancellationRequested)image.ToolTip="Preview unavailable. Retry Search or choose another cover.";}
        finally{if(entered)thumbnails.Release();if(visibleImages.TryGetValue(image,out var current)&&ReferenceEquals(current,cancellation))visibleImages.Remove(image);cancellation.Dispose();}
    }
    private void ThumbnailUnloaded(object sender,RoutedEventArgs e)
    {if(sender is System.Windows.Controls.Image image){if(visibleImages.Remove(image,out var cancellation))cancellation.Cancel();image.Source=null;}}
    private async void UseCover(object sender,RoutedEventArgs e)
    {
        if(disposed||Covers.SelectedItem is not ArtworkCover cover){Notice.Text="Choose a cover first.";return;}artwork.Cancel();artwork.Dispose();artwork=new();var token=artwork.Token;UseButton.IsEnabled=false;Notice.Text="Downloading selected cover…";
        try{var bytes=await provider.DownloadAsync(cover.Image,token);var png=await Task.Run(()=>ArtworkImageNormalizer.Normalize(bytes),token);if(!token.IsCancellationRequested)CoverChosen?.Invoke(png,$"{cover.Credit} · {cover.Page}");}
        catch(OperationCanceledException){}catch(Exception error){if(!token.IsCancellationRequested)ShowError(error);}finally{UseButton.IsEnabled=true;}
    }
    private void ShowError(Exception error)=>Notice.Text=error is ArtworkException artworkError?artworkError.Message:"Artwork could not be loaded. Retry, choose another cover, or use local Browse.";
    private void Back(object sender,RoutedEventArgs e){CancelRequests();ApiKey.Clear();Cancelled?.Invoke();}
    public void Dispose(){if(disposed)return;disposed=true;CancelRequests();ApiKey.Clear();provider.Dispose();}
}
