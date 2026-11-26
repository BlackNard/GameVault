using System.Text;

namespace GameVault.Infrastructure.Library;

public sealed record SteamAccount(string Id, string Name);

/// <summary>
/// Reads, never writes, what Steam keeps on this PC: each account's playtime in minutes (userdata/&lt;account&gt;/config/localconfig.vdf) and the names of
/// installed games. No sign-in, key or web request; the account's display name is only used to tell two accounts apart.
/// </summary>
public sealed class SteamLocalData(string? steamRoot)
{
    private const int MaxFileBytes = 32 * 1024 * 1024, MaxAccounts = 32;

    public IReadOnlyList<SteamAccount> Accounts()
    {
        var result = new List<SteamAccount>();
        try
        {
            if (string.IsNullOrWhiteSpace(steamRoot)) return result;
            var userdata = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdata)) return result;
            foreach (var folder in Directory.EnumerateDirectories(userdata).Take(MaxAccounts))
            {
                var id = Path.GetFileName(folder);
                if (Config(id) is not { } config) continue;
                var name = config.Child("friends")?.Text("PersonaName")?.Trim();
                result.Add(new(id, string.IsNullOrEmpty(name) ? "Steam account " + (result.Count + 1) : name));
            }
        }
        catch (Exception error) when (IsReadError(error)) { }
        return result;
    }

    /// <summary>The minutes Steam recorded for one app on one account, or null when it recorded none.</summary>
    public long? Minutes(string accountId, string appId)
    {
        var text = Config(accountId)?.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps")?.Child(appId)?.Text("Playtime");
        return long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minutes) && minutes <= 60_000_000 ? minutes : null;
    }

    /// <summary>Steam's own name for the app installed in <paramref name="installRoot"/> (steamapps/common/&lt;folder&gt;).</summary>
    public static string? InstalledName(string installRoot, string appId)
    {
        try
        {
            var steamapps = Directory.GetParent(installRoot)?.Parent?.FullName;
            return steamapps is null ? null : Read(Path.Combine(steamapps, $"appmanifest_{appId}.acf"))?.Child("AppState")?.Text("name")?.Trim();
        }
        catch (Exception error) when (IsReadError(error)) { return null; }
    }

    private Vdf? Config(string accountId)
    {
        if (string.IsNullOrWhiteSpace(steamRoot) || accountId is not { Length: > 0 and <= 12 } || !accountId.All(char.IsAsciiDigit)) return null;
        try { return Read(Path.Combine(steamRoot, "userdata", accountId, "config", "localconfig.vdf"))?.Child("UserLocalConfigStore"); }
        catch (Exception error) when (IsReadError(error)) { return null; }
    }

    private static Vdf? Read(string file)
    {
        if (!File.Exists(file)) return null;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxFileBytes) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Vdf.Parse(reader.ReadToEnd());
    }

    private static bool IsReadError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException;
}
