using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameVault.Core;

namespace GameVault.App;

// Reordering the cards by hand: the card is picked up and follows the pointer, the others slide aside to show where it will land, and it settles there.
// A picture of the card is what moves; the card itself stays in the grid, faint, as the place it would take.
public sealed partial class ListsView
{
    private const double Lifted = 1.06, Tilt = 2.5, Edge = 70;
    private static readonly TimeSpan Slide = TimeSpan.FromMilliseconds(180);
    private readonly Canvas overlay = new() { IsHitTestVisible = false, ClipToBounds = true };
    private (Button Card, Point At)? press;
    private CardDrag? drag;
    private bool renderPending;

    /// <summary>What one draggable card carries.</summary>
    private sealed record CardInfo(Guid Key, TextBlock? Rank, Action<Guid, Guid> Dropped);

    private sealed class CardDrag(Button card, Panel grid, Rect[] slots, Button[] order, Image picture, Point grab)
    {
        public Button Card { get; } = card;
        public Panel Grid { get; } = grid;
        /// <summary>Where the cards were when the drag began; cards are all one size, so these places stay.</summary>
        public Rect[] Slots { get; } = slots;
        public Button[] Order { get; } = order;
        public Image Picture { get; } = picture;
        public Point Grab { get; } = grab;
        public int From { get; } = Array.IndexOf(order, card);
        public int To { get; set; } = Array.IndexOf(order, card);
        public bool Settling { get; set; }
        public DispatcherTimer? Scroller { get; set; }
    }

    private void PrepareDragging()
    {
        MouseMove += (_, e) => { if (drag is { Settling: false } current) DragTo(e.GetPosition(current.Grid), e.GetPosition(overlay)); };
        MouseLeftButtonUp += (_, e) => { if (drag is { Settling: false }) { e.Handled = true; EndDrag(true); } };
        LostMouseCapture += (_, e) => { if (ReferenceEquals(e.OriginalSource, this) && drag is { Settling: false }) EndDrag(false); };
        IsVisibleChanged += (_, e) => { if (e.NewValue is false && drag is { Settling: false }) EndDrag(false); };
    }

    /// <summary>A card can be picked up and put in another's place. A click without moving still opens it.</summary>
    private void Draggable(Button card, Guid key, Action<Guid, Guid> dropped)
    {
        var rank = card.Content is Panel { Children: [Panel top, ..] } ? top.Children.OfType<Border>().Select(b => b.Child).OfType<TextBlock>().FirstOrDefault(t => t.Text.StartsWith('#')) : null;
        card.Tag = new CardInfo(key, rank, dropped);
        card.PreviewMouseLeftButtonDown += (_, e) =>
            press = e.OriginalSource is DependencyObject source && !InMoreButton(source, card) ? (card, e.GetPosition(card)) : null;
        card.PreviewMouseLeftButtonUp += (_, _) => press = null;
        card.PreviewMouseMove += (_, e) =>
        {
            if (drag is not null || press is not { } pressed || pressed.Card != card || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(card);
            if (Math.Abs(now.X - pressed.At.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - pressed.At.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            press = null;
            card.ReleaseMouseCapture();     // so letting go is not also a click
            if (!BeginDrag(card, pressed.At, e.GetPosition(overlay))) return;
            if (!CaptureMouse()) EndDrag(false);
        };
    }

    private static bool InMoreButton(DependencyObject source, Button card)
    {
        for (var current = source; current is not null && current != card; current = VisualTreeHelper.GetParent(current))
            if (current is Button) return true;
        return false;
    }

    private bool BeginDrag(Button card, Point grab, Point pointer)
    {
        if (drag is not null || card.Parent is not Panel grid || card.ActualWidth < 1) return false;
        var order = grid.Children.OfType<Button>().ToArray();
        var slots = order.Select(c => new Rect(c.TranslatePoint(new Point(), grid), c.RenderSize)).ToArray();
        // The picture of the card, without its ⋯ button.
        if (card.Content is Panel { Children: [Panel top, ..] })
            foreach (var more in top.Children.OfType<Button>()) more.Visibility = Visibility.Hidden;
        // Nor with its number: the faint place it leaves shows the number it will get.
        var badge = (card.Tag as CardInfo)?.Rank?.Parent as UIElement;
        if (badge is not null) badge.Visibility = Visibility.Hidden;
        card.UpdateLayout();
        var picture = new Image { Source = Snapshot(card), Width = card.ActualWidth, Height = card.ActualHeight, RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = 0.55, Color = Colors.Black } };
        if (badge is not null) badge.Visibility = Visibility.Visible;
        var scale = new ScaleTransform(Lifted, Lifted);
        var turn = new RotateTransform(Motion.Enabled ? Tilt : 0);
        picture.RenderTransform = new TransformGroup { Children = { scale, turn } };
        if (Motion.Enabled)
        {
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) scale.BeginAnimation(property, To(1, Lifted));
            turn.BeginAnimation(RotateTransform.AngleProperty, To(0, Tilt));
        }
        overlay.Children.Add(picture);
        drag = new CardDrag(card, grid, slots, order, picture, grab);
        card.Opacity = 0.25;
        Cursor = Cursors.SizeAll;
        Place(picture, new Point(pointer.X - grab.X, pointer.Y - grab.Y));
        // A card held near the top or bottom brings the rest of a long list into view.
        var scroller = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        scroller.Tick += (_, _) => ScrollWhileDragging();
        drag.Scroller = scroller;
        scroller.Start();
        if (Window.GetWindow(this) is { } window) window.PreviewKeyDown += CancelOnEscape;
        return true;
    }

    private void DragTo(Point inGrid, Point inOverlay)
    {
        if (drag is not { Settling: false } current) return;
        Place(current.Picture, new Point(inOverlay.X - current.Grab.X, inOverlay.Y - current.Grab.Y));
        var over = Array.FindIndex(current.Slots, s => s.Contains(inGrid));
        if (over >= 0) MoveTo(current, over);
    }

    /// <summary>Shows the grid as it would be with the card at <paramref name="index"/>; the cards that move slide from where they were.</summary>
    private static void MoveTo(CardDrag current, int index)
    {
        if (index == current.To) return;
        var before = current.Grid.Children.OfType<Button>().ToList();
        current.To = index;
        var after = current.Order.Where(c => c != current.Card).ToList();
        after.Insert(index, current.Card);
        current.Grid.Children.Clear();
        foreach (var card in after) current.Grid.Children.Add(card);
        for (var i = 0; i < after.Count; i++)
        {
            var card = after[i];
            if (card.Tag is CardInfo { Rank: { } rank }) rank.Text = "#" + (i + 1);
            var was = before.IndexOf(card);
            if (was == i || card == current.Card || !Motion.Enabled) continue;
            var move = new TranslateTransform();
            card.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.XProperty, To(current.Slots[was].X - current.Slots[i].X, 0));
            move.BeginAnimation(TranslateTransform.YProperty, To(current.Slots[was].Y - current.Slots[i].Y, 0));
        }
    }

