using System.Text.Json;

namespace GameVault.Infrastructure.Notes;

/// <summary>Read-only: whether the Obsidian app already knows a vault. It can only open vaults that were opened in it before.</summary>
public static class ObsidianApp
{
    public static string DefaultConfig => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obsidian", "obsidian.json");

    public static string? KnownVaultId(string vaultPath, string configFile)
    {
        try
        {
            var info = new FileInfo(configFile);
            if (!info.Exists || info.Length > 1024 * 1024) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(configFile));
            if (!document.RootElement.TryGetProperty("vaults", out var vaults) || vaults.ValueKind != JsonValueKind.Object) return null;
            var wanted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(vaultPath));
            foreach (var vault in vaults.EnumerateObject())
                if (vault.Value.ValueKind == JsonValueKind.Object && vault.Value.TryGetProperty("path", out var path) && path.GetString() is { } known &&
                    Path.IsPathFullyQualified(known) && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(known)), wanted, StringComparison.OrdinalIgnoreCase))
                    return vault.Name;
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    public static string OpenUri(string vaultId) => "obsidian://open?vault=" + Uri.EscapeDataString(vaultId);
}
