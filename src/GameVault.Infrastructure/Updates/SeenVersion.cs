using System.Text;

namespace GameVault.Infrastructure.Updates;

/// <summary>The newest version whose "What's new" the player has had (Cache/whats-new.txt). Never throws; a missing or odd file means "none".</summary>
public static class SeenVersion
{
    private static string PathFor(string root) => Path.Combine(root, "Cache", "whats-new.txt");

    public static string? Read(string root)
    {
        try
        {
            var file = new FileInfo(PathFor(root));
            return file.Exists && file.Length <= 200 ? File.ReadAllText(file.FullName, Encoding.UTF8).Trim() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void Write(string root, string version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFor(root))!);
            File.WriteAllText(PathFor(root), version, new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
