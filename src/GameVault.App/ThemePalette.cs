using System.Windows;
using System.Windows.Media;
using GameVault.Core;

namespace GameVault.App;

internal static class ThemePalette
{
    public static void Apply(FrameworkElement element, string theme)
    {
        element.Resources["AppDisabledOpacity"] = SystemParameters.HighContrast ? 1.0 : 0.45;
        if (SystemParameters.HighContrast)
        {
            element.Resources["AppBackground"] = SystemColors.WindowBrush;
            element.Resources["AppChrome"] = SystemColors.WindowBrush;
            element.Resources["AppText"] = SystemColors.WindowTextBrush;
            element.Resources["AppMuted"] = SystemColors.WindowTextBrush;
            foreach (var key in new[] { "AppSurface", "AppRaised", "AppBackground" }) element.Resources[key] = SystemColors.WindowBrush;
            element.Resources["AppLine"] = SystemColors.WindowTextBrush;
            element.Resources["AppAccent"] = SystemColors.HighlightBrush;
            element.Resources["AppGlow"] = SystemColors.HighlightBrush;
            element.Resources["AppPrimary"] = SystemColors.HighlightBrush;
            element.Resources["AppPrimaryText"] = SystemColors.HighlightTextBrush;
            element.Resources["AppWarning"] = SystemColors.WindowTextBrush;
            return;
        }
        ApplyDefinition(element, ThemeCatalog.Find(theme));
    }

    /// <summary>Puts one theme's colours into the element's resources. Also used by the theme picker to preview a theme inside a small card.</summary>
    public static void ApplyDefinition(FrameworkElement element, ThemeDefinition t)
    {
        element.Resources["AppChrome"] = Brush(t.Chrome);
        element.Resources["AppBackground"] = t.BackgroundGradient is { Count: 3 } g ? Gradient(g[0], g[1], g[2]) : Brush(t.Background);
        element.Resources["AppText"] = Brush(t.Text);
        element.Resources["AppMuted"] = Brush(t.Muted);
        element.Resources["AppSurface"] = Brush(t.Surface);
        element.Resources["AppRaised"] = Brush(t.Raised);
        element.Resources["AppLine"] = Brush(t.Line);
        element.Resources["AppAccent"] = Brush(t.Accent);
        element.Resources["AppGlow"] = Brush(t.GlowColor);
        element.Resources["AppPrimary"] = Brush(t.Primary);
        element.Resources["AppPrimaryText"] = Brush(t.PrimaryText);
        element.Resources["AppWarning"] = Brush(t.Warning);
    }

    private static LinearGradientBrush Gradient(string top, string middle, string bottom)
    {
        var brush = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop((Color)ColorConverter.ConvertFromString(top), 0),
            new GradientStop((Color)ColorConverter.ConvertFromString(middle), 0.55),
            new GradientStop((Color)ColorConverter.ConvertFromString(bottom), 1)
        }, new Point(0.2, 0), new Point(0.8, 1));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
