using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

// Play actions and before/after hooks in the launch settings (ticket #87).
public partial class LaunchSettingsWindow
{
    private sealed class ActionRow
    {
        public Guid Id = Guid.NewGuid();
        public required Border Panel;
        public required TextBox Name, Executable, Arguments, Folder;
        public required CheckBox Admin;
        public required RadioButton Default;
    }

    private sealed class HookEditor
    {
        public required string Label;
        public required StackPanel Panel;
        public required TextBox Executable, Arguments, Timeout;
        public required CheckBox Wait;
    }

    private readonly List<ActionRow> actionRows = [];
    private HookEditor before = null!, after = null!;

    private void BuildActionsAndHooks(LaunchSettings? current)
    {
        foreach (var action in current?.PlayActions ?? []) AddActionRow(action, action.Id == current!.DefaultActionId);
        NoDefaultAction.IsChecked = !actionRows.Any(r => r.Default.IsChecked == true);
        before = AddHookEditor("Before launch", current?.BeforeLaunch);
        after = AddHookEditor("After exit", current?.AfterExit);
        UpdateAddButton();
    }

    private static TextBox Box(string label, string text, int max = 1000)
    {
        var box = new TextBox { Text = text, MaxLength = max, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(box, label);
        return box;
    }

    private static Label Caption(string text) => new() { Content = text, Padding = new Thickness(0, 0, 0, 2) };

    private DockPanel WithBrowse(TextBox box, string title, bool folder)
    {
        var row = new DockPanel();
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 8), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(browse, "Browse " + title);
        browse.Click += (_, _) =>
        {
            try
            {
                var owner = Window.GetWindow(this)!;
                var path = folder ? NativePickers.Folder(owner, title, box.Text) : NativePickers.File(owner, title, "Windows executables (*.exe)|*.exe", box.Text);
                if (path is not null) box.Text = path;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            { Notice.Text = "The browser could not open. Paste a full path instead. " + error.Message; }
        };
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(box);
        return row;
    }

    private void AddActionRow(PlayAction? action, bool isDefault)
    {
        var row = new ActionRow
        {
            Id = action?.Id ?? Guid.NewGuid(),
            Panel = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12) },
            Name = Box("Play action name", action?.Name ?? "", LaunchRules.MaxNameLength),
            Executable = Box("Play action program", action?.ExecutablePath ?? ""),
            Arguments = Box("Play action arguments", action?.Arguments ?? "", LaunchRules.MaxArgumentLength),
            Folder = Box("Play action working folder", action?.WorkingDirectory ?? ""),
            Admin = new CheckBox { Content = "Run as administrator (Windows asks for approval)", IsChecked = action?.RunAsAdministrator ?? false, Margin = new Thickness(0, 0, 0, 8) },
            Default = new RadioButton { Content = "Play button runs this action", GroupName = "DefaultAction", IsChecked = isDefault, Margin = new Thickness(0, 0, 0, 8) },
        };
        row.Panel.SetResourceReference(Border.BorderBrushProperty, "AppLine");
        var remove = new Button { Content = "Remove this action", HorizontalAlignment = Left };
        remove.Click += (_, _) => { actionRows.Remove(row); ActionsHost.Children.Remove(row.Panel); if (!actionRows.Any(r => r.Default.IsChecked == true)) NoDefaultAction.IsChecked = true; UpdateAddButton(); };
        var stack = new StackPanel();
        stack.Children.Add(Caption("Name (shown in the Play menu)")); stack.Children.Add(row.Name);
        stack.Children.Add(Caption("Program (.exe)")); stack.Children.Add(WithBrowse(row.Executable, "program for the play action", false));
        stack.Children.Add(Caption("Arguments (optional)")); stack.Children.Add(row.Arguments);
        stack.Children.Add(Caption("Working folder (optional, defaults to the game's folder)")); stack.Children.Add(WithBrowse(row.Folder, "working folder for the play action", true));
        stack.Children.Add(row.Admin); stack.Children.Add(row.Default); stack.Children.Add(remove);
        row.Panel.Child = stack;
        actionRows.Add(row);
        ActionsHost.Children.Add(row.Panel);
        UpdateAddButton();
    }

    private const HorizontalAlignment Left = HorizontalAlignment.Left;

    private void UpdateAddButton() => AddActionButton.IsEnabled = actionRows.Count < LaunchRules.MaxActions;

    private void AddAction(object sender, RoutedEventArgs e)
    {
        if (actionRows.Count >= LaunchRules.MaxActions) return;
        AddActionRow(null, false);
        actionRows[^1].Name.Focus();
    }

    private HookEditor AddHookEditor(string label, LaunchHook? hook)
    {
        var editor = new HookEditor
        {
            Label = label,
            Panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) },
            Executable = Box(label + " program", hook?.ExecutablePath ?? ""),
            Arguments = Box(label + " arguments", hook?.Arguments ?? "", LaunchRules.MaxArgumentLength),
            Timeout = Box(label + " timeout in seconds", (hook?.TimeoutSeconds ?? 30).ToString(CultureInfo.InvariantCulture), 3),
            Wait = new CheckBox { Content = label == "Before launch" ? "Wait for it to finish before the game starts" : "Wait for it to finish (and stop it after the timeout)", IsChecked = hook?.WaitForExit ?? true, Margin = new Thickness(0, 0, 0, 8) },
        };
        editor.Timeout.Width = 80;
        editor.Timeout.HorizontalAlignment = Left;
        editor.Panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        editor.Panel.Children.Add(Caption("Program (.exe) – empty runs nothing")); editor.Panel.Children.Add(WithBrowse(editor.Executable, "program for " + label.ToLowerInvariant(), false));
        editor.Panel.Children.Add(Caption("Arguments (optional)")); editor.Panel.Children.Add(editor.Arguments);
        editor.Panel.Children.Add(Caption("Timeout (seconds, 1 to " + LaunchRules.MaxTimeoutSeconds + ")")); editor.Panel.Children.Add(editor.Timeout);
        editor.Panel.Children.Add(editor.Wait);
        HooksHost.Children.Add(editor.Panel);
        return editor;
    }

    /// <summary>Reads the editors into settings, or explains what is wrong.</summary>
    private bool TryReadActionsAndHooks(out PlayAction[]? actions, out Guid? defaultId, out LaunchHook? beforeHook, out LaunchHook? afterHook, out string error)
    {
        actions = null; defaultId = null; beforeHook = afterHook = null; error = "";
        var list = new List<PlayAction>();
        foreach (var row in actionRows)
        {
            var action = new PlayAction(row.Id, row.Name.Text.Trim(), row.Executable.Text.Trim().Trim('"'), row.Arguments.Text.Trim(), row.Folder.Text.Trim().Trim('"'), row.Admin.IsChecked == true);
            if (!LaunchRules.IsValidAction(action))
            {
                error = $"Play action \"{(action.Name.Length == 0 ? "(no name)" : action.Name)}\" needs a name and a full path to an .exe file. Arguments must be plain text.";
                return false;
            }
            list.Add(action);
            if (row.Default.IsChecked == true) defaultId = row.Id;
        }
        if (list.Select(a => a.Name.ToUpperInvariant()).Distinct().Count() != list.Count) { error = "Two play actions have the same name."; return false; }
        if (!TryReadHook(before, out beforeHook, out error) || !TryReadHook(after, out afterHook, out error)) return false;
        actions = list.Count == 0 ? null : [.. list];
        return true;
    }

    private static bool TryReadHook(HookEditor editor, out LaunchHook? hook, out string error)
    {
        hook = null; error = "";
        var path = editor.Executable.Text.Trim().Trim('"');
        if (path.Length == 0) return true;
        if (!int.TryParse(editor.Timeout.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) seconds = 0;
        var candidate = new LaunchHook(path, editor.Arguments.Text.Trim(), seconds, editor.Wait.IsChecked == true);
        if (!LaunchRules.IsValidHook(candidate)) { error = $"{editor.Label}: use a full path to an .exe file, plain-text arguments and a timeout of 1 to {LaunchRules.MaxTimeoutSeconds} seconds."; return false; }
        hook = candidate;
        return true;
    }

    private string ActionsSnapshot() => string.Join("|", actionRows.Select(r => $"{r.Id}:{r.Name.Text}:{r.Executable.Text}:{r.Arguments.Text}:{r.Folder.Text}:{r.Admin.IsChecked}:{r.Default.IsChecked}")) +
        $"#{before?.Executable.Text}:{before?.Arguments.Text}:{before?.Timeout.Text}:{before?.Wait.IsChecked}#{after?.Executable.Text}:{after?.Arguments.Text}:{after?.Timeout.Text}:{after?.Wait.IsChecked}";
}
