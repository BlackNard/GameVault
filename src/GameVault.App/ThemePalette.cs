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
            return;
        }
        var crimson = theme == "Crimson";
        element.Resources["AppBackground"] = Brush(crimson ? "#120e11" : "#131b26");
        element.Resources["AppText"] = Brush(crimson ? "#f8eef2" : "#edf2f8");
        element.Resources["AppMuted"] = Brush(crimson ? "#d0bbc5" : "#b5c3d6");
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
