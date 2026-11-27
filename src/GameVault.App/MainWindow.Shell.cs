using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GameVault.Core;

namespace GameVault.App;

/// <summary>What a toast needs for display; the queue itself lives in GameVault.Core and is tested there.</summary>
internal sealed record ToastView(long Id, string Title, string? Detail, bool IsError, string? ActionLabel)
{
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ActionVisibility => ActionLabel is null ? Visibility.Collapsed : Visibility.Visible;
}

// Title bar history (Back/Forward + breadcrumb), the Ctrl+K command palette, toasts and the "now playing" chip.
public partial class MainWindow
{
    private readonly NavigationHistory navHistory = new();
    private readonly ToastQueue toastQueue = new();
    private readonly ObservableCollection<ToastView> toastViews = [];
    private readonly DispatcherTimer shellTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private bool navigatingByHistory;
    private Guid? nowPlayingGame;

    private void InitializeShell()
    {
        ToastHost.ItemsSource = toastViews;
        InitializeNotices();
        toastQueue.Changed += (_, _) => SyncToasts();
        // Every message the app already writes to the old status line becomes a toast; the old line stays hidden.
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(OperationNotice, (_, _) => OperationNoticeChanged());
        shellTimer.Tick += (_, _) => { toastQueue.Prune(); UpdateNowPlaying(); };
        shellTimer.Start();
        Closed += (_, _) => shellTimer.Stop();
        PreviewKeyDown += ShellKeyDown;
        PreviewMouseDown += ShellMouseDown;
        UpdateNavUi();
    }

    // ---------- toasts ----------
    public void ShowToast(string title, string? detail = null, bool isError = false, string? actionLabel = null, Action? action = null)
    {
        if (isError) GameVault.Infrastructure.Diagnostics.AppLog.Warn(title + (detail is null ? "" : ": " + detail));
        toastQueue.Add(title, detail, isError, actionLabel, action);
    }

    private static readonly string[] PageLabels = ["Journal · ", "Save sources · ", "Backups · "];

    private void OperationNoticeChanged()
    {
        var text = OperationNotice.Text;
        if (string.IsNullOrWhiteSpace(text) || PageLabels.Any(label => text.StartsWith(label, StringComparison.Ordinal))) return;
        var (title, detail) = ToastText.Split(text);
        toastQueue.Add(title, detail, ToastText.LooksLikeProblem(text));
    }

    private void SyncToasts()
    {
        // Updated in place: toasts that stay are not recreated, so only a new one slides in.
        GameVault.Core.ListSync.Apply(toastViews, [.. toastQueue.Items.Select(toast => new ToastView(toast.Id, toast.Title, toast.Detail, toast.IsError, toast.ActionLabel))]);
    }

