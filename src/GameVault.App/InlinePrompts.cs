using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace GameVault.App;

/// <summary>Awaitable confirmations mounted in the existing shell, with one prompt per window.</summary>
internal static class InlinePrompts
{
    private static readonly ConditionalWeakTable<Window, SemaphoreSlim> Gates = new();
    private static readonly ConditionalWeakTable<Window, object> Active = new();

    public static bool IsOpen(Window window) => Active.TryGetValue(window, out _);

    public static async Task<MessageBoxResult> ShowAsync(FrameworkElement owner, string message, string title,
        MessageBoxButton buttons, MessageBoxResult defaultResult = MessageBoxResult.Cancel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!owner.Dispatcher.CheckAccess())
        {
            return await owner.Dispatcher.InvokeAsync(() => ShowAsync(owner, message, title, buttons, defaultResult)).Task.Unwrap();
        }
        var window = owner as Window ?? Window.GetWindow(owner);
        if (window is null || !window.IsLoaded || !window.IsVisible) return MessageBoxResult.Cancel;
        var gate = Gates.GetValue(window, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!window.IsLoaded || !window.IsVisible) return MessageBoxResult.Cancel;
            if (window.Content is not Grid root)
                throw new InvalidOperationException("Inline prompts require the shell's root Grid.");
            return await PresentAsync(window, root, message, title, buttons, defaultResult);
        }
        finally { gate.Release(); }
    }

    private static Task<MessageBoxResult> PresentAsync(Window window, Grid root, string message, string title,
        MessageBoxButton buttons, MessageBoxResult requestedDefault)
    {
        var completion = new TaskCompletionSource<MessageBoxResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousFocus = Keyboard.FocusedElement;
        var siblings = root.Children.Cast<UIElement>().Select(child => (Child: child, Enabled: child.IsEnabled)).ToArray();
        var overlay = new Grid { Focusable = true };
        overlay.SetResourceReference(Panel.BackgroundProperty, "AppBackground");
        Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(overlay, Math.Max(1, root.ColumnDefinitions.Count));
        Panel.SetZIndex(overlay, int.MaxValue);
        KeyboardNavigation.SetTabNavigation(overlay, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(overlay, KeyboardNavigationMode.Cycle);
        FocusManager.SetIsFocusScope(overlay, true);
        var panel = new StackPanel { Margin = new Thickness(24), MaxWidth = 560, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        var heading = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppText");
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 24) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "AppText");
        AutomationProperties.SetLiveSetting(body, AutomationLiveSetting.Assertive);
        AutomationProperties.SetName(overlay, title);
        AutomationProperties.SetName(body, message);
        panel.Children.Add(heading);
        panel.Children.Add(body);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var choices = Choices(buttons);
        var defaultChoice = choices.Contains(requestedDefault) ? requestedDefault : choices[^1];
        Button? defaultButton = null;
        foreach (var choice in choices)
        {
            var button = new Button { Content = Label(choice), Tag = choice, MinWidth = 96, Margin = new Thickness(8, 8, 0, 0) };
            AutomationProperties.SetName(button, Label(choice));
            if (choice == defaultChoice)
            {
                button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
                AutomationProperties.SetHelpText(button, "Default action. Press Enter.");
                defaultButton = button;
            }
            button.Click += (_, _) => Finish(choice);
            actions.Children.Add(button);
        }
        panel.Children.Add(actions);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        overlay.Children.Add(scroll);
        overlay.PreviewKeyDown += HandleKey;
        window.Closed += WindowClosed;
        window.IsVisibleChanged += WindowVisibilityChanged;
        overlay.Unloaded += OverlayUnloaded;
        foreach (var sibling in siblings) sibling.Child.SetCurrentValue(UIElement.IsEnabledProperty, false);
        Active.Add(window, new object());
        root.Children.Add(overlay);
        // The element exists in the current visual tree; no window or native message loop is created.
        defaultButton?.Focus();
        return completion.Task;

        void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (choices.Contains(MessageBoxResult.Cancel)) Finish(MessageBoxResult.Cancel);
                else if (buttons == MessageBoxButton.OK) Finish(MessageBoxResult.OK);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Finish(Keyboard.FocusedElement is Button { Tag: MessageBoxResult focusedChoice } ? focusedChoice : defaultChoice);
                e.Handled = true;
            }
        }
        void WindowClosed(object? sender, EventArgs e) => Finish(MessageBoxResult.Cancel);
        void WindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!window.IsVisible) Finish(MessageBoxResult.Cancel);
        }
        void OverlayUnloaded(object sender, RoutedEventArgs e) => Finish(MessageBoxResult.Cancel);
        void Finish(MessageBoxResult result)
        {
            if (completion.Task.IsCompleted) return;
            Active.Remove(window);
            overlay.PreviewKeyDown -= HandleKey;
            window.Closed -= WindowClosed;
            window.IsVisibleChanged -= WindowVisibilityChanged;
            overlay.Unloaded -= OverlayUnloaded;
            root.Children.Remove(overlay);
            foreach (var sibling in siblings) sibling.Child.SetCurrentValue(UIElement.IsEnabledProperty, sibling.Enabled);
            if (previousFocus is UIElement element && element.IsVisible && element.IsEnabled) Keyboard.Focus(previousFocus);
            completion.TrySetResult(result);
        }
    }

    internal static MessageBoxResult[] Choices(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => [MessageBoxResult.OK],
        MessageBoxButton.OKCancel => [MessageBoxResult.OK, MessageBoxResult.Cancel],
        MessageBoxButton.YesNo => [MessageBoxResult.Yes, MessageBoxResult.No],
        MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
        _ => throw new ArgumentOutOfRangeException(nameof(buttons))
    };

    private static string Label(MessageBoxResult result) => result == MessageBoxResult.OK ? "OK" : result.ToString();
}
