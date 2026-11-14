using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
namespace GameVault.App;

public partial class NotesView
{
    private HistoryData? history;
    private HistorySummary? summary;
    private SessionRow[] rows=[];
    private DurationCorrectionDraft? correction;
    private Guid correctionSession;
    private bool correctionChanging;
    private static string Duration(long seconds)=>LibraryItemView.FormatSeconds(seconds);
    private void BindRows(Guid? id)
    {
        var previousChanging=changing;changing=true;
        IEnumerable<SessionRow> sorted=SortChoice.SelectedIndex switch
        {
            1=>rows.OrderByDescending(r=>r.Detail?.AdjustedSeconds??r.Row.Session?.DurationSeconds??-1).ThenBy(r=>r.Row.Id),
            2=>rows.OrderBy(r=>r.Row.Session?.StartedAt).ThenBy(r=>r.Row.Id),
            _=>rows.OrderByDescending(r=>r.Row.Session?.StartedAt).ThenBy(r=>r.Row.Id)
        };
        SessionList.ItemsSource=sorted.ToArray();selected=SessionList.Items.Cast<SessionRow>().FirstOrDefault(r=>r.Row.Id==id)??SessionList.Items.Cast<SessionRow>().FirstOrDefault();
        SessionList.SelectedItem=selected;changing=previousChanging;
    }
    private void SortChanged(object sender,SelectionChangedEventArgs e){if(!changing&&!busy)BindRows(selected?.Row.Id);}
    private void UpdateSummary()
    {
        if(summary is null)return;
        TotalTime.Text=summary.TotalSeconds is long total?Duration(total):"Unavailable";SessionCount.Text=summary.Sessions.Length.ToString(CultureInfo.CurrentCulture);
        var longest=summary.Sessions.FirstOrDefault(s=>s.Session.Id==summary.LongestSessionId);
        LongestButton.Content=longest is null?"None":Duration(longest.AdjustedSeconds);LongestButton.IsEnabled=longest is not null&&!busy;
        LastPlayed.Text=summary.LastPlayed?.ToLocalTime().ToString("g")??"Not yet";
        TimezoneNotice.Text="Times: "+TimeZoneInfo.Local.Id+" (offsets shown per session)";
        UpdateHistoryButtons();
    }
    private void UpdateHistoryButtons()
    {
        if(StatusChoice is null)return;
        var active=isGameActive()||summary?.Sessions.Any(s=>s.EndKind==SessionEndKind.Active)==true;
        StatusChoice.IsEnabled=!busy&&!active&&correction is null&&catalog?.Profile.State==DocumentState.Ready;
        CorrectButton.IsEnabled=!busy&&!review&&!activity&&!active&&selected?.Detail?.CanCorrect==true;
        CorrectButton.Visibility=review?Visibility.Collapsed:Visibility.Visible;
        if(LongestButton is not null)LongestButton.IsEnabled=!busy&&summary?.LongestSessionId is not null;
    }
    private async void SelectLongest(object sender,RoutedEventArgs e)
    {
        var id=summary?.LongestSessionId;if(id is null||!await LeaveDraft())return;
        review=activity=false;BindRows(id);LoadEntry();if(selected is not null)SessionList.ScrollIntoView(selected);
    }
    private void GroupChanged(object sender,SelectionChangedEventArgs e){if(!changing)UpdateActivity();}
    private void UpdateActivity()
    {
        if(summary is null)return;var today=DateOnly.FromDateTime(DateTime.Now);var week=SessionHistory.PeriodStart(today,ActivityGrouping.Week);var month=new DateOnly(today.Year,today.Month,1);
        long Total(DateOnly start,DateOnly end)=>summary.Days.Where(d=>d.Date>=start&&d.Date<end).Aggregate(0L,(n,d)=>checked(n+d.Seconds));
        TodayTime.Text=Duration(Total(today,today.AddDays(1)));WeekTime.Text=Duration(Total(week,week.AddDays(7)));MonthTime.Text=Duration(Total(month,month.AddMonths(1)));
        var group=(ActivityGrouping)Math.Max(0,GroupChoice.SelectedIndex);
        ActivityList.ItemsSource=summary.Days.GroupBy(d=>SessionHistory.PeriodStart(d.Date,group)).OrderByDescending(g=>g.Key)
            .Select(g=>new ActivityRow(group==ActivityGrouping.Month?g.Key.ToString("MMMM yyyy"):group==ActivityGrouping.Week?$"{g.Key:d} – {g.Key.AddDays(6):d}":g.Key.ToString("d"),Duration(g.Aggregate(0L,(n,d)=>checked(n+d.Seconds))))).ToArray();
        ActivityExplanation.Text=(summary.Days.Length==0?"No date-allocated activity yet. ":"")+"Current Windows local timezone; weeks begin Monday. Cross-boundary splits are proportional estimates because sleep/unobserved gaps cannot always be placed exactly.\n"+
            "Measured total: "+Duration(summary.MeasuredSeconds)+". Undated legacy adjustments: "+Signed(summary.UndatedAdjustments)+". Unallocated session time: "+Duration(summary.UnallocatedSessionSeconds)+". These amounts remain in all-time totals.";
        CorrectionHistory.Text=history?.Corrections.Length>0?string.Join("\n\n",history.Corrections.Reverse().Select(c=>
            $"{c.CreatedAt.ToLocalTime():g} · Session {summary.Sessions.FirstOrDefault(s=>s.Session.Id==c.SessionId)?.Session.StartedAt.ToLocalTime().ToString("g")??c.SessionId.ToString("N")}\nMeasured {Duration(c.MeasuredSeconds)} · {Duration(c.PreviousSeconds)} → {Duration(c.NewSeconds)} ({Signed(c.Delta)})\n{c.Reason}")):"No manual corrections yet.";
    }
    private static string Signed(long seconds)=>(seconds<0?"−":"+")+Duration(Math.Abs(seconds));
    private void BeginCorrection(object sender,RoutedEventArgs e)
    {
        if(!CorrectButton.IsEnabled||selected?.Detail is not HistorySession row)return;
        correction=new(row.AdjustedSeconds);correctionSession=row.Session.Id;correctionChanging=true;
        CorrectionHours.Text=correction.Hours;CorrectionMinutes.Text=correction.Minutes;CorrectionSeconds.Text=correction.Seconds;CorrectionReason.Text="";
        CorrectionContext.Text=$"Session {row.Session.StartedAt.ToLocalTime():g}\nMeasured: {Duration(row.Session.DurationSeconds)}. Current adjusted: {Duration(row.AdjustedSeconds)}. Original measurement and linked note are kept.";
        CorrectionNotice.Text="";correctionChanging=false;CorrectionPanel.Visibility=Visibility.Visible;UpdateHistoryButtons();
    }
    private void CorrectionEdited(object sender,TextChangedEventArgs e)
    {
        if(correctionChanging||correction is null)return;
        correction.Hours=CorrectionHours.Text;correction.Minutes=CorrectionMinutes.Text;correction.Seconds=CorrectionSeconds.Text;correction.Reason=CorrectionReason.Text;
        CorrectionNotice.Text=correction.IsDirty?"Unsaved correction":"";
    }
    private void CloseCorrection(){correction=null;CorrectionPanel.Visibility=Visibility.Collapsed;UpdateHistoryButtons();}
    private async void CancelCorrection(object sender,RoutedEventArgs e){if(await LeaveCorrection())CloseCorrection();}
    private async void SaveCorrection(object sender,RoutedEventArgs e)=>await SaveCorrectionCore();
    private async Task<bool> SaveCorrectionCore()
    {
        if(busy||correction is null)return false;
        if(isGameActive()){CorrectionNotice.Text="Finish the current tracked game before correcting time.";return false;}
        SetBusy(true);
        try
        {
            var value=correction.Validate();
            await store.CorrectSessionAsync(gameId,correctionSession,correction.OriginalSeconds,value,correction.Reason);
            CloseCorrection();SetBusy(false);await Reload(true);GameDataChanged?.Invoke(this,EventArgs.Empty);return true;
        }
        catch(Exception e)when(Expected(e)){CorrectionNotice.Text="Could not save correction. "+e.Message;return false;}
        finally{SetBusy(false);}
    }
    private async Task<bool> LeaveCorrection()
    {
        if(correction is null)return true;
        if(!correction.IsDirty){CloseCorrection();return true;}
        var choice=await InlinePrompts.ShowAsync(this,"Save this time correction before leaving?\n\nYes: save correction.\nNo: discard correction.\nCancel: keep editing.","Unsaved time correction",MessageBoxButton.YesNoCancel,MessageBoxResult.Cancel);
        if(choice==MessageBoxResult.Yes)return await SaveCorrectionCore();
        if(choice==MessageBoxResult.No){CloseCorrection();return true;}return false;
    }
    private async void StatusChanged(object sender,SelectionChangedEventArgs e)
    {
        if(changing||busy||catalog?.Profile.Value is not GameProfile profile||StatusChoice.SelectedValue is not GameStatus next||next==profile.Status)return;
        if(isGameActive()){changing=true;StatusChoice.SelectedValue=profile.Status;changing=false;return;}
        SetBusy(true);
        try{await store.SetGameStatusAsync(gameId,next);SetBusy(false);await Reload(true);GameDataChanged?.Invoke(this,EventArgs.Empty);}
        catch(Exception error)when(Expected(error)){changing=true;StatusChoice.SelectedValue=profile.Status;changing=false;Warnings.Text="Could not change status. "+error.Message;}
        finally{SetBusy(false);}
    }
    public void UpdateLiveProgress(SessionProgress progress)
    {
        if(progress.GameId!=gameId)return;
        if(progress.IsActive)TotalTime.Text=Duration(progress.TotalSeconds);
        var row=rows.FirstOrDefault(r=>r.Row.Id==progress.SessionId);
        if(row is not null){row.LiveSeconds=progress.SessionSeconds;var wasChanging=changing;changing=true;SessionList.Items.Refresh();changing=wasChanging;}
        TrackingNotice.Text=progress.IsActive?$"Active session: {Duration(progress.SessionSeconds)} · refresh for saved checkpoints":
            "Tracking ended. Refresh to see final times and write a note. Existing note drafts are kept.";
        UpdateHistoryButtons();
    }
    private sealed record ActivityRow(string Period,string Duration);
    private sealed record SessionRow(SessionNote Row,HistorySession? Detail)
    {
        public long? LiveSeconds{get;set;}
        public string Date=>Row.Session?.StartedAt.ToLocalTime().ToString("d")??"Unavailable";
        public string Started=>Row.Session?.StartedAt.ToLocalTime().ToString("HH:mm:ss zzz")??"Unavailable";
        public string Ended=>Detail is null?"Unavailable":Detail.EndKind switch
        {
            SessionEndKind.Active=>"Active — no end",SessionEndKind.Unavailable=>"Needs attention",
            SessionEndKind.Interrupted=>"Last checkpoint:\n"+Time(Detail.ObservedAt),
            SessionEndKind.Unknown=>"Recorded (unverified):\n"+Time(Row.Session?.EndedAt),_=>Time(Row.Session?.EndedAt)
        };
        private static string Time(DateTimeOffset? value)=>value?.ToLocalTime().ToString("g")+" "+value?.ToLocalTime().ToString("zzz");
        public string Duration=>Row.Session is PlaySession s?NotesView.Duration(LiveSeconds??Detail?.AdjustedSeconds??s.DurationSeconds)+
            (Detail is not null&&Detail.AdjustedSeconds!=s.DurationSeconds?"\nMeasured: "+NotesView.Duration(s.DurationSeconds):""):"Unavailable";
        public string Context=>Row.Context+(Detail?.EndKind==SessionEndKind.Interrupted?" · Actual exit time unknown":"");
        public string NoteLabel=>Row.Note.Value is not null?"Note saved":Row.Note.State==DocumentState.Missing?"Write note below":"Note needs attention";
    }
}
