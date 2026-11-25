using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameVault.Core;

namespace GameVault.App;

/// <summary>
/// Five clickable stars with half steps (issue #59). Click the left or right half of a star; click the current rating again to clear it.
/// Keyboard: arrows change by half a star, 1 to 5 choose whole stars, Delete or 0 clears. Colours come from the theme.
/// </summary>
public sealed class StarRatingControl : UserControl
{
    private const string Filled = "";      // Segoe Fluent Icons: FavoriteStarFill
    private const string Outline = "";     // FavoriteStar
    private const double StarWidth = 30;
    private const double GlyphSize = 22;
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
    private readonly TextBlock[] fills = new TextBlock[StarRating.Stars];
    private readonly TextBlock caption = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private int value;
    private int? preview;

    /// <summary>Raised when the player picks a new rating (0 clears it). The owner saves it and sets <see cref="Value"/>.</summary>
    public event EventHandler<int>? Chosen;

    public StarRatingControl()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        var family = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        for (var i = 0; i < fills.Length; i++)
        {
            var star = new Grid { Width = StarWidth, Height = StarWidth };
            TextBlock Glyph(string text, string brush)
            {
                var glyph = new TextBlock { Text = text, FontFamily = family, FontSize = GlyphSize, Width = GlyphSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                glyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
                return glyph;
            }
            star.Children.Add(Glyph(Outline, "AppMuted"));
            star.Children.Add(fills[i] = Glyph(Filled, "AppAccent"));
            row.Children.Add(star);
        }
        caption.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        var layout = new StackPanel { Orientation = Orientation.Horizontal };
        layout.Children.Add(row);
        layout.Children.Add(caption);
        var frame = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(2), Child = layout, HorizontalAlignment = HorizontalAlignment.Left };
        Content = frame;
        GotKeyboardFocus += (_, _) => frame.SetResourceReference(Border.BorderBrushProperty, "AppAccent");
        LostKeyboardFocus += (_, _) => frame.BorderBrush = Brushes.Transparent;
        row.MouseMove += (_, e) => { preview = At(e); Draw(); };
        row.MouseLeave += (_, _) => { preview = null; Draw(); };
        row.MouseLeftButtonUp += (_, e) => { Focus(); Choose(StarRating.Toggle(value, At(e))); preview = null; Draw(); e.Handled = true; };
        IsEnabledChanged += (_, _) => { Opacity = IsEnabled ? 1 : 0.6; preview = null; Draw(); };
        Draw();
    }

    public int Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, StarRating.Max); preview = null; Draw(); }
    }

    internal string Caption => caption.Text;

    /// <summary>The rating a key leads to, or null when the key is not for the stars.</summary>
    internal static int? ForKey(Key key, int current) => key switch
    {
        Key.Right or Key.Up => Math.Min(StarRating.Max, current + 1),
        Key.Left or Key.Down => Math.Max(0, current - 1),
        Key.Home or Key.Delete or Key.Back or Key.D0 or Key.NumPad0 => 0,
        Key.End => StarRating.Max,
        >= Key.D1 and <= Key.D5 => (key - Key.D0) * 2,
        >= Key.NumPad1 and <= Key.NumPad5 => (key - Key.NumPad0) * 2,
        _ => null
    };

    internal void Choose(int halfStars)
    {
        if (IsEnabled && halfStars != value) Chosen?.Invoke(this, halfStars);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Keyboard.Modifiers != ModifierKeys.None || ForKey(e.Key, value) is not { } next) return;
        e.Handled = true;
        Choose(next);
    }

    private int At(MouseEventArgs e) => StarRating.FromPosition(e.GetPosition(row).X, StarWidth * StarRating.Stars);

    private void Draw()
    {
        var shown = preview ?? value;
        for (var i = 0; i < fills.Length; i++)
        {
            var part = StarRating.Fill(shown, i);
            fills[i].Visibility = part > 0 ? Visibility.Visible : Visibility.Hidden;
            fills[i].Clip = part >= 1 ? null : new RectangleGeometry(new Rect(0, 0, GlyphSize / 2, StarWidth));      // the left half of the glyph
        }
        caption.Text = StarRating.Describe(shown);
        AutomationProperties.SetName(this, "Game rating: " + StarRating.Describe(value));
    }
}
