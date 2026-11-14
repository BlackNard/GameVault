using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameVault.App.Artwork;
namespace GameVault.App;
public partial class CoverSearchView : UserControl, IDisposable
{
    private byte[]? candidate;
    private string candidateName = "";
    private bool disposed;
    private readonly InlineDraftGuard navigation = new("");
    public bool IsBusy { get; private set; }
    public event Action<byte[],string>? CoverChosen;
    public event Action? Cancelled;
    public CoverSearchView(string title)
    {
        InitializeComponent(); Query.Text = title;
        Loaded += (_, _) => Query.Focus();
    }
    private void QueryKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; Search(sender,e); } }
    private void Search(object sender,RoutedEventArgs e)
    {
        try
        {
            var uri = CoverImport.BrowserSearch(Query.Text);
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            Notice.Text = "Browser image search opened. Save artwork you may use, then Browse or drop that file here.";
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Notice.Text = "Could not open image search. " + error.Message; }
    }
    private void ForgetKey(object sender,RoutedEventArgs e)
    {
        try { new WindowsArtworkCredentialStore().Delete(); Notice.Text = "Previously saved artwork key removed from Windows Credential Manager."; }
        catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Notice.Text = "Windows could not remove the saved key. " + error.Message; }
    }
    private async void Browse(object sender,RoutedEventArgs e)
    {
        if (IsBusy) return;
        try
        {
            var path = NativePickers.File(Window.GetWindow(this)!, "Choose cover image", "Cover images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
            if(path is not null) await Import([path]);
        }
        catch(Exception error) when(Expected(error)) { Notice.Text = error.Message; }
    }
    private void DragOverImage(object sender,DragEventArgs e)
    { e.Effects = !IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void DropImage(object sender,DragEventArgs e)
    {
        e.Handled = true;
        if(!IsBusy && e.Data.GetData(DataFormats.FileDrop) is string[] files) await Import(files);
    }
    public async Task ImportAsync(string path) => await Import([path]);
    private async Task Import(string[] files)
    {
        if(IsBusy || disposed) return;
        IsBusy = true; UseButton.IsEnabled = BrowseButton.IsEnabled = BackButton.IsEnabled = false;
        try
        {
            var png = await CoverImport.ReadAsync(files);
            if(disposed) return;
            var preview = ArtworkImageNormalizer.Preview(png);
            candidate = png; candidateName = Path.GetFileName(files[0]); Preview.Source = preview;
            Notice.Text = "Preview ready. Choose Use this cover to confirm, or Back to keep your previous cover.";
        }
        catch(Exception error) when(Expected(error)) { if(!disposed) Notice.Text = "Could not import artwork. " + error.Message; }
        finally { IsBusy = false; UseButton.IsEnabled = candidate is not null; BrowseButton.IsEnabled = BackButton.IsEnabled = true; }
    }
    private void UseCover(object sender,RoutedEventArgs e)
    { if(!IsBusy && candidate is not null) CoverChosen?.Invoke(candidate,candidateName); }
    public Task<bool> CanLeaveAsync() => navigation.CanLeaveAsync(IsBusy, candidate is null ? "" : "preview", async () =>
        await InlinePrompts.ShowAsync(this,"Discard this unconfirmed cover preview?", "Cover preview",MessageBoxButton.YesNo,MessageBoxResult.No) == MessageBoxResult.Yes);
    private async void Back(object sender,RoutedEventArgs e) { if(await CanLeaveAsync()) Cancelled?.Invoke(); }
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or System.IO.FileFormatException;
    public void Dispose() { disposed = true; candidate = null; Preview.Source = null; }
}
