using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace GameVault.App;

/// <summary>
/// The short explanation of a Settings option (issue #77). It is not shown on the page: it appears when the pointer is over the row or the keyboard is on its
/// control, and screen readers get it as the control's help text. Set it on the row, or directly on one control.
/// </summary>
public static class SettingHelp
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(SettingHelp), new PropertyMetadata(null, Changed));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not string text || text.Length == 0) return;
        Apply(element, text);
        // In XAML the row's controls are added after this property is set.
        if (!element.IsInitialized) element.Initialized += (_, _) => Apply(element, text);
    }

    private static void Apply(FrameworkElement element, string text)
    {
        element.ToolTip = Tip(text);
        foreach (var control in Controls(element))
        {
            AutomationProperties.SetHelpText(control, text);
            // A control with its own tooltip keeps it; the others show the row's help when the keyboard reaches them.
            if (control.ReadLocalValue(FrameworkElement.ToolTipProperty) == DependencyProperty.UnsetValue || control.ToolTip is ToolTip) control.ToolTip = Tip(text);
        }
    }

    private static ToolTip Tip(string text) => new() { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 } };

    private static IEnumerable<Control> Controls(DependencyObject root)
    {
        if (root is System.Windows.Controls.Primitives.ButtonBase or ComboBox) yield return (Control)root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Controls(child)) yield return found;
    }
}
