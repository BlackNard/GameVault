using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Saves;
namespace GameVault.App;

public partial class RestoreView:UserControl
{
    private readonly RestoreService restores;private readonly Guid id;private readonly Func<bool> active;
    private RestoreChoice choice;private RestorePreview? preview;private RestoreResult? last;private bool busy,changing=true;private CancellationTokenSource? stop;private TaskCompletionSource? idle;
    public event EventHandler? BackRequested;public event EventHandler? GameDataChanged;
    public RestoreView(RestoreService restores,Guid id,RestoreChoice choice,Func<bool> active)
    {this.restores=restores;this.id=id;this.choice=choice;this.active=active;InitializeComponent();Loaded+=async(_,_)=>{changing=false;await LoadTargets();};}
    private async Task LoadTargets()
    {
        if(busy)return;SetBusy(true);
        try
        {
            var sources=await restores.GetTargetsAsync(id,choice,stop!.Token);changing=true;Mappings.ItemsSource=sources.Select(s=>new MappingRow(s.Id,s.Kind,s.Path)).ToArray();Extras.ItemsSource=null;Changes.ItemsSource=null;preview=null;Confirm.IsChecked=false;
            Heading.Text=choice.RecoveryId is null?"Restore saved progress":"Undo a restore";SelectedIdentity.Text="Selected "+(choice.BackupId is not null?"backup ":"recovery ")+(choice.BackupId??choice.RecoveryId)?.ToString("N");Notice.Text="Review the destination paths, then preview.";
        }
        catch(Exception error)when(Expected(error)){Notice.Text=error.Message;}
        finally{changing=false;SetBusy(false);}
    }
    private RestoreRequest Request()=>new(id,choice,((MappingRow[]?)Mappings.ItemsSource??[]).Select(m=>new RestoreDestination(m.SourceId,m.Target)).ToArray(),((ExtraRow[]?)Extras.ItemsSource??[]).Where(e=>e.Remove).Select(e=>e.Path).ToArray());
    private async void PreviewChanges(object sender,RoutedEventArgs e)
    {
        if(busy||active())return;SetBusy(true);
        try
        {
            preview=await restores.PreviewAsync(Request(),stop!.Token);changing=true;SelectedIdentity.Text=preview.SelectedLabel;Changes.ItemsSource=preview.Changes;Extras.ItemsSource=preview.OtherFiles.Select(e=>new ExtraRow(e.Path,e.Remove)).ToArray();Confirm.IsChecked=false;
            PreviewSummary.Text=$"{preview.Changes.Count(c=>c.Action==RestoreAction.Write)} file(s) to create/replace · {preview.Changes.Count(c=>c.Action==RestoreAction.Remove)} to remove · {preview.OtherFiles.Count(f=>!f.Remove)} unrecognized file(s) kept.";Notice.Text="Preview verified. Confirm only after reviewing the destinations and changes.";
        }
        catch(Exception error)when(Expected(error)){preview=null;Notice.Text=error.Message;}
        finally{changing=false;SetBusy(false);}
    }
    private async void RestoreSaves(object sender,RoutedEventArgs e)
    {
        if(busy||active()||preview is null||Confirm.IsChecked!=true)return;var fingerprint=preview.Fingerprint;var request=Request();SetBusy(true);
        try
        {
            last=await restores.ExecuteAsync(request,fingerprint,stop!.Token);Notice.Text=last.Message+(last.RecoveryDirectory is null?"":"\nRecovery copy: "+last.RecoveryDirectory);preview=null;Confirm.IsChecked=false;UndoButton.Visibility=last.RecoveryId is not null?Visibility.Visible:Visibility.Collapsed;GameDataChanged?.Invoke(this,EventArgs.Empty);
        }
        catch(Exception error)when(Expected(error)){preview=null;Notice.Text=error.Message;}
        finally{SetBusy(false);}
    }
    private async void PreviewUndo(object sender,RoutedEventArgs e){if(busy||last?.RecoveryId is not Guid recoveryId)return;choice=new(null,recoveryId);last=null;UndoButton.Visibility=Visibility.Collapsed;await LoadTargets();}
    private void InputsChanged(object sender,RoutedEventArgs e){if(changing)return;preview=null;Confirm.IsChecked=false;PreviewSummary.Text="Inputs changed. Preview again before restoring.";UpdateAvailability();}
    private void ConfirmationChanged(object sender,RoutedEventArgs e){if(!changing)UpdateAvailability();}
    public void UpdateAvailability(){if(changing)return;Fields.IsEnabled=!busy;BackButton.IsEnabled=!busy;PreviewButton.IsEnabled=!busy&&!active()&&Mappings.ItemsSource is MappingRow[] {Length:>0};RestoreButton.IsEnabled=!busy&&!active()&&preview is not null&&Confirm.IsChecked==true;CancelButton.Visibility=busy?Visibility.Visible:Visibility.Collapsed;}
    private void SetBusy(bool value)
    {
        if(value&&!busy){idle=new(TaskCreationOptions.RunContinuationsAsynchronously);stop=new();}busy=value;
        if(!value){stop?.Dispose();stop=null;idle?.TrySetResult();}UpdateAvailability();
    }
    private void BrowseDestination(object sender,RoutedEventArgs e)
    {
        if(busy||sender is not FrameworkElement{DataContext:MappingRow row})return;
        try
        {
            string? picked;
            if(row.Kind==SaveSourceKind.Folder)picked=NativePickers.Folder(Window.GetWindow(this),"Choose save destination folder",row.Target);
            else
            {
                var dialog=new Microsoft.Win32.SaveFileDialog{Title="Choose save destination file",Filter="All files (*.*)|*.*",OverwritePrompt=false,FileName=Path.GetFileName(row.Target)};
                var parent=Path.GetDirectoryName(row.Target);if(parent is not null&&Directory.Exists(parent))dialog.InitialDirectory=parent;picked=dialog.ShowDialog(Window.GetWindow(this))==true?dialog.FileName:null;
            }
            if(picked is null)return;row.Target=picked;Mappings.Items.Refresh();InputsChanged(this,new RoutedEventArgs());
        }
        catch(Exception error)when(Expected(error)||error is System.Runtime.InteropServices.COMException){Notice.Text="The browser could not open. You can enter a full destination path. "+error.Message;}
    }
    public async Task<bool> CanLeaveAsync(){if(busy&&idle is not null)await idle.Task;return true;}
    private void Back(object sender,RoutedEventArgs e){if(!busy)BackRequested?.Invoke(this,EventArgs.Empty);}
    private void Cancel(object sender,RoutedEventArgs e)=>stop?.Cancel();
    private static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or OperationCanceledException or NotSupportedException;
    private sealed class MappingRow(Guid id,SaveSourceKind kind,string path){public Guid SourceId{get;}=id;public SaveSourceKind Kind{get;}=kind;public string Target{get;set;}=path;public string OriginalPath{get;}=path;public string Label=>$"{Kind} · original location: {OriginalPath}";}
    private sealed class ExtraRow(string path,bool remove){public string Path{get;}=path;public bool Remove{get;set;}=remove;}
}