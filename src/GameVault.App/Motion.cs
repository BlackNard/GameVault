using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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

    // ---------- covers ----------
    /// <summary>Use instead of <c>Image.Source</c>: a picture that arrives fades in.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached("Source", typeof(ImageSource), typeof(Motion),
        new PropertyMetadata(null, (d, e) =>
        {
            if (d is not Image image) return;
            image.Source = (ImageSource?)e.NewValue;
            if (e.NewValue is not null) Play(image, MotionKind.Fade);
        }));

    public static ImageSource? GetSource(DependencyObject element) => (ImageSource?)element.GetValue(SourceProperty);
    public static void SetSource(DependencyObject element, ImageSource? value) => element.SetValue(SourceProperty, value);

    // ---------- cards ----------
    /// <summary>A card that rises a little under the pointer, with the theme's glow behind it. The child tagged "Glow" carries the light.</summary>
    public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached("Lift", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) =>
    {
        if (d is not FrameworkElement element) return;
        element.MouseEnter -= Entered; element.MouseLeave -= Left;
        if (e.NewValue is true) { element.MouseEnter += Entered; element.MouseLeave += Left; }
    }));

    public static bool GetLift(DependencyObject element) => (bool)element.GetValue(LiftProperty);
    public static void SetLift(DependencyObject element, bool value) => element.SetValue(LiftProperty, value);
    private static void Entered(object sender, System.Windows.Input.MouseEventArgs e) { if (sender is Panel card) Hover(card, true); }
    private static void Left(object sender, System.Windows.Input.MouseEventArgs e) { if (sender is Panel card) Hover(card, false); }

    public static void Hover(Panel card, bool over)
    {
        var glow = card.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "Glow"));
        if (over && !Enabled) return;
        if (!over && glow?.Effect is null) return;
        var spec = MotionRules.For(MotionKind.Lift);
        var length = TimeSpan.FromMilliseconds(spec.Milliseconds);
        if (card.RenderTransform is not TranslateTransform { IsFrozen: false } move) card.RenderTransform = move = new TranslateTransform();
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(over ? spec.OffsetY : 0, length) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (glow is null) return;
        if (over)
        {
            var colour = card.TryFindResource("AppGlow") is SolidColorBrush brush ? brush.Color : Colors.White;
            glow.Effect = new DropShadowEffect { Color = colour, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.9 };
        }
        var fade = new DoubleAnimation(over ? 1 : 0, length);
        // The effect is only there while it shows: cards at rest cost nothing extra.
        if (!over) fade.Completed += (_, _) => { if (!card.IsMouseOver) glow.Effect = null; };
        glow.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ---------- the first cards ----------
    private const int StaggeredCards = 24;
    private static int staggered;
    private static long firstCard;

    /// <summary>The cards present when the library first appears come in one after another; later ones (scrolling) do not.</summary>
    public static readonly DependencyProperty StaggerProperty = DependencyProperty.RegisterAttached("Stagger", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) =>
    {
        if (d is FrameworkElement element && e.NewValue is true) element.Loaded += FirstAppearance;
    }));

    public static bool GetStagger(DependencyObject element) => (bool)element.GetValue(StaggerProperty);
    public static void SetStagger(DependencyObject element, bool value) => element.SetValue(StaggerProperty, value);

    private static void FirstAppearance(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= FirstAppearance;
        if (!Enabled || staggered >= StaggeredCards) return;
        if (staggered == 0) firstCard = Environment.TickCount64;
        else if (Environment.TickCount64 - firstCard > 1500) { staggered = StaggeredCards; return; }
        var delay = TimeSpan.FromMilliseconds(MotionRules.StaggerDelay(staggered++));
        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(MotionRules.For(MotionKind.Section).Milliseconds))));
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ---------- bars ----------
    /// <summary>A bar of a chart or a progress bar grows from nothing to its size.</summary>
    public static void Grow(FrameworkElement bar, bool upwards)
    {
        if (!Enabled) return;
        var grow = new ScaleTransform(1, 1);
        bar.RenderTransformOrigin = upwards ? new Point(0.5, 1) : new Point(0, 0.5);
        bar.RenderTransform = grow;
        grow.BeginAnimation(upwards ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty, Ease(0, MotionRules.For(MotionKind.Grow)));
    }

    /// <summary>Sets a progress bar's value, counting up to it from where it was.</summary>
    public static void Progress(System.Windows.Controls.Primitives.RangeBase bar, double value)
    {
        var from = bar.Value;
        bar.Value = value;
        if (Enabled && Math.Abs(value - from) > 0.001) bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, Ease(from, MotionRules.For(MotionKind.Grow)));
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
