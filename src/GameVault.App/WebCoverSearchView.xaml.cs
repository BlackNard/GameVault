using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameVault.App.Artwork;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GameVault.App;

public partial class WebCoverSearchView : UserControl, IDisposable
{
    private readonly WebCoverImageDownload downloader = new();
    private WebView2CompositionControl? browser;
    private CancellationTokenSource? pending;
    private bool loaded;
    private bool disposed;
    private bool initializing;
    public bool IsBusy { get; private set; }
    public event Action<byte[], Uri>? ImageChosen;
    public event Action? Cancelled;

    public WebCoverSearchView(string title)
    {
        InitializeComponent();
        Query.Text = title;
        Loaded += (_, _) => { loaded = true; Query.Focus(); };
    }

    private void QueryKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; Search(sender, e); } }

    private async void Search(object sender, RoutedEventArgs e)
    {
        if (!loaded || disposed || initializing) return;
        Uri query;
        try { query = WebCoverPolicy.SearchUri(Query.Text); }
        catch (ArgumentException error) { Notice.Text = error.Message; return; }
        CancelDownload();
        initializing = true; SearchButton.IsEnabled = false;
        try
        {
            if (browser is null)
            {
                // Verify managed composition dependencies before the control schedules layout work.
                BrowserDependencies.Verify();
                // Never use the owner's Edge profile or the portable library's Data/Profiles.
                browser = new WebView2CompositionControl
                {
                    CreationProperties = new CoreWebView2CreationProperties
                    {
                        UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameVault", "WebCoverSearch"),
                        IsInPrivateModeEnabled = true
                    }
                };
                BrowserHost.Children.Add(browser);
                await browser.EnsureCoreWebView2Async();
                if (disposed) return;
                ConfigureBrowser(browser.CoreWebView2);
            }
            if (disposed) return;
            browser.CoreWebView2.Navigate(query.AbsoluteUri);
            ReloadButton.IsEnabled = true;
            RuntimeHelp.Visibility = Visibility.Collapsed;
            Notice.Text = "Choose a result, then right-click the full image and select Preview as cover. Sign-in, external pages and browser downloads are unavailable here.";
        }
        catch (Exception error) when (error is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or TypeLoadException or BadImageFormatException)
        {
            if (!disposed)
            {
                browser?.Dispose(); browser = null; BrowserHost.Children.Clear();
                var runtimeMissing=error is WebView2RuntimeNotFoundException;
                Notice.Text = runtimeMissing
                    ? "Microsoft Edge WebView2 Runtime is unavailable. Use Browse image, or open Microsoft's runtime download page. GameVault will not install it automatically."
                    : "Google Images could not start because a browser component is unavailable. Use the latest clean GameVault build, or Browse an image. Other app features remain available.";
                RuntimeHelp.Visibility = runtimeMissing ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        finally { initializing = false; if (!disposed) SearchButton.IsEnabled = true; }
    }

    private void ConfigureBrowser(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !WebCoverPolicy.AllowsNavigation(uri))
            { e.Cancel = true; Notice.Text = "This page is outside Google Images. Stay in the image search and right-click a full image to preview it, or Browse a local image."; }
        };
        core.HistoryChanged += (_, _) => { if (!disposed) BrowserBack.IsEnabled = core.CanGoBack; };
        core.NewWindowRequested += (_, e) => { e.Handled = true; Notice.Text = "Opening another window is unavailable. Select the image inside Google Images, or Browse a local image."; };
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; Notice.Text = "Use the image's Preview as cover menu to download a PNG or JPEG for cropping."; };
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        core.ContextMenuRequested += ContextMenuRequested;
    }

    private void ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        // Native WebView2 menu API only: no DOM, script injection, scraping or web-message bridge.
        e.MenuItems.Clear();
        if (disposed || sender is not CoreWebView2 core || e.ContextMenuTarget.Kind != CoreWebView2ContextMenuTargetKind.Image ||
            !Uri.TryCreate(e.ContextMenuTarget.SourceUri, UriKind.Absolute, out var source))
        { e.Handled = true; return; }
        try { source = WebCoverPolicy.ImageUri(source); }
        catch (InvalidOperationException) { e.Handled = true; Notice.Text = "This image has no public HTTPS download address. Choose a full PNG or JPEG result, or Browse a local image."; return; }
        var selected = source;
        var item = core.Environment.CreateContextMenuItem("Preview as cover", null, CoreWebView2ContextMenuItemKind.Command);
        item.IsEnabled = !IsBusy;
        item.CustomItemSelected += async (_, _) => await SelectImageAsync(selected);
        e.MenuItems.Add(item);
    }

    private async Task SelectImageAsync(Uri source)
    {
        if (disposed) return;
        CancelDownload();
        var operation = new CancellationTokenSource(); pending = operation;
        IsBusy = true; StopButton.IsEnabled = true;
        Notice.Text = "Downloading and checking this image before opening the crop preview…";
        try
        {
            var bytes = await downloader.DownloadAsync(source, operation.Token);
            var image = await Task.Run(() => ArtworkImageNormalizer.Preview(bytes), operation.Token);
            if (!PortraitCoverImages.CanCropToPortrait(image.PixelWidth, image.PixelHeight))
            {
                if (!disposed && pending == operation) Notice.Text = "This image is too small for a 600 × 900 portrait crop without upscaling. Choose a larger full image.";
                return;
            }
            operation.Token.ThrowIfCancellationRequested();
            if (!disposed && pending == operation)
            {
                Notice.Text = "Image checked. Review the crop, then confirm Use this cover.";
                // Pass original bytes: the crop picker validates the effective portrait dimensions.
                ImageChosen?.Invoke(bytes, source);
            }
        }
        catch (OperationCanceledException) { if (!disposed && pending == operation) Notice.Text = "Image download cancelled or timed out. Choose another image or Browse a local image."; }
        catch (Exception error) when (error is IOException or FileFormatException or System.Net.Http.HttpRequestException or InvalidOperationException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        { if (!disposed && pending == operation) Notice.Text = "This image could not be downloaded or decoded as a safe static PNG or JPEG. The website may block direct downloads. Choose another full image or Browse a local image."; }
        finally
        {
            if (pending == operation) { pending = null; IsBusy = false; if (!disposed) StopButton.IsEnabled = false; }
            operation.Dispose();
        }
    }

    private void CancelDownload()
    { pending?.Cancel(); pending = null; IsBusy = false; if (!disposed) StopButton.IsEnabled = false; }
    private void Stop(object sender, RoutedEventArgs e)
    { CancelDownload(); Notice.Text = "Download cancelled. Choose another image or go Back to covers to Browse a local image."; }
    private void PreviousPage(object sender, RoutedEventArgs e)
    { if (!disposed && browser?.CoreWebView2 is { CanGoBack: true } core) core.GoBack(); }
    private void Reload(object sender, RoutedEventArgs e) { if (!disposed) browser?.CoreWebView2?.Reload(); }
    private void OpenRuntimeHelp(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/#download-section") { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Notice.Text = "The help page could not be opened. Use Browse image in the cover picker."; }
    }
    public Task<bool> CanLeaveAsync() { CancelDownload(); browser?.CoreWebView2?.Stop(); return Task.FromResult(true); }
    private async void Back(object sender, RoutedEventArgs e) { if (await CanLeaveAsync()) Cancelled?.Invoke(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; CancelDownload(); browser?.Dispose(); browser = null; downloader.Dispose(); BrowserHost.Children.Clear();
    }
}
