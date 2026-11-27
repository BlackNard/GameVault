using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace GameVault.App;

/// <summary>
/// A small form in the app's own window, used for playthroughs, journal entries and lists: labelled fields, Save, an optional Delete, and a question before
/// unsaved typing is thrown away. <c>save</c> throws <see cref="ArgumentException"/> or <see cref="IOException"/> with words for the player.
/// </summary>
public sealed class FormView : UserControl
{
    private readonly StackPanel fields = new() { Width = 560, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Button saveButton = new() { Content = "Save", MinWidth = 110 };
    private readonly Button cancelButton = new() { Content = "Cancel", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button deleteButton = new() { Content = "Delete", MinWidth = 100, Visibility = Visibility.Collapsed };
    private readonly List<Func<string>> values = [];
    private InlineDraftGuard? guard;
    private bool busy;

    public event EventHandler<bool>? Completed;

    public FormView(string theme, string title, Func<Task> save, Func<Task>? delete = null)
    {
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppText");
        FontSize = 14;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
        ThemePalette.Apply(this, theme);
        var root = new DockPanel();
        var heading = new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(cancelButton);
        saveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        buttons.Children.Add(saveButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        deleteButton.HorizontalAlignment = HorizontalAlignment.Left;
        footer.Children.Add(deleteButton);
        notice.SetResourceReference(TextBlock.ForegroundProperty, "AppWarning");
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        var bottom = new StackPanel();
        bottom.Children.Add(notice);
        bottom.Children.Add(footer);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
        cancelButton.Click += async (_, _) => { if (await CanLeaveAsync()) Completed?.Invoke(this, false); };
        saveButton.Click += async (_, _) => await RunAsync(save);
        if (delete is not null)
        {
            deleteButton.Visibility = Visibility.Visible;
            deleteButton.Click += async (_, _) =>
            {
                if (await InlinePrompts.ShowAsync(this, "Delete this? It cannot be undone.", "Delete", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes) await RunAsync(delete);
            };
        }
        // What the form holds once it is shown is "unchanged".
        Loaded += (_, _) => guard ??= new(Snapshot());
    }

    public Task<bool> CanLeaveAsync() => guard is null ? Task.FromResult(!busy) : guard.CanLeaveAsync(busy, Snapshot(), async () =>
        await InlinePrompts.ShowAsync(this, "Discard what you typed?", "Unsaved changes", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes);

    private string Snapshot() => string.Join('\u001f', values.Select(v => v()));

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; fields.IsEnabled = saveButton.IsEnabled = cancelButton.IsEnabled = deleteButton.IsEnabled = false;
        notice.Text = "";
        try { await action(); busy = false; Completed?.Invoke(this, true); }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { notice.Text = error.Message.Split(" (Parameter")[0]; }
        finally { busy = false; fields.IsEnabled = saveButton.IsEnabled = cancelButton.IsEnabled = deleteButton.IsEnabled = true; }
    }

    private T Add<T>(string label, T control, Func<string> value) where T : FrameworkElement
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        fields.Children.Add(caption);
        AutomationProperties.SetName(control, label);
        control.Margin = new Thickness(0, 0, 0, 12);
        fields.Children.Add(control);
        values.Add(value);
        return control;
    }

    public TextBox Text(string label, string? value, int max = 200, bool lines = false)
    {
        var box = new TextBox { Text = value ?? "", MaxLength = max };
        if (lines) { box.AcceptsReturn = true; box.TextWrapping = TextWrapping.Wrap; box.MinHeight = 110; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
        return Add(label, box, () => box.Text);
    }

    public ComboBox Choice<T>(string label, IEnumerable<(T Value, string Label)> options, T selected)
    {
        var box = new ComboBox();
        foreach (var (value, text) in options)
        {
            var item = new ComboBoxItem { Content = text, Tag = value };
            box.Items.Add(item);
            if (EqualityComparer<T>.Default.Equals(value, selected)) box.SelectedItem = item;
        }
        if (box.SelectedItem is null && box.Items.Count > 0) box.SelectedIndex = 0;
        return Add(label, box, () => box.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static T? Chosen<T>(ComboBox box) => box.SelectedItem is ComboBoxItem { Tag: T value } ? value : default;

    public CheckBox Check(string label, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 0, 12) };
        fields.Children.Add(box);
        values.Add(() => box.IsChecked == true ? "1" : "0");
        return box;
    }

    public StarRatingControl Stars(string label, int halfStars)
    {
        var stars = new StarRatingControl { Value = halfStars, HorizontalAlignment = HorizontalAlignment.Left };
        stars.Chosen += (_, chosen) => stars.Value = chosen;
        return Add(label, stars, () => stars.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A date typed as 2026-11-27; empty means no date.</summary>
    public static DateOnly? Date(TextBox box)
    {
        var text = box.Text.Trim();
        if (text.Length == 0) return null;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            ? date : throw new ArgumentException($"Write dates as year-month-day, for example {DateOnly.FromDateTime(DateTime.Now):yyyy-MM-dd}.");
    }

    public static string DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
