using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App;

/// <summary>A large screenshot preview inside GameVault with previous and next (issue #60). Left and Right move, Escape closes.</summary>
public sealed class CapturePreviewView : UserControl
{
    private readonly IReadOnlyList<CaptureMedia> media;
    private readonly Image picture = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    private readonly TextBlock heading = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
    private readonly Button previous = new() { Content = "‹ Previous", Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button next = new() { Content = "Next ›", Margin = new Thickness(0, 0, 16, 0) };
    private readonly Func<string, BitmapSource> load;
    private CaptureMedia current;

    public event EventHandler<bool>? Completed;

    public CapturePreviewView(string theme, IReadOnlyList<CaptureMedia> media, CaptureMedia first, Action<CaptureMedia> openExternally, Action<CaptureMedia> showInExplorer, Func<string, BitmapSource>? loader = null)
    {
        this.media = media;
        current = first;
        load = loader ?? Load;
        Focusable = true;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppText");
        FontSize = 14;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        ThemePalette.Apply(this, theme);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(heading);
        notice.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        header.Children.Add(notice);
        root.Children.Add(header);
        Grid.SetRow(picture, 1);
        AutomationProperties.SetName(picture, "Screenshot preview");
        root.Children.Add(picture);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var open = new Button { Content = "Open in default app", Margin = new Thickness(0, 0, 8, 0) };
        var explorer = new Button { Content = "Show in File Explorer", Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", MinWidth = 100 };
        foreach (var button in new[] { previous, next, open, explorer, close }) buttons.Children.Add(button);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        previous.Click += (_, _) => Move(-1);
        next.Click += (_, _) => Move(1);
        open.Click += (_, _) => openExternally(current);
        explorer.Click += (_, _) => showInExplorer(current);
        close.Click += (_, _) => Completed?.Invoke(this, false);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; Move(e.Key == Key.Left ? -1 : 1); }
            else if (e.Key == Key.Escape) { e.Handled = true; Completed?.Invoke(this, false); }
        };
        Loaded += (_, _) => Focus();
        Show();
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(true);
    internal CaptureMedia Current => current;

    internal void Move(int step)
    {
        if (CaptureGalleryRules.Neighbour(media, current.Id, step) is not { } target) return;
        current = target;
        Show();
    }

    private void Show()
    {
        heading.Text = "Screenshot · " + current.CapturedUtc.ToLocalTime().ToString("d MMM yyyy · HH:mm:ss");
        previous.IsEnabled = CaptureGalleryRules.Neighbour(media, current.Id, -1) is not null;
        next.IsEnabled = CaptureGalleryRules.Neighbour(media, current.Id, 1) is not null;
        try
        {
            picture.Source = load(current.FilePath);
            notice.Text = Path.GetFileName(current.FilePath) + (current.Warning is null ? "" : " · " + current.Warning);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            picture.Source = null;
            notice.Text = "This screenshot could not be shown. " + error.Message;
        }
    }

    private static BitmapSource Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var width = frame.PixelWidth;
        if (width > 16384 || frame.PixelHeight > 16384) throw new NotSupportedException("The picture is too large to preview here. Open it in the default app.");
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = Math.Min(width, 2560);
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
