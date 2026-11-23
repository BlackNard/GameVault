namespace GameVault.Core;

/// <summary>What a player can choose on the first-run welcome.</summary>
public enum WelcomeAction { AddGame, ImportFromStores, ChooseTheme, DownloadSaveDatabase, Skip }

/// <summary>When the first-run welcome replaces the library's empty message.</summary>
public static class WelcomeRules
{
    /// <summary>
    /// Shown only for a library that has never had a game (nothing stored, not even removed profiles), outside the
    /// "removed profiles" view, until the player skips it. A search or filter that matches nothing keeps the normal message.
    /// </summary>
    public static bool ShouldShow(int storedGames, bool showingRemovedProfiles, bool dismissed) =>
        storedGames == 0 && !showingRemovedProfiles && !dismissed;
}
