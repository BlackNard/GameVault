using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using GameVault.Core;
using GameVault.Infrastructure.Metadata;

namespace GameVault.App;

/// <summary>"Not the right game?": lists the games Steam, GOG and Wikidata know for a title so the owner can choose the correct one (or say none fits).</summary>
public sealed class MatchPickerView : UserControl
{
    private readonly Func<string, CancellationToken, Task<GameMetadataService.MatchSearch>> search;
    private readonly Func<MatchCandidate?, Task<string>> apply;
    private readonly TextBox query = new() { MinWidth = 320, MaxLength = 120 };
    private readonly Button searchButton = new() { Content = "Search", Margin = new Thickness(8, 0, 0, 0), MinWidth = 90 };
    private readonly StackPanel results = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Button noneButton = new() { Content = "None of these match", Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button closeButton = new() { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
    private readonly CancellationTokenSource cancel = new();
    private bool busy;

    public event EventHandler<bool>? Completed;
    public bool IsBusy => busy;

    public MatchPickerView(string theme, string gameName, Func<string, CancellationToken, Task<GameMetadataService.MatchSearch>> search, Func<MatchCandidate?, Task<string>> apply)
    {
        this.search = search; this.apply = apply;
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
        var title = new TextBlock { Text = "Find the right game", FontSize = 26, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        SettingHelp.SetText(title, "Search the stores and Wikidata by title, then choose the game you actually have. GameVault remembers your choice and never changes it on its own. Anything you wrote yourself stays.");
        header.Children.Add(title);
        var bar = new DockPanel();
        DockPanel.SetDock(searchButton, Dock.Right);
        bar.Children.Add(searchButton);
        bar.Children.Add(query);
        header.Children.Add(bar);
        status.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        header.Children.Add(status);
        root.Children.Add(header);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = results };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(noneButton);
        footer.Children.Add(closeButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        AutomationProperties.SetName(query, "Game title to search");
        query.Text = gameName;
        searchButton.Click += async (_, _) => await SearchAsync();
        query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        noneButton.Click += async (_, _) => await ApplyAsync(null);
        closeButton.Click += (_, _) => { if (!busy) Completed?.Invoke(this, false); };
        Loaded += async (_, _) => { query.Focus(); await SearchAsync(); };
        Unloaded += (_, _) => cancel.Cancel();
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(!busy);

    private void SetBusy(bool value)
    {
        busy = value;
        searchButton.IsEnabled = noneButton.IsEnabled = closeButton.IsEnabled = !value;
        foreach (var button in results.Children.OfType<Border>().Select(b => b.Tag).OfType<Button>()) button.IsEnabled = !value;
    }

    private async Task SearchAsync()
    {
        if (busy) return;
        var title = query.Text.Trim();
        SetBusy(true);
        results.Children.Clear();
        status.Text = "Searching…";
        try
        {
            var found = await search(title, cancel.Token);
            foreach (var candidate in found.Candidates) AddRow(candidate);
            status.Text = found.Candidates.Count == 0
                ? "Nothing found for that title. Try fewer words or another spelling." + (found.Problems.Length > 0 ? " " + string.Join(" ", found.Problems) : "")
                : $"{found.Candidates.Count} possible game(s). Pick the one you have." + (found.Problems.Length > 0 ? " Some sources did not answer: " + string.Join(" ", found.Problems) : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidOperationException) { status.Text = "The search failed. " + error.Message; }
        finally { if (!cancel.IsCancellationRequested) SetBusy(false); }
    }

    private void AddRow(MatchCandidate candidate)
    {
        var panel = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
        panel.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        var row = new DockPanel();
        var choose = new Button { Content = "Use this game", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(choose, "Use " + candidate.Title + " (" + candidate.Detail + ")");
        choose.Click += async (_, _) => await ApplyAsync(candidate);
        DockPanel.SetDock(choose, Dock.Right);
        row.Children.Add(choose);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = candidate.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock { Text = candidate.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        text.Children.Add(detail);
        row.Children.Add(text);
        panel.Child = row;
        panel.Tag = choose;
        results.Children.Add(panel);
    }

    private async Task ApplyAsync(MatchCandidate? candidate)
    {
        if (busy) return;
        SetBusy(true);
        status.Text = candidate is null ? "Saving your choice…" : "Looking up " + candidate.Title + "…";
        try
        {
            var message = await apply(candidate);
            status.Text = message;
            Completed?.Invoke(this, true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            status.Text = "Could not save your choice. " + error.Message;
            SetBusy(false);
        }
    }
}
