using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Saves;
namespace GameVault.App;

public partial class BackupsView:UserControl
{
    private readonly BackupHistoryService history;
    private readonly Func<bool> active;
    private BackupHistorySnapshot? snapshot;
    private bool changing=true,busy;
    private TaskCompletionSource? idle;
    public Guid GameId{get;}
    public event EventHandler? BackRequested;
    public event EventHandler? GameDataChanged;
    public BackupsView(BackupHistoryService history,Guid id,string name,Func<bool> active)
    {
        this.history=history;this.active=active;GameId=id;InitializeComponent();Heading.Text=name+" · Backups";
        Loaded+=async(_,_)=>{changing=false;await RefreshAsync();};
    }
    public async Task RefreshAsync()
    {
        if(busy)return;SetBusy(true);
        try{Apply(await history.ListAsync(GameId));}
        catch(Exception e)when(Expected(e)){Notice.Text="History could not be refreshed. "+e.Message;}
        finally{SetBusy(false);}
    }
    private async void RefreshHistory(object sender,RoutedEventArgs e)=>await RefreshAsync();
    private void Apply(BackupHistorySnapshot value)
    {
        var selected=(HistoryList.SelectedItem as BackupRow)?.Entry.Backup.Manifest.Id;changing=true;snapshot=value;
        HistoryList.ItemsSource=value.Entries.Select(e=>new BackupRow(e,value.CanChange)).ToArray();
        HistoryList.SelectedItem=((BackupRow[])HistoryList.ItemsSource).FirstOrDefault(r=>r.Entry.Backup.Manifest.Id==selected)??((BackupRow[])HistoryList.ItemsSource).FirstOrDefault();
        Limit.Text=(value.Policy?.Limit??20).ToString(CultureInfo.InvariantCulture);KeepEverything.IsChecked=value.Policy?.KeepEverything==true;
        StorageSummary.Text=$"{value.Entries.Length} completed · {Size(value.TotalBytes)} recorded save data · "+(value.CanChange?$"{value.Entries.Count(e=>e.Pinned)} pinned":"pin status unavailable");
        Warnings.Text=string.Join("\n",value.Warnings.Concat(value.RecoveryFolders>0?new[]{$"{value.RecoveryFolders} recovery folder(s) preserved. Their size is excluded from the completed total."}:[]));
        EmptyNotice.Visibility=value.Entries.Length==0?Visibility.Visible:Visibility.Collapsed;
        changing=false;UpdateAvailability();
    }
    private BackupRetentionPolicy? Draft()
    {
        if(!int.TryParse(Limit.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var limit)||limit<=0)
            return KeepEverything.IsChecked==true&&snapshot?.Policy is BackupRetentionPolicy stored?stored with{KeepEverything=true}:null;
        return new(limit,KeepEverything.IsChecked==true);
    }
    public void UpdateAvailability()
    {
        if(changing)return;var canChange=!busy&&!active()&&snapshot?.CanChange==true;
        RefreshButton.IsEnabled=BackButton.IsEnabled=!busy;KeepEverything.IsEnabled=canChange;Limit.IsEnabled=canChange&&KeepEverything.IsChecked!=true;
        var draft=Draft();ApplyButton.IsEnabled=canChange&&draft is not null;
        PinButton.IsEnabled=canChange&&HistoryList.SelectedItem is BackupRow;
        if(draft is null)RetentionPreview.Text="Enter a positive whole number of unpinned backups to keep.";
        else if(draft.KeepEverything)RetentionPreview.Text="All completed copies will be kept. Nothing will be removed.";
        else
        {
            var latest=snapshot?.Entries.FirstOrDefault()?.Backup.Manifest.Id;
            var count=snapshot?.Entries.Where(e=>!e.Pinned).Skip(draft.Limit).Count(e=>e.Backup.Manifest.Id!=latest)??0;
            RetentionPreview.Text=$"Apply keeps the newest {draft.Limit} unpinned copies and every pinned copy. Up to {count} older copy/copies will be removed after safety checks. Recovery folders are kept.";
        }
        UpdateSelection();
    }
    private void SetBusy(bool value)
    {
        if(value&&!busy)idle=new(TaskCreationOptions.RunContinuationsAsynchronously);busy=value;if(!value)idle?.TrySetResult();UpdateAvailability();
    }
    private void DraftChanged(object sender,RoutedEventArgs e){if(!changing)UpdateAvailability();}
    private void SelectionChanged(object sender,SelectionChangedEventArgs e)=>UpdateSelection();
    private void UpdateSelection()
    {
        var row=HistoryList.SelectedItem as BackupRow;PinButton.Content=row?.Entry.Pinned==true?"Unpin backup":"Pin backup";
        PinButton.IsEnabled=!busy&&!active()&&snapshot?.CanChange==true&&row is not null;
        SelectedInfo.Text=row is null?"Select a backup to keep it beyond the retention limit.":$"Backup ID: {row.Entry.Backup.Manifest.Id:N}\n{row.Entry.Backup.Directory}";
    }
    private async void TogglePin(object sender,RoutedEventArgs e)
    {
        if(busy||active()||snapshot?.CanChange!=true||HistoryList.SelectedItem is not BackupRow row)return;SetBusy(true);
        try{Apply(await history.SetPinnedAsync(GameId,row.Entry.Backup.Manifest.Id,!row.Entry.Pinned));Notice.Text=row.Entry.Pinned?"Backup unpinned. No cleanup was run.":"Backup pinned and protected from retention.";GameDataChanged?.Invoke(this,EventArgs.Empty);}
        catch(Exception error)when(Expected(error)){Notice.Text=error.Message;Apply(await history.ListAsync(GameId));}
        finally{SetBusy(false);}
    }
    private async void ApplyRetention(object sender,RoutedEventArgs e)
    {
        if(busy||active()||snapshot?.CanChange!=true||snapshot.Policy is not BackupRetentionPolicy expected||Draft() is not BackupRetentionPolicy draft)return;SetBusy(true);
        try{var result=await history.ApplyRetentionAsync(GameId,draft,expected);Apply(result.History);Notice.Text=result.Message;GameDataChanged?.Invoke(this,EventArgs.Empty);}
        catch(Exception error)when(Expected(error)){Notice.Text=error.Message;Apply(await history.ListAsync(GameId));}
        finally{SetBusy(false);}
    }
    public async Task<bool> CanLeaveAsync(){if(busy&&idle is not null)await idle.Task;return true;}
    private void Back(object sender,RoutedEventArgs e){if(!busy)BackRequested?.Invoke(this,EventArgs.Empty);}
    private static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException or OperationCanceledException;
    internal static string Size(long bytes)=>bytes<1024?$"{bytes:N0} B":bytes<1024L*1024?$"{bytes/1024d:N1} KiB":bytes<1024L*1024*1024?$"{bytes/(1024d*1024):N1} MiB":$"{bytes/(1024d*1024*1024):N1} GiB";
    private sealed record BackupRow(BackupHistoryEntry Entry,bool PinsKnown)
    {
        public string Date=>Entry.Backup.Manifest.CreatedAt.ToLocalTime().ToString("G",CultureInfo.CurrentCulture)+" "+Entry.Backup.Manifest.CreatedAt.ToLocalTime().ToString("zzz",CultureInfo.CurrentCulture);
        public string Detail=>$"{Size(Entry.SizeBytes)} · {Entry.Backup.Manifest.Files.Length} file(s) · {Entry.Backup.Manifest.Id.ToString("N")[..8]}";
        public string PinState=>!PinsKnown?"Pin status unavailable":Entry.Pinned?"Pinned":"Unpinned";
    }
}