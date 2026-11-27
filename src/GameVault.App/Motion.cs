using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App;

/// <summary>
/// All of GameVault's transitions go through here (issue #55), so one switch turns them off. Mark an element with <c>local:Motion.Appear</c> and it eases
/// in whenever it becomes visible. Nothing waits for an animation: input works at once, and every property ends at its own value.
/// </summary>
public static class Motion
{
    /// <summary>The player's Reduce motion setting.</summary>
    public static bool ReduceMotion { get; set; }

    public static bool Enabled => MotionRules.Enabled(ReduceMotion, SystemParameters.ClientAreaAnimation);

    public static readonly DependencyProperty AppearProperty = DependencyProperty.RegisterAttached("Appear", typeof(MotionKind), typeof(Motion),
        new PropertyMetadata(MotionKind.None, AppearChanged));

    public static MotionKind GetAppear(DependencyObject element) => (MotionKind)element.GetValue(AppearProperty);
    public static void SetAppear(DependencyObject element, MotionKind value) => element.SetValue(AppearProperty, value);

    private static void AppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.IsVisibleChanged -= Appeared;
        if ((MotionKind)e.NewValue != MotionKind.None) element.IsVisibleChanged += Appeared;
    }

    private static void Appeared(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is FrameworkElement element) Play(element, GetAppear(element));
    }

    private static DoubleAnimation Ease(double from, MotionSpec spec) => new()
    {
        From = from, Duration = TimeSpan.FromMilliseconds(spec.Milliseconds), FillBehavior = FillBehavior.Stop,
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    /// <summary>Fades the element in from a small offset.</summary>
    public static void Play(FrameworkElement element, MotionKind kind)
    {
        if (!Enabled || kind == MotionKind.None) return;
        var spec = MotionRules.For(kind);
        // No "To": the animation ends at the element's own opacity.
        element.BeginAnimation(UIElement.OpacityProperty, Ease(0, spec));
        if (spec.OffsetX == 0 && spec.OffsetY == 0) return;
        if (element.RenderTransform is not TranslateTransform { IsFrozen: false } move)
        {
            // An element that is already transformed for another reason is only faded.
            if (element.RenderTransform is { } other && other != Transform.Identity) return;
            element.RenderTransform = move = new TranslateTransform();
        }
        if (spec.OffsetX != 0) move.BeginAnimation(TranslateTransform.XProperty, Ease(spec.OffsetX, spec));
        if (spec.OffsetY != 0) move.BeginAnimation(TranslateTransform.YProperty, Ease(spec.OffsetY, spec));
    }

    /// <summary>
    /// Applies a change of colours behind a picture of the old look that then fades away. The picture never takes clicks. Without motion, or if the
    /// picture cannot be made, the change simply happens.
    /// </summary>
    public static void CrossFade(Window window, Action change)
    {
        if (!Enabled || window.Content is not Grid { ActualWidth: >= 1, ActualHeight: >= 1 } root) { change(); return; }
        Image? old = null;
        try
        {
            var size = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            var picture = new DrawingVisual();
            using (var drawing = picture.RenderOpen())
            {
                // The window paints the background; the grid above it is drawn over that.
                if (window.Background is { } background) drawing.DrawRectangle(background.CloneCurrentValue(), null, size);
                drawing.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, size);
            }
            var dpi = VisualTreeHelper.GetDpi(root);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(picture);
            bitmap.Freeze();
            old = new Image { Source = bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false, Focusable = false };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.COMException) { }
        change();
        if (old is null) return;
        Grid.SetRowSpan(old, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(old, Math.Max(1, root.ColumnDefinitions.Count));
        Panel.SetZIndex(old, 90);
        root.Children.Add(old);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(MotionRules.For(MotionKind.Theme).Milliseconds));
        fade.Completed += (_, _) => root.Children.Remove(old);
        old.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
