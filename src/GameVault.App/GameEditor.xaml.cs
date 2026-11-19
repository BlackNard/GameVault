using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

public partial class GameEditor : UserControl
{
    private readonly Guid? id;
    private readonly Func<GameDraft, byte[]?, Task<GameSaveResult>> save;
    private readonly Func<Guid, Task<GameSaveResult>>? restore;
    private int coverRevision;
    private byte[]? newCoverBytes;
    private CoverSearchView? coverSearch;
    private bool saving;
    public GameSaveResult? Result { get; private set; }
    public bool WasRestored { get; private set; }
    /// <summary>Raised with true while the artwork search is open, so the host can give it the whole window, and false when it closes.</summary>
    public event EventHandler<bool>? FullWidthChanged;

    public GameEditor(string theme, GameProfile? profile, Func<GameDraft, byte[]?, Task<GameSaveResult>> save, Func<Guid, Task<GameSaveResult>>? restore = null)
    {
        this.save = save;
        this.restore = restore;
        id = profile?.Id;
        InitializeComponent();
        ThemePalette.Apply(this, theme);
        if (profile is not null)
        {
            Heading.Text = "Edit game";
            SaveButton.Content = "Save changes";
            GameName.Text = profile.Name;
            Executable.Text = profile.ExecutablePath;
            WorkingDirectory.Text = profile.WorkingDirectory;
            SaveSources.Text = string.Join(Environment.NewLine, profile.SaveSources);
            CoverPath.Text = "Existing cover is kept unless you choose another image.";
            foreach (ComboBoxItem item in StatusChoice.Items)
                if ((string)item.Tag == profile.Status.ToString()) StatusChoice.SelectedItem = item;
        }
        Unloaded += (_, _) => coverSearch?.Dispose();
        navigation = new(Snapshot());
        Loaded += (_, _) => GameName.Focus();
    }

    private void BrowseExecutable(object sender, RoutedEventArgs e) => Pick(() =>
    {
        var path = NativePickers.File(Window.GetWindow(this)!, "Choose game executable", "Windows executables (*.exe)|*.exe", Executable.Text);
        if (path is null) return;
        WorkingDirectory.Text = GamePathSuggestions.WorkingDirectory(Executable.Text, WorkingDirectory.Text, path);
        Executable.Text = path;
        GameName.Text = GamePathSuggestions.GameName(GameName.Text, path);
    });

    private void BrowseWorking(object sender, RoutedEventArgs e) => Pick(() =>
    {
        var path = NativePickers.Folder(Window.GetWindow(this)!, "Choose working directory", WorkingDirectory.Text);
        if (path is not null) WorkingDirectory.Text = path;
    });

