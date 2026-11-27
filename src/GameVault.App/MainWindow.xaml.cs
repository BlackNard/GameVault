using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Saves;

namespace GameVault.App;

public partial class MainWindow : Window
{
    private readonly RestoreService restores;private RestoreView? restoreView;
    private BackupsView? backupsView;
    private readonly BackupHistoryService backupHistory;
    private long backupViewRevision;
    private readonly BackupService backups;
    private readonly BackupCoordinator backupCoordinator;
    private readonly SaveDiscoveryService discovery;
    private readonly SaveDiscoveryCoordinator discoveryCoordinator;
    private SaveSourcesView? saveSourcesView;
    private readonly AppDataStore store;
    private readonly ApplicationPaths paths;
    private readonly LibraryService library;
    private readonly LaunchService launches;
    private readonly LaunchSettingsStore launchSettings;
    private AppSettings settings;
    private readonly bool settingsReadOnly;
    private bool initializing = true;
    private bool busy;
    private bool closeAfterSave;
    private bool editorOpen;
    private bool stoppingObservation;
    private bool observationStopped;
    private NotesView? notesView;
    private bool notesNavigationPending;
    private LibraryItemView[] items = [];
    private string[] recoveryNotices = [];

    public MainWindow(ApplicationPaths paths, AppDataStore store, AppDataSnapshot snapshot)
    {
        this.store = store;
        this.paths = paths;
        library = new(paths, store);
        saveLocations = new(paths);
        discovery = new(paths, store, SaveDiscoveryEnvironment.Current() with { Database = saveLocations });
        launches = new(paths, store, new WindowsGameProcessSystem());
        discoveryCoordinator = new(launches, discovery);
        backups=new(paths,store,discovery,new GameOperationLeases(),launches.IsGameRunningAsync);
        backupCoordinator=new(launches,backups,launches.WaitForIdleAsync);
        backupHistory=new(paths,store,new(),launches.IsGameRunningAsync);
        restores=new(paths,store,new(),launches.IsGameRunningAsync);
        backups.Changed+=BackupChanged;
        discovery.Changed += SourceDiscoveryChanged;
        launchSettings = new(paths, store);
        launches.StateChanged += LaunchStateChanged;
        launches.HookCompleted += HookCompleted;
        launches.SessionUpdated += SessionUpdated;
        settings = snapshot.Settings.Value ?? AppSettings.Default;
        settingsReadOnly = snapshot.Settings.IsReadOnly;
        InitializeComponent();
        HeroRating.Chosen += HeroRatingChosen;
        InitializeSettingsPages();
        RememberSection(ManageInfo, "manage");
        lifetime=new(Hide,RestoreWindow,PrepareToQuitAsync,QuitRuntimeAsync);
        ThemePalette.Apply(this, settings.Theme);
        Motion.ReduceMotion = settings.ReduceMotion;
        ReduceMotionChoice.IsChecked = settings.ReduceMotion;
        CloseToTrayChoice.IsChecked=settings.CloseToTray;BackupNotificationsChoice.IsChecked=settings.NotifyBackupFailures;CheckForUpdatesChoice.IsChecked=settings.CheckForUpdates;AutoUpdateChoice.IsChecked=settings.AutoUpdate;
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width);
        ThemeName.Text = ThemeCatalog.Find(settings.Theme).Name;
        ProfilesPath.Text = paths.Profiles;
        StorageNotice.Text = string.Join("\n", snapshot.Warnings.Distinct());
        initializing = false;
        SetNavigation(true);
        UpdateButtons();
        Closing += BeforeClosing;
        Closed += (_, _) => StopAutoInfo();
        Loaded += InitializeTray;
        Loaded += ShowPreviousRunNotice;
        Loaded += StartUpdateCheckLater;
        Loaded += LoadLibrary;
        InitializePerformanceFeatures();
        InitializeRedesign();
        InitializeCaptureFeatures();
        InitializeShell();
    }

    private async void LoadLibrary(object sender, RoutedEventArgs e)
    {
        if(libraryLoaded)return;libraryLoaded=true;
        busy = true; UpdateButtons();
        try
        {
            recoveryNotices = await launches.RecoverAsync(); await RefreshLibrary(); _ = RefreshSteamHoursAtStartAsync();
            _ = StartupInfoAsync();
            // Automatic discovery follows session signals; never block startup by scanning every game.
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = "Could not load the library. " + error.Message; }
        finally { EndOperation(); }
    }

    private async Task RefreshLibrary(Guid? select = null)
    {
        var previous = select ?? (GameList.SelectedItem as LibraryItemView)?.Game.Id;
        await EnsureTagsLoadedAsync();
        var snapshot = await library.ListAsync();
        var previousRows=libraryRows;
        items=snapshot.Games.Select(game=>
        {
            if(previousRows.TryGetValue(game.Id,out var row)){row.RefreshGame(game);return row;}
            return new LibraryItemView(game);
        }).ToArray();
        libraryRows=items.ToDictionary(item=>item.Game.Id);
        foreach (var item in items)
        { item.SetLaunchState(launches.GetState(item.Game.Id)); item.SetStoredProgress(launches.GetSessionProgress(item.Game.Id)); }
        StorageNotice.Text = string.Join("\n", snapshot.Warnings.Concat(recoveryNotices).Distinct());
        ApplyFilters(previous);
        ReloadVisibleCovers();
        UpdateNavUi();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if(initializing)return;
        if(sender is TextBox){LibrarySummary.Text="Searching…";filterTimer.Stop();filterTimer.Start();}
        else{filterTimer.Stop();ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id);}
    }

    private void ApplyFilters(Guid? selected)
    {
        var removed = RemovedFilter.IsChecked == true;
        var filter = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        var search = Search.Text.Trim();
        var matching = items.Where(i => (i.Game.Profile?.IsArchived ?? false) == removed &&
            (search.Length == 0 || i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (filter == "All" || i.Playing?.ToString() == filter) && TagRules.HasAll(TagsOf(i.Game.Id), selectedTags) && RatingFilters.Matches(ratingFilter, i.Game.Rating));
        var visible = LibrarySortOrder.Apply(matching, sortOrder, i => i.Name, i => i.LastPlayedAt, i => i.Game.PlayedSeconds, i => i.Game.Rating).ToArray();
        if(GameList.ItemsSource is not LibraryItemView[] current || !current.SequenceEqual(visible))GameList.ItemsSource=visible;
        GameList.SelectedItem = visible.FirstOrDefault(i => i.Game.Id == selected) ?? visible.FirstOrDefault();
        LibrarySummary.Text = $"{visible.Length} {(removed ? "removed profiles" : "games")} · stored on your device";
        EmptyState.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHeading.Text = removed ? "No removed profiles here" : items.Length == 0 ? "Make room for your favorites" : "No matching games";
        EmptyDescription.Text = removed ? "Removed games keep their data here so you can restore them." : items.Length == 0 ? "Choose Add game and browse to its executable to start your library." : "Try another search or status. You can also check Removed profiles.";
        UpdateWelcome(removed);
        ShowSelection();
        ShowStatusChips();
        SyncChips();
        UpdateFilterButtons();
        UpdateContinueRow();
    }

    private Guid playStateGame;

    /// <summary>The Play button eases to a new state of the same game (starting, running, back to Play).</summary>
    private void ShowPlayState(Guid game, string text)
    {
        var changed = playStateGame == game && !Equals(PlayButton.Content, text);
        playStateGame = game;
        PlayButton.Content = text;
        if (changed) Motion.Play(PlayButton, GameVault.Core.MotionKind.Fade);
    }

    private void SelectedGameChanged(object sender, SelectionChangedEventArgs e) { if (!initializing) ShowSelection(); }

    private void ShowSelection()
    {
        var item = GameList.SelectedItem as LibraryItemView;
        if(item is not null)_=RefreshHeroCoverAsync(item);else {GameHeroCover.Source=null;heroCoverGame=null;heroCoverRequest?.Cancel();}
        DetailTitle.Text = item?.Name ?? "A home for every game";
        DetailState.Text = item is null ? "Select a game to see its details or choose Add game to get started." :
            string.Join("\n", new[] { item.Playing is null ? item.Status : null, item.Availability, item.Game.Warning }.Where(s => !string.IsNullOrWhiteSpace(s)));
        ShowHeroStatus(item);
        DetailFields.Visibility = item?.Game.Profile is not null ? Visibility.Visible : Visibility.Collapsed;
        if(item is null)summarizedGame=null;
        if (item?.Game.Profile is GameProfile profile)
        {
            DetailTime.Text = item.Played;
            HeroRating.Value = item.Game.Rating;
            HeroRating.IsEnabled = !item.Game.IsReadOnly && !profile.IsArchived;
            SessionElapsed.Text = item.SessionTime;
            SessionWarning.Text = item.SessionWarning ?? "";
            DetailExecutable.Text = profile.ExecutablePath;
            DetailWorking.Text = profile.WorkingDirectory;
            if(!ReferenceEquals(summarizedGame,item.Game))
            {
                summarizedGame=item.Game;
                DetailSaves.Text="Checking save sources…";_=RefreshSourceSummary(profile.Id);
                BackupNotice.Text="Checking backup history…";LastBackup.Text="";AttentionCard.Visibility=Visibility.Collapsed;_=RefreshBackupSummary(profile.Id);
            }
            ArchiveButton.Content = profile.IsArchived ? "Restore to library" : "Remove from library";
            var launch = launches.GetState(profile.Id);
            LaunchNotice.Text = launch.Message;
            ShowPlayState(profile.Id, launch.Phase switch { LaunchPhase.Starting => "Starting…", LaunchPhase.Running => "Running", _ => "Play" });
        }
        UpdateTagsUi(item);
        _ = RefreshAboutAsync(item);
        _ = RefreshPlayActionsAsync(item);
        _ = RefreshSteamAsync(item);
        RefreshGameStats(item);
        UpdateButtons();
        _=RefreshBackupDestinationsAsync();
        if(gameSection=="Media"&&!libraryVisible)_=RefreshCaptureMediaAsync();
    }

    private async void CoverRowLoaded(object sender, RoutedEventArgs e)
    {
        if(sender is FrameworkElement row)await BindCoverRowAsync(row);
    }

    private async void CoverRowChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if(sender is FrameworkElement row)await BindCoverRowAsync(row);
    }

    private async void ShowLibrary(object sender, RoutedEventArgs e) { if (!editorOpen && !InlinePrompts.IsOpen(this) && await LeaveNotes()) SetNavigation(true); }
    private async void ShowSettings(object sender, RoutedEventArgs e) { if (!editorOpen && !InlinePrompts.IsOpen(this) && await LeaveNotes()) SetNavigation(false); }

    private void SetNavigation(bool showLibrary)
    {
        libraryVisible=showLibrary;
        GamePage.Visibility=Visibility.Collapsed;
        LibraryPanel.Visibility = showLibrary ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = showLibrary ? Visibility.Collapsed : Visibility.Visible;
        OverviewPanel.Visibility = StatisticsPanel.Visibility = Visibility.Collapsed;
        if(!showLibrary){_=RefreshBackupDestinationsAsync();RefreshSavedViewsManager();RefreshMetadataSettingsUi();RefreshSaveDatabaseStatus();_=RefreshObsidianAsync();}
        else if(IsLoaded)RestoreLibraryFocus();
        LibraryNav.SetResourceReference(Control.BorderBrushProperty, showLibrary ? "AppAccent" : "AppLine");
        SettingsNav.SetResourceReference(Control.BorderBrushProperty, showLibrary ? "AppLine" : "AppAccent");
        RecordRoute();
    }
    private void AddGame(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void EditGame(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is LibraryItemView { Game.IsReadOnly: false } item && !launches.GetState(item.Game.Id).IsActive) OpenEditor(item.Game.Profile);
    }

    private async void PlayGame(object sender, RoutedEventArgs e) => await PlayWithAsync(null);

    private void LaunchStateChanged(object? sender, LaunchState state)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() => ApplyLaunchState(state));
    }

    private void SessionUpdated(object? sender, SessionProgress progress)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (stoppingObservation) return;
            libraryRows.TryGetValue(progress.GameId,out var item);
            item?.SetSessionProgress(progress);
            notesView?.UpdateLiveProgress(progress);
            if (GameList.SelectedItem is LibraryItemView selected && selected.Game.Id == progress.GameId)
            {
                DetailTime.Text = selected.Played;
                SessionElapsed.Text = selected.SessionTime;
                SessionWarning.Text = selected.SessionWarning ?? "";
                ApplySteamTotal();
            }
        });
    }

    private async void ApplyLaunchState(LaunchState state)
    {
        if (stoppingObservation) return;
        try
        {
            if (state.Phase == LaunchPhase.Running)
            {
                // A launch notification needs only a read; do not initialize storage during shutdown.
                var game = await store.LoadProfileAsync(state.GameId);
                if (stoppingObservation) return;
                if (game.Profile.Value is GameProfile profile)
                    items.FirstOrDefault(i => i.Game.Id == state.GameId)?.RefreshProfile(profile);
                ApplyFilters((GameList.SelectedItem as LibraryItemView)?.Game.Id);
            }
            if (state.Phase == LaunchPhase.Stopped) await RefreshLibrary();
            foreach (var item in items)
            { item.SetLaunchState(launches.GetState(item.Game.Id)); item.SetStoredProgress(launches.GetSessionProgress(item.Game.Id)); }
            ShowSelection();
            var name = items.FirstOrDefault(i => i.Game.Id == state.GameId)?.Name ?? "Game";
            OperationNotice.Text = $"{name}: {launches.GetState(state.GameId).Message}";
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = "The game state changed, but the library could not refresh. " + error.Message; }
    }

    private async void ConfigureLaunch(object sender, RoutedEventArgs e)
    {
        GameOptionsPopup.IsOpen=false;
        if (busy || editorOpen || GameList.SelectedItem is not LibraryItemView { Game.IsReadOnly: false, Game.Profile: not null } item || launches.GetState(item.Game.Id).IsActive) return;
        busy = editorOpen = true; UpdateButtons();
        try
        {
            var configuration = await launchSettings.LoadAsync(item.Game.Id);
            var dialog = new LaunchSettingsWindow(settings.Theme, item.Game.Profile, launchSettings, configuration);
            if (await ShowInlineEditorAsync(dialog,h=>dialog.Completed+=h,h=>dialog.Completed-=h)) { OperationNotice.Text = "Launch settings saved."; _ = RefreshPlayActionsAsync(item); }
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = error.Message; }
        finally { editorOpen = false; EndOperation(); CompleteInlineEditor(); }
    }

    private async void OpenEditor(GameProfile? profile, string? prefillExecutable = null)
    {
        GameOptionsPopup.IsOpen=false;
        if (busy || editorOpen || quitPreparing || stoppingObservation) return;
        editorOpen = true;
        UpdateButtons();
        try
        {
            var editor = new GameEditor(settings.Theme, profile, async (draft, cover) =>
            {
                busy = true;
                try { return await library.SaveAsync(draft, cover); }
                finally { busy = false; }
            }, async id =>
            {
                busy = true;
                try { return await library.RestoreAsync(id); }
                finally { busy = false; }
            }, items.FirstOrDefault(i => i.Game.Id == profile?.Id)?.Playing);
            if (profile is null && prefillExecutable is not null) editor.Prefill(prefillExecutable);
            editor.FullWidthChanged += (_, _) => SetDrawerFullWidth(true);   // the editor always fills the window
            if (await ShowInlineEditorAsync(editor,h=>editor.Completed+=h,h=>editor.Completed-=h) && editor.Result is GameSaveResult result)
            {
                busy = true;
                // A new game should be visible immediately; editing retains the current library filters.
                if (profile is null)
                {
                    initializing = true;
                    Search.Text = ""; StatusFilter.SelectedIndex = 0; RemovedFilter.IsChecked = false; ratingFilter = RatingFilters.Any;
                    initializing = false;
                }
                if (!editor.WasRestored) await discovery.ScanAsync(result.Profile.Id);
                await RefreshLibrary(result.Profile.Id);
                if(profile is null){ShowGamePage();ShowGameSection("Overview");QueueAutoInfo([(result.Profile.Id, result.Profile.Name)], urgent: true);}
                OperationNotice.Text = result.Warning ?? (editor.WasRestored ? $"{result.Profile.Name} restored with its saved configuration and history." : $"{result.Profile.Name} saved.");
            }
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = "Could not refresh the library. " + error.Message; }
        finally
        {
            busy = editorOpen = false;
            try
            {
                if (saveSourcesView is not null) await saveSourcesView.RefreshAsync();
                UpdateButtons();
                ContinueDeferredQuit();
            }
            finally { CompleteInlineEditor(); }
        }
    }

    private async void ArchiveGame(object sender, RoutedEventArgs e)
    {
        GameOptionsPopup.IsOpen=false;
        if (busy || quitPreparing || stoppingObservation || GameList.SelectedItem is not LibraryItemView { Game.IsReadOnly: false, Game.Profile: not null } item || launches.GetState(item.Game.Id).IsActive) return;
        var profile = item.Game.Profile;
        var remove = !profile.IsArchived;
        if (remove && await InlinePrompts.ShowAsync(this, $"Remove {profile.Name} from your library?\n\nIts profile, statistics, and any existing backups will be kept. You can restore it from Removed profiles. The installed game is untouched.",
            "Remove from library", MessageBoxButton.OKCancel, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        busy = true; UpdateButtons();
        try
        {
            await library.SetArchivedAsync(item.Game.Id, remove);
            await RefreshLibrary();
            OperationNotice.Text = remove ? "Game removed. Its data is kept in Removed profiles." : "Game restored to your library. Uncheck Removed profiles to see it.";
        }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = error.Message; }
        finally { EndOperation(); }
    }

    private async void BrowseThemesClicked(object sender, RoutedEventArgs e) => await OpenThemePickerAsync();

    private async Task OpenThemePickerAsync()
    {
        if (busy || editorOpen || quitPreparing || stoppingObservation || settingsReadOnly || InlinePrompts.IsOpen(this)) return;
        editorOpen = true; UpdateButtons();
        try
        {
            var view = new ThemePickerView(settings.Theme, theme => ApplyThemeAsync(theme));
            await ShowInlineEditorAsync(view, h => view.Completed += h, h => view.Completed -= h);
        }
        catch (Exception error) when (IsExpected(error)) { ShowToast("Could not open the themes", error.Message, true); }
        finally { editorOpen = false; CompleteInlineEditor(); UpdateButtons(); ContinueDeferredQuit(); }
    }

    /// <summary>Saves and applies a theme. Returns null on success, otherwise a sentence saying why it did not work (the previous theme stays).</summary>
    private async Task<string?> ApplyThemeAsync(string theme)
    {
        if (!ThemeCatalog.IsKnown(theme)) return "That theme is not available.";
        if (settingsReadOnly) return "Settings are read-only, so the theme cannot be saved.";
        try
        {
            var updated = settings with { Theme = theme };
            await store.SaveSettingsAsync(updated);
            settings = updated;
            Motion.CrossFade(this, () => ThemePalette.Apply(this, theme));
            if (captureOverlay is not null) ThemePalette.Apply(captureOverlay, theme);
            ThemeName.Text = ThemeCatalog.Find(theme).Name;
            OperationNotice.Text = "Theme saved. It will be restored next time you open GameVault.";
            return null;
        }
        catch (Exception error) when (IsExpected(error)) { return "Could not save the theme. " + error.Message; }
    }

    private async void NotesDataChanged(object? sender, EventArgs e)
    {
        try { await RefreshLibrary(); }
        catch (Exception error) when (IsExpected(error)) { OperationNotice.Text = "History was saved, but library refresh needs attention. " + error.Message; }
    }
    private void OpenNotes(object sender, RoutedEventArgs e)
    {
        if (busy || editorOpen || stoppingObservation || quitPreparing || notesView is not null || saveSourcesView is not null || backupsView is not null || GameList.SelectedItem is not LibraryItemView item) return;
        notesView = new NotesView(paths, store, item.Game.Id, settings.Theme, () => launches.GetState(item.Game.Id).IsActive);
        notesView.GameDataChanged += NotesDataChanged;
        notesView.BackRequested += BackFromNotes;
        NotesHost.Content = notesView;
        LibraryPanel.Visibility = SettingsPanel.Visibility = Visibility.Collapsed;
        NotesHost.Visibility = Visibility.Visible;
        DetailFields.Visibility=Visibility.Collapsed;
        OperationNotice.Text = "Sessions & notes · " + item.Name;
        UpdateButtons();
        RecordRoute();
    }
    private async void BackFromNotes(object? sender, EventArgs e) { var section=gameSection=="Sessions"?"Overview":gameSection; if (await LeaveNotes()){ShowGamePage();ShowGameSection(section);} }
    private async Task<bool> LeaveNotes(bool detach = true)
    {
        if(notesView is null&&saveSourcesView is null&&backupsView is null)return true;
        if(notesNavigationPending)return false;
        notesNavigationPending=true;LibraryNav.IsEnabled=SettingsNav.IsEnabled=false;
        try
        {
            if(restoreView is not null&&!await restoreView.CanLeaveAsync())return false;
            if(backupsView is not null&&!await backupsView.CanLeaveAsync())return false;
            if(notesView is not null&&!await notesView.CanLeaveAsync())return false;
            if(saveSourcesView is not null&&!await saveSourcesView.CanLeaveAsync())return false;
            if(!detach)return true;
            if(notesView is not null){notesView.GameDataChanged-=NotesDataChanged;notesView.BackRequested-=BackFromNotes;}
            if(saveSourcesView is not null){saveSourcesView.ManualEditRequested-=EditManualSources;saveSourcesView.BackRequested-=BackFromNotes;}
            if(restoreView is not null){restoreView.BackRequested-=BackFromRestore;restoreView.GameDataChanged-=NotesDataChanged;restoreView=null;}
            if(backupsView is not null){backupsView.BackRequested-=BackFromNotes;backupsView.GameDataChanged-=NotesDataChanged;backupsView.RestoreRequested-=OpenRestore;}
            DetailFields.Visibility=Visibility.Visible;backupsView=null;NotesHost.Content=null;notesView=null;saveSourcesView=null;NotesHost.Visibility=Visibility.Collapsed;
            UpdateButtons();return true;
        }
        finally{notesNavigationPending=false;LibraryNav.IsEnabled=SettingsNav.IsEnabled=true;}
    }
    private void OpenSaveSources(object sender,RoutedEventArgs e)
    {
        if(busy||editorOpen||stoppingObservation||quitPreparing||notesView is not null||saveSourcesView is not null||backupsView is not null||GameList.SelectedItem is not LibraryItemView item)return;
        saveSourcesView=new(discovery,item.Game.Id,item.Name,()=>quitPreparing||stoppingObservation||launches.GetState(item.Game.Id).IsActive||backups.IsWorking(item.Game.Id));
        saveSourcesView.BackRequested+=BackFromNotes;saveSourcesView.ManualEditRequested+=EditManualSources;
        NotesHost.Content=saveSourcesView;LibraryPanel.Visibility=SettingsPanel.Visibility=Visibility.Collapsed;NotesHost.Visibility=Visibility.Visible;DetailFields.Visibility=Visibility.Collapsed;
        OperationNotice.Text="Save sources · "+item.Name;UpdateButtons();
    }
    private async void EditManualSources(object? sender,EventArgs e)
    {
        if(busy||editorOpen||stoppingObservation||quitPreparing||sender is not SaveSourcesView view||launches.GetState(view.GameId).IsActive||backups.IsWorking(view.GameId))return;
        try
        {
            var game=await store.LoadProfileAsync(view.GameId);
            if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is null)throw new IOException("The profile needs attention before manual locations can be edited.");
            OpenEditor(game.Profile.Value);
        }
        catch(Exception error)when(IsExpected(error)){OperationNotice.Text=error.Message;}
    }
    private async Task RefreshSourceSummary(Guid id)
    {
        try
        {
            var result=await discovery.ReadAsync(id);
            if(GameList.SelectedItem is LibraryItemView item&&item.Game.Id==id)ApplySourceSummary(result);
        }
        catch(Exception error)when(IsExpected(error)){if(GameList.SelectedItem is LibraryItemView item&&item.Game.Id==id)DetailSaves.Text="Source discovery needs attention. "+error.Message;}
    }
    private void SourceDiscoveryChanged(object? sender,SaveDiscoveryChanged change)
    {
        if(Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.InvokeAsync(()=>{if(!stoppingObservation&&GameList.SelectedItem is LibraryItemView item&&item.Game.Id==change.GameId)ApplySourceSummary(change.Result);});
    }
    private void ApplySourceSummary(SaveDiscoveryResult result)
    {
        var sources=result.Phase==SaveDiscoveryPhase.Error?[]:result.Document.Value?.Sources.Where(s=>s.Selected).Select(s=>s.Path+" ("+s.Access+")").ToArray()??[];
        DetailSaves.Text=result.Summary+(sources.Length==0?"":"\n"+string.Join("\n",sources));
    }
    private void OpenBackupHistory(object sender,RoutedEventArgs e)
    {
        if(busy||editorOpen||stoppingObservation||quitPreparing||notesView is not null||saveSourcesView is not null||backupsView is not null||GameList.SelectedItem is not LibraryItemView item)return;
        backupsView=new(backupHistory,restores,item.Game.Id,item.Name,()=>quitPreparing||stoppingObservation||launches.GetState(item.Game.Id).IsActive||backups.IsWorking(item.Game.Id));
        backupsView.BackRequested+=BackFromNotes;backupsView.GameDataChanged+=NotesDataChanged;backupsView.RestoreRequested+=OpenRestore;
        NotesHost.Content=backupsView;LibraryPanel.Visibility=SettingsPanel.Visibility=Visibility.Collapsed;NotesHost.Visibility=Visibility.Visible;DetailFields.Visibility=Visibility.Collapsed;
        OperationNotice.Text="Backups · "+item.Name;UpdateButtons();
    }
    private void OpenRestore(object? sender,RestoreChoice choice)
    {
        if(busy||editorOpen||stoppingObservation||quitPreparing||backupsView is null||restoreView is not null)return;
        var id=backupsView.GameId;restoreView=new(restores,id,choice,()=>quitPreparing||stoppingObservation||launches.GetState(id).IsActive||backups.IsWorking(id));
        restoreView.BackRequested+=BackFromRestore;restoreView.GameDataChanged+=NotesDataChanged;NotesHost.Content=restoreView;
        OperationNotice.Text="Restore saves · review destinations and changes before applying.";UpdateButtons();
    }
    private async void BackFromRestore(object? sender,EventArgs e)
    {
        if(restoreView is null||!await restoreView.CanLeaveAsync())return;
        restoreView.BackRequested-=BackFromRestore;restoreView.GameDataChanged-=NotesDataChanged;restoreView=null;
        NotesHost.Content=backupsView;UpdateButtons();
    }
    private async void BackUpNow(object sender,RoutedEventArgs e)
    {
        if(busy||editorOpen||stoppingObservation||quitPreparing||GameList.SelectedItem is not LibraryItemView item)return;
        busy=true;UpdateButtons();
        try{var result=await backups.BackUpAsync(item.Game.Id,false);if(GameList.SelectedItem is LibraryItemView selected&&selected.Game.Id==item.Game.Id)ApplyBackupSummary(result);OperationNotice.Text=item.Name+": "+result.Message;}
        finally{EndOperation();}
    }
    private async Task RefreshBackupSummary(Guid id)
    {
        var revision=++backupViewRevision;
        var result=await backups.ReadAsync(id);
        if(revision==backupViewRevision&&!stoppingObservation&&!quitPreparing&&GameList.SelectedItem is LibraryItemView item&&item.Game.Id==id&&!backups.IsWorking(id))ApplyBackupSummary(result);
    }
    private void BackupChanged(object? sender,BackupResult result)
    {
        if(Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.InvokeAsync(()=>{if(stoppingObservation)return;RecordBackupAlert(result);if(backupsView is not null&&backupsView.GameId==result.GameId&&result.Phase!=BackupPhase.Working)_=backupsView.RefreshAsync();if(OverviewVisible&&overviewKind==NavKind.Backups&&result.Phase!=BackupPhase.Working)_=RefreshOverviewAsync();if(GameList.SelectedItem is LibraryItemView item&&item.Game.Id==result.GameId){backupViewRevision++;ApplyBackupSummary(result);}UpdateButtons();});
    }
    private void ApplyBackupSummary(BackupResult result)
    {
        BackupNotice.Text=result.Message;
        // The overview only speaks up when there is something to do.
        var attention=result.Phase is BackupPhase.Blocked or BackupPhase.Failed;
        AttentionCard.Visibility=attention?Visibility.Visible:Visibility.Collapsed;
        var confirm=attention&&SaveConfirmation.IsNeeded(result.Message);
        AttentionText.Text=!attention?"":confirm?result.Message:"The last save backup needs attention. "+result.Message;AttentionText.ToolTip=attention?result.Message:null;
        // The one button that solves it stands out.
        if(confirm)SaveSourcesButton.SetResourceReference(StyleProperty,"PrimaryButton");else SaveSourcesButton.ClearValue(StyleProperty);
        if(result.Phase==BackupPhase.Working&&result.LastSuccess is null)return;
        LastBackup.Text=result.LastSuccess is CompletedBackup backup?$"Last verified backup: {backup.Manifest.CreatedAt.ToLocalTime():g}\n{backup.Manifest.Files.Length} file(s) · {backup.Manifest.Files.Sum(f=>f.Size):N0} bytes\n{backup.Directory}":"No verified backup available.";
    }
    private void UpdateButtons()
    {
        ReduceMotionChoice.IsEnabled=CloseToTrayChoice.IsEnabled=BackupNotificationsChoice.IsEnabled=CheckForUpdatesChoice.IsEnabled=AutoUpdateChoice.IsEnabled=!busy&&!editorOpen&&!stoppingObservation&&!quitPreparing&&!settingsReadOnly;
        NotesHost.IsEnabled=!quitPreparing&&!stoppingObservation;
        QuitButton.IsEnabled=!stoppingObservation&&!quitPreparing;
        LibraryNav.IsEnabled=SettingsNav.IsEnabled=!editorOpen&&!quitPreparing&&!stoppingObservation;
        UpdateTray();
        UpdateCaptureControls();
        restoreView?.UpdateAvailability();
        backupsView?.UpdateAvailability();
        BackupHistoryButton.IsEnabled=!busy&&!editorOpen&&!stoppingObservation&&!quitPreparing&&GameList.SelectedItem is LibraryItemView;
        SaveSourcesButton.IsEnabled = !busy && !editorOpen && !stoppingObservation && !quitPreparing && GameList.SelectedItem is LibraryItemView;
        NotesButton.IsEnabled = !busy && !editorOpen && !stoppingObservation && !quitPreparing && GameList.SelectedItem is LibraryItemView;
        AddButton.IsEnabled = !busy && !editorOpen && !settingsReadOnly && !quitPreparing && !stoppingObservation;
        StoreImportButton.IsEnabled = AddButton.IsEnabled;
        BrowseThemesButton.IsEnabled = !busy && !editorOpen && !settingsReadOnly && !quitPreparing && !stoppingObservation;
        var selected = GameList.SelectedItem as LibraryItemView;
        var active = selected is not null && (launches.GetState(selected.Game.Id).IsActive||backups.IsWorking(selected.Game.Id));
        var editable = !busy && !editorOpen && !stoppingObservation && !quitPreparing && !active && selected is { Game.IsReadOnly: false, Game.Profile: not null };
        EditButton.IsEnabled = ArchiveButton.IsEnabled = LaunchSettingsButton.IsEnabled = editable;
        HeroStatusButton.IsEnabled = editable && CanChangeStatus(selected);
        BackupButton.IsEnabled = editable && selected is { Game.Profile.IsArchived: false };
        PlayButton.IsEnabled = editable && selected is { Game.ExecutableMissing: false, Game.Profile.IsArchived: false };
    }

    private void EndOperation() { busy = false; UpdateButtons(); ContinueDeferredQuit(); }
    private static bool IsExpected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException;

}
