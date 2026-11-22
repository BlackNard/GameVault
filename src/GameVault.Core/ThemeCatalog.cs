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
    ];

    public static bool IsKnown(string? id) => All.Any(t => t.Id == id);

    /// <summary>The theme with this id, or Midnight when the id is not known.</summary>
    public static ThemeDefinition Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static int IndexOf(string? id) { for (var i = 0; i < All.Count; i++) if (All[i].Id == id) return i; return 0; }
}
