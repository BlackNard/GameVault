using System.Text.Json;

namespace GameVault.Core;

/// <summary>What the startup window needs to know before the library is read, taken tolerantly from settings.json.</summary>
public sealed record SplashPreferences(string Theme, bool Show, bool ReduceMotion)
{
    public static SplashPreferences Default { get; } = new(ThemeCatalog.DefaultId, true, false);

    /// <summary>
    /// Reads only the theme, the "show the startup window" switch and reduce-motion from the settings file text. A missing, damaged
    /// or odd file gives the defaults (themed Midnight, shown, animated): the real settings load and report their own problems later.
    /// </summary>
    public static SplashPreferences Parse(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return Default;
        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Default;
            var root = document.RootElement;
            var theme = root.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String && ThemeCatalog.IsKnown(t.GetString()) ? t.GetString()! : ThemeCatalog.DefaultId;
            var show = !(root.TryGetProperty("showSplash", out var s) && s.ValueKind == JsonValueKind.False);
            var reduce = root.TryGetProperty("reduceMotion", out var r) && r.ValueKind == JsonValueKind.True;
            return new SplashPreferences(theme, show, reduce);
        }
        catch (JsonException) { return Default; }
    }
}

/// <summary>How long the startup window stays: at least a moment so it never just flashes, never longer than that once loading is done.</summary>
public static class SplashTiming
{
    public static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(1200);

    /// <summary>The wait still needed before the splash may close (zero when it has already been visible long enough).</summary>
    public static TimeSpan RemainingDelay(DateTimeOffset shownAt, DateTimeOffset now, TimeSpan? minimum = null)
    {
        var elapsed = now - shownAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;                 // a clock that moved backwards must not lengthen the wait
        var left = (minimum ?? MinimumVisible) - elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}
