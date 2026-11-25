using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GameVault.Core;

namespace GameVault.App;

/// <summary>A scrolling grid of capture tiles. It is a virtualised list of rows, so thousands of captures only create the tiles that are visible.</summary>
public sealed class CaptureGallery : ListBox
{
    private CaptureCard[] cards = [];
    private int columns = 1;

    public CaptureGallery()
    {
        MaxHeight = 620;
        BorderThickness = new Thickness(0);
        Background = System.Windows.Media.Brushes.Transparent;
        Focusable = false;
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        ScrollViewer.SetCanContentScroll(this, true);
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);

        // Each row is a horizontal strip of tiles.
        var strip = new FrameworkElementFactory(typeof(StackPanel));
        strip.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var row = new FrameworkElementFactory(typeof(ItemsControl));
        row.SetBinding(ItemsSourceProperty, new Binding());
        row.SetValue(FocusableProperty, false);
        row.SetValue(ItemsPanelProperty, new ItemsPanelTemplate(strip));
        row.SetValue(ItemTemplateProperty, new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(CaptureTile)) });
        ItemTemplate = new DataTemplate { VisualTree = row };

        // Rows are containers only: no selection highlight and no tab stop of their own.
        var container = new Style(typeof(ListBoxItem));
        container.Setters.Add(new Setter(FocusableProperty, false));
        container.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }));
        ItemContainerStyle = container;
        SizeChanged += (_, e) => { if (e.WidthChanged) Layout(CaptureGalleryRules.Columns(e.NewSize.Width - SystemParameters.VerticalScrollBarWidth), false); };
    }

    internal IReadOnlyList<CaptureCard> Cards => cards;

    public void Show(IEnumerable<CaptureCard> items)
    {
        cards = [.. items];
        Layout(columns, true);
        Visibility = cards.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void Layout(int count, bool force)
    {
        if (!force && count == columns) return;
        columns = count;
        ItemsSource = CaptureGalleryRules.Rows(cards, columns);
    }
}
