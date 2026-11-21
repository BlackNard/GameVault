using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using GameVault.Infrastructure.Diagnostics;
using Microsoft.Win32;

namespace GameVault.App;

// "Create diagnostics package" for bug reports (ticket #86).
public partial class MainWindow
{
    private static string GraphicsSummary()
    {
        var found = new List<string>();
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var name in classKey?.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)) ?? [])
            {
                using var key = classKey!.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is string description) found.Add($"{description} (driver {key.GetValue("DriverVersion")})");
            }
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        return found.Count == 0 ? "unknown" : string.Join("; ", found);
    }

    private DiagnosticsInput BuildDiagnosticsInput(bool includeNames)
    {
        var played = items.Count(i => i.Game.PlayedSeconds > 0);
        var log = AppLog.FilePath is { } file ? AppLog.Tail(file, 400) : [];
        string settingsJson;
        try { settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }); }
        catch (NotSupportedException) { settingsJson = "{}"; }
        return new DiagnosticsInput(typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown", RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
            GraphicsSummary(), nvidia.Installed, capture is not null, settingsJson, items.Length, played, [.. items.Select(i => i.Game.Profile?.Name ?? "").Where(n => n.Length > 0)], log, includeNames);
    }

    private async void CreateDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        if (busy || editorOpen || quitPreparing || InlinePrompts.IsOpen(this)) { ShowToast("GameVault is busy", "Finish what you are doing first, then try again.", true); return; }
        var includeNames = DiagnosticsGameNames.IsChecked == true;
        busy = true; UpdateButtons();
        try
        {
            var input = BuildDiagnosticsInput(includeNames);
            var files = await Task.Run(() => DiagnosticsPackage.Build(paths, input));
            var listing = string.Join("\n", files.Select(f => $"• {f.Name} ({Math.Max(1, f.Text.Length / 1024)} KB)"));
            var environment = files.First(f => f.Name == "environment.txt").Text;
            var message = $"The package contains:\n{listing}\n\n{environment}\n\nNo save contents, notes, covers, tokens or user folders are included{(includeNames ? "" : ", and game names are replaced by numbers")}. Next you choose where to save it.";
            if (await InlinePrompts.ShowAsync(this, message, "Create diagnostics package", MessageBoxButton.OKCancel, MessageBoxResult.OK) != MessageBoxResult.OK) return;
            var folder = NativePickers.Folder(this, "Choose where to save the diagnostics package");
            if (folder is null) return;
            var zip = await Task.Run(() => DiagnosticsPackage.Create(paths, input, folder));
            ShowToast("Diagnostics package saved", Path.GetFileName(zip), false, "Show in folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true }));
        }
        catch (Exception error) when (IsExpected(error) || error is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            ShowToast("Could not create the diagnostics package", error.Message, true);
        }
        finally { EndOperation(); }
    }
}