    /// <summary>Lets go: the card settles into its new place, or back into its old one.</summary>
    private void EndDrag(bool drop)
    {
        if (drag is not { Settling: false } current) return;
        current.Settling = true;
        current.Scroller?.Stop();
        if (Window.GetWindow(this) is { } window) window.PreviewKeyDown -= CancelOnEscape;
        Cursor = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!drop) MoveTo(current, current.From);
        var moved = drop && current.To != current.From;
        void Finish()
        {
            overlay.Children.Remove(current.Picture);
            current.Card.Opacity = 1;
            drag = null;
            var info = (CardInfo)current.Card.Tag;
            // The change redraws the page; otherwise anything that arrived meanwhile is drawn now.
            if (moved) { renderPending = false; info.Dropped(info.Key, ((CardInfo)current.Order[current.To].Tag).Key); }
            else if (renderPending) { renderPending = false; Render(); }
        }
        if (!Motion.Enabled || !IsVisible) { Finish(); return; }
        var home = current.Grid.TranslatePoint(current.Slots[current.To].TopLeft, overlay);
        var x = new DoubleAnimation(home.X, Slide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        x.Completed += (_, _) => Finish();
        if (current.Picture.RenderTransform is TransformGroup { Children: [ScaleTransform scale, RotateTransform turn] })
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Slide));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Slide));
            turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, Slide));
        }
        current.Picture.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(home.Y, Slide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        current.Picture.BeginAnimation(Canvas.LeftProperty, x);
    }

    private void CancelOnEscape(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || drag is null) return;
        e.Handled = true;
        EndDrag(false);
    }

    private void ScrollWhileDragging()
    {
        if (drag is not { Settling: false } current) return;
        var y = Mouse.GetPosition(scroll).Y;
        // The nearer the edge, the faster.
        var step = y < Edge ? -(Edge - y) / 3 : y > scroll.ActualHeight - Edge ? (y - (scroll.ActualHeight - Edge)) / 3 : 0;
        if (step == 0) return;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + Math.Clamp(step, -30, 30));
        scroll.UpdateLayout();
        DragTo(Mouse.GetPosition(current.Grid), Mouse.GetPosition(overlay));
    }

    private static DoubleAnimation To(double from, double to) =>
        new(from, to, Slide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    private static void Place(UIElement element, Point at)
    {
        Canvas.SetLeft(element, at.X);
        Canvas.SetTop(element, at.Y);
    }

    private static BitmapSource Snapshot(FrameworkElement element)
    {
        var size = new Size(element.ActualWidth, element.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(element), null, new Rect(size));
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }
}
