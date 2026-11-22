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
        // The four game themes take their colours from the games' store cover art (dominant colours sampled from each cover).
        new("Witcher 3", "Witcher 3", "Icy fog and steel on charcoal, with the blood-red of the Wild Hunt logo.",
            Chrome: "#120b11", Background: "#150a13", Text: "#e9fbf5", Muted: "#b9c9c5", Surface: "#251b22", Raised: "#392b35",
            Line: "#605e61", Accent: "#cfeee4", Primary: "#c3262e", PrimaryText: "#ffffff", Warning: "#ff9b52",
            BackgroundGradient: ["#150a13", "#241a20", "#3d1e22"]),
        new("Cyberpunk 2077", "Cyberpunk 2077", "Cover-art yellow over jacket black, with neon blue and red.",
            Chrome: "#0d0708", Background: "#0e080a", Text: "#fffbe0", Muted: "#d3caa5", Surface: "#211815", Raised: "#35271f",
            Line: "#6b5a30", Accent: "#62b6ff", Primary: "#fdf802", PrimaryText: "#150b0c", Warning: "#ff6a5a",
            BackgroundGradient: ["#0e080a", "#241b18", "#443724"]),
        new("Mass Effect", "Mass Effect", "The Legendary Edition cover: deep-space blue splitting into Reaper red.",
            Chrome: "#070e23", Background: "#0b1430", Text: "#f3f6ff", Muted: "#bfcbe3", Surface: "#141b36", Raised: "#25395d",
            Line: "#3d5a8f", Accent: "#5aa9ff", Primary: "#c31a1e", PrimaryText: "#ffffff", Warning: "#ff9a5a",
            BackgroundGradient: ["#0b1430", "#2a1d3f", "#7a1727"]),
        new("Silent Hill 2", "Silent Hill 2", "Fog greys with the sage green of James's jacket.",
            Chrome: "#181918", Background: "#1b1c1b", Text: "#f2f1ee", Muted: "#c4c3bf", Surface: "#2a2b29", Raised: "#3a3b39",
            Line: "#646360", Accent: "#e4e1de", Primary: "#8fa89a", PrimaryText: "#101312", Warning: "#d68a63",
            BackgroundGradient: ["#1b1c1b", "#2c2d2b", "#434442"]),
        new("Resident Evil 7", "Resident Evil 7", "Stained plaster and dark ink, with the sun-glow amber and the orange VII.",
            Chrome: "#120b07", Background: "#150d09", Text: "#f4e9d6", Muted: "#d2bfa3", Surface: "#251912", Raised: "#3a281c",
            Line: "#7f5c43", Accent: "#f0a64a", Primary: "#e8531f", PrimaryText: "#1a0b05", Warning: "#ffb060",
            BackgroundGradient: ["#150d09", "#2a1b13", "#4a3022"]),
        new("Bloodborne", "Bloodborne", "Yharnam fog and soot black, with a smear of blood red.",
            Chrome: "#080707", Background: "#0a0909", Text: "#eceff1", Muted: "#bcc2c6", Surface: "#1a1919", Raised: "#2c2a2b",
            Line: "#6a6e72", Accent: "#bdc4c8", Primary: "#a81f26", PrimaryText: "#ffffff", Warning: "#e58a78",
            BackgroundGradient: ["#0a0909", "#1f1d1d", "#3f3d3f"]),
        new("Disco Elysium", "Disco Elysium", "Painted ochre and brown, with a teal outline and orange sky.",
            Chrome: "#16110a", Background: "#1e170e", Text: "#fbf3dc", Muted: "#d9cbab", Surface: "#2a2118", Raised: "#42301f",
            Line: "#7a5a3a", Accent: "#3fd0b4", Primary: "#f08a2d", PrimaryText: "#1a0f05", Warning: "#ff7a5a",
            BackgroundGradient: ["#1e170e", "#39271f", "#5f3a25"]),
        new("Resident Evil 9", "Resident Evil 9", "Requiem: cold white-blue and graphite, with a red glow at the edge.",
            Chrome: "#121015", Background: "#17141a", Text: "#f4f5f8", Muted: "#c3c5cc", Surface: "#24222a", Raised: "#3a3840",
            Line: "#5c5e65", Accent: "#cfd8e6", Primary: "#e0463f", PrimaryText: "#1a0606", Warning: "#ff9a6a",
            BackgroundGradient: ["#17141a", "#2b2c33", "#3c3940"]),
    ];

    public static bool IsKnown(string? id) => All.Any(t => t.Id == id);

    /// <summary>The theme with this id, or Midnight when the id is not known.</summary>
    public static ThemeDefinition Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static int IndexOf(string? id) { for (var i = 0; i < All.Count; i++) if (All[i].Id == id) return i; return 0; }
}
