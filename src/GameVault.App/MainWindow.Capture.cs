using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using GameVault.Core;
using GameVault.Capture;
using GameVault.Infrastructure.Capture;

namespace GameVault.App;

public partial class MainWindow
{
    private CaptureCoordinator? capture;
    private CapturePreferences capturePreferences=CapturePreferences.Default;
    private CaptureHotkeySet? captureHotkeys;
    private HwndSource? captureHwnd;
    private CaptureOverlay? captureOverlay;
    private bool captureUiBusy,captureDisposed,capturePreferencesReadOnly;
    private readonly DispatcherTimer captureTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(1)};
    private readonly SemaphoreSlim mediaReadGate=new(1,1);
    private long mediaRevision;
    private string? captureFeedback;
    private bool? shortcutsSessionActive;
    private bool HasCaptureSession()=>launches.GetActiveStates().Any(state=>state.Phase==LaunchPhase.Running);
    private void UpdateCaptureSessionShortcuts()
    {
        if(captureHotkeys is null || captureDisposed)return;
        var active=HasCaptureSession();if(shortcutsSessionActive==active)return;
        shortcutsSessionActive=active;var result=captureHotkeys.Apply(capturePreferences,active);CaptureSettings.SetNotice(result.Message);
        if(!active)captureOverlay?.Hide();
    }
    private void InitializeCaptureFeatures()
    {
        capture=new(paths,()=>new WindowsCaptureBackend());capture.Changed+=CaptureChanged;
        launches.SessionEnded+=CaptureSessionEnded;
        GameCaptureControls.Command+=CaptureCommandRequested;
        CaptureSettings.ApplyRequested+=ApplyCapturePreferences;
        CaptureSettings.InstallRuntimeRequested+=(_,_)=>
        {
            try{CapturePrerequisites.OpenRuntimeInstaller(paths.Root);OperationNotice.Text="Microsoft runtime setup opened. Restart GameVault after installation if capture still reports missing components.";}
            catch(Exception error){OperationNotice.Text="Could not open capture runtime setup. "+error.Message;}
        };
        captureTimer.Tick+=CaptureTick;
        Loaded+=LoadCapturePreferences;
    }
    private async void LoadCapturePreferences(object sender,RoutedEventArgs e)
    {
        try
        {
            var saved=await store.LoadCapturePreferencesAsync();
            if(captureDisposed)return;
            capturePreferences=saved.Value??CapturePreferences.Default;capturePreferencesReadOnly=saved.IsReadOnly;
            CaptureSettings.SetPreferences(capturePreferences);CaptureSettings.SetEditingEnabled(!capturePreferencesReadOnly);
            captureHwnd=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            captureHwnd?.AddHook(CaptureMessage);
            captureHotkeys=new(new CaptureHotkeyPlatform(new WindowInteropHelper(this).Handle));
            var result=captureHotkeys.Apply(capturePreferences,HasCaptureSession());
            shortcutsSessionActive=HasCaptureSession();
            CaptureSettings.SetNotice(saved.Message??result.Message);
            captureTimer.Start();UpdateCaptureControls();
        }
        catch(Exception error){CaptureSettings.SetNotice("Capture shortcuts could not initialize. Launcher controls remain available. "+error.Message);}
    }
    private nint CaptureMessage(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        if(message!=0x0312 || InlinePrompts.IsOpen(this))return 0;
        var number=wParam.ToInt64()-CaptureHotkeySet.IdBase;
        if(number<1 || number>3 || captureHotkeys?.IsRegistered((CaptureCommand)number)!=true)return 0;
        handled=true;
        _=Dispatcher.InvokeAsync(()=>RunCaptureCommandAsync((CaptureCommand)number));
        return 0;
    }
    private void CaptureCommandRequested(object? sender,CaptureCommand command)=>_=RunCaptureCommandAsync(command);
    private Guid? CaptureGameId()
    {
        if(capture?.ActiveProfileId is Guid recording)return recording;
        if(GameList.SelectedItem is LibraryItemView selected && launches.GetState(selected.Game.Id).Phase==LaunchPhase.Running)return selected.Game.Id;
        var active=launches.GetActiveStates().Where(state=>state.Phase==LaunchPhase.Running).ToArray();
        return active.Length==1?active[0].GameId:null;
    }
    private async Task<(Guid Profile,CaptureTarget Target)> RequireCaptureTargetAsync()
    {
        var id=CaptureGameId()??throw new InvalidOperationException("Select a running game before capturing. If several games are running, select the game you want.");
        var state=launches.GetState(id);var progress=launches.GetSessionProgress(id);
        if(state.Phase!=LaunchPhase.Running || progress is not{IsActive:true})throw new InvalidOperationException("Wait until the game session is running.");
        var journals=await store.LoadSessionJournalsAsync(id);
        var journal=journals.SingleOrDefault(item=>item.Id==progress.SessionId)?.Document;
        if(journal?.State!=DocumentState.Ready || journal.Value is not{State:SessionState.Active} actual || actual.ProcessId!=state.ProcessId)
            throw new IOException("The game session identity is not ready. Retry after its next tracking update.");
        var monitor=capture?.ActiveProfileId==id?capture.ActiveTarget?.MonitorDeviceName:GameCaptureControls.ExplicitMonitor;
        return(id,new(actual.SessionId,actual.ProcessId,actual.ProcessCreatedUtcTicks,monitor));
    }
    private async Task RunCaptureCommandAsync(CaptureCommand command)
    {
        if(captureDisposed || capture is null || stoppingObservation || quitPreparing || editorOpen || InlinePrompts.IsOpen(this))return;
        if(command==CaptureCommand.Overlay)
        {
            try{ToggleCaptureOverlay();}catch(Exception error){OperationNotice.Text="Capture controls could not open. "+error.Message;}
            return;
        }
        if(captureUiBusy)return;
        captureUiBusy=true;captureFeedback=null;UpdateCaptureControls();
        try
        {
            if(command==CaptureCommand.Recording && capture.ActiveProfileId is not null){await FinishCaptureAsync();return;}
            var target=await RequireCaptureTargetAsync();
            if(command==CaptureCommand.Screenshot)
            {
                OperationNotice.Text="Capturing screenshot…";
                var recorder=capture;
                var media=await Task.Run(()=>recorder.ScreenshotAsync(target.Profile,target.Target));
                OperationNotice.Text="Screenshot saved for "+CaptureName(target.Profile)+" at "+media.CapturedUtc.ToLocalTime().ToString("t")+".";
            }
            else
            {
                var height=capturePreferences.Height;
                var options=new CaptureOptions(capturePreferences.GameAudio?CaptureAudioMode.ProcessOnly:CaptureAudioMode.None,true,height==720?1280:1920,height,capturePreferences.FramesPerSecond);
                var recorder=capture;
                await Task.Run(()=>recorder.StartAsync(target.Profile,target.Target,options));
                OperationNotice.Text="Recording "+CaptureName(target.Profile)+". Use the recording shortcut or Stop recording to finish.";
            }
            await RefreshCaptureMediaAsync();
        }
        catch(Exception error){captureFeedback="Capture needs attention. "+error.Message;OperationNotice.Text=captureFeedback;}
        finally{captureUiBusy=false;UpdateCaptureControls();ContinueDeferredQuit();}
    }
    private string CaptureName(Guid id)=>items.FirstOrDefault(item=>item.Game.Id==id)?.Name??"game session";
    private async Task FinishCaptureAsync()
    {
        if(capture?.ActiveProfileId is null)return;
        try
        {
            var recorder=capture;var media=await Task.Run(()=>recorder.StopAsync());
            if(media is not null)OperationNotice.Text="Recording saved for "+CaptureName(media.ProfileId)+".";
        }
        catch(Exception error){OperationNotice.Text="Recording could not be finalized. Its incomplete file was retained. "+error.Message;}
        await RefreshCaptureMediaAsync();UpdateCaptureControls();
    }
    private void CaptureSessionEnded(object? sender,GameSessionEnded session)
    {
        if(Dispatcher.HasShutdownStarted || captureDisposed)return;
        _=Dispatcher.InvokeAsync(async()=>
        {
            if(!captureUiBusy && capture?.ActiveProfileId==session.GameId)
            {
                captureUiBusy=true;
                try{await FinishCaptureAsync();}finally{captureUiBusy=false;ContinueDeferredQuit();}
            }
            UpdateCaptureControls();
        });
    }
    private async void CaptureTick(object? sender,EventArgs e)
    {
        if(captureDisposed || capture is null)return;
        UpdateCaptureSessionShortcuts();
        if(!captureUiBusy && capture.ActiveProfileId is Guid id && !launches.GetState(id).IsActive)
        {
            captureUiBusy=true;
            try{await FinishCaptureAsync();}finally{captureUiBusy=false;ContinueDeferredQuit();}
        }
        if(IsVisible || captureOverlay?.IsVisible==true)UpdateCaptureControls();
    }
    private void CaptureChanged(object? sender,EventArgs e)
    {
        if(Dispatcher.HasShutdownStarted || captureDisposed)return;
        _=Dispatcher.InvokeAsync(async()=>
        {
            if(captureDisposed)return;UpdateCaptureControls();
            if(capture?.LastError is string error)OperationNotice.Text="Capture needs attention. "+error;
            await RefreshCaptureMediaAsync();
        });
    }
    private void UpdateCaptureControls()
    {
        if(captureDisposed || capture is null)return;
        var id=CaptureGameId();var active=capture.ActiveProfileId is not null;
        var target=id is Guid game?CaptureName(game):"Select a running game";
        var elapsed=capture.RecordingStartedUtc is DateTimeOffset start?DateTimeOffset.UtcNow-start:TimeSpan.Zero;
        var message=capture.State switch
        {
            CaptureState.Recording=>$"Recording · {Math.Max(0,(long)elapsed.TotalHours):00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}",
            CaptureState.Starting=>"Preparing capture…",CaptureState.Stopping=>"Finalizing recording…",
            CaptureState.Failed=>capture.LastError??"Capture needs attention",_=>captureFeedback??capture.LastError??"PNG screenshots · MP4 recordings"
        };
        if(active)target="Recording: "+target;
        var blocked=captureUiBusy||stoppingObservation||quitPreparing;
        GameCaptureControls.Update(target,message,id is not null,active,blocked);
        captureOverlay?.Controls.Update(target,message,id is not null,active,blocked,true);
    }
    private void ToggleCaptureOverlay()
    {
        if(captureOverlay is null)
        {
            captureOverlay=new(settings.Theme);captureOverlay.Controls.Command+=CaptureCommandRequested;
        }
        if(captureOverlay.IsVisible)captureOverlay.Hide();else{UpdateCaptureControls();captureOverlay.Show();}
    }
    private async void ApplyCapturePreferences(object? sender,CapturePreferences value)
    {
        if(capturePreferencesReadOnly || captureUiBusy || capture?.ActiveProfileId is not null){CaptureSettings.SetNotice("Stop recording before changing capture settings.");return;}
        captureUiBusy=true;CaptureSettings.SetEditingEnabled(false);
        var old=capturePreferences;
        try
        {
            var result=captureHotkeys?.Apply(value,HasCaptureSession())??new(false,"Shortcuts are unavailable. Reopen GameVault and retry.");
            if(!result.Success){CaptureSettings.SetNotice(result.Message);return;}
            try{await store.SaveCapturePreferencesAsync(value);capturePreferences=value;shortcutsSessionActive=HasCaptureSession();CaptureSettings.SetNotice("Capture settings saved. "+result.Message);}
            catch(Exception error)
            {
                var restored=captureHotkeys?.Apply(old,HasCaptureSession());CaptureSettings.SetNotice("Settings could not be saved. "+error.Message+" "+restored?.Message);CaptureSettings.SetPreferences(old);
            }
        }
        finally{captureUiBusy=false;CaptureSettings.SetEditingEnabled(!capturePreferencesReadOnly);UpdateCaptureControls();ContinueDeferredQuit();}
    }
    private async void CaptureMediaExpanded(object sender,RoutedEventArgs e)=>await RefreshCaptureMediaAsync();
    private async Task RefreshCaptureMediaAsync()
    {
        if(captureDisposed || capture is null || libraryVisible || gameSection!="Media" || GamePage.Visibility!=Visibility.Visible || !CaptureMediaExpander.IsExpanded)return;
        var revision=++mediaRevision;
        var id=(GameList.SelectedItem as LibraryItemView)?.Game.Id;
        await mediaReadGate.WaitAsync();
        try
        {
            if(captureDisposed || revision!=mediaRevision)return;
            var media=id is Guid game?await capture.ListMediaAsync(game):[];
            if(captureDisposed || revision!=mediaRevision || (GameList.SelectedItem as LibraryItemView)?.Game.Id!=id)return;
            CaptureMediaList.ItemsSource=media.Take(1000).Select(item=>new MediaRow(item)).ToArray();
            CaptureMediaNotice.Text=media.Length==0?"No captures for this game yet.":$"{media.Length} captures · incomplete files are retained but not marked ready.";
        }
        catch(Exception error){if(revision==mediaRevision)CaptureMediaNotice.Text="Captures could not be listed. "+error.Message;}
        finally{mediaReadGate.Release();}
    }
    private async void OpenCaptureMedia(object sender,RoutedEventArgs e)
    {
        if(capture is null || CaptureMediaList.SelectedItem is not MediaRow row || row.Media.Status!=CaptureMediaStatus.Complete)return;
        try
        {
            var current=(await capture.ListMediaAsync(row.Media.ProfileId)).SingleOrDefault(item=>item.Id==row.Media.Id && item.Status==CaptureMediaStatus.Complete)
                ??throw new IOException("Capture is no longer available.");
            Process.Start(new ProcessStartInfo(current.FilePath){UseShellExecute=true});
        }
        catch(Exception error){OperationNotice.Text="Capture could not open. "+error.Message;}
    }
    private void CaptureMediaSelected(object sender,SelectionChangedEventArgs e)=>OpenCaptureMediaButton.IsEnabled=CaptureMediaList.SelectedItem is MediaRow{Media.Status:CaptureMediaStatus.Complete};
    private async Task PrepareCaptureShutdownAsync()
    {
        captureTimer.Stop();await FinishCaptureAsync();
    }
    internal void DisposeCaptureFeatures()
    {
        if(captureDisposed)return;captureDisposed=true;++mediaRevision;captureTimer.Stop();captureTimer.Tick-=CaptureTick;
        if(capture is not null)capture.Changed-=CaptureChanged;
        launches.SessionEnded-=CaptureSessionEnded;captureHotkeys?.Dispose();captureHotkeys=null;
        captureHwnd?.RemoveHook(CaptureMessage);captureHwnd=null;
        captureOverlay?.DisposeOverlay();captureOverlay=null;
    }
    private sealed record MediaRow(CaptureMedia Media)
    {
        public override string ToString()=>$"{Media.CapturedUtc.ToLocalTime():g} · {Media.Kind}"+(Media.Status==CaptureMediaStatus.Complete?"":" · Incomplete");
    }
}
