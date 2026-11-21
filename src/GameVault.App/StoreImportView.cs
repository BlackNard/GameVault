using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

/// <summary>Lists games installed through Steam and Epic and adds the chosen ones in one step (ticket #85). Local files only: no sign-in, no web calls.</summary>
public sealed class StoreImportView : UserControl
{
    private sealed class Row
    {
        public required InstalledStoreGame Game;
        public required StoreGameStatus Status;
        public Guid? ExistingId;
        public required CheckBox Select;
        public required ComboBox Executable;
        public required Border Panel;
    }

    private readonly StoreLibraryScanner scanner;
    private readonly Func<IReadOnlyList<LibraryExecutable>> libraryExecutables;
    private readonly Func<GameDraft, Task<GameSaveResult>> add;
    private readonly Func<Guid, Task<GameSaveResult>> restore;
    private readonly List<Row> rows = [];
    private readonly StackPanel list = new();
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) };
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Button addButton = new() { Content = "Add selected games", MinWidth = 150, IsEnabled = false };
    private readonly Button cancelButton = new() { Content = "Close", Margin = new Thickness(0, 0, 8, 0) };
    private bool busy;
    private bool dirty;

    public event EventHandler<bool>? Completed;
    public bool IsBusy => busy;
    public List<Guid> ChangedGameIds { get; } = [];

    public StoreImportView(string theme, StoreLibraryScanner scanner, Func<IReadOnlyList<LibraryExecutable>> libraryExecutables,
        Func<GameDraft, Task<GameSaveResult>> add, Func<Guid, Task<GameSaveResult>> restore)
    {
        this.scanner = scanner; this.libraryExecutables = libraryExecutables; this.add = add; this.restore = restore;
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
        header.Children.Add(new TextBlock { Text = "Add installed games", FontSize = 26, FontWeight = FontWeights.SemiBold });
        header.Children.Add(summary);
        root.Children.Add(header);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(notice);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancelButton);
        addButton.SetResourceReference(StyleProperty, "PrimaryButton");
        buttons.Children.Add(addButton);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        notice.SetResourceReference(TextBlock.ForegroundProperty, "AppWarning");
        summary.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        cancelButton.Click += async (_, _) => { if (await CanLeaveAsync()) Completed?.Invoke(this, ChangedGameIds.Count > 0); };
        addButton.Click += async (_, _) => await AddSelectedAsync();
        summary.Text = "Looking for installed Steam and Epic games…";
        Loaded += async (_, _) => await ScanAsync();
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(!busy);

    private async Task ScanAsync()
    {
        try
        {
            var result = await Task.Run(scanner.Scan);
            var existing = libraryExecutables();
            foreach (var game in result.Games)
            {
                var (status, id) = StoreLibraryScanner.Classify(game, existing);
                AddRow(game, status, id);
            }
            var fresh = rows.Count(r => r.Status == StoreGameStatus.New);
            summary.Text = rows.Count == 0
                ? "No installed Steam or Epic games were found. Steam and Epic must be installed on this PC; games added by hand can still use Add game."
                : $"{rows.Count} installed game(s) found, {fresh} not yet in your library. Check the program for each game, then add them in one step.";
            if (result.Warnings.Length > 0) notice.Text = string.Join(" ", result.Warnings);
            UpdateAddButton();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            summary.Text = "The installed games could not be read. " + error.Message;
        }
    }

    private void AddRow(InstalledStoreGame game, StoreGameStatus status, Guid? existingId)
    {
        var panel = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
        panel.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        var select = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 12, 0), IsEnabled = status != StoreGameStatus.InLibrary };
        AutomationProperties.SetName(select, "Select " + game.Name);
        var combo = new ComboBox { IsEditable = true, ItemsSource = game.Candidates, Text = game.ExecutablePath ?? "", IsEnabled = status == StoreGameStatus.New, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(combo, "Executable for " + game.Name);
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 6, 0, 0), IsEnabled = status == StoreGameStatus.New };
        AutomationProperties.SetName(browse, "Browse executable for " + game.Name);
        var row = new Row { Game = game, Status = status, ExistingId = existingId, Select = select, Executable = combo, Panel = panel };
        var size = game.SizeBytes is long bytes ? " · " + (bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB" : $"{Math.Max(1, bytes >> 20)} MB") : "";
        var state = status switch { StoreGameStatus.InLibrary => " · already in your library", StoreGameStatus.Removed => " · removed earlier, can be restored", _ => "" };
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = game.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock { Text = $"{game.Provider}{size}{state}", Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        text.Children.Add(detail);
        if (status == StoreGameStatus.New)
        {
            var fields = new DockPanel();
            DockPanel.SetDock(browse, Dock.Right);
            fields.Children.Add(browse);
            fields.Children.Add(combo);
            text.Children.Add(fields);
            if (game.ExecutablePath is null)
            {
                var hint = new TextBlock { Text = "No game program was found automatically. Browse to its .exe.", Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "AppWarning");
                text.Children.Add(hint);
            }
        }
        var layout = new DockPanel();
        DockPanel.SetDock(select, Dock.Left);
        layout.Children.Add(select);
        layout.Children.Add(text);
        panel.Child = layout;
        browse.Click += (_, _) =>
        {
            try
            {
                var path = NativePickers.File(Window.GetWindow(this)!, "Choose the game executable", "Windows executables (*.exe)|*.exe", combo.Text);
                if (path is not null) { combo.Text = path; UpdateAddButton(); }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            { notice.Text = "The browser could not open. Paste a full path instead. " + error.Message; }
        };
        select.Checked += (_, _) => UpdateAddButton();
        select.Unchecked += (_, _) => UpdateAddButton();
        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateAddButton()));
        combo.SelectionChanged += (_, _) => UpdateAddButton();
        rows.Add(row);
        list.Children.Add(panel);
    }

    private static string? ExecutableOf(Row row)
    {
        var text = (row.Executable.Text ?? "").Trim().Trim('"');
        return text.Length > 0 && Path.IsPathFullyQualified(text) && text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(text) ? Path.GetFullPath(text) : null;
    }

    private bool Ready(Row row) => row.Select.IsChecked == true && row.Status != StoreGameStatus.InLibrary && (row.Status == StoreGameStatus.Removed || ExecutableOf(row) is not null);

    private void UpdateAddButton() => addButton.IsEnabled = !busy && rows.Any(Ready);

    private async Task AddSelectedAsync()
    {
        if (busy) return;
        busy = true; addButton.IsEnabled = cancelButton.IsEnabled = false; notice.Text = "";
        var failures = new List<string>();
        try
        {
            foreach (var row in rows.Where(Ready).ToArray())
            {
                try
                {
                    GameSaveResult result;
                    if (row.Status == StoreGameStatus.Removed) result = await restore(row.ExistingId!.Value);
                    else
                    {
                        var exe = ExecutableOf(row)!;
                        result = await add(new GameDraft(null, row.Game.Name, exe, Path.GetDirectoryName(exe)!, []));
                    }
                    ChangedGameIds.Add(result.Profile.Id);
                    dirty = true;
                    row.Select.IsChecked = false;
                    row.Select.IsEnabled = false;
                    row.Executable.IsEnabled = false;
                    row.Panel.Opacity = 0.6;
                    ((TextBlock)((StackPanel)((DockPanel)row.Panel.Child).Children[1]).Children[1]).Text += " · added";
                }
                catch (DuplicateGameException duplicate)
                { failures.Add($"{row.Game.Name}: {(duplicate.IsArchived ? "it was removed earlier; restore it instead" : "already in your library")}."); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                { failures.Add($"{row.Game.Name}: {error.Message}"); }
            }
        }
        finally
        {
            busy = false; cancelButton.IsEnabled = true; UpdateAddButton();
        }
        if (failures.Count > 0) notice.Text = string.Join("\n", failures);
        else if (dirty) Completed?.Invoke(this, true);
    }
}
