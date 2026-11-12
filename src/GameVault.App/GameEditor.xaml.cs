using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.Core;

namespace GameVault.App;

public partial class GameEditor : Window
{
    private readonly Guid? id;
    private readonly Func<GameDraft, byte[]?, Task<GameSaveResult>> save;
    private string? newCover;
    private bool saving;
    public GameSaveResult? Result { get; private set; }

    public GameEditor(string theme, GameProfile? profile, Func<GameDraft, byte[]?, Task<GameSaveResult>> save)
    {
        this.save = save;
        id = profile?.Id;
        InitializeComponent();
        ThemePalette.Apply(this, theme);
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        MinHeight = Math.Min(MinHeight, MaxHeight);
        MinWidth = Math.Min(MinWidth, MaxWidth);
        if (profile is not null)
        {
            Title = Heading.Text = "Edit game";
            SaveButton.Content = "Save changes";
            GameName.Text = profile.Name;
            Executable.Text = profile.ExecutablePath;
            WorkingDirectory.Text = profile.WorkingDirectory;
            SaveSources.Text = string.Join(Environment.NewLine, profile.SaveSources);
            CoverPath.Text = "Existing cover is kept unless you choose another image.";
            foreach (ComboBoxItem item in StatusChoice.Items)
                if ((string)item.Tag == profile.Status.ToString()) StatusChoice.SelectedItem = item;
        }
        Closing += BeforeClosing;
        Loaded += (_, _) => GameName.Focus();
    }

    private void BrowseExecutable(object sender, RoutedEventArgs e) => Pick(() =>
    {
        var path = NativePickers.File(this, "Choose game executable", "Windows executables (*.exe)|*.exe", Executable.Text);
        if (path is null) return;
        WorkingDirectory.Text = GamePathSuggestions.WorkingDirectory(Executable.Text, WorkingDirectory.Text, path);
        Executable.Text = path;
        GameName.Text = GamePathSuggestions.GameName(GameName.Text, path);
    });

    private void BrowseWorking(object sender, RoutedEventArgs e) => Pick(() =>
    {
        var path = NativePickers.Folder(this, "Choose working directory", WorkingDirectory.Text);
        if (path is not null) WorkingDirectory.Text = path;
    });

    private void BrowseCover(object sender, RoutedEventArgs e) => Pick(() =>
    {
        var path = NativePickers.File(this, "Choose cover image", "Cover images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", newCover ?? "");
        if (path is not null) { newCover = path; CoverPath.Text = path; }
    });

    private void BrowseSaveFolder(object sender, RoutedEventArgs e) => Pick(() => AppendSavePath(NativePickers.Folder(this, "Choose game save folder")));
    private void BrowseSaveFile(object sender, RoutedEventArgs e) => Pick(() => AppendSavePath(NativePickers.File(this, "Choose game save file", "All files (*.*)|*.*")));

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
            var coverPath = newCover;
            byte[]? png = coverPath is null ? null : await Task.Run(() => CoverImages.Normalize(coverPath));
            Result = await save(draft, png);
            saving = false;
            DialogResult = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { ErrorNotice.Text = error.Message; }
        finally
        {
            saving = false;
            Fields.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = true;
        }
    }

    private void BeforeClosing(object? sender, CancelEventArgs e)
    {
        if (!saving) return;
        e.Cancel = true;
        ErrorNotice.Text = "Please wait for the game to finish saving.";
    }
}
