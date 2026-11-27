using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Threading;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.App.Tests;

/// <summary>The game's journal editor (issue #96), driven headlessly: nothing is shown.</summary>
public sealed class JournalEditorTests
{
    [Fact]
    public void SessionsAndAddedEntriesAreWrittenInOneEditor() => OnSta(async (view, store, id, session) =>
    {
        await view.RefreshAsync();
        var list = Part<DataGrid>(view, "SessionList");
        Assert.Single(list.Items);
        Assert.True(await view.CanLeaveAsync());                                       // nothing typed, nothing asked

        // A recorded session: tags and a note are saved from the same boxes.
        Part<TextBox>(view, "EntryTags").Text = "boss, story";
        Part<TextBox>(view, "NoteBody").Text = "Found the key.";
        Assert.True(await Save(view));
        Assert.Equal(["boss", "story"], Assert.Single((await store.LoadJournalAsync(id)).Value!.Sessions!).Tags);
        Assert.Equal("Found the key.", Assert.Single(await store.LoadSessionNoteDocumentsAsync(id)).Body);

        // A new entry left untouched disappears without a question.
        await view.OpenEntryAsync(null, true);
        Assert.Equal(2, list.Items.Count);
        Assert.True(await view.CanLeaveAsync());
        Assert.Single(list.Items);

        await view.OpenEntryAsync(null, true);
        Part<TextBox>(view, "EntryDate").Text = "2026-05-04";
        Part<TextBox>(view, "EntryMinutes").Text = "20";
        Part<TextBox>(view, "NoteTitle").Text = "Plans";
        Part<TextBox>(view, "NoteBody").Text = "Thought about it.";
        await view.RefreshAsync();                                                     // an outside change keeps the draft
        Assert.Equal("20", Part<TextBox>(view, "EntryMinutes").Text);
        Assert.Equal("Thought about it.", Part<TextBox>(view, "NoteBody").Text);
        Assert.True(await Save(view));
        var entry = Assert.Single((await store.LoadJournalAsync(id)).Value!.Entries);
        Assert.Equal(("Plans", 20, new DateOnly(2026, 5, 4)), (entry.Title, entry.Minutes!.Value, entry.Date));
        Assert.Equal(2, list.Items.Count);
        Assert.True(await view.CanLeaveAsync());

        // The journal page opens a chosen line.
        await view.OpenEntryAsync(session, false);
        Assert.Equal("Found the key.", Part<TextBox>(view, "NoteBody").Text);
        await view.OpenEntryAsync(entry.Id, false);
        Assert.Equal("Thought about it.", Part<TextBox>(view, "NoteBody").Text);
    });

    private static T Part<T>(NotesView view, string name) where T : class => Assert.IsType<T>(view.FindName(name));

    private static Task<bool> Save(NotesView view) =>
        (Task<bool>)typeof(NotesView).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;

    private static void OnSta(Func<NotesView, AppDataStore, Guid, Guid, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "GameVault journal editor", Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var frame = new DispatcherFrame();
            async void Run()
            {
                try
                {
                    var paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
                    var store = new AppDataStore(paths);
                    Directory.CreateDirectory(Path.Combine(root, "game"));
                    Directory.CreateDirectory(paths.Root);
                    File.WriteAllText(Path.Combine(root, "game", "game.exe"), "");
                    var id = (await new LibraryService(paths, store).SaveAsync(new(null, "Alpha", Path.Combine(root, "game", "game.exe"), Path.Combine(root, "game"), []))).Profile.Id;
                    var session = Guid.NewGuid();
                    var at = new DateTimeOffset(2026, 5, 3, 20, 0, 0, TimeSpan.Zero);
                    await store.SaveStatisticsAsync(new(1, id, [new(session, at, at.AddHours(1), 3600)], []));
                    await test(new NotesView(paths, store, id, "Midnight"), store, id, session);
                }
                catch (Exception e) { failure = e; }
                finally { frame.Continue = false; }
            }
            Dispatcher.CurrentDispatcher.BeginInvoke(Run);
            Dispatcher.PushFrame(frame);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        try { Directory.Delete(root, true); } catch (IOException) { }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
