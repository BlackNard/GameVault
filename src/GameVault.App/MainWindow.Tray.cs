using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using GameVault.Core;
namespace GameVault.App;

public partial class MainWindow
{
    private readonly TrayLifetime lifetime;
    private readonly BackupAlertPolicy backupAlerts=new();
    private readonly DispatcherTimer trayTimer=new(){Interval=TimeSpan.FromSeconds(10)};
    private TrayIconHost? tray;
    private bool trayInitialized,libraryLoaded,quitPreparing;
    private void InitializeTray(object sender,RoutedEventArgs e)
    {
        if(trayInitialized)return;trayInitialized=true;
        try
        {
            tray=new(()=>PostTray(()=>lifetime.Reopen()),()=>PostTray(RequestQuit),id=>PostTray(()=>ReviewBackupAlert(id)));
            trayTimer.Tick+=TrayTick;trayTimer.Start();UpdateTray();
        }
        catch(Exception error)when(IsTrayError(error)){DisposeTrayOnApplicationExit();OperationNotice.Text="The tray icon is unavailable. Keep the window open for tracking, or use Quit GameVault. "+error.Message;}

    }
    private void PostTray(Action action){if(!Dispatcher.HasShutdownStarted)_=Dispatcher.InvokeAsync(()=>{if(!observationStopped&&!stoppingObservation)action();});}
    private void RestoreWindow()
    {
        if(observationStopped||stoppingObservation)return;
        Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();
    }
    private async void BeforeClosing(object? sender,CancelEventArgs e)
    {
        if(observationStopped)return;e.Cancel=true;
        try
        {
            var result=await lifetime.CloseAsync(settings.CloseToTray,tray?.Available==true);
            if(result==WindowCloseResult.StayedOpen&&settings.CloseToTray&&tray?.Available!=true)OperationNotice.Text="The tray is unavailable, so GameVault stayed open. Use Quit GameVault to exit.";
        }
        catch(Exception error)when(IsTrayError(error)){RestoreWindow();OperationNotice.Text="Could not finish closing GameVault. "+error.Message;}
    }
    private void QuitClicked(object sender,RoutedEventArgs e)=>RequestQuit();
    private async void RequestQuit()
    {
        try{await lifetime.QuitAsync();}
        catch(Exception error)when(IsTrayError(error)){RestoreWindow();OperationNotice.Text="GameVault could not finish quitting. "+error.Message;}
    }
    private async Task<bool> PrepareToQuitAsync()
    {
        RestoreWindow();if(stoppingObservation)return false;
        if(!await LeaveNotes(detach:false))return false;
        if(busy||editorOpen){closeAfterSave=true;OperationNotice.Text=editorOpen?"Finish or cancel the editor; GameVault will then continue quitting.":"Finishing the current operation before quitting…";return false;}
        quitPreparing=true;UpdateButtons();var proceed=false;
        try
        {
            var count=launches.GetActiveStates().Length;
            if(count>0&&MessageBox.Show(this,$"{count} game session(s) are still active or starting.\n\nQuitting stops tracking and automatic backups. GameVault will save a checkpoint and leave the games open. Reopen it to recover supported running sessions.\n\nQuit GameVault?","Quit GameVault?",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return false;
            var warnings=await launches.CheckpointActiveAsync();
            if(warnings.Length>0&&MessageBox.Show(this,"The latest checkpoint could not be fully saved:\n\n"+string.Join("\n",warnings)+"\n\nThe last successful checkpoint is retained. Quit anyway?","Checkpoint needs attention",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return false;
            proceed=true;return true;
        }
        finally{if(!proceed){quitPreparing=false;UpdateButtons();}}
    }
    private async Task QuitRuntimeAsync()
    {
        stoppingObservation=true;busy=true;trayTimer.Stop();UpdateButtons();OperationNotice.Text="Saving checkpoints and finishing confirmed-exit backups. Running games stay open…";
        try
        {
            try{await launches.DisposeAsync();}
            finally
            {
                try{await discoveryCoordinator.DisposeAsync();}
                finally{try{await backupCoordinator.FlushAsync();}finally{await backupCoordinator.DisposeAsync();}}
            }
        }
        catch(Exception error)when(IsTrayError(error))
        {MessageBox.Show(this,"Some work could not finish while quitting. Existing checkpoints and recovery copies were preserved.\n\n"+error.Message,"GameVault shutdown needs attention",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally
        {
            backups.Changed-=BackupChanged;discovery.Changed-=SourceDiscoveryChanged;launches.StateChanged-=LaunchStateChanged;launches.SessionUpdated-=SessionUpdated;
            DisposeTrayOnApplicationExit();observationStopped=true;_=Dispatcher.BeginInvoke(new Action(Close));
        }
    }
    private void ContinueDeferredQuit()
    {if(closeAfterSave&&!busy&&!editorOpen&&!stoppingObservation){closeAfterSave=false;_=Dispatcher.BeginInvoke(new Action(RequestQuit));}}
    internal void DisposeTrayOnApplicationExit()
    {
        trayTimer.Stop();trayTimer.Tick-=TrayTick;
        try{tray?.Dispose();}catch(Exception error)when(IsTrayError(error)){System.Diagnostics.Debug.WriteLine(error.Message);}tray=null;
    }
    private void TrayTick(object? sender,EventArgs e)=>UpdateTray();
    private void UpdateTray()
    {
        if(stoppingObservation||observationStopped||quitPreparing)return;
        var pending=backupAlerts.Alerts;TrayAlertsButton.Visibility=pending.Length>0?Visibility.Visible:Visibility.Collapsed;TrayAlertsButton.Content=$"Backup alerts ({pending.Length})";
        try
        {
            var active=launches.GetActiveStates().Length;tray?.Update(active,pending);
            if(tray?.Available==true&&backupAlerts.TakeNotification(DateTimeOffset.UtcNow,settings.NotifyBackupFailures,active>0) is BackupAlert alert)tray.Notify(alert);
        }
        catch(Exception error)when(IsTrayError(error))
        {DisposeTrayOnApplicationExit();RestoreWindow();OperationNotice.Text="The tray icon stopped responding. GameVault is still running in this window. "+error.Message;}
    }
    private void RecordBackupAlert(BackupResult result)
    {backupAlerts.Record(result,items.FirstOrDefault(i=>i.Game.Id==result.GameId)?.Name??"Game",DateTimeOffset.UtcNow);UpdateTray();}
    private void ReviewNextAlert(object sender,RoutedEventArgs e){var alert=backupAlerts.Alerts.FirstOrDefault();if(alert is not null)ReviewBackupAlert(alert.GameId);}
    private async void ReviewBackupAlert(Guid id)
    {
        if(stoppingObservation||quitPreparing||busy||editorOpen)return;lifetime.Reopen();if(!await LeaveNotes())return;
        try
        {
            await RefreshLibrary();var item=items.FirstOrDefault(i=>i.Game.Id==id);
            if(item is null){OperationNotice.Text="That game profile is no longer available. Existing backup diagnostics were preserved.";return;}
            Search.Clear();StatusFilter.SelectedIndex=0;RemovedFilter.IsChecked=item.Game.Profile?.IsArchived==true;ApplyFilters(id);SetNavigation(true);
            backupAlerts.Acknowledge(id);OperationNotice.Text="Review the backup result. Check Save sources or use Back up now to retry when the game is closed.";UpdateTray();
        }
        catch(Exception error)when(IsTrayError(error)){OperationNotice.Text="Could not open backup diagnostics. "+error.Message;}
    }
    private async void TrayPreferencesChanged(object sender,RoutedEventArgs e)
    {
        if(initializing||busy||settingsReadOnly||stoppingObservation||quitPreparing)return;var close=CloseToTrayChoice.IsChecked==true;var notify=BackupNotificationsChoice.IsChecked==true;
        if(close==settings.CloseToTray&&notify==settings.NotifyBackupFailures)return;busy=true;UpdateButtons();
        try{var updated=settings with{CloseToTray=close,NotifyBackupFailures=notify};await store.SaveSettingsAsync(updated);settings=updated;OperationNotice.Text="Background preferences saved.";UpdateTray();}
        catch(Exception error)when(IsTrayError(error))
        {initializing=true;CloseToTrayChoice.IsChecked=settings.CloseToTray;BackupNotificationsChoice.IsChecked=settings.NotifyBackupFailures;initializing=false;OperationNotice.Text="Preferences could not be saved. "+error.Message;}
        finally{EndOperation();}
    }
    private static bool IsTrayError(Exception error)=>IsExpected(error)||error is Win32Exception or System.Runtime.InteropServices.COMException or NotSupportedException;
}