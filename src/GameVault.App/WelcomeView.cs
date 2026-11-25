using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

/// <summary>
/// The first-run welcome: a few clear ways to get the first games in. Shown inline in an empty library (with "Skip for now")
/// and, from the command palette, as a drawer (with "Close"). Every colour comes from the active theme.
/// </summary>
public sealed class WelcomeView : UserControl
{
    private readonly List<Button> buttons = [];

    public event EventHandler<WelcomeAction>? ActionChosen;
    public event EventHandler<bool>? Completed;

    public WelcomeView(string theme, bool embedded)
    {
        Embedded = embedded;
        SetResourceReference(ForegroundProperty, "AppText");
        FontSize = 14;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
        if (!embedded)
        {
            SetResourceReference(BackgroundProperty, "AppBackground");
            ThemePalette.Apply(this, theme);
        }
        var root = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(embedded ? 0 : 8) };
        root.Children.Add(new TextBlock { Text = "Welcome to GameVault", FontSize = 28, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var intro = new TextBlock
        {
            Text = "Your games, saves and notes live on this PC. Start by adding the games you play. You can always do more later.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20)
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        root.Children.Add(intro);
        root.Children.Add(Row("", "Add a game", "Browse to a game's .exe and give it a name and cover.", "Add a game", WelcomeAction.AddGame, primary: true));
        root.Children.Add(Row("", "Import from Steam, Epic or GOG", "Find games that are already installed through a store and add them in one go.", "Choose from my stores", WelcomeAction.ImportFromStores));
        root.Children.Add(Row("", "Choose a theme", "Preview colour themes, from calm navy to your favourite game's look.", "Browse themes", WelcomeAction.ChooseTheme));
        root.Children.Add(Row("", "Get save locations (optional)", "Download a list of where thousands of games keep their saves, about 18 MB. It is used offline afterwards.", "Download the list", WelcomeAction.DownloadSaveDatabase));
        root.Children.Add(Row("", "Used GameVault before?", "Bring your games, playtime and save backups from an old GameVault folder. The old folder is not changed.", "Choose the old folder", WelcomeAction.ImportOldFolder));
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var close = new Button { Content = embedded ? "Skip for now" : "Close", MinWidth = 120 };
        AutomationProperties.SetName(close, embedded ? "Skip the welcome" : "Close the welcome");
        close.Click += (_, _) =>
        {
            if (embedded) ActionChosen?.Invoke(this, WelcomeAction.Skip);
            else Completed?.Invoke(this, false);
        };
        buttons.Add(close);
        footer.Children.Add(close);
        root.Children.Add(footer);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = root };
    }

    public bool Embedded { get; }
    public IReadOnlyList<Button> Buttons => buttons;
    public Task<bool> CanLeaveAsync() => Task.FromResult(true);

    private Border Row(string glyph, string title, string description, string buttonText, WelcomeAction action, bool primary = false)
    {
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
        card.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        card.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        grid.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(4, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var button = new Button { Content = buttonText, MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) =>
        {
            ActionChosen?.Invoke(this, action);
            if (!Embedded) Completed?.Invoke(this, true);
        };
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        buttons.Add(button);
        card.Child = grid;
        return card;
    }
}
