using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using GameVault.Core;

namespace GameVault.App;

/// <summary>
/// Full-window page for choosing a theme. Every theme in <see cref="ThemeCatalog"/> is shown as a live miniature of the app
/// (window, side bar, cards, button, text) drawn with that theme's own colours, so it can be judged before it is used.
/// </summary>
public sealed class ThemePickerView : UserControl
{
    private readonly Func<string, Task<string?>> apply;
    private readonly WrapPanel cards = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Button closeButton = new() { Content = "Close", MinWidth = 100 };
    private string current;
    private bool busy;
    private bool changed;

    public event EventHandler<bool>? Completed;

    public ThemePickerView(string currentTheme, Func<string, Task<string?>> apply)
    {
        this.apply = apply;
        current = ThemeCatalog.Find(currentTheme).Id;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppText");
        FontSize = 14;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        ThemePalette.Apply(this, current);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Themes", FontSize = 26, FontWeight = FontWeights.SemiBold });
        var explain = new TextBlock { Text = "Each preview is drawn with that theme's own colours. Choose Use this theme and the whole app changes at once; GameVault remembers it next time.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 14) };
        explain.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        header.Children.Add(explain);
        status.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        header.Children.Add(status);
        root.Children.Add(header);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = cards };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(closeButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        closeButton.Click += (_, _) => { if (!busy) Completed?.Invoke(this, changed); };
        Rebuild();
    }

    public bool IsBusy => busy;
    public Task<bool> CanLeaveAsync() => Task.FromResult(!busy);
    public string CurrentTheme => current;
    public int CardCount => cards.Children.Count;

    private void Rebuild()
    {
        cards.Children.Clear();
        foreach (var theme in ThemeCatalog.All) cards.Children.Add(Card(theme));
    }

    private UIElement Card(ThemeDefinition theme)
    {
        var isCurrent = theme.Id == current;
        var card = new Border { Width = 320, BorderThickness = new Thickness(isCurrent ? 2 : 1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 16, 16) };
        card.SetResourceReference(Border.BorderBrushProperty, isCurrent ? "AppAccent" : "AppLine");
        var stack = new StackPanel();
        stack.Children.Add(Preview(theme));
        var name = new TextBlock { Text = theme.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) };
        stack.Children.Add(name);
        var tagline = new TextBlock { Text = theme.Tagline, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10), MinHeight = 38 };
        tagline.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        stack.Children.Add(tagline);
        var choose = new Button { Content = isCurrent ? "Current theme" : "Use this theme", IsEnabled = !isCurrent && !busy, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 140 };
        AutomationProperties.SetName(choose, (isCurrent ? "Current theme: " : "Use theme ") + theme.Name);
        choose.Click += async (_, _) => await ChooseAsync(theme);
        stack.Children.Add(choose);
        card.Child = stack;
        return card;
    }

    /// <summary>A miniature of the library drawn only with the theme's own brushes (its resources live on the preview, not on the page).</summary>
    private static Border Preview(ThemeDefinition theme)
    {
        var frame = new Border { Height = 150, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true };
        ThemePalette.ApplyDefinition(frame, theme);
        frame.SetResourceReference(Border.BackgroundProperty, "AppBackground");
        frame.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var side = new Border { Padding = new Thickness(8) };
        side.SetResourceReference(Border.BackgroundProperty, "AppChrome");
        var sideItems = new StackPanel();
        for (var i = 0; i < 4; i++)
        {
            var dot = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 8), Opacity = i == 0 ? 1 : 0.5 };
            dot.SetResourceReference(Border.BackgroundProperty, i == 0 ? "AppPrimary" : "AppMuted");
            sideItems.Children.Add(dot);
        }
        side.Child = sideItems;
        grid.Children.Add(side);
        var body = new StackPanel { Margin = new Thickness(12) };
        Grid.SetColumn(body, 1);
        var title = new Border { Height = 10, Width = 110, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        title.SetResourceReference(Border.BackgroundProperty, "AppText");
        body.Children.Add(title);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var tile = new Border { Width = 44, Height = 58, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1) };
            tile.SetResourceReference(Border.BackgroundProperty, i == 1 ? "AppRaised" : "AppSurface");
            tile.SetResourceReference(Border.BorderBrushProperty, i == 1 ? "AppAccent" : "AppLine");
            row.Children.Add(tile);
        }
        body.Children.Add(row);
        var line = new Border { Height = 6, Width = 160, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 8), Opacity = 0.8 };
        line.SetResourceReference(Border.BackgroundProperty, "AppMuted");
        body.Children.Add(line);
        var button = new Border { Height = 20, Width = 70, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left };
        button.SetResourceReference(Border.BackgroundProperty, "AppPrimary");
        var label = new TextBlock { Text = "Play", FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AppPrimaryText");
        button.Child = label;
        body.Children.Add(button);
        grid.Children.Add(body);
        frame.Child = grid;
        AutomationProperties.SetName(frame, theme.Name + " preview");
        return frame;
    }

    private async Task ChooseAsync(ThemeDefinition theme)
    {
        if (busy || theme.Id == current) return;
        busy = true;
        closeButton.IsEnabled = false;
        status.Text = "Saving…";
        try
        {
            var problem = await apply(theme.Id);
            if (problem is null)
            {
                current = theme.Id;
                changed = true;
                ThemePalette.Apply(this, current);
                status.Text = theme.Name + " is now your theme.";
            }
            else status.Text = problem;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { status.Text = "Could not save the theme. " + error.Message; }
        finally
        {
            busy = false;
            closeButton.IsEnabled = true;
            Rebuild();
        }
    }
}
