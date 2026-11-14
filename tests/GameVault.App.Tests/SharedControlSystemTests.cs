using System.IO;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GameVault.App;

namespace GameVault.App.Tests;

public sealed class SharedControlSystemTests
{
    [Fact]
    public void Compiled_templates_apply_without_opening_any_windows()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var resources = (ResourceDictionary)Application.LoadComponent(new Uri("/GameVault;component/Styles.xaml", UriKind.Relative));
                Control[] controls = [new Button(), new TextBox(), new ComboBox(), new ComboBoxItem(), new CheckBox(), new RadioButton(), new Expander(), new ScrollBar(), new TabControl(), new TabItem(), new ListBox(), new ListBoxItem(), new ContextMenu(), new MenuItem()];
                foreach (var control in controls)
                {
                    ThemePalette.Apply(control, "Midnight");
                    control.Resources.MergedDictionaries.Add(resources);
                    control.Style = (Style)resources[control.GetType()];
                    control.ApplyTemplate();
                    control.Measure(new Size(600, 400));
                    Assert.NotNull(control.Template);
                    control.IsEnabled = false;
                    control.Measure(new Size(600, 400));
                    Assert.True(control.Opacity > 0);
                }
                Assert.Equal(MessageBoxResult.Cancel, InlinePrompts.ShowAsync(new Button(), "Detached", "Check", MessageBoxButton.YesNo).GetAwaiter().GetResult());
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Headless template loading timed out.");
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(MessageBoxButton.OK, MessageBoxResult.OK)]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxResult.No)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public void Prompt_choices_end_with_the_safe_default(MessageBoxButton buttons, MessageBoxResult expected)
    {
        Assert.Equal(expected, InlinePrompts.Choices(buttons)[^1]);
    }
    private static string Source(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "GameVault.App"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "GameVault.App", name));
    }

    [Fact]
    public void Every_interactive_control_has_an_explicit_themed_template()
    {
        var document = XDocument.Parse(Source("Styles.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        foreach (var type in new[] { "Button", "TextBox", "ComboBox", "ComboBoxItem", "CheckBox", "RadioButton", "Expander", "ScrollBar", "TabControl", "TabItem", "ListBox", "ListBoxItem", "ContextMenu", "MenuItem" })
        {
            var style = document.Root!.Elements(ns + "Style").Single(e => (string?)e.Attribute("TargetType") == type && e.Attributes().All(a => a.Name.LocalName != "Key"));
            Assert.Contains(style.Elements(ns + "Setter"), e => (string?)e.Attribute("Property") == "Template");
        }
        Assert.DoesNotContain("#edf2f8", Source("Styles.xaml"));
    }

    [Fact]
    public void Inline_prompts_have_no_separate_or_native_window_boundary()
    {
        var source = Source("InlinePrompts.cs");
        Assert.DoesNotContain("new Window", source);
        Assert.DoesNotContain("MessageBox.Show", source);
        Assert.Contains("ConditionalWeakTable", source);
        Assert.Contains("SemaphoreSlim", source);
        Assert.Contains("KeyboardNavigationMode.Cycle", source);
    }
}
