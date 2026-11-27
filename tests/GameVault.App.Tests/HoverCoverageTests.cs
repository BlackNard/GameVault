using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace GameVault.App.Tests;

/// <summary>
/// Every button, switch, list choice and field says what it does when pointed at (issue #102), unless its own words are already clear. A new control either
/// gets <c>local:SettingHelp.Text</c> (or a tooltip) or, when it needs no words, its label is added to <see cref="Clear"/> on purpose.
/// </summary>
public sealed class HoverCoverageTests
{
    private static readonly string[] Kinds = ["Button", "ToggleButton", "RadioButton", "CheckBox", "ComboBox", "TextBox", "MenuItem"];

    /// <summary>Labels (or, for fields, names) that explain themselves.</summary>
    private static readonly HashSet<string> Clear = new(StringComparer.Ordinal)
    {
        "Cancel", "Close", "Back", "Back to game", "Back to backups", "Back to covers", "‹ All games", "Previous page", "Refresh", "Cancel download", "Cancel operation",
        "Browse…", "Add game", "Save settings", "Save tags", "Save description", "Save correction", "Read more", "Open game", "Open folder", "Open Saves",
        "Library", "Settings", "Quit GameVault", "Play", "Status", "Edit game…", "Sessions & notes", "Open install folder",
        "All", "Playing", "Completed", "On hold", "Not started", "Backlog", "Abandoned", "Retired", "Game status",
        "Acknowledge", "I reviewed the selected copy, destinations and file changes.",
        // the label comes from the item shown
        "{Binding Name}", "{Binding PrimaryLabel}", "{Binding ActionLabel}", "{Binding Path}",
        // fields with a placeholder or a caption beside them
        "Search settings", "Search games or run a command", "Game description", "Session note text", "Restore destination",
        "CorrectionHours", "CorrectionMinutes", "CorrectionSeconds"
    };

    private static string App([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GameVault.App");

    [Fact]
    public void EveryControlExplainsItselfOrIsAlreadyClear()
    {
        var silent = new List<string>();
        foreach (var file in Directory.GetFiles(App(), "*.xaml").Where(f => Path.GetFileName(f) is not ("Styles.xaml" or "App.xaml")))
            Walk(XDocument.Load(file).Root!, false, Path.GetFileName(file), silent);
        Assert.True(silent.Count == 0, "These controls say nothing on hover:\n" + string.Join("\n", silent));
    }

    private static void Walk(XElement element, bool explained, string file, List<string> silent)
    {
        string? Value(string name) => element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
        var help = Value("SettingHelp.Text") is not null;
        var tooltip = Value("ToolTip") is not null || element.Elements().Any(e => e.Name.LocalName.EndsWith(".ToolTip", StringComparison.Ordinal));
        if (!explained && !help && !tooltip && Kinds.Contains(element.Name.LocalName) && Value("IsReadOnly") != "True")
        {
            var label = Value("Content") ?? Value("Header") ?? Value("AutomationProperties.Name") ?? Value("Name") ?? "";
            if (!Clear.Contains(label)) silent.Add($"{file}: {element.Name.LocalName} \"{label}\"");
        }
        // A row with help explains the controls inside it.
        foreach (var child in element.Elements()) Walk(child, explained || help, file, silent);
    }
}
