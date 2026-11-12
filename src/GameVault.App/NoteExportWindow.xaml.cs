using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameVault.App.Notes;
using GameVault.Core;
using GameVault.Infrastructure.Notes;
using GameVault.Infrastructure.Storage;
using Microsoft.Win32;
namespace GameVault.App;
public partial class NoteExportWindow : Window
{
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly Guid gameId;
    private readonly NoteExportEntry? selected;
    private readonly string? selectedWarning;
    private NoteExportCatalog catalog = new([], []);
    private bool initializing = true, busy;
    public NoteExportWindow(ApplicationPaths paths, AppDataStore store, Guid gameId, string theme, NoteExportEntry? selected, string? warning)
    {
        this.paths = paths; this.store = store; this.gameId = gameId; this.selected = selected; selectedWarning = warning;
        InitializeComponent(); ThemePalette.Apply(this, theme);
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        MinHeight = Math.Min(MinHeight, MaxHeight); MinWidth = Math.Min(MinWidth, MaxWidth);
        SelectedScope.IsEnabled = selected is not null;
        if (selected is null) ScopeChoice.SelectedIndex = 1;
        Loaded += async (_, _) => { initializing = false; await LoadScope(); };
        Closing += BeforeClosing;
    }
    private void BeforeClosing(object? sender, CancelEventArgs e) { if (busy) e.Cancel = true; }
    private async void ScopeChanged(object sender, SelectionChangedEventArgs e) { if (!initializing && !busy) await LoadScope(); }
    private async Task LoadScope()
    {
        SetBusy(true); ResultNotice.Text = ""; Acknowledge.IsChecked = false;
        try
        {
            catalog = ScopeChoice.SelectedIndex == 0 ? new(selected is null ? [] : [selected], selectedWarning is null ? [] : [selectedWarning]) :
                await NoteExports.LoadAsync(store, ScopeChoice.SelectedIndex == 1 ? gameId : null);
            Packaging.Visibility = ScopeChoice.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
            CountNotice.Text = catalog.Entries.Length == 0 ? "No saved notes in this scope. Save a session note or review first." : $"{catalog.Entries.Length} saved entries ready to export.";
            Warnings.Text = string.Join("\n", catalog.Warnings);
            Acknowledge.Visibility = catalog.Warnings.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception e) when (Expected(e)) { catalog = new([], []); CountNotice.Text = "Could not load the export scope."; Warnings.Text = e.Message; }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool value) { busy = value; Fields.IsEnabled = CloseButton.IsEnabled = !value; UpdateExport(); }
    private void UpdateExport() => ExportButton.IsEnabled = !busy && catalog.Entries.Length > 0 && (catalog.Warnings.Length == 0 || Acknowledge.IsChecked == true);
    private void Acknowledged(object sender, RoutedEventArgs e) { if (!initializing) UpdateExport(); }
    private async void Export(object sender, RoutedEventArgs e)
    {
        if (!ExportButton.IsEnabled) return;
        try
        {
            var pdf = FormatChoice.SelectedIndex == 1;
            var extension = pdf ? ".pdf" : ".md";
            var separate = ScopeChoice.SelectedIndex != 0 && SeparateChoice.IsChecked == true;
            string? target;
            if (separate) target = NativePickers.Folder(this, "Choose a folder for the exported notes");
            else
            {
                var dialog = new SaveFileDialog { Title = "Export GameVault notes", Filter = pdf ? "PDF document (*.pdf)|*.pdf" : "Markdown document (*.md)|*.md", DefaultExt = extension,
                    AddExtension = true, OverwritePrompt = true, FileName = "GameVault-notes" + extension };
                target = dialog.ShowDialog(this) == true ? dialog.FileName : null;
            }
            if (target is null) return;
            SetBusy(true); ResultNotice.Text = "Exporting…";
            var result = await NoteExports.WriteAsync(paths, catalog.Entries, target, extension, separate, !separate, pdf ? PdfNoteWriter.Write : NoteExports.WriteMarkdown);
            ResultNotice.Text = $"{result.Paths.Length} file(s) exported; {result.Failures.Length} failed; {catalog.Warnings.Length} scope warning(s).\n" +
                string.Join("\n", result.Paths) + (result.Failures.Length == 0 ? "" : "\nFailures (completed files were kept):\n" + string.Join("\n", result.Failures));
        }
        catch (Exception error) when (Expected(error)) { ResultNotice.Text = "Could not export. " + error.Message; }
        finally { SetBusy(false); }
    }
    private void CloseExport(object sender, RoutedEventArgs e) => Close();
    private static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException;
}