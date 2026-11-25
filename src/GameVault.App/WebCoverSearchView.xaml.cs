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
    private readonly WebCoverSelection selection = new();
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
        Loaded += (_, _) =>
        {
            if (loaded) return;
            loaded = true; Query.Focus();
            // Opening this page is the search: no second click for the game's own name.
            if (SearchOnOpen && !string.IsNullOrWhiteSpace(Query.Text)) Search(this, new RoutedEventArgs());
        };
    }

    /// <summary>Tests turn this off so that no browser is started.</summary>
    internal bool SearchOnOpen { get; set; } = true;

    private WebCoverFilter Filter => FilterChoice.SelectedIndex switch { 1 => WebCoverFilter.BigEnough, 2 => WebCoverFilter.Everything, _ => WebCoverFilter.CoverShape };

    private async void FilterChanged(object sender, SelectionChangedEventArgs e) => await ApplyFilterAsync();

    private async Task ApplyFilterAsync()
    {
        if (disposed || browser?.CoreWebView2 is not { } core) return;
        try
        {
            var result = await core.ExecuteScriptAsync(WebCoverResults.FilterScript(Filter));
            if (disposed || IsBusy || selection.Source is not null) return;
            using var counts = System.Text.Json.JsonDocument.Parse(result);
            if (counts.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            var shown = counts.RootElement.GetProperty("shown").GetInt32();
            var hidden = counts.RootElement.GetProperty("hidden").GetInt32();
            if (shown + hidden == 0) Notice.Text = "Sizes are not available on this page, so every result is shown. Click an image, then choose Preview selected image.";
            else if (shown == 0) Notice.Text = $"None of the {hidden} results fit this filter. Choose a wider one in the list next to Search, or change the title.";
            else Notice.Text = $"Click a picture to see it as a cover. {shown} result(s) shown" + (hidden > 0 ? $", {hidden} that do not fit are hidden." : ".");
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException) { }
    }

    private void QueryKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; Search(sender, e); } }

    private async void Search(object sender, RoutedEventArgs e)
    {
        if (!loaded || disposed || initializing) return;
        Uri query;
        try { query = WebCoverPolicy.SearchUri(Query.Text); }
        catch (ArgumentException error) { Notice.Text = error.Message; return; }
        ResetSelection(); CancelDownload();
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
                browser.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(ImageMouseUp), handledEventsToo: true);
                BrowserHost.Children.Add(browser);
                await browser.EnsureCoreWebView2Async();
                if (disposed) return;
                ConfigureBrowser(browser.CoreWebView2);
            }
            if (disposed) return;
            browser.CoreWebView2.Navigate(query.AbsoluteUri);
            ReloadButton.IsEnabled = true;
            RuntimeHelp.Visibility = Visibility.Collapsed;
            Notice.Text = "Click an image, then choose Preview selected image. If selection is unavailable, right-click the full image and select Preview as cover. Sign-in, external pages and browser downloads are unavailable here.";
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
            ResetSelection(); CancelDownload();
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !WebCoverPolicy.AllowsNavigation(uri))
            { e.Cancel = true; Notice.Text = "This page is outside Google Images. Stay in the image search and click a full image to select it, or Browse a local image."; }
        };
        core.NavigationCompleted += async (_, _) =>
        {
            // The result data can arrive a moment after the page: look again shortly after.
            await ApplyFilterAsync(); await Task.Delay(700); await ApplyFilterAsync(); await Task.Delay(1800); await ApplyFilterAsync();
        };
        core.HistoryChanged += (_, _) => { if (!disposed) BrowserBack.IsEnabled = core.CanGoBack; };
        core.NewWindowRequested += (_, e) => { e.Handled = true; Notice.Text = "Opening another window is unavailable. Select the image inside Google Images, or Browse a local image."; };
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; Notice.Text = "Use Preview selected image, or the image's Preview as cover menu, to download a PNG or JPEG for cropping."; };
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        core.ContextMenuRequested += ContextMenuRequested;
    }

    private async void ImageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || disposed || IsBusy || sender is not WebView2CompositionControl control || control.CoreWebView2 is not { } core) return;
        var operation = selection.Begin();
        UpdatePreviewButton();
        SelectionNotice.Text = "Checking the image you clicked…";
        try
        {
            // WPF mouse coordinates are DIPs; the DOM viewport uses CSS pixels at browser zoom.
            // Observe the click without handling it, so Google's normal full-preview action still works.
            var point = e.GetPosition(control);
            var payload = await core.ExecuteScriptAsync(WebCoverSelection.HitTestScript(point.X, point.Y, control.ZoomFactor));
            if (disposed || browser != control || !selection.Complete(operation, payload)) return;
            if (selection.Source is { } picked && selection.Size is var (width, height))
            {
                if (!WebCoverResults.Fits(width, height))
                {
                    SelectionNotice.Text = $"That picture is {width} × {height}: " + (height <= width ? "not a portrait shape." : "too small for a cover (at least 600 × 900).");
                    UpdatePreviewButton();
                    return;
                }
                // A fitting original: go straight to the crop preview.
                await SelectImageAsync(picked);
                return;
            }
            SelectionNotice.Text = selection.Source is { } source
                ? $"Image selected from {source.IdnHost}. Choose Preview selected image to check its size and crop."
                : "No downloadable image found at that click. Open the full image preview and click the image itself, or right-click it and choose Preview as cover.";
            UpdatePreviewButton();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            if (!disposed && selection.IsCurrent(operation))
            {
                ResetSelection();
                SelectionNotice.Text = "This page could not identify the clicked image. Open the full image preview and click it again, or right-click it and choose Preview as cover.";
            }
        }
    }

    private async void PreviewSelected(object sender, RoutedEventArgs e)
    {
        if (!disposed && !IsBusy && selection.Source is { } source) await SelectImageAsync(source);
    }

    private void UpdatePreviewButton() => PreviewButton.IsEnabled = !disposed && !IsBusy && selection.Source is not null;
    private void ResetSelection()
    {
        selection.Reset();
        if (disposed) return;
        SelectionNotice.Text = "No image selected. Click an image in the search below.";
        UpdatePreviewButton();
    }
    private void ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        // Keep the native image menu as an alternative to explicit left-click selection.
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
        selection.Select(source);
        SelectionNotice.Text = $"Image selected from {source.IdnHost}. Checking the downloaded image before crop preview.";
        CancelDownload();
        var operation = new CancellationTokenSource(); pending = operation;
        IsBusy = true; StopButton.IsEnabled = true; UpdatePreviewButton();
        Notice.Text = "Downloading and checking this image before opening the crop preview…";
        try
        {
            var bytes = await downloader.DownloadAsync(source, operation.Token);
            var image = await Task.Run(() => ArtworkImageNormalizer.Preview(bytes), operation.Token);
            if (!PortraitCoverImages.CanCropToPortrait(image.PixelWidth, image.PixelHeight))
            {
                if (!disposed && pending == operation) Notice.Text = $"This image is {image.PixelWidth} × {image.PixelHeight} pixels. A portrait crop needs at least 600 × 900 pixels without upscaling. Click a larger full image, then preview again.";
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
        catch (WebCoverDownloadException error)
        { if (!disposed && pending == operation) Notice.Text = error.Message; }
        catch (System.Net.Http.HttpRequestException)
        { if (!disposed && pending == operation) Notice.Text = "The image server could not be reached. Check your connection or choose another image."; }
        catch (Exception error) when (error is IOException or FileFormatException or InvalidOperationException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        { if (!disposed && pending == operation) Notice.Text = "The downloaded file could not be decoded as a safe static PNG or JPEG within the 16 megapixel limit. Choose another full image or Browse a local image."; }
        finally
        {
            if (pending == operation) { pending = null; IsBusy = false; if (!disposed) { StopButton.IsEnabled = false; UpdatePreviewButton(); } }
            operation.Dispose();
        }
    }

    private void CancelDownload()
    { pending?.Cancel(); pending = null; IsBusy = false; if (!disposed) { StopButton.IsEnabled = false; UpdatePreviewButton(); } }
    private void Stop(object sender, RoutedEventArgs e)
    { ResetSelection(); CancelDownload(); Notice.Text = "Download cancelled. Choose another image or go Back to covers to Browse a local image."; }
    private void PreviousPage(object sender, RoutedEventArgs e)
    { if (!disposed && browser?.CoreWebView2 is { CanGoBack: true } core) { ResetSelection(); CancelDownload(); core.GoBack(); } }
    private void Reload(object sender, RoutedEventArgs e) { if (!disposed) { ResetSelection(); CancelDownload(); browser?.CoreWebView2?.Reload(); } }
    private void OpenRuntimeHelp(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/#download-section") { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Notice.Text = "The help page could not be opened. Use Browse image in the cover picker."; }
    }
    public Task<bool> CanLeaveAsync() { ResetSelection(); CancelDownload(); browser?.CoreWebView2?.Stop(); return Task.FromResult(true); }
    private async void Back(object sender, RoutedEventArgs e) { if (await CanLeaveAsync()) Cancelled?.Invoke(); }
    public void Dispose()
    {
        if (disposed) return;
        selection.Reset(); disposed = true; PreviewButton.IsEnabled = false; CancelDownload(); browser?.Dispose(); browser = null; downloader.Dispose(); BrowserHost.Children.Clear();
    }
}
