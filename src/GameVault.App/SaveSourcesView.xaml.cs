using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Saves;
namespace GameVault.App;
public partial class SaveSourcesView:UserControl
{
    private readonly SaveDiscoveryService discovery;private readonly Func<bool> isGameActive;
    private SaveDiscoveryResult? result;private bool changing=true,busy;private TaskCompletionSource? idle;
    public Guid GameId{get;}
    public event EventHandler? BackRequested;
    public event EventHandler? ManualEditRequested;
    public SaveSourcesView(SaveDiscoveryService discovery,Guid gameId,string gameName,Func<bool> isGameActive)
    {
        this.discovery=discovery;GameId=gameId;this.isGameActive=isGameActive;InitializeComponent();Heading.Text=gameName+" · Save sources";
        Loaded+=async(_,_)=>{discovery.Changed+=DiscoveryChanged;changing=false;await RefreshAsync();};
        Unloaded+=(_,_)=>discovery.Changed-=DiscoveryChanged;
    }
    private void SetBusy(bool value)
    {
        if(value&&!busy)idle=new(TaskCreationOptions.RunContinuationsAsynchronously);if(!value)idle?.TrySetResult();busy=value;
        RefreshButton.IsEnabled=BackButton.IsEnabled=!value;
        Automatic.IsEnabled=!value&&result?.Document.IsReadOnly!=true;
        ManualButton.IsEnabled=!value&&!isGameActive();UpdateConfirm();
    }
    public Task RefreshAsync()=>Act(()=>discovery.ScanAsync(GameId));
    private async void RefreshSources(object sender,RoutedEventArgs e)=>await RefreshAsync();
    private async Task Act(Func<Task<SaveDiscoveryResult>> action)
    {
        if(busy)return;SetBusy(true);ActionNotice.Text="Checking save locations…";
        try{Apply(await action());ActionNotice.Text="Source check complete. Game files were left unchanged.";}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException){ActionNotice.Text="Could not update save sources. "+e.Message;Apply(await discovery.ReadAsync(GameId));}
        finally{SetBusy(false);}
    }
    private void DiscoveryChanged(object? sender,SaveDiscoveryChanged change)
    {
        if(change.GameId!=GameId||Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.InvokeAsync(()=>{if(!busy&&IsLoaded)Apply(change.Result);});
    }
    private void Apply(SaveDiscoveryResult value)
    {
        if(result?.Document.Value is SaveDiscoveryDocument old&&value.Document.Value is SaveDiscoveryDocument next&&next.Revision<old.Revision)return;
        result=value;Summary.Text=value.Summary;changing=true;Automatic.IsChecked=value.Document.Value?.Automatic??true;changing=false;
        CheckedAt.Text=value.Document.Value is SaveDiscoveryDocument doc?$"Last check: {doc.ScannedAt.ToLocalTime():g} · sources are verified again before backup use":"No completed source check yet";
        Diagnostics.Text=string.Join("\n",new[]{value.Document.Message}.Concat(value.Document.Value?.Warnings??[]).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct());
        BindRows();Automatic.IsEnabled=!busy&&!value.Document.IsReadOnly;ManualButton.IsEnabled=!busy&&!isGameActive();UpdateConfirm();
    }
    private void BindRows()
    {
        var selected=(Sources.SelectedItem as SourceRow)?.Value.Id;var mappings=result?.Document.Value?.Sources??[];
        var visible=mappings.Where(s=>ShowPending.IsChecked==true||s.Selected||s.CanConfirm||s.Message==SaveSourceWords.Rejected||s.Origin==SaveSourceOrigin.KnownRule||s.Access is SaveSourceAccess.Unreadable or SaveSourceAccess.Unsafe or SaveSourceAccess.Limited)
            .OrderByDescending(s=>s.Selected).ThenByDescending(s=>s.Access==SaveSourceAccess.Ready).ThenBy(s=>s.Path,StringComparer.OrdinalIgnoreCase).Select(s=>new SourceRow(s)).ToArray();
        Sources.ItemsSource=visible;Sources.SelectedItem=visible.FirstOrDefault(s=>s.Value.Id==selected)??visible.FirstOrDefault();EmptyNotice.Visibility=visible.Length==0?Visibility.Visible:Visibility.Collapsed;
    }
    private void FilterChanged(object sender,RoutedEventArgs e){if(!changing)BindRows();}
    private void SourceSelected(object sender,SelectionChangedEventArgs e)=>UpdateConfirm();
    private void UpdateConfirm()
    {
        if(ConfirmButton is null||RejectButton is null||RestoreFolderButton is null)return;
        var writable=!busy&&result?.Document.IsReadOnly==false;var row=(Sources.SelectedItem as SourceRow)?.Value;var rejected=row?.Message==SaveSourceWords.Rejected;
        ConfirmButton.IsEnabled=writable&&row is{CanConfirm:true,Selected:false,Access:SaveSourceAccess.Ready};
        RejectButton.IsEnabled=writable&&!rejected&&row is not null&&row.Origin!=SaveSourceOrigin.Manual&&(row.Selected||row.CanConfirm);
        RestoreFolderButton.Visibility=rejected?Visibility.Visible:Visibility.Collapsed;RestoreFolderButton.IsEnabled=writable&&rejected;
    }
    private async void RejectSource(object sender,RoutedEventArgs e){if(Sources.SelectedItem is SourceRow row&&RejectButton.IsEnabled)await Act(()=>discovery.RejectAsync(GameId,row.Value.Id));}
    private async void RestoreSource(object sender,RoutedEventArgs e){if(Sources.SelectedItem is SourceRow row&&RestoreFolderButton.IsEnabled)await Act(()=>discovery.RestoreAsync(GameId,row.Value.Path));}
    private async void ConfirmSource(object sender,RoutedEventArgs e){if(Sources.SelectedItem is SourceRow row&&ConfirmButton.IsEnabled)await Act(()=>discovery.ConfirmAsync(GameId,row.Value.Id));}
    private async void AutomaticChanged(object sender,RoutedEventArgs e){if(!changing&&!busy)await Act(()=>discovery.SetAutomaticAsync(GameId,Automatic.IsChecked==true));}
    private void EditManual(object sender,RoutedEventArgs e){if(!busy&&!isGameActive())ManualEditRequested?.Invoke(this,EventArgs.Empty);}
    private void Back(object sender,RoutedEventArgs e)=>BackRequested?.Invoke(this,EventArgs.Empty);
    public async Task<bool> CanLeaveAsync(){while(busy&&idle is not null)await idle.Task;return true;}
    private sealed record SourceRow(SaveSourceMapping Value)
    {
        public string Path=>Value.Path;
        public string State=>$"{(Value.Selected?"Selected · ":"")}{(Value.Access==SaveSourceAccess.Ready?"Readable":Value.Access==SaveSourceAccess.NotChecked?"Not checked":Value.Access.ToString())} · {Value.Kind} · "+
            SaveSourceWords.Origin(Value);
        public string Explanation=>Value.Evidence+(Value.Message is null||Value.Message==SaveSourceWords.Rejected?"":"\n"+Value.Message);
    }
}