using System.Windows;
using GameVault.Infrastructure.Notes;

namespace GameVault.App;

/// <summary>The questions an Obsidian export may ask, the same wherever notes are exported from.</summary>
internal static class ObsidianPrompts
{
    /// <summary>Makes sure a vault is there: a first one is created after the player agrees to the place; a missing one is located or replaced.</summary>
    public static async Task<bool> ReadyVaultAsync(FrameworkElement owner, ObsidianVault vault)
    {
        var status = await vault.StatusAsync();
        if (status.State == ObsidianVaultState.Ready) return true;
        if (status.State == ObsidianVaultState.NotLinked)
        {
            if (await InlinePrompts.ShowAsync(owner, "No Obsidian vault is linked yet. Create one here?\n\n" + vault.DefaultPath, "Create vault", MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            await vault.CreateAsync(vault.DefaultPath);
            return true;
        }
        var answer = await InlinePrompts.ShowAsync(owner, $"{status.Message}\n\nYes: choose where the vault is now.\nNo: create a new one at {vault.DefaultPath}\nCancel: do nothing.",
            "Vault not found", MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.No) { await vault.CreateAsync(vault.DefaultPath); return true; }
        if (answer != MessageBoxResult.Yes || NativePickers.Folder(Window.GetWindow(owner)!, "Choose your Obsidian vault") is not { } folder) return false;
        await vault.UseExistingAsync(folder);
        return true;
    }

    public static async Task<ObsidianConflictChoice> ConflictAsync(FrameworkElement owner, ObsidianConflict conflict) =>
        await InlinePrompts.ShowAsync(owner, $"“{conflict.Title}” for {conflict.GameName} was changed in Obsidian.\n\nYes: replace it (a copy of the Obsidian version is kept).\nNo: keep the Obsidian version.\nCancel: stop exporting.",
            "Note changed in Obsidian", MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel) switch
        { MessageBoxResult.Yes => ObsidianConflictChoice.Replace, MessageBoxResult.No => ObsidianConflictChoice.KeepTheirs, _ => ObsidianConflictChoice.Cancel };

    /// <summary>What happened, in one message.</summary>
    public static async Task<string> SummaryAsync(ObsidianVault vault, ObsidianExportResult result)
    {
        var path = (await vault.StatusAsync()).Path!;
        var where = ObsidianApp.KnownVaultId(path, ObsidianApp.DefaultConfig) is null ? ObsidianApp.OpenHint(path) : path;
        return (result.Cancelled ? "Stopped. " : "") + $"{result.Created} new, {result.Updated} updated, {result.Unchanged} already exported, {result.Kept} kept as in Obsidian.\n" +
            where + (result.Failures.Length == 0 ? "" : "\nFailures:\n" + string.Join("\n", result.Failures));
    }
}
