using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App;

/// <summary>One capture in a gallery and what can be done with it. The window supplies the actions, which re-check the file before using it.</summary>
public sealed record CaptureCard(CaptureMedia Media, Action<CaptureMedia> Open, Action<CaptureMedia> ShowInExplorer, Action<CaptureMedia> CopyPath);

/// <summary>A thumbnail tile for a screenshot or recording, used on a game's Media tab and the Captures page (issue #60). It draws whatever card is its DataContext.</summary>
public sealed class CaptureTile : UserControl
{
    private CancellationTokenSource? loading;

    /// <summary>Replaced in tests so no file is handed to Windows.</summary>
    internal static Func<string, CancellationToken, Task<BitmapSource?>> LoadThumbnail { get; set; } = ShellMedia.ThumbnailAsync;

    public CaptureTile()
    {
        Width = CaptureGalleryRules.TileWidth - 8;
        Margin = new Thickness(0, 0, 8, 8);
        Focusable = false;
        DataContextChanged += (_, _) => Draw();
        Unloaded += (_, _) => loading?.Cancel();
        Loaded += (_, _) => { if (loading is { IsCancellationRequested: true }) Draw(); };
    }

    internal Button? Picture { get; private set; }
    internal Button? ExplorerButton { get; private set; }

    private void Draw()
    {
        loading?.Cancel();
        loading = new CancellationTokenSource();
        if (DataContext is not CaptureCard card) { Content = null; Picture = ExplorerButton = null; return; }
        var media = card.Media;
        var complete = media.Status == CaptureMediaStatus.Complete;
        var video = media.Kind == CaptureMediaKind.Video;
        var when = media.CapturedUtc.ToLocalTime().ToString("d MMM yyyy · HH:mm");

        var surface = new Grid { Width = 232, Height = 131 };
        var placeholder = new TextBlock { Text = video ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        var image = new Image { Stretch = Stretch.UniformToFill };
        surface.Children.Add(placeholder);
        surface.Children.Add(image);
        var length = new TextBlock { Foreground = Brushes.White, FontSize = 12 };
        var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = length, Visibility = Visibility.Collapsed };
        if (video)
        {
            length.Text = "▶";
            badge.Visibility = Visibility.Visible;
        }
        surface.Children.Add(badge);

        Picture = new Button { Content = surface, Padding = new Thickness(0), IsEnabled = complete, HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = !complete ? "This capture did not finish, so it cannot be opened." : video ? "Open in your video player" : "Preview" };
        ToolTipService.SetShowOnDisabled(Picture, true);
        AutomationProperties.SetName(Picture, (video ? "Open recording from " : "Preview screenshot from ") + when);
        Picture.Click += (_, _) => card.Open(media);

        var title = new TextBlock { Text = CaptureGalleryRules.Title(media), FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = media.Warning ?? media.Error };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");      // otherwise the list row gives it the system's black
        var size = "";
        try { size = " · " + CaptureGalleryRules.Size(new FileInfo(media.FilePath).Length); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        var detail = new TextBlock { Text = when + size, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        var file = new TextBlock { Text = Path.GetFileName(media.FilePath), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Path.GetFileName(media.FilePath), Visibility = video ? Visibility.Visible : Visibility.Collapsed };
        file.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");

        ExplorerButton = new Button { Content = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Width = 34, Height = 30, Padding = new Thickness(0), ToolTip = "Show in File Explorer", VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(ExplorerButton, "Show the capture from " + when + " in File Explorer");
        ExplorerButton.Click += (_, _) => card.ShowInExplorer(media);
        var text = new StackPanel { Margin = new Thickness(0, 6, 6, 0) };
        text.Children.Add(title);
        text.Children.Add(detail);
        text.Children.Add(file);
        var footer = new DockPanel();
        DockPanel.SetDock(ExplorerButton, Dock.Right);
        ExplorerButton.Margin = new Thickness(0, 6, 0, 0);
        footer.Children.Add(ExplorerButton);
        footer.Children.Add(text);
        var layout = new StackPanel();
        layout.Children.Add(Picture);
        layout.Children.Add(footer);

        var menu = new ContextMenu();
        foreach (var (header, action, enabled) in new (string, Action<CaptureMedia>, bool)[] { (video ? "Open in video player" : "Preview", card.Open, complete), ("Show in File Explorer", card.ShowInExplorer, true), ("Copy file path", card.CopyPath, true) })
        {
            var entry = new MenuItem { Header = header, IsEnabled = enabled };
            entry.Click += (_, _) => action(media);
            menu.Items.Add(entry);
        }
        ContextMenu = menu;
        Content = layout;
        if (complete) _ = LoadAsync(media, image, video ? length : null, loading.Token);
    }

    private static async Task LoadAsync(CaptureMedia media, Image image, TextBlock? length, CancellationToken cancellationToken)
    {
        try
        {
            var picture = await LoadThumbnail(media.FilePath, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            image.Source = picture;
            if (length is null) return;
            var duration = CaptureGalleryRules.Duration(await ShellMedia.DurationAsync(media.FilePath));
            if (!cancellationToken.IsCancellationRequested && duration.Length > 0) length.Text = "▶ " + duration;
        }
        catch (OperationCanceledException) { }
    }
}
