using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App.Tests;

/// <summary>Reordering list cards by dragging (issue #97), driven headlessly: nothing is shown.</summary>
public sealed class ListCardDragTests
{
    [Theory]
    [InlineData(true, "B C A")]
    [InlineData(false, "A B C")]
    public void TheOtherCardsMakeRoomAndTheDropIsSaved(bool drop, string expected) => OnSta(() =>
    {
        var entries = new[] { "A", "B", "C" }.Select(n => new ListEntry(Guid.NewGuid(), null, n, null)).ToArray();
        var list = new GameList(Guid.NewGuid(), "Ranked", "", true, entries);
        var document = new ListsDocument(1, [list], []);
        var view = new ListsView();
        var changes = 0;
        view.ChangeRequested += (_, change) => { changes++; document = change(document); view.Show(document, []); };
        view.Select(list.Id);
        view.Show(document, []);
        Layout(view);

        var cards = Cards(view);
        var grid = (Panel)cards[0].Parent;
        var third = cards[2].TranslatePoint(new Point(20, 20), grid);
        Assert.True((bool)Call(view, "BeginDrag", cards[0], new Point(10, 10), new Point(40, 40))!);
        Call(view, "DragTo", third, new Point(300, 80));
        // While held, the grid already shows where the card will land, numbered as it will be.
        Assert.Equal(["B", "C", "A"], grid.Children.OfType<Button>().Select(AutomationProperties.GetName));
        view.Show(document, []);                                  // a lookup arriving meanwhile does not rebuild the page
        Assert.Same(cards[0], Cards(view).Single(c => AutomationProperties.GetName(c) == "A"));

        Call(view, "EndDrag", drop);
        Assert.Equal(drop ? 1 : 0, changes);
        Assert.Equal(expected, string.Join(' ', document.Lists[0].Entries.Select(e => e.Title)));
        Layout(view);
        Assert.Equal(expected.Split(' '), Cards(view).Select(AutomationProperties.GetName));
        Assert.All(Cards(view), c => Assert.Equal(1, c.Opacity));
    });

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(900, 700)); view.Arrange(new Rect(0, 0, 900, 700)); view.UpdateLayout();
    }

    private static Button[] Cards(DependencyObject root) => [.. All<Button>(root).Where(b => AutomationProperties.GetName(b) is "A" or "B" or "C")];

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in All<T>(child)) yield return found;
    }

    private static object? Call(ListsView view, string name, params object[] arguments) =>
        typeof(ListsView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, arguments);

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
