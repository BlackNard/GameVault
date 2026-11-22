namespace GameVault.Core;

/// <summary>
/// One colour theme as plain data: adding a theme means adding one entry to <see cref="ThemeCatalog.All"/>.
/// Colours are "#rrggbb". When <see cref="BackgroundGradient"/> is set (three stops, top to bottom) it replaces the flat
/// <see cref="Background"/> for the window; <see cref="Background"/> is still used as the fallback colour.
/// </summary>
public sealed record ThemeDefinition(
    string Id, string Name, string Tagline,
    string Chrome, string Background, string Text, string Muted, string Surface, string Raised,
    string Line, string Accent, string Primary, string PrimaryText, string Warning,
    IReadOnlyList<string>? BackgroundGradient = null);

/// <summary>Every theme the app offers, in the order the picker shows them. The saved theme name is the <see cref="ThemeDefinition.Id"/>.</summary>
public static class ThemeCatalog
{
    public const string DefaultId = "Midnight";

    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new("Midnight", "Midnight", "Calm navy blue. The default.",
            Chrome: "#111822", Background: "#131b26", Text: "#edf2f8", Muted: "#adbdcf", Surface: "#1c2838", Raised: "#26364a",
            Line: "#48607d", Accent: "#b6ceff", Primary: "#b6ceff", PrimaryText: "#131b26", Warning: "#ffc6ab"),
        new("Crimson", "Crimson", "Red and black.",
            Chrome: "#160f14", Background: "#120e11", Text: "#f8eef2", Muted: "#d0bbc5", Surface: "#21171d", Raised: "#35232c",
            Line: "#63404f", Accent: "#ffb2c0", Primary: "#cb3c54", PrimaryText: "#fff6f8", Warning: "#ffc5aa"),
        new("Vice City", "Vice City", "Neon dusk: purple fading to pink, with sunset orange.",
            Chrome: "#140828", Background: "#150a2e", Text: "#fff4fb", Muted: "#dcc0ea", Surface: "#26103f", Raised: "#3b1a5c",
            Line: "#8a43a6", Accent: "#ffb36b", Primary: "#ff4f9f", PrimaryText: "#1a0630", Warning: "#ffcf8a",
            BackgroundGradient: ["#150a2e", "#2c1050", "#5c1a62"]),
        new("Witcher 3", "Witcher 3", "Dark forest green and old parchment, with wolf-school gold.",
            Chrome: "#0e130f", Background: "#121a15", Text: "#f3ecd9", Muted: "#cbc3a8", Surface: "#1b261e", Raised: "#2a3a2d",
            Line: "#5d6b4f", Accent: "#e3bd69", Primary: "#d9a441", PrimaryText: "#17110a", Warning: "#f0b98a",
            BackgroundGradient: ["#0e1410", "#18241b", "#2b2518"]),
        new("Cyberpunk 2077", "Cyberpunk 2077", "Night City black with neon yellow and cyan.",
            Chrome: "#07070c", Background: "#0a0a10", Text: "#f5f7fa", Muted: "#b9c0d0", Surface: "#14141f", Raised: "#232334",
            Line: "#3f4f6e", Accent: "#00e5ff", Primary: "#fcee0a", PrimaryText: "#0a0a0a", Warning: "#ff8a5c",
            BackgroundGradient: ["#08080d", "#13111f", "#24102e"]),
        new("Mass Effect", "Mass Effect", "Deep-space navy with ice-blue light and omni-tool orange.",
            Chrome: "#060b14", Background: "#0a1220", Text: "#eaf4ff", Muted: "#b4c6dc", Surface: "#111c30", Raised: "#1c2d48",
            Line: "#3d5f8f", Accent: "#6fd3ff", Primary: "#ff9f1c", PrimaryText: "#0d0a05", Warning: "#ffc27a",
            BackgroundGradient: ["#070c16", "#0e1a30", "#142a4a"]),
        new("Silent Hill 2", "Silent Hill 2", "Fog grey and rust, drained of colour.",
            Chrome: "#101214", Background: "#15171a", Text: "#e9e7df", Muted: "#b8b5a9", Surface: "#202327", Raised: "#2f3337",
            Line: "#5f625c", Accent: "#c9b98a", Primary: "#a8472f", PrimaryText: "#fbf3ee", Warning: "#e0a86a",
            BackgroundGradient: ["#121416", "#1f2225", "#33322d"]),
    ];

    public static bool IsKnown(string? id) => All.Any(t => t.Id == id);

    /// <summary>The theme with this id, or Midnight when the id is not known.</summary>
    public static ThemeDefinition Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static int IndexOf(string? id) { for (var i = 0; i < All.Count; i++) if (All[i].Id == id) return i; return 0; }
}
