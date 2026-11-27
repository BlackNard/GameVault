using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GameVault.App.Tests;

/// <summary>Saving a form must never crash the app (found in manual testing: saving a new list closed GameVault).</summary>
public sealed class FormSaveTests
{
    private static string Source(string file, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GameVault.App", file));

    /// <summary>The change given to the store runs on a background thread, where reading a control throws.</summary>
    [Fact]
    public void ListChangesNeverReadAControlOnTheStoresThread()
    {
        var code = Source("MainWindow.Lists.cs");
        var calls = 0;
        for (var at = code.IndexOf("ChangeListsAsync(", StringComparison.Ordinal); at >= 0; at = code.IndexOf("ChangeListsAsync(", at + 1, StringComparison.Ordinal))
        {
            var open = at + "ChangeListsAsync".Length;
            int depth = 0, end = open;
            for (; end < code.Length; end++)
            {
                if (code[end] == '(') depth++;
                else if (code[end] == ')' && --depth == 0) break;
            }
            var argument = code[open..end];
            calls++;
            Assert.False(Regex.IsMatch(argument, @"\.(Text|IsChecked|SelectedItem|SelectedIndex|Value)\b|FormView\."), "A control is read inside the store change: " + argument);
        }
        Assert.True(calls >= 5);
    }

    [Fact]
    public void AnUnexpectedSaveErrorIsShownInTheForm() => OnSta(() =>
    {
        var form = new FormView("Midnight", "Test", () => throw new InvalidOperationException("The calling thread cannot access this object."));
        static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T hit) yield return hit;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var found in All<T>(child)) yield return found;
        }
        var completed = false;
        form.Completed += (_, _) => completed = true;
        All<Button>(form).Single(b => Equals(b.Content, "Save")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(completed);
        Assert.Contains(All<TextBlock>(form), t => t.Text.Contains("cannot access"));
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
