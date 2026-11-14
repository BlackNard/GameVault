using System.IO;
namespace GameVault.App.Artwork;
/// <summary>Privacy boundary for explicit browser search and bounded local cover import.</summary>
internal static class CoverImport
{
    private const int MaxBytes = 12 * 1024 * 1024;
    public static Uri BrowserSearch(string title)
    {
        if (title is null || title.Any(char.IsControl)) throw new ArgumentException("Enter a game title, never a path.");
        title = title.Trim();
        if (title.Length is 0 or > 240 || title.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(title) ||
            (title.Length >= 2 && char.IsAsciiLetter(title[0]) && title[1] == ':') || title.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter a game title, never a path or address.");
        return new Uri("https://www.bing.com/images/search?q=" + Uri.EscapeDataString(title + " game cover"));
    }
    public static async Task<byte[]> ReadAsync(string[] files)
    {
        if(files.Length != 1) throw new ArgumentException("Choose exactly one PNG or JPEG image.");
        var path = files[0];
        var extension = Path.GetExtension(path);
        if(!new[]{".png", ".jpg", ".jpeg"}.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a static PNG or JPEG image.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if(input.Length is < 24 or > MaxBytes) throw new InvalidOperationException("Artwork exceeds the safe size limit.");
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes);
        // The independent normalizer validates magic bytes, dimensions and frame count before accepting artwork.
        return await Task.Run(() => ArtworkImageNormalizer.Normalize(bytes));
    }
}