    private void DismissToast(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long id }) toastQueue.Dismiss(id);
    }

    private void ToastActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: long id }) return;
        var toast = toastQueue.Items.FirstOrDefault(t => t.Id == id);
        toastQueue.Dismiss(id);
        toast?.Action?.Invoke();
    }

    // ---------- history ----------
    private NavRoute CurrentRoute()
    {
        if (SettingsPanel.Visibility == Visibility.Visible) return NavRoute.SettingsPage(settingsCategory);
        if (StatisticsVisible) return NavRoute.Statistics;
        if (JournalVisible) return NavRoute.Journal;
        if (ListsVisible) return NavRoute.Lists;
        if (OverviewVisible) return overviewKind == NavKind.Backups ? NavRoute.Backups : captureFolder is Guid folder ? NavRoute.CapturesOf(folder) : NavRoute.Captures;
        if (GameList.SelectedItem is LibraryItemView item && (GamePage.Visibility == Visibility.Visible || notesView is not null))
        {
            var section = notesView is not null ? "Sessions" : gameSection;
            return NavRoute.Game(item.Game.Id, section);
        }
        return NavRoute.Library;
    }

    private void RecordRoute()
    {
        if (navigatingByHistory) return;
        var route = CurrentRoute();
        navHistory.Record(route);
        UpdateRecent(route);
        UpdateNavUi();
    }

    private void UpdateNavUi()
    {
        NavBackButton.IsEnabled = navHistory.CanGoBack;
        NavForwardButton.IsEnabled = navHistory.CanGoForward;
        var route = CurrentRoute();
        var name = route.GameId is Guid id ? items.FirstOrDefault(i => i.Game.Id == id)?.Name : null;
        BreadcrumbText.Text = NavBreadcrumb.Text(route, name);
        UpdateRailSelection(route);
        UpdateRecent();
    }

    private bool RouteStillExists(NavRoute route) => route.GameId is null || items.Any(i => i.Game.Id == route.GameId);

    private async Task NavigateHistoryAsync(bool back)
    {
        // A full-window view (the theme picker, an editor) is closed first, through its own discard/save guard; Back/Forward then continue from the page underneath.
        if (activeInlineEditor is { } open && !await open.TryLeaveAsync()) return;
        var route = back ? navHistory.Back(RouteStillExists) : navHistory.Forward(RouteStillExists);
        if (route is null) return;
        navigatingByHistory = true;
        bool shown;
        try { shown = await NavigateToAsync(route); }
        finally { navigatingByHistory = false; }
        if (!shown) navHistory.Revert();
        UpdateNavUi();
    }

    private async void NavBackClicked(object sender, RoutedEventArgs e) => await NavigateHistoryAsync(true);
    private async void NavForwardClicked(object sender, RoutedEventArgs e) => await NavigateHistoryAsync(false);

    /// <summary>Shows a screen using the same guards as the existing buttons (unsaved notes, open editors, running operations). Returns false when it could not.</summary>
    private async Task<bool> NavigateToAsync(NavRoute route)
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this)) return false;
        switch (route.Kind)
        {
            case NavKind.Library:
                if (!await LeaveNotes()) return false;
                SetNavigation(true);
                return true;
            case NavKind.Settings:
                if (!await LeaveNotes()) return false;
                if (SettingsCatalog.IsKnown(route.Section)) { settingsCategory = route.Section!; ApplySettingsCategory(); }
                SetNavigation(false);
                return true;
            case NavKind.Lists:
                if (!await LeaveNotes()) return false;
                await ShowListsAsync();
                return true;
            case NavKind.Journal:
                if (!await LeaveNotes()) return false;
                await ShowJournalAsync();
                return true;
            case NavKind.Statistics:
                if (!await LeaveNotes()) return false;
                await ShowStatisticsAsync();
                return true;
            case NavKind.Backups:
            case NavKind.Captures:
                if (!await LeaveNotes()) return false;
                if (route.GameId is Guid wanted && items.All(i => i.Game.Id != wanted)) { ShowToast("That game is no longer in the library", null, true); return false; }
                await ShowOverviewAsync(route.Kind, route.GameId);
                return true;
            default:
                var item = items.FirstOrDefault(i => i.Game.Id == route.GameId);
                if (item is null) { ShowToast("That game is no longer in the library", null, true); return false; }
                if (!await LeaveNotes()) return false;
                if (GameList.ItemsSource is not IEnumerable<LibraryItemView> shown || !shown.Contains(item))
                {
                    // The game is hidden by the current search or filter: show everything of its kind so it can be selected.
                    initializing = true;
                    Search.Text = ""; StatusFilter.SelectedIndex = 0; ratingFilter = GameVault.Core.RatingFilters.Any; RemovedFilter.IsChecked = item.Game.Profile?.IsArchived == true;
                    initializing = false;
                    ApplyFilters(item.Game.Id);
                }
                GameList.SelectedItem = item;
                ShowSelection(); ShowGamePage(); ShowGameSection(route.Section ?? "Overview");
                if (route.Section == "Sessions") OpenNotes(this, new RoutedEventArgs());
                return true;
        }
    }

    private void ShellKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; TogglePalette(); return; }
        if (key == Key.Escape && NoticePanel.Visibility == Visibility.Visible && PaletteOverlay.Visibility != Visibility.Visible) { e.Handled = true; CloseNotices(); return; }
        if (PaletteOverlay.Visibility == Visibility.Visible) return;
        if (key == Key.OemQuestion && Keyboard.Modifiers == ModifierKeys.None && libraryVisible && LibraryPanel.Visibility == Visibility.Visible && Keyboard.FocusedElement is not TextBox)
        {
            e.Handled = true;
            Search.Focus();
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Alt && key is Key.Left or Key.Right && !InlinePrompts.IsOpen(this))
        {
            e.Handled = true;
            _ = NavigateHistoryAsync(key == Key.Left);
        }
    }

    private void ShellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && NoticePanel.Visibility == Visibility.Visible && !IsInside(e.OriginalSource as DependencyObject, NoticePanel) && !IsInside(e.OriginalSource as DependencyObject, NoticesButton)) CloseNotices();
        if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2) || PaletteOverlay.Visibility == Visibility.Visible) return;
        e.Handled = true;
        _ = NavigateHistoryAsync(e.ChangedButton == MouseButton.XButton1);
    }

    // ---------- now playing ----------
    private void UpdateNowPlaying()
    {
        var running = launches.GetActiveStates().FirstOrDefault(s => s.Phase == LaunchPhase.Running);
        if (running is null)
        {
            nowPlayingGame = null;
            if (NowPlayingButton.Visibility != Visibility.Collapsed) NowPlayingButton.Visibility = Visibility.Collapsed;
            return;
        }
        nowPlayingGame = running.GameId;
        var name = items.FirstOrDefault(i => i.Game.Id == running.GameId)?.Name ?? "Game";
        var seconds = launches.GetSessionProgress(running.GameId) is { IsActive: true } progress ? progress.SessionSeconds : 0;
        NowPlayingText.Text = $"{name}   {TimeSpan.FromSeconds(Math.Max(0, seconds)):hh\\:mm\\:ss}";
        NowPlayingButton.Visibility = Visibility.Visible;
    }

    private async void NowPlayingClicked(object sender, RoutedEventArgs e)
    {
        if (nowPlayingGame is Guid id) await NavigateToAsync(NavRoute.Game(id, "Overview"));
    }

    // ---------- command palette ----------
    private void OpenPaletteClicked(object sender, RoutedEventArgs e) => TogglePalette();

    private void TogglePalette()
    {
        if (PaletteOverlay.Visibility == Visibility.Visible) { ClosePalette(); return; }
        if (editorOpen || quitPreparing || stoppingObservation || InlinePrompts.IsOpen(this)) return;
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteInput.Clear();
        RefreshPalette();
        PaletteInput.Focus();
    }

    private void ClosePalette()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        if (libraryVisible && IsLoaded) RestoreLibraryFocus();
    }

    private void PaletteScrimClicked(object sender, MouseButtonEventArgs e) => ClosePalette();

    private IEnumerable<PaletteEntry> BuildPaletteEntries()
    {
        Func<Task> Go(NavRoute route) => async () => { await NavigateToAsync(route); };
        var visibleGames = items.Where(i => i.Game.Profile is not null).OrderBy(i => i.Game.Profile!.IsArchived).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var item in visibleGames)
            yield return new PaletteEntry("Game", item.Name, "", Go(NavRoute.Game(item.Game.Id, "Overview")));
        yield return new PaletteEntry("Go to", "Library", "", Go(NavRoute.Library));
        yield return new PaletteEntry("Go to", "Settings", "", Go(NavRoute.Settings));
        foreach (var page in SettingsCatalog.Categories) yield return new PaletteEntry("Go to", "Settings: " + page.Title, page.Description, Go(NavRoute.SettingsPage(page.Key)));
        yield return new PaletteEntry("Go to", "Backups", "", Go(NavRoute.Backups));
        yield return new PaletteEntry("Go to", "Captures", "", Go(NavRoute.Captures));
        yield return new PaletteEntry("Go to", "Statistics", "", Go(NavRoute.Statistics));
        yield return new PaletteEntry("Go to", "Journal", "", Go(NavRoute.Journal));
        yield return new PaletteEntry("Go to", "Lists", "", Go(NavRoute.Lists));
        yield return new PaletteEntry("Action", "Add game", "", (Func<Task>)(() => { AddGame(this, new RoutedEventArgs()); return Task.CompletedTask; }));
        foreach (var item in visibleGames.Where(i => i.Game is { IsReadOnly: false, Profile.IsArchived: false }))
        {
            var id = item.Game.Id;
            yield return new PaletteEntry("Action", "Play " + item.Name, "", (Func<Task>)(async () =>
            {
                if (await NavigateToAsync(NavRoute.Game(id, "Overview"))) PlayGame(this, new RoutedEventArgs());
            }));
            yield return new PaletteEntry("Action", "Back up " + item.Name + " now", "", (Func<Task>)(async () =>
            {
                if (await NavigateToAsync(NavRoute.Game(id, "Saves"))) BackUpNow(this, new RoutedEventArgs());
            }));
        }
        yield return new PaletteEntry("Action", "Show the welcome", "Ways to add your first games", (Func<Task>)ShowWelcomeDrawerAsync);
        yield return new PaletteEntry("Appearance", "Browse themes", "Preview every theme", (Func<Task>)OpenThemePickerAsync);
        foreach (var theme in ThemeCatalog.All)
        {
            var id = theme.Id;
            yield return new PaletteEntry("Appearance", "Switch to " + theme.Name + " theme", "", (Func<Task>)(async () =>
            {
                if (busy || editorOpen || settingsReadOnly) return;
                var problem = await ApplyThemeAsync(id);
                if (problem is not null) ShowToast("Could not change the theme", problem, true);
            }));
        }
    }

    private void RefreshPalette()
    {
        var results = PaletteSearch.Filter(BuildPaletteEntries(), PaletteInput.Text);
        PaletteList.ItemsSource = results;
        PaletteEmpty.Visibility = results.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Length > 0) PaletteList.SelectedIndex = 0;
    }

    private void PaletteInputChanged(object sender, TextChangedEventArgs e) => RefreshPalette();

    private void PaletteInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: e.Handled = true; ClosePalette(); break;
            case Key.Down: e.Handled = true; MovePalette(1); break;
            case Key.Up: e.Handled = true; MovePalette(-1); break;
            case Key.Enter: e.Handled = true; _ = RunPaletteAsync(PaletteList.SelectedItem as PaletteEntry); break;
        }
    }

    private void MovePalette(int step)
    {
        var count = PaletteList.Items.Count;
        if (count == 0) return;
        PaletteList.SelectedIndex = Math.Clamp(PaletteList.SelectedIndex + step, 0, count - 1);
        PaletteList.ScrollIntoView(PaletteList.SelectedItem);
    }

    private void PaletteListClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var container = ItemsControl.ContainerFromElement(PaletteList, source) as ListBoxItem;
        if (container?.DataContext is PaletteEntry entry) _ = RunPaletteAsync(entry);
    }

    private async Task RunPaletteAsync(PaletteEntry? entry)
    {
        if (entry?.Tag is not Func<Task> run) return;
        PaletteOverlay.Visibility = Visibility.Collapsed;
        try { await run(); }
        catch (Exception error) when (IsExpected(error)) { ShowToast("That command could not run", error.Message, true); }
    }
}
