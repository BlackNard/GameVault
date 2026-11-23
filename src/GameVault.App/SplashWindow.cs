using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App;

/// <summary>
/// The small startup window: logo, name and a short status line, in the saved theme. It is shown before anything is read, never takes
/// focus, has no taskbar button and no animation when the player asked for reduced motion.
/// </summary>
public sealed class SplashWindow : Window
{
    private bool closed;

    public SplashWindow(string theme, bool reduceMotion)
    {
        Title = "GameVault";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 460;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        ThemePalette.Apply(this, theme);
        AutomationProperties.SetName(this, "GameVault is starting");

        var frame = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(8) };
        frame.SetResourceReference(Border.BackgroundProperty, "AppBackground");
        frame.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var logo = Logo();
        if (logo is not null) stack.Children.Add(new Image { Source = logo, Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Center });
        var name = new TextBlock { Text = "GameVault", FontSize = 28, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "AppText");
        stack.Children.Add(name);
        var status = new TextBlock { Text = "Loading your library…", Margin = new Thickness(0, 6, 0, 18), HorizontalAlignment = HorizontalAlignment.Center };
        status.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        stack.Children.Add(status);
        var bar = new ProgressBar { Width = 160, Height = 3, IsIndeterminate = !reduceMotion, Value = reduceMotion ? 100 : 0, Maximum = 100, Focusable = false, BorderThickness = new Thickness(0) };
        bar.SetResourceReference(ForegroundProperty, "AppPrimary");
        bar.SetResourceReference(BackgroundProperty, "AppRaised");
        stack.Children.Add(bar);
        frame.Child = stack;
        Content = frame;
    }

    public bool IsClosed => closed;

    /// <summary>Closes the window once; calling it again (for example from a failure path) does nothing.</summary>
    public void CloseOnce()
    {
        if (closed) return;
        closed = true;
        try { Close(); }
        catch (InvalidOperationException) { }
    }

    private static BitmapSource? Logo()
    {
        try
        {
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/GameVault.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
        }
        catch (Exception error) when (error is IOException or NotSupportedException or InvalidOperationException or UriFormatException or System.Runtime.InteropServices.COMException) { return null; }
    }
}
