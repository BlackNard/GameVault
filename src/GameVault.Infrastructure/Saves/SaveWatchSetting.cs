using GameVault.Core;

namespace GameVault.Infrastructure.Saves;

/// <summary>Watching for saves while playing is on unless <c>Data/save-watch-off.marker</c> exists: its own file, so older versions are unaffected.</summary>
public static class SaveWatchSetting
{
    private static string Marker(ApplicationPaths paths) => Path.Combine(paths.Data, "save-watch-off.marker");

    public static bool IsOn(ApplicationPaths paths) => !File.Exists(Marker(paths));

    public static void Set(ApplicationPaths paths, bool on)
    {
        if (on) { File.Delete(Marker(paths)); return; }
        Directory.CreateDirectory(paths.Data);
        File.WriteAllText(Marker(paths), "");
    }
}
