using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using GameVault.Infrastructure.Notes;

namespace GameVault.App;

// The game's journal (issue #96): recorded sessions and entries added by hand are listed together and edited in the same place, each with tags and a playthrough.
public partial class NotesView
{
    private StoredDocument<GameJournal>? journal;
    private Playthrough[] runs = [];
    private JournalEntry? draftEntry;       // a new entry that is not saved yet
    private string detailsSaved = "";
    private (Guid? Id, bool AddNew)? pending;

    private bool JournalWritable => journal is { IsReadOnly: false } && catalog?.Profile is { State: DocumentState.Ready, Value.IsArchived: false };
    private bool Dirty => editor.IsDirty || !review && DetailsSnapshot() != detailsSaved;

    /// <summary>Selects a session or entry, or starts a new entry, as soon as the journal has been read.</summary>
    public async Task OpenEntryAsync(Guid? id, bool addNew)
    {
        pending = (id, addNew);
        if (catalog is not null && !busy) await ApplyPendingAsync();
    }

    private async Task ApplyPendingAsync()
    {
        if (pending is not { } wanted) return;
        pending = null;
        if (wanted.AddNew) { await StartEntryAsync(); return; }
        if (wanted.Id is not { } id || !await LeaveDraft()) return;
        review = activity = false;
        BindRows(id);
        LoadEntry();
        if (selected is not null) SessionList.ScrollIntoView(selected);
    }

    private async Task LoadJournalDataAsync()
    {
        journal = await store.LoadJournalAsync(gameId);
        runs = (await store.LoadPlaythroughsAsync(gameId)).Value?.Runs ?? [];
        if (journal.Message is { } message) Warnings.Text = string.Join("\n", new[] { Warnings.Text, message }.Where(s => s.Length > 0));
    }

    /// <summary>The added entries as lines of the list, with the unsaved new one if there is one.</summary>
    private IEnumerable<SessionRow> EntryRows()
    {
        var entries = journal?.Value?.Entries ?? [];
        if (draftEntry is { } draft && entries.All(e => e.Id != draft.Id)) entries = [draft, .. entries];
        return entries.Select(e => new SessionRow(new SessionNote(e.Id, null, true, "Added entry", new(null, DocumentState.Missing)), null, e));
    }

