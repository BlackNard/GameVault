using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameVault.Core;
using GameVault.Infrastructure.Notes;
using GameVault.Infrastructure.Storage;
namespace GameVault.App;

public partial class NotesView : UserControl
{
    private readonly AppDataStore store;
    private readonly ApplicationPaths paths;
    private readonly Guid gameId;
    private readonly string theme;
    private readonly Func<bool> isGameActive;
    private GameNotes? catalog;
    private NoteEditorState editor=new(null);
    private SessionRow? selected;
    private bool review,activity,changing=true,busy;
    private NoteExportWindow? exportView;
    private TaskCompletionSource? idle;
    public event EventHandler? BackRequested;
    public event EventHandler? GameDataChanged;
    public NotesView(ApplicationPaths paths,AppDataStore store,Guid gameId,string theme,Func<bool>? isGameActive=null)
    {
        this.paths=paths;this.store=store;this.gameId=gameId;this.theme=theme;this.isGameActive=isGameActive??(()=>false);
        InitializeComponent();detailsSaved=DetailsSnapshot();Rating.Chosen+=RatingChosen;StatusChoice.SelectedValuePath="Tag";StatusChoice.ItemsSource=PlayStatuses.All.Select(s=>new ComboBoxItem{Tag=s,Content=PlayStatuses.Label(s)}).ToArray();
        Loaded+=async(_,_)=>await Reload();
        PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.S&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){e.Handled=true;if(!busy&&CanEdit)await Save();}};
    }
    private bool CanEdit=>catalog is not null&&(review?catalog.CanReview:selected?.Added is not null?JournalWritable:selected?.Row.CanEdit==true);
    private StoredDocument<NoteDocument>? Document=>review?catalog?.Review:selected?.Added is { } added?EntryDocument(added):selected?.Row.Note;
    private void SetBusy(bool value)
    {
        if(value&&!busy)idle=new(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!value)idle?.TrySetResult();busy=value;
        Fields.IsEnabled=ActivityPanel.IsEnabled=CorrectionPanel.IsEnabled=SessionsTab.IsEnabled=ActivityTab.IsEnabled=ReviewTab.IsEnabled=RefreshButton.IsEnabled=ExportButton.IsEnabled=BackButton.IsEnabled=!value;
        UpdateHistoryButtons();
    }
    /// <summary>Reads the game again after it was changed outside this view; an unsaved draft is kept.</summary>
    public Task RefreshAsync()=>Reload(true);
    private async Task Reload(bool preserveDraft=false)
    {
        if(busy)return;SetBusy(true);
        try
        {
            history=await store.LoadHistoryAsync(gameId);summary=SessionHistory.Build(history,TimeZoneInfo.Local);
            catalog=await store.LoadNotesAsync(gameId);await LoadRating();GameHeading.Text=catalog.Profile.Value?.Name??"Unavailable game profile";
            Warnings.Text=string.Join("\n",catalog.Warnings.Concat(summary.Warnings).Distinct());
            statusDetail=(await store.LoadStatusDetailAsync(gameId)).Value?.Detail;
            var previous=selected?.Row.Id;changing=true;StatusChoice.SelectedValue=ShownStatus(catalog.Profile.Value);
            await LoadJournalDataAsync();
            rows=[..catalog.Sessions.Select(s=>new SessionRow(s,summary.Sessions.SingleOrDefault(h=>h.Session.Id==s.Id))),..EntryRows()];
            BindRows(previous);UpdateSummary();UpdateCorrections();LoadEntry(preserveDraft);
        }
        catch(Exception e)when(Expected(e)){Warnings.Text="Could not load session history. "+e.Message;}
        finally{changing=false;SetBusy(false);}
        await ApplyPendingAsync();
    }
    private void LoadEntry(bool preserveDraft=false)
    {
        changing=true;if(!preserveDraft)editor=new(Document?.Value);
        ListColumn.MinWidth=review?0:380;ListColumn.Width=review?new GridLength(0):new GridLength(2,GridUnitType.Star);SessionsPane.Visibility=review?Visibility.Collapsed:Visibility.Visible;
        EntryHeading.Text=review?"Your game review":selected?.Added is not null?"Journal entry":selected?.Date??"Nothing in the journal yet";
        EntryContext.Text=review?catalog?.CanReview==true?"Your review stays when you replay or change status.":Document?.Value is null?
            "Mark this game Completed to write your first review.":"This review is protected; see the warning.":selected is null?
            "Play a session, or choose Add entry.":selected.Added is not null?"Added by you. It does not change recorded playtime.":selected.Started+" → "+selected.Ended+" · "+selected.Duration+"\n"+selected.Context;
        NoteTitle.Text=editor.Title;NoteBody.Text=editor.Body;if(!CanEdit)editor.IsReadMode=true;LoadDetails(preserveDraft);
        SaveStatus.Text=Dirty?"Unsaved changes":!review&&selected?.Added is { } shown?(shown.Id==draftEntry?.Id?"New entry":"Saved"):Document?.Message??(editor.Saved is null?"No saved note":$"Saved · {editor.Saved.UpdatedAt.ToLocalTime():g}");
        SaveButton.Content=review?"Save review":"Save note";RatingPanel.Visibility=review?Visibility.Visible:Visibility.Collapsed;changing=false;Render();
    }
    private void Render()
    {
        Fields.Visibility=activity?Visibility.Collapsed:Visibility.Visible;ActivityPanel.Visibility=activity?Visibility.Visible:Visibility.Collapsed;
        EditPanel.Visibility=editor.IsReadMode?Visibility.Collapsed:Visibility.Visible;ReadPanel.Visibility=editor.IsReadMode?Visibility.Visible:Visibility.Collapsed;
        ReadTitle.Text=editor.Title;ReadDetails.Text=review?"":DetailsText();DeleteEntryButton.Visibility=!review&&selected?.Added is not null&&JournalWritable?Visibility.Visible:Visibility.Collapsed;AddEntryButton.IsEnabled=JournalWritable;ReadBody.Text=editor.Body.Length==0?"No note written yet.":editor.Body;
        EditButton.IsEnabled=CanEdit;SaveButton.Visibility=editor.IsReadMode?Visibility.Collapsed:Visibility.Visible;SaveButton.IsEnabled=CanEdit&&Dirty;
        ReadButton.SetResourceReference(BorderBrushProperty,editor.IsReadMode?"AppAccent":"AppLine");EditButton.SetResourceReference(BorderBrushProperty,editor.IsReadMode?"AppLine":"AppAccent");
        SessionsTab.SetResourceReference(BorderBrushProperty,review||activity?"AppLine":"AppAccent");ReviewTab.SetResourceReference(BorderBrushProperty,review&&!activity?"AppAccent":"AppLine");
        ActivityTab.SetResourceReference(BorderBrushProperty,activity?"AppAccent":"AppLine");UpdateHistoryButtons();
    }
    private void DraftChanged(object sender,TextChangedEventArgs e)
    {
        if(changing)return;editor.Title=NoteTitle.Text;editor.Body=NoteBody.Text;SaveStatus.Text=Dirty?"Unsaved changes":"Saved";Render();
    }
    private async Task LoadRating()
    {
        var stored=await store.LoadRatingAsync(gameId);
        Rating.Value=stored.Value?.HalfStars??0;
        Rating.IsEnabled=!stored.IsReadOnly&&catalog?.Profile is {State:DocumentState.Ready,Value.IsArchived:false};
        RatingStatus.Text=stored.Message??"Your rating is saved as soon as you choose it. Click the same stars again to clear it.";
    }
    private async void RatingChosen(object? sender,int halfStars)
    {
        if(busy)return;SetBusy(true);
        try
        {
            await store.SaveRatingAsync(gameId,halfStars);
            Rating.Value=halfStars;RatingStatus.Text=halfStars==0?"Rating cleared.":"Rating saved.";
            GameDataChanged?.Invoke(this,EventArgs.Empty);
        }
        catch(Exception e)when(Expected(e)){RatingStatus.Text="The rating could not be saved. "+e.Message;}
        finally{SetBusy(false);}
    }
    private void ReadNote(object sender,RoutedEventArgs e){editor.IsReadMode=true;Render();}
    private void EditNote(object sender,RoutedEventArgs e){if(CanEdit){editor.IsReadMode=false;Render();NoteBody.Focus();}}
    private async void SaveNote(object sender,RoutedEventArgs e)=>await Save();
    private async Task<bool> Save()
    {
        if(busy||!CanEdit)return false;SetBusy(true);SaveStatus.Text="Saving…";
        try
        {
            if(review){var note=await store.SaveNoteAsync(gameId,null,editor.Title,editor.Body);editor.AcceptSaved(note);SetBusy(false);await Reload(true);}
            else{await SaveSelectedAsync();SetBusy(false);await Reload();}
            SaveStatus.Text="Saved";GameDataChanged?.Invoke(this,EventArgs.Empty);return true;
        }
        catch(Exception e)when(Expected(e)){editor.IsReadMode=false;SaveStatus.Text="Could not save. "+e.Message.Split(" (Parameter")[0];Render();return false;}
        finally{SetBusy(false);}
    }
    private async Task<bool> LeaveDraft()
    {
        if(busy||!await LeaveCorrection())return false;
        if(!Dirty){if(draftEntry is not null)DropDraft();return true;}
        var answer=await InlinePrompts.ShowAsync(this,"Save changes before leaving this entry?\n\nYes: save changes.\nNo: discard changes.\nCancel: keep editing.","Unsaved entry",MessageBoxButton.YesNoCancel,MessageBoxResult.Cancel);
        if(answer==MessageBoxResult.Yes)return await Save();
        if(answer!=MessageBoxResult.No)return false;
        // Discarded: the boxes go back to what is stored, and a new entry that was never saved disappears.
        if(draftEntry is not null&&selected?.Row.Id==draftEntry.Id)DropDraft();else LoadEntry();
        return true;
    }
    private async void SessionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(changing||review||activity||busy)return;var next=SessionList.SelectedItem as SessionRow;
        changing=true;SessionList.SelectedItem=selected;changing=false;if(!await LeaveDraft())return;
        changing=true;selected=SessionList.Items.Cast<SessionRow>().FirstOrDefault(r=>r.Row.Id==next?.Row.Id);SessionList.SelectedItem=selected;changing=false;LoadEntry();
    }
    private async void ShowSessions(object sender,RoutedEventArgs e){if((review||activity)&&await LeaveDraft()){review=activity=false;LoadEntry();}}
    private async void ShowReview(object sender,RoutedEventArgs e){if((!review||activity)&&await LeaveDraft()){review=true;activity=false;LoadEntry();}}
    private async void ShowActivity(object sender,RoutedEventArgs e){if(!activity&&await LeaveDraft()){LoadEntry();activity=true;Render();UpdateCorrections();}}
    private async void RefreshSessions(object sender,RoutedEventArgs e){if(await LeaveDraft())await Reload();}
    private async void ExportNotes(object sender,RoutedEventArgs e)
    {
        if(busy||catalog is null||exportView is not null)return;
        if(Dirty)
        {
            var answer=await InlinePrompts.ShowAsync(this,"This entry has unsaved changes.\n\nYes: save before exporting.\nNo: export saved notes only (keep this draft).\nCancel: return to the note.","Export notes",MessageBoxButton.YesNoCancel,MessageBoxResult.Cancel);
            if(answer==MessageBoxResult.Cancel||(answer==MessageBoxResult.Yes&&!await Save()))return;
        }
        NoteExportEntry? entry=!review&&selected?.Added is { } added?(added.Id==draftEntry?.Id?null:NoteExportEntry.FromJournal(GameHeading.Text,gameId,added)):editor.Saved is NoteDocument saved?new(GameHeading.Text,saved,review?null:selected?.Row.Session,review?"Game review":selected?.Context??"Session metadata unavailable",review?Rating.Value:0):null;
        var warning=Document?.IsReadOnly==true?Document.Message??"Recovered/protected entry: exported read-only copy.":null;
        exportView=new NoteExportWindow(paths,store,gameId,theme,entry,warning);
        exportView.Completed+=(_,_)=>CloseExportView();
        ExportHost.Content=exportView;NotesContent.Visibility=Visibility.Collapsed;ExportHost.Visibility=Visibility.Visible;
    }
    private void CloseExportView()
    {
        ExportHost.Content=null;exportView=null;ExportHost.Visibility=Visibility.Collapsed;NotesContent.Visibility=Visibility.Visible;
    }
    private void BackToLibrary(object sender,RoutedEventArgs e)=>BackRequested?.Invoke(this,EventArgs.Empty);
    public async Task<bool> CanLeaveAsync(){while(busy&&idle is not null)await idle.Task;if(exportView is not null&&!await exportView.CanLeaveAsync())return false;if(!await LeaveDraft())return false;if(exportView is not null)CloseExportView();return true;}
    private static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException;
}
