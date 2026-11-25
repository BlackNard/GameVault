using System.Globalization;

namespace GameVault.Core;

/// <summary>The testable parts of the capture gallery (issue #60): layout, labels and moving through screenshots.</summary>
public static class CaptureGalleryRules
{
    public const double TileWidth = 248;

    /// <summary>How many tiles fit across a gallery of this width (at least one).</summary>
    public static int Columns(double width) => double.IsFinite(width) && width >= TileWidth * 2 ? (int)(width / TileWidth) : 1;

    public static T[][] Rows<T>(IReadOnlyList<T> items, int columns) => [.. items.Chunk(Math.Max(1, columns))];

    public static string Size(long bytes) => bytes switch
    {
        < 0 => "",
        < 1024 * 1024 => Math.Max(1, bytes / 1024).ToString(CultureInfo.CurrentCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / 1024.0 / 1024).ToString("0.#", CultureInfo.CurrentCulture) + " MB",
        _ => (bytes / 1024.0 / 1024 / 1024).ToString("0.##", CultureInfo.CurrentCulture) + " GB"
    };

    /// <summary>"0:07", "12:05", "1:02:03"; empty when unknown.</summary>
    public static string Duration(TimeSpan? length)
    {
        if (length is not { } value || value <= TimeSpan.Zero) return "";
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }

    public static string Title(CaptureMedia media) => (media.Kind == CaptureMediaKind.Video ? "Recording" : "Screenshot") +
        (media.SourceFingerprint is null ? "" : " · NVIDIA App") + (media.Warning is null ? "" : " · video froze") + (media.Status == CaptureMediaStatus.Complete ? "" : " · incomplete");

    /// <summary>The screenshot <paramref name="step"/> places from <paramref name="current"/> among the finished screenshots, or null at an end.</summary>
    public static CaptureMedia? Neighbour(IReadOnlyList<CaptureMedia> media, Guid current, int step)
    {
        var pictures = media.Where(m => m is { Kind: CaptureMediaKind.Screenshot, Status: CaptureMediaStatus.Complete }).ToArray();
        var index = Array.FindIndex(pictures, m => m.Id == current);
        if (index < 0) return null;
        var next = index + step;
        return next >= 0 && next < pictures.Length ? pictures[next] : null;
    }
}
