using System.Windows;
using System.Windows.Media;

namespace GameVault.App;

internal static class ThemePalette
{
    public static void Apply(FrameworkElement element, string theme)
    {
        if (SystemParameters.HighContrast)
        {
            element.Resources["AppBackground"] = SystemColors.WindowBrush;
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
        var crimson = theme == "Crimson";
        element.Resources["AppBackground"] = Brush(crimson ? "#120e11" : "#131b26");
        element.Resources["AppText"] = Brush(crimson ? "#f8eef2" : "#edf2f8");
        element.Resources["AppMuted"] = Brush(crimson ? "#d0bbc5" : "#b5c3d6");
        element.Resources["AppSurface"] = Brush(crimson ? "#21171d" : "#1c2838");
        element.Resources["AppRaised"] = Brush(crimson ? "#35232c" : "#26364a");
        element.Resources["AppLine"] = Brush(crimson ? "#63404f" : "#48607d");
        element.Resources["AppAccent"] = Brush(crimson ? "#ffb2c0" : "#b6ceff");
        element.Resources["AppPrimary"] = Brush(crimson ? "#b62b43" : "#b6ceff");
        element.Resources["AppPrimaryText"] = Brush(crimson ? "#fff6f8" : "#131b26");
        element.Resources["AppWarning"] = Brush(crimson ? "#ffc5aa" : "#ffc6ab");
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
