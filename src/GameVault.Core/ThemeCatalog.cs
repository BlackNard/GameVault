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
    IReadOnlyList<string>? BackgroundGradient = null, string? Glow = null)
{
    /// <summary>The colour of the light around a hovered game card: the theme's own, or its accent.</summary>
    public string GlowColor => Glow ?? Accent;
}

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
            BackgroundGradient: ["#150a2e", "#2c1050", "#5c1a62"], Glow: "#ff4f9f"),
        // The four game themes take their colours from the games' store cover art (dominant colours sampled from each cover).
        new("Witcher 3", "Witcher 3", "Icy fog and steel on charcoal, with the blood-red of the Wild Hunt logo.",
            Chrome: "#120b11", Background: "#150a13", Text: "#e9fbf5", Muted: "#b9c9c5", Surface: "#251b22", Raised: "#392b35",
            Line: "#605e61", Accent: "#cfeee4", Primary: "#c3262e", PrimaryText: "#ffffff", Warning: "#ff9b52",
            BackgroundGradient: ["#150a13", "#241a20", "#3d1e22"]),
        new("Cyberpunk 2077", "Cyberpunk 2077", "Cover-art yellow over jacket black, with neon blue and red.",
            Chrome: "#0d0708", Background: "#0e080a", Text: "#fffbe0", Muted: "#d3caa5", Surface: "#211815", Raised: "#35271f",
            Line: "#6b5a30", Accent: "#62b6ff", Primary: "#fdf802", PrimaryText: "#150b0c", Warning: "#ff6a5a",
            BackgroundGradient: ["#0e080a", "#3a340b", "#5a520f"], Glow: "#fdf802"),
        new("Mass Effect", "Mass Effect", "The Legendary Edition cover: deep-space blue splitting into Reaper red.",
            Chrome: "#070e23", Background: "#0b1430", Text: "#f3f6ff", Muted: "#bfcbe3", Surface: "#141b36", Raised: "#25395d",
            Line: "#3d5a8f", Accent: "#5aa9ff", Primary: "#c31a1e", PrimaryText: "#ffffff", Warning: "#ff9a5a",
            BackgroundGradient: ["#0b1430", "#2a1d3f", "#7a1727"], Glow: "#c31a1e"),
        new("Silent Hill 2", "Silent Hill 2", "Fog greys with the sage green of James's jacket.",
            Chrome: "#181918", Background: "#1b1c1b", Text: "#f2f1ee", Muted: "#c4c3bf", Surface: "#2a2b29", Raised: "#3a3b39",
            Line: "#646360", Accent: "#e4e1de", Primary: "#8fa89a", PrimaryText: "#101312", Warning: "#d68a63",
            BackgroundGradient: ["#1b1c1b", "#2c2d2b", "#434442"]),
        new("Resident Evil 7", "Resident Evil 7", "Stained plaster and dark ink, with the sun-glow amber and the orange VII.",
            Chrome: "#120b07", Background: "#150d09", Text: "#f4e9d6", Muted: "#d2bfa3", Surface: "#251912", Raised: "#3a281c",
            Line: "#7f5c43", Accent: "#f0a64a", Primary: "#e8531f", PrimaryText: "#1a0b05", Warning: "#ffb060",
            BackgroundGradient: ["#150d09", "#2a1b13", "#4a3022"]),
        new("Bloodborne", "Bloodborne", "The dark Yharnam cover: soot black, cold slate fog and moonlight.",
            Chrome: "#080808", Background: "#0d0b0b", Text: "#eceeee", Muted: "#b8c1c5", Surface: "#181a1b", Raised: "#262c2f",
            Line: "#5d6c73", Accent: "#dfe3e4", Primary: "#8fa6b2", PrimaryText: "#0d0b0b", Warning: "#d8c9a0",
            BackgroundGradient: ["#0d0b0b", "#1a1d1f", "#2e373b"]),
        // Disco Elysium is painted, so its colours come from in-game screens (muddy olive and ochre, teal-black shadows, halftone orange) as well as the cover.
        new("Disco Elysium", "Disco Elysium", "Painted olive, ochre and teal-black, with halftone orange and skill-check cyan.",
            Chrome: "#0b1612", Background: "#0f1f1a", Text: "#f1ead2", Muted: "#cdc5a6", Surface: "#1d2421", Raised: "#2f3a35",
            Line: "#6b6a4a", Accent: "#46c2b6", Primary: "#e8892c", PrimaryText: "#150c03", Warning: "#ff6a3d",
            BackgroundGradient: ["#0f1f1a", "#242925", "#514534"], Glow: "#e8892c"),
        new("Resident Evil 9", "Resident Evil 9", "Requiem: cold white-blue and graphite, with a red glow at the edge.",
            Chrome: "#121015", Background: "#17141a", Text: "#f4f5f8", Muted: "#c3c5cc", Surface: "#24222a", Raised: "#3a3840",
            Line: "#5c5e65", Accent: "#cfd8e6", Primary: "#e0463f", PrimaryText: "#1a0606", Warning: "#ff9a6a",
            BackgroundGradient: ["#17141a", "#2b2c33", "#3c3940"]),
        new("Clair Obscur: Expedition 33", "Clair Obscur: Expedition 33", "Ash and sepia with Belle Epoque gold, a cyan glow and fallen red petals.",
            Chrome: "#080807", Background: "#0b0a09", Text: "#f3ead8", Muted: "#cfc6ae", Surface: "#1c1b18", Raised: "#2e2c27",
            Line: "#7b6f5a", Accent: "#6fdcdc", Primary: "#c9a96e", PrimaryText: "#140f06", Warning: "#e8665c",
            BackgroundGradient: ["#0b0a09", "#292824", "#4c4236"]),
        new("Tekken 4", "Tekken 4", "Storm-night navy and electric blue, with the gold of the flame jacket.",
            Chrome: "#080c16", Background: "#0a1020", Text: "#f2f6ff", Muted: "#bcd0ee", Surface: "#121c33", Raised: "#1b2f5a",
            Line: "#3b6fc4", Accent: "#8fc8ff", Primary: "#f2b52a", PrimaryText: "#140c00", Warning: "#ff8a3a",
            BackgroundGradient: ["#0a1020", "#0c284d", "#064c9f"]),
        // Two thank-you themes for the assistants that helped build GameVault, in their well-known colours (no cover art involved).
        new("Claude", "Claude", "Warm charcoal and cream with the terracotta orange.",
            Chrome: "#141413", Background: "#1f1e1d", Text: "#faf9f5", Muted: "#c2c0b6", Surface: "#262624", Raised: "#30302e",
            Line: "#5e5d59", Accent: "#e8a07f", Primary: "#d97757", PrimaryText: "#141413", Warning: "#f0b27a"),
        new("ChatGPT", "ChatGPT", "Neutral dark grey with the signature green.",
            Chrome: "#171717", Background: "#212121", Text: "#ececec", Muted: "#b4b4b4", Surface: "#2a2a2a", Raised: "#343434",
            Line: "#4d4d4d", Accent: "#3fcfa6", Primary: "#10a37f", PrimaryText: "#04120d", Warning: "#f0b86e"),
    ];

    public static bool IsKnown(string? id) => All.Any(t => t.Id == id);

    /// <summary>The theme with this id, or Midnight when the id is not known.</summary>
    public static ThemeDefinition Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static int IndexOf(string? id) { for (var i = 0; i < All.Count; i++) if (All[i].Id == id) return i; return 0; }
}
