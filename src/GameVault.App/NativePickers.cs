using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GameVault.App;

internal static class NativePickers
{
    public static string? File(Window owner, string title, string filter, string current = "")
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = false };
        var initial = InitialFolder(current);
        if (initial is not null) dialog.InitialDirectory = initial;
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public static string? Folder(Window owner, string title, string current = "")
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        var initial = InitialFolder(current);
        if (initial is not null) dialog.InitialDirectory = initial;
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    private static string? InitialFolder(string current)
    {
        try
        {
            current = current.Trim().Trim('"');
            if (Directory.Exists(current)) return current;
            var parent = Path.GetDirectoryName(current);
            return Directory.Exists(parent) ? parent : null;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }
}