    private async void BrowseCover(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = NativePickers.File(Window.GetWindow(this)!, "Choose cover image", "Cover images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
            if (path is null) return;
            OpenArtwork();
            await coverSearch!.ImportAsync(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { ErrorNotice.Text = "The image browser could not open. " + error.Message; }
    }

    private void FindArtwork(object sender, RoutedEventArgs e) => OpenArtwork();
    private void OpenArtwork()
    {
        coverSearch?.Dispose();
        coverSearch = new CoverSearchView(GameName.Text);
        coverSearch.Cancelled += CloseArtwork;
        coverSearch.CoverChosen += (png, credit) =>
        {
            newCoverBytes = png;
            coverRevision++;
            CoverPath.Text = "Imported cover: " + credit;
            CloseArtwork();
        };
        ArtworkHost.Content = coverSearch;
        EditorScroll.Visibility = EditorFooter.Visibility = Visibility.Collapsed;
        ArtworkHost.Visibility = Visibility.Visible;
        FullWidthChanged?.Invoke(this, true);
    }

    private void CloseArtwork()
    {
        coverSearch?.Dispose();
        coverSearch = null;
        ArtworkHost.Content = null;
        ArtworkHost.Visibility = Visibility.Collapsed;
        EditorScroll.Visibility = EditorFooter.Visibility = Visibility.Visible;
        FullWidthChanged?.Invoke(this, false);
        GameName.Focus();
    }

    private void BrowseSaveFolder(object sender, RoutedEventArgs e) => Pick(() => AppendSavePath(NativePickers.Folder(Window.GetWindow(this)!, "Choose game save folder")));
    private void BrowseSaveFile(object sender, RoutedEventArgs e) => Pick(() => AppendSavePath(NativePickers.File(Window.GetWindow(this)!, "Choose game save file", "All files (*.*)|*.*")));

    private void AppendSavePath(string? path)
    {
        if (path is not null) SaveSources.Text = string.IsNullOrWhiteSpace(SaveSources.Text) ? path : SaveSources.Text.TrimEnd() + Environment.NewLine + path;
    }

    private void Pick(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { ErrorNotice.Text = "The browser could not open. You can paste a full path instead. " + error.Message; }
    }

    private async void SaveGame(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        var draft = new GameDraft(id, GameName.Text, Executable.Text, WorkingDirectory.Text,
            SaveSources.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Enum.Parse<GameStatus>((string)((ComboBoxItem)StatusChoice.SelectedItem).Tag));
        saving = true;
        Fields.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = false;
        ErrorNotice.Text = "Saving game…";
        try
        {
            byte[]? png = newCoverBytes;
            var result = await GameEditorSaveFlow.SaveAsync(draft, png, save, restore,
                async () => await InlinePrompts.ShowAsync(this,
                    "This executable already has a removed profile. Restore its saved name, configuration, cover, history, notes and backups?\n\nThe entries and artwork in this Add game form will not replace the saved profile. Choose No to keep this form and leave the profile removed.",
                    "Restore saved profile?", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes);
            if (result is null) { ErrorNotice.Text = "Restore cancelled. Your entries are kept; the saved profile is still removed."; return; }
            Result = result.Saved;
            WasRestored = result.WasRestored;
            navigation.AcceptSaved(Snapshot());
            saving = false;
            Completed?.Invoke(this, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { ErrorNotice.Text = error.Message; }
        finally
        {
            saving = false;
            Fields.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = true;
        }
    }

    public bool IsBusy => saving || coverSearch?.IsBusy == true;
    public event EventHandler<bool>? Completed;
    private readonly InlineDraftGuard navigation;
    private string Snapshot() => string.Join("\n", GameName.Text, Executable.Text, WorkingDirectory.Text, SaveSources.Text, StatusChoice.SelectedIndex.ToString(), coverRevision.ToString());
    public async Task<bool> CanLeaveAsync()
    {
        if (saving) { ErrorNotice.Text = "Please wait for the current operation to finish."; return false; }
        if (coverSearch is not null && !await coverSearch.CanLeaveAsync()) return false;
        return await navigation.CanLeaveAsync(IsBusy, Snapshot(), async () => await InlinePrompts.ShowAsync(this, "Discard changes to this game?", "Unsaved game", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes);
    }
    private async void CancelEditing(object sender, RoutedEventArgs e)
    { if (await CanLeaveAsync()) Completed?.Invoke(this, false); }
}

internal sealed record GameEditorSaveOutcome(GameSaveResult Saved, bool WasRestored);

internal static class GameEditorSaveFlow
{
    internal static async Task<GameEditorSaveOutcome?> SaveAsync(GameDraft draft, byte[]? cover,
        Func<GameDraft, byte[]?, Task<GameSaveResult>> save,
        Func<Guid, Task<GameSaveResult>>? restore, Func<Task<bool>> confirmRestore)
    {
        try { return new(await save(draft, cover), false); }
        catch (DuplicateGameException error) when (draft.Id is null && error.IsArchived && restore is not null)
        {
            if (!await confirmRestore()) return null;
            return new(await restore(error.ExistingId), true);
        }
    }
}
