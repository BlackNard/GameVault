using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameVault.Core;

namespace GameVault.App;

/// <summary>"What's new" (ticket #100): what the versions since the last one brought, newest first, in the app's own window, closed with one button.</summary>
public sealed class WhatsNewView : UserControl
{
    private readonly Button closeButton = new() { Content = "Got it", MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };

    public event EventHandler<bool>? Completed;

    public WhatsNewView(string theme, IReadOnlyList<ReleaseNote> notes)
    {
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppText");
        FontSize = 14;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        ThemePalette.Apply(this, theme);
        var root = new DockPanel();
        var heading = new TextBlock { Text = "What's new", FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        closeButton.SetResourceReference(StyleProperty, "PrimaryButton");
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);
        var list = new StackPanel();
        foreach (var note in notes)
        {
            list.Children.Add(new TextBlock { Text = Heading(note.Version), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap });
            Part(list, "New", note.New);
            Part(list, "Fixed", note.Fixed);
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
        AutomationProperties.SetName(this, "What's new in GameVault");
        closeButton.Click += (_, _) => Completed?.Invoke(this, false);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Completed?.Invoke(this, false); } };
        Loaded += (_, _) => closeButton.Focus();
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(true);

    /// <summary>"0.1.1" reads as "Version 0.1.1"; an older "0.1.0-preview.8" as "Version 0.1.0, preview 8".</summary>
    private static string Heading(string version) => "Version " + version.Replace("-preview.", ", preview ", StringComparison.Ordinal);

    private static void Part(Panel list, string title, string[] lines)
    {
        if (lines.Length == 0) return;
        var caption = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        list.Children.Add(caption);
        foreach (var line in lines)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var dot = new TextBlock { Text = "•", Width = 18 };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
            list.Children.Add(row);
        }
    }
}
