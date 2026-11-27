namespace GameVault.Core;

/// <summary>A page of Settings (issue #56). <paramref name="Icon"/> is a Windows symbol-font character; <paramref name="StartsGroup"/> draws a divider above it.</summary>
public sealed record SettingsCategory(string Key, string Title, string Description, string Icon, bool StartsGroup = false);

/// <summary>One option the Settings search can find. <paramref name="Target"/> is the name of the control on that page.</summary>
public sealed record SettingsEntry(string Category, string Title, string Target, string Keywords)
{
    public override string ToString() => Title + " · " + SettingsCatalog.Title(Category);
}

/// <summary>The Settings pages and what the search knows about them.</summary>
public static class SettingsCatalog
{
    public const string Appearance = "appearance";
    public const string General = "general";
    public const string Library = "library";
    public const string Backups = "backups";
    public const string Capture = "capture";
    public const string Updates = "updates";
    public const string Advanced = "advanced";

    public static readonly IReadOnlyList<SettingsCategory> Categories =
    [
        new(Appearance, "Appearance", "How GameVault looks and starts.", "\uE790"),
        new(General, "General", "What GameVault does while it runs in the background.", "\uE713"),
        new(Library, "Library and game info", "Where information about your games comes from, and your saved views.", "\uE8F1", true),
        new(Backups, "Backups and data", "Where save backups go, and backing up, restoring or importing your library.", "\uE74E"),
        new(Capture, "Capture", "Screenshots, recordings and their shortcuts.", "\uE722"),
        new(Updates, "Updates", "New versions of GameVault.", "\uE895", true),
        new(Advanced, "Advanced", "Help with problems, and where your profiles are stored.", "\uE90F")
    ];

    public static readonly IReadOnlyList<SettingsEntry> Entries =
    [
        new(Appearance, "Color theme", "BrowseThemesButton", "themes colour look dark skin appearance preview every theme"),
        new(Appearance, "Reduce motion", "ReduceMotionChoice", "animation animations motion transitions fade slide windows accessibility"),
        new(General, "Keep running in the tray", "CloseToTrayChoice", "close minimise minimize background tray tracking window closing leaves it tracking"),
        new(General, "Backup notifications", "BackupNotificationsChoice", "notify alerts bell attention failed"),
        new(Library, "Game information sources", "MetadataSources", "metadata description steam gog wikidata howlongtobeat providers lookup automatic look up information automatically cover automatically titles sent only fill empty never replaced install size on disk"),
        new(Library, "Fetch missing info for all games", "FetchAllInfoButton", "refresh metadata covers descriptions download"),
        new(Library, "Save-location database", "DownloadSaveDatabaseButton", "ludusavi pcgamingwiki saves folders list github community creative commons suggestions confirmation"),
        new(Library, "Saved views", "SavedViewsSection", "filters quick buttons rename order delete quick buttons above the library"),
        new(Backups, "Watch for saves while playing", "WatchSavesSwitch", "save folder find detect automatic discovery notes which folders a game writes to while it runs nothing leaves your pc"),
        new(Backups, "Guide: how to back up and restore", "BackupGuide", "help guide restore reinstall new pc"),
        new(Backups, "Default backup folder", "DefaultBackupLocation", "save backups storage destination drive cloud location future backups original folders"),
        new(Backups, "Obsidian vault", "ObsidianStatusText", "obsidian notes markdown export session review folder create link unlink"),
        new(Backups, "Export to CSV", "ExportCsvButton", "spreadsheet excel csv export games sessions playtime playthroughs journal lists"),
        new(Backups, "Back up GameVault data", "BackUpDataButton", "export zip library profiles move upgrade new pc sessions notes settings include recorded media"),
        new(Backups, "Restore from a backup", "RestoreDataButton", "import zip recover library replaces replaced replace the whole library keeps a copy"),
        new(Backups, "Import from an old GameVault folder", "ImportOldFolderButton", "profiles old copy uninstall remains games left behind only new games"),
        new(Capture, "Recorder, video and audio", "CaptureSettings", "screenshot recording video resolution frame rate fps audio nvidia size silent microphone game sound never stutters gpu nvidia app"),
        new(Capture, "Capture shortcuts", "CaptureSettings", "hotkeys keys keyboard screenshot record overlay xbox game bar amd steam conflict same keys floating controls windowed fullscreen monitor"),
        new(Capture, "Capture troubleshooting", "CaptureSettings", "install runtime visual c++ microsoft administrator media feature pack missing components"),
        new(Updates, "What's new", "WhatsNewButton", "release notes changes fixed what each version brought"),
        new(Updates, "Check for updates", "CheckForUpdatesButton", "new version release update now"),
        new(Updates, "Check at start", "CheckForUpdatesChoice", "update check github automatic one request to github nothing sent"),
        new(Updates, "Install updates automatically", "AutoUpdateChoice", "auto update download install close gamevault update now bell"),
        new(Advanced, "Diagnostics package", "DiagnosticsButton", "bug report logs support crash zip redacted settings counts log lines game names"),
        new(Advanced, "Local profile storage", "SetStorage", "folder path profiles location disk")
    ];

    public static bool IsKnown(string? key) => Categories.Any(c => c.Key == key);

    public static string Title(string? key) => Categories.FirstOrDefault(c => c.Key == key)?.Title ?? "";

    /// <summary>Options whose title, page or keywords contain every word typed; title matches come first.</summary>
    public static IReadOnlyList<SettingsEntry> Search(string? query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];
        bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
        return [.. Entries.Where(e => words.All(w => Has(e.Title, w) || Has(e.Keywords, w) || Has(Title(e.Category), w)))
            .OrderByDescending(e => words.Count(w => Has(e.Title, w)))];
    }
}
