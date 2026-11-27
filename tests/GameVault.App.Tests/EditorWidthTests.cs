using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;

namespace GameVault.App.Tests;

/// <summary>
/// A form keeps its width whatever text it holds. A centred form is only as wide as its widest content, so removing the long explanations (issue #102)
/// once left the game editor's fields narrow.
/// </summary>
public sealed class EditorWidthTests
{
    private static string Source(string file, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GameVault.App", file));

    [Theory]
    [InlineData(1200, 800)]
    [InlineData(700, 600)]
    public void TheGameEditorsFieldsFillTheFormUpToItsWidestSize(int window, int expectedAtLeast) => OnSta(() =>
    {
        var document = XDocument.Parse(Source("GameEditor.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var element in document.Descendants())
            element.Attributes().Where(a => a.Name == x + "Class" || a.Name.LocalName is "Click" or "TextChanged").Remove();
        var xaml = document.ToString().Replace("clr-namespace:GameVault.App\"", "clr-namespace:GameVault.App;assembly=GameVault\"");
        xaml = Regex.Replace(xaml, @"\{StaticResource PrimaryButton\}", "{DynamicResource PrimaryButton}");
        var view = (FrameworkElement)XamlReader.Parse(xaml);
        view.Measure(new Size(window, 760)); view.Arrange(new Rect(0, 0, window, 760)); view.UpdateLayout();

        var name = (TextBox)view.FindName("GameName");
        var scroll = (ScrollViewer)view.FindName("EditorScroll");
        Assert.InRange(name.ActualWidth, expectedAtLeast - 40, 820);
        Assert.InRange(scroll.ActualWidth, expectedAtLeast, 820);
    });

    /// <summary>The same for the tags editor on the game page, which is aligned to the left.</summary>
    [Fact]
    public void TheTagsEditorHasItsOwnWidth()
    {
        var editor = XDocument.Parse(Source("MainWindow.xaml")).Descendants()
            .Single(e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "TagsEditor"));
        Assert.True(int.Parse((string)editor.Attribute("MinWidth")!) >= 420);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
