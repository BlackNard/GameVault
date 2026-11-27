namespace GameVault.Core;

/// <summary>The linked Obsidian vault (Data/obsidian.json). A vault inside the GameVault folder is stored relative to it, so the link survives moving the folder.</summary>
public sealed record ObsidianSettings(int SchemaVersion, string? VaultPath, Guid VaultId)
{
    /// <summary>The folder a new vault gets unless the player chooses another place.</summary>
    public const string DefaultFolder = "Obsidian Vault";

    public static bool IsValid(ObsidianSettings? value) => value is { SchemaVersion: 1 } && (value.VaultPath is null || value.VaultPath is { Length: > 0 and <= 1000 });
}

/// <summary>What GameVault last wrote for each note of one game (Profiles/&lt;game&gt;/obsidian-export.json), to notice edits made in Obsidian.</summary>
public sealed record ObsidianExportRecord(int SchemaVersion, Guid GameId, Dictionary<string, string> Written)
{
    public static bool IsValid(ObsidianExportRecord? value, Guid id) => value is { SchemaVersion: 1, Written: not null } && value.GameId == id &&
        value.Written.All(p => p.Key.Length is > 0 and <= 80 && p.Value is { Length: 64 });
}
