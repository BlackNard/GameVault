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
    private GameNotes? catalog;
    private NoteEditorState editor = new(null);
    private SessionRow? selected;
    private bool review, changing = true, busy, exportOpen;
    private TaskCompletionSource? idle;
    public event EventHandler? BackRequested;
    public NotesView(ApplicationPaths paths, AppDataStore store, Guid gameId, string theme)
    {
        this.paths = paths; this.store = store; this.gameId = gameId; this.theme = theme;
        InitializeComponent();
        Loaded += async (_, _) => await Reload();
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; if (!busy && CanEdit) await Save(); } };
    }
    private bool CanEdit => catalog is not null && (review ? catalog.CanReview : selected?.Row.CanEdit == true);
    private StoredDocument<NoteDocument>? Document => review ? catalog?.Review : selected?.Row.Note;
    private void SetBusy(bool value) { if (value && !busy) idle = new(TaskCreationOptions.RunContinuationsAsynchronously); if (!value) idle?.TrySetResult(); busy = value; Fields.IsEnabled = SessionsTab.IsEnabled = ReviewTab.IsEnabled = RefreshButton.IsEnabled = ExportButton.IsEnabled = BackButton.IsEnabled = !value; }
    private async Task Reload()
    {
        if (busy) return; SetBusy(true);
        try
        {
            catalog = await store.LoadNotesAsync(gameId);
            GameHeading.Text = catalog.Profile.Value?.Name ?? "Unavailable game profile";
            Warnings.Text = string.Join("\n", catalog.Warnings);
            var previous = selected?.Row.Id;
            changing = true;
            SessionList.ItemsSource = catalog.Sessions.Select(s => new SessionRow(s)).ToArray();
            SessionList.SelectedItem = SessionList.Items.Cast<SessionRow>().FirstOrDefault(s => s.Row.Id == previous) ?? SessionList.Items.Cast<SessionRow>().FirstOrDefault();
            selected = SessionList.SelectedItem as SessionRow;
            LoadEntry();
        }
        catch (Exception e) when (Expected(e)) { Warnings.Text = "Could not load notes. " + e.Message; }
        finally { changing = false; SetBusy(false); }
    }
    private void LoadEntry()
    {
        changing = true; editor = new(Document?.Value);
        SessionColumn.Width = new GridLength(review ? 0 : 250);
        SessionList.Visibility = review ? Visibility.Collapsed : Visibility.Visible;
        EntryHeading.Text = review ? "Your game review" : selected?.Date ?? "No recorded sessions yet";
        EntryContext.Text = review ? catalog?.CanReview == true ? "Your review is kept when you replay or change the game status." :
            Document?.Value is null ? "Mark this game Completed through Edit game to write your first review." : "This review is protected; see the warning below." : selected is null ? "Play a session, then refresh this list to write a note." : selected.Duration + " · " + selected.Context;
        NoteTitle.Text = editor.Title; NoteBody.Text = editor.Body;
        if (!CanEdit) editor.IsReadMode = true;
        SaveStatus.Text = Document?.Message ?? (editor.Saved is null ? "No saved note" : $"Saved · {editor.Saved.UpdatedAt.ToLocalTime():g}");
        SaveButton.Content = review ? "Save review" : "Save note";
        changing = false; Render();
    }
    private void Render()
    {
        EditPanel.Visibility = editor.IsReadMode ? Visibility.Collapsed : Visibility.Visible;
        ReadPanel.Visibility = editor.IsReadMode ? Visibility.Visible : Visibility.Collapsed;
        ReadTitle.Text = editor.Title; ReadBody.Text = editor.Body.Length == 0 ? "No note written yet." : editor.Body;
        EditButton.IsEnabled = CanEdit; SaveButton.Visibility = editor.IsReadMode ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = CanEdit && editor.IsDirty;
        ReadButton.SetResourceReference(BorderBrushProperty, editor.IsReadMode ? "AppAccent" : "AppLine");
        EditButton.SetResourceReference(BorderBrushProperty, editor.IsReadMode ? "AppLine" : "AppAccent");
        SessionsTab.SetResourceReference(BorderBrushProperty, review ? "AppLine" : "AppAccent");
        ReviewTab.SetResourceReference(BorderBrushProperty, review ? "AppAccent" : "AppLine");
    }
    private void DraftChanged(object sender, TextChangedEventArgs e)
    {
        if (changing) return; editor.Title = NoteTitle.Text; editor.Body = NoteBody.Text;
        SaveStatus.Text = editor.IsDirty ? "Unsaved changes" : "Saved"; Render();
    }
    private void ReadNote(object sender, RoutedEventArgs e) { editor.IsReadMode = true; Render(); }
    private void EditNote(object sender, RoutedEventArgs e) { if (CanEdit) { editor.IsReadMode = false; Render(); NoteBody.Focus(); } }
    private async void SaveNote(object sender, RoutedEventArgs e) => await Save();
    private async Task<bool> Save()
    {
        if (busy || !CanEdit) return false;
        SetBusy(true); SaveStatus.Text = "Saving…";
        try
        {
            var note = await store.SaveNoteAsync(gameId, review ? null : selected!.Row.Id, editor.Title, editor.Body);
            editor.AcceptSaved(note); SaveStatus.Text = "Saved";
            // Reload to update note indicators and current session/review eligibility.
            SetBusy(false); await Reload(); return true;
        }
        catch (Exception e) when (Expected(e)) { editor.IsReadMode = false; SaveStatus.Text = "Could not save. " + e.Message; Render(); return false; }
        finally { SetBusy(false); }
    }
    private Task<bool> LeaveDraft()
    {
        if (busy) return Task.FromResult(false);
        return editor.TryLeaveAsync(() =>
        {
            var choice = MessageBox.Show(Window.GetWindow(this), "Save changes before leaving this entry?\n\nYes: save changes.\nNo: discard changes.\nCancel: keep editing.", "Unsaved note", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            return choice switch { MessageBoxResult.No => NoteLeaveChoice.Discard, MessageBoxResult.Yes => NoteLeaveChoice.Save, _ => NoteLeaveChoice.Cancel };
        }, Save);
    }
    private async void SessionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (changing || review || busy) return;
        var next = SessionList.SelectedItem as SessionRow;
        changing = true; SessionList.SelectedItem = selected; changing = false;
        if (!await LeaveDraft()) return;
        changing = true; selected = SessionList.Items.Cast<SessionRow>().FirstOrDefault(row => row.Row.Id == next?.Row.Id); SessionList.SelectedItem = selected; changing = false; LoadEntry();
    }
    private async void ShowSessions(object sender, RoutedEventArgs e) { if (review && await LeaveDraft()) { review = false; LoadEntry(); } }
    private async void ShowReview(object sender, RoutedEventArgs e) { if (!review && await LeaveDraft()) { review = true; LoadEntry(); } }
    private async void RefreshSessions(object sender, RoutedEventArgs e) { if (await LeaveDraft()) await Reload(); }
    private async void ExportNotes(object sender, RoutedEventArgs e)
    {
        if (busy || catalog is null) return;
        if (editor.IsDirty)
        {
            var answer = MessageBox.Show(Window.GetWindow(this), "This entry has unsaved changes.\n\nYes: save before exporting.\nNo: export saved notes only (keep this draft).\nCancel: return to the note.", "Export notes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !await Save())) return;
        }
        NoteExportEntry? entry = editor.Saved is NoteDocument saved ? new(GameHeading.Text, saved, review ? null : selected?.Row.Session, review ? "Game review" : selected?.Context ?? "Session metadata unavailable") : null;
        var warning = Document?.IsReadOnly == true ? Document.Message ?? "Recovered/protected entry: exported read-only copy." : null;
        var dialog = new NoteExportWindow(paths, store, gameId, theme, entry, warning) { Owner = Window.GetWindow(this) };
        exportOpen = true;
        try { dialog.ShowDialog(); } finally { exportOpen = false; }
    }
    private void BackToLibrary(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    public async Task<bool> CanLeaveAsync()
    {
        while (busy && idle is not null) await idle.Task;
        if (exportOpen) return false;
        return await LeaveDraft();
    }
    private static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException;
    private sealed record SessionRow(SessionNote Row)
    {
        public string Date => Row.Session?.StartedAt.ToLocalTime().ToString("g") ?? "Session data unavailable";
        public string Duration => Row.Session is PlaySession s ? $"{s.DurationSeconds / 3600}h {s.DurationSeconds / 60 % 60}m {s.DurationSeconds % 60}s" : "Duration unavailable";
        public string Context => Row.Context;
        public string NoteLabel => Row.Note.Value is not null ? "Note saved" : Row.Note.State == DocumentState.Missing ? "No note yet" : "Note needs attention";
    }
}