    /// <summary>An added entry looks like a saved note to the editor; a new one like a note not written yet.</summary>
    private StoredDocument<NoteDocument> EntryDocument(JournalEntry entry)
    {
        if (entry.Id == draftEntry?.Id) return new(null, DocumentState.Missing);
        var day = new DateTimeOffset(entry.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new(new NoteDocument(1, gameId, entry.Id, entry.Title ?? "", entry.Note, day, day), DocumentState.Ready);
    }

    private string DetailsSnapshot() => review ? "" : string.Join('\u001f', EntryDate.Text, EntryMinutes.Text, EntryTags.Text,
        (EntryRun.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");

    /// <summary>Fills the boxes from what is stored; with <paramref name="keep"/>, unsaved typing in them survives.</summary>
    private void LoadDetails(bool keep)
    {
        var typed = keep && !review && DetailsSnapshot() != detailsSaved ? (EntryDate.Text, EntryMinutes.Text, EntryTags.Text, Run: TypedRun()) : ((string, string, string, Guid? Run)?)null;
        var added = selected?.Added;
        var session = added is null && selected is not null ? journal?.Value?.Sessions?.FirstOrDefault(s => s.SessionId == selected.Row.Id) : null;
        var tags = added?.Tags ?? session?.Tags ?? [];
        var run = added?.PlaythroughId ?? session?.PlaythroughId;
        EntryDetails.Visibility = review || selected is null ? Visibility.Collapsed : Visibility.Visible;
        AddedDetails.Visibility = added is null ? Visibility.Collapsed : Visibility.Visible;
        EntryDate.Text = added is null ? "" : FormView.DateText(added.Date);
        EntryMinutes.Text = added?.Minutes?.ToString(CultureInfo.CurrentCulture) ?? "";
        EntryTags.Text = string.Join(", ", tags);
        EntryRun.Items.Clear();
        EntryRun.Items.Add(new ComboBoxItem { Content = "No playthrough", Tag = Guid.Empty });
        foreach (var item in runs) EntryRun.Items.Add(new ComboBoxItem { Content = item.Title, Tag = item.Id });
        EntryRun.SelectedItem = EntryRun.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, run)) ?? EntryRun.Items[0];
        EntryDetails.IsEnabled = CanEdit && JournalWritable;
        detailsSaved = DetailsSnapshot();
        if (typed is not { } kept) return;
        (EntryDate.Text, EntryMinutes.Text, EntryTags.Text) = (kept.Item1, kept.Item2, kept.Item3);
        EntryRun.SelectedItem = EntryRun.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, kept.Run)) ?? EntryRun.Items[0];
    }

    private string DetailsText()
    {
        var parts = new List<string>();
        if (selected?.Added?.Minutes is { } minutes) parts.Add(StatisticsView.Duration(minutes * 60L));
        parts.AddRange(EntryTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => "#" + t));
        if ((EntryRun.SelectedItem as ComboBoxItem) is { Tag: Guid id, Content: string title } && id != Guid.Empty) parts.Add(title);
        return string.Join(" · ", parts);
    }

    private string[] TypedTags() => EntryTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
    private Guid? TypedRun() => EntryRun.SelectedItem is ComboBoxItem { Tag: Guid id } && id != Guid.Empty ? id : null;

    private void DetailsChanged()
    {
        if (changing) return;
        SaveStatus.Text = Dirty ? "Unsaved changes" : "Saved";
        Render();
    }

    private void DetailTextChanged(object sender, TextChangedEventArgs e) => DetailsChanged();
    private void DetailRunChanged(object sender, SelectionChangedEventArgs e) => DetailsChanged();

    /// <summary>Saves the selected session or entry. Throws with words for the player.</summary>
    private async Task SaveSelectedAsync()
    {
        if (selected!.Added is { } entry)
        {
            int? minutes = null;
            if (EntryMinutes.Text.Trim() is { Length: > 0 } typed)
                minutes = int.TryParse(typed, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ? value : throw new ArgumentException("Enter the minutes as a whole number, or leave the box empty.");
            await store.SaveJournalEntryAsync(gameId, entry with
            {
                Title = editor.Title, Note = editor.Body, Date = FormView.Date(EntryDate) ?? throw new ArgumentException("Enter the date."),
                Minutes = minutes, Tags = TypedTags(), PlaythroughId = TypedRun()
            });
            draftEntry = null;
            return;
        }
        var id = selected.Row.Id;
        var details = DetailsSnapshot() != detailsSaved;
        if (details) await store.SaveSessionDetailAsync(gameId, id, TypedTags(), TypedRun());
        if (editor.IsDirty) await store.SaveNoteAsync(gameId, id, editor.Title, editor.Body);
    }

    private async void AddEntry(object sender, RoutedEventArgs e) => await StartEntryAsync();

    private async Task StartEntryAsync()
    {
        if (busy || !JournalWritable || !await LeaveDraft()) return;
        review = activity = false;
        draftEntry = new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Now), null, "", [], null);
        rows = [.. rows.Where(r => r.Added is null), .. EntryRows()];
        BindRows(draftEntry.Id);
        LoadEntry();
        editor.IsReadMode = false;
        Render();
        NoteBody.Focus();
    }

    /// <summary>Forgets a new entry that was never saved.</summary>
    private void DropDraft()
    {
        if (draftEntry is null) return;
        var id = draftEntry.Id;
        draftEntry = null;
        rows = [.. rows.Where(r => r.Row.Id != id)];
        if (selected?.Row.Id == id) { BindRows(null); LoadEntry(); }
        else BindRows(selected?.Row.Id);
    }

    private async void DeleteEntry(object sender, RoutedEventArgs e)
    {
        if (busy || selected?.Added is not { } entry) return;
        if (await InlinePrompts.ShowAsync(this, "Delete this journal entry? It cannot be undone.", "Delete entry", MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (entry.Id == draftEntry?.Id) { DropDraft(); return; }
        SetBusy(true);
        try { await store.DeleteJournalEntryAsync(gameId, entry.Id); editor = new(null); detailsSaved = DetailsSnapshot(); SetBusy(false); await Reload(); GameDataChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception error) when (Expected(error)) { SaveStatus.Text = "Could not delete. " + error.Message; }
        finally { SetBusy(false); }
    }
}
