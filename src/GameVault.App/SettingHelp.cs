using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace GameVault.App;

/// <summary>
/// A short explanation that is not written on the page (issues #77 and #102): it appears when the pointer is over the item or the keyboard is on its
/// control, and screen readers get it as help text. Set it on a row, a heading, or directly on one control.
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
        AutomationProperties.SetHelpText(element, text);
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
        if (root is System.Windows.Controls.Primitives.ButtonBase or ComboBox or System.Windows.Controls.Primitives.TextBoxBase) yield return (Control)root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Controls(child)) yield return found;
    }
}
