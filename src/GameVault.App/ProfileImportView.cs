using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

/// <summary>Lists the games found in an old GameVault folder and copies the chosen ones into this library (issue #53). The old folder is only read.</summary>
public sealed class ProfileImportView : UserControl
{
    private readonly Func<IReadOnlyCollection<Guid>, Task<ImportResult>> import;
    private readonly List<(ImportCandidate Game, CheckBox Select)> rows = [];
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Button importButton = new() { Content = "Import selected games", MinWidth = 170, IsEnabled = false };
    private readonly Button cancelButton = new() { Content = "Close", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
    private bool busy;

    public event EventHandler<bool>? Completed;
    public List<Guid> ImportedIds { get; } = [];

    public ProfileImportView(string theme, ImportScan scan, Func<IReadOnlyCollection<Guid>, Task<ImportResult>> import)
    {
        this.import = import;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppText");
        FontSize = 14;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
        ThemePalette.Apply(this, theme);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Import from an old GameVault folder", FontSize = 26, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var ready = scan.Candidates.Count(c => c.Status == ImportStatus.Ready);
        var summary = Muted(scan.Candidates.Length == 0
            ? $"No game profiles were found in {scan.ProfilesFolder}."
            : $"{scan.Candidates.Length} game(s) found in {scan.ProfilesFolder}, {ready} can be imported. The old folder is not changed, and games you already have are not overwritten. Settings are not imported.");
        summary.Margin = new Thickness(0, 8, 0, 16);
        header.Children.Add(summary);
        root.Children.Add(header);
        var list = new StackPanel();
        foreach (var candidate in scan.Candidates) list.Children.Add(Row(candidate));
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        notice.SetResourceReference(TextBlock.ForegroundProperty, "AppWarning");
        footer.Children.Add(notice);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancelButton);
        importButton.SetResourceReference(StyleProperty, "PrimaryButton");
        buttons.Children.Add(importButton);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        cancelButton.Click += (_, _) => { if (!busy) Completed?.Invoke(this, ImportedIds.Count > 0); };
        importButton.Click += async (_, _) => await ImportSelectedAsync();
        Update();
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(!busy);

    internal IReadOnlyList<CheckBox> Choices => [.. rows.Select(r => r.Select)];
    internal Button ImportButton => importButton;

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        return block;
    }

    private static string Describe(ImportCandidate game)
    {
        var played = game.PlayedSeconds >= 3600 ? $"{game.PlayedSeconds / 3600.0:0.#} h played" : $"{game.PlayedSeconds / 60} min played";
        var size = game.Bytes >= 1L << 30 ? $"{game.Bytes / (double)(1L << 30):0.#} GB" : $"{Math.Max(1, game.Bytes >> 20)} MB";
        return $"{played} · {game.Sessions} session(s) · {game.Backups} save backup(s) · {size}";
    }

    private Border Row(ImportCandidate game)
    {
        var panel = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
        panel.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        var ready = game.Status == ImportStatus.Ready;
        var select = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 12, 0), IsEnabled = ready, IsChecked = ready };
        AutomationProperties.SetName(select, "Import " + game.Name);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = game.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (game.ExecutablePath is not null) text.Children.Add(Muted(Describe(game)));
        if (game.Reason is not null) text.Children.Add(Muted(game.Reason));
        else if (!game.ExecutableExists)
        {
            var missing = new TextBlock { Text = $"The game program was not found at {game.ExecutablePath}. You can import it and change the path with Edit game.", TextWrapping = TextWrapping.Wrap };
            missing.SetResourceReference(TextBlock.ForegroundProperty, "AppWarning");
            text.Children.Add(missing);
        }
        var layout = new DockPanel();
        DockPanel.SetDock(select, Dock.Left);
        layout.Children.Add(select);
        layout.Children.Add(text);
        panel.Child = layout;
        select.Checked += (_, _) => Update();
        select.Unchecked += (_, _) => Update();
        rows.Add((game, select));
        return panel;
    }

    private Guid[] Chosen() => [.. rows.Where(r => r.Select is { IsChecked: true, IsEnabled: true } && r.Game.Id is not null).Select(r => r.Game.Id!.Value)];

    private void Update() => importButton.IsEnabled = !busy && Chosen().Length > 0;

    private async Task ImportSelectedAsync()
    {
        var chosen = Chosen();
        if (busy || chosen.Length == 0) return;
        busy = true; importButton.IsEnabled = cancelButton.IsEnabled = false; notice.Text = "Copying… large save backups can take a while.";
        try
        {
            var result = await import(chosen);
            ImportedIds.AddRange(result.Imported);
            foreach (var row in rows.Where(r => r.Game.Id is { } id && result.Imported.Contains(id))) { row.Select.IsChecked = false; row.Select.IsEnabled = false; }
            notice.Text = string.Join("\n", result.Failures);
            busy = false;
            if (result.Failures.Length == 0) Completed?.Invoke(this, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { notice.Text = "The games could not be imported. " + error.Message; }
        finally { busy = false; cancelButton.IsEnabled = true; Update(); }
    }
}
