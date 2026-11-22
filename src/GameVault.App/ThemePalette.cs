using System.Windows;
using System.Windows.Media;

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
            element.Resources["AppPrimary"] = SystemColors.HighlightBrush;
            element.Resources["AppPrimaryText"] = SystemColors.HighlightTextBrush;
            element.Resources["AppWarning"] = SystemColors.WindowTextBrush;
            return;
        }
        if (theme == "Vice City")
        {
            // Neon dusk: deep purple fading to magenta, with hot-pink and sunset-orange accents.
            element.Resources["AppChrome"] = Brush("#140828");
            element.Resources["AppBackground"] = Gradient("#150a2e", "#2c1050", "#5c1a62");
            element.Resources["AppText"] = Brush("#fff4fb");
            element.Resources["AppMuted"] = Brush("#dcc0ea");
            element.Resources["AppSurface"] = Brush("#26103f");
            element.Resources["AppRaised"] = Brush("#3b1a5c");
            element.Resources["AppLine"] = Brush("#8a43a6");
            element.Resources["AppAccent"] = Brush("#ffb36b");
            element.Resources["AppPrimary"] = Brush("#ff4f9f");
            element.Resources["AppPrimaryText"] = Brush("#1a0630");
            element.Resources["AppWarning"] = Brush("#ffcf8a");
            return;
        }
        var crimson = theme == "Crimson";
        element.Resources["AppChrome"] = Brush(crimson ? "#160f14" : "#111822" );
        element.Resources["AppBackground"] = Brush(crimson ? "#120e11" : "#131b26");
        element.Resources["AppText"] = Brush(crimson ? "#f8eef2" : "#edf2f8");
        element.Resources["AppMuted"] = Brush(crimson ? "#d0bbc5" : "#adbdcf");
        element.Resources["AppSurface"] = Brush(crimson ? "#21171d" : "#1c2838");
        element.Resources["AppRaised"] = Brush(crimson ? "#35232c" : "#26364a");
        element.Resources["AppLine"] = Brush(crimson ? "#63404f" : "#48607d");
        element.Resources["AppAccent"] = Brush(crimson ? "#ffb2c0" : "#b6ceff");
        element.Resources["AppPrimary"] = Brush(crimson ? "#cb3c54" : "#b6ceff");
        element.Resources["AppPrimaryText"] = Brush(crimson ? "#fff6f8" : "#131b26");
        element.Resources["AppWarning"] = Brush(crimson ? "#ffc5aa" : "#ffc6ab");
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

    /// <summary>Position of a saved theme name in the theme picker (Midnight, Crimson, Vice City).</summary>
    public static int IndexOf(string theme) => theme switch { "Crimson" => 1, "Vice City" => 2, _ => 0 };

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
