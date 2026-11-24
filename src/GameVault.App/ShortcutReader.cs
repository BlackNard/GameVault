using System.Runtime.InteropServices;

namespace GameVault.App;

/// <summary>Reads where a Windows .lnk shortcut points, through the shell's own scripting object (no extra package). Nothing is run or modified.</summary>
internal static class ShortcutReader
{
    public static string? Target(string shortcutPath)
    {
        object? shell = null;
        object? link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            shell = Activator.CreateInstance(type);
            if (shell is null) return null;
            link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
            if (link is null) return null;
            return link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null) as string;
        }
        catch (Exception error) when (error is COMException or System.Reflection.TargetInvocationException or MissingMethodException or InvalidCastException or NotSupportedException or UnauthorizedAccessException)
        { return null; }
        finally
        {
            if (link is not null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }
}
