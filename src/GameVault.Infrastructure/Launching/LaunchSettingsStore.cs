using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Launching;

public sealed class LaunchSettingsStore
{
    private readonly AppDataStore store;
    public LaunchSettingsStore(ApplicationPaths paths, AppDataStore store)
    { ArgumentNullException.ThrowIfNull(paths); this.store = store; }
    public Task<StoredDocument<LaunchSettings>> LoadAsync(Guid id) => store.LoadLaunchSettingsAsync(id);
    public Task SaveAsync(LaunchSettings settings)
    {
        if (settings.TargetExecutablePath is string path && !File.Exists(path))
            throw new ArgumentException("The actual game executable was not found. Browse to an existing .exe file.");
        foreach (var action in settings.PlayActions ?? [])
        {
            if (!LaunchRules.IsValidAction(action)) throw new ArgumentException($"The play action \"{action.Name}\" is not valid. Use a full .exe path, a name up to {LaunchRules.MaxNameLength} characters and plain-text arguments.");
            if (!File.Exists(action.ExecutablePath)) throw new ArgumentException($"The program for \"{action.Name}\" was not found. Browse to an existing .exe file.");
            if (action.WorkingDirectory.Length > 0 && !Directory.Exists(action.WorkingDirectory)) throw new ArgumentException($"The working folder for \"{action.Name}\" does not exist.");
        }
        foreach (var (hook, label) in new[] { (settings.BeforeLaunch, "Before launch"), (settings.AfterExit, "After exit") })
        {
            if (hook is null) continue;
            if (!LaunchRules.IsValidHook(hook)) throw new ArgumentException($"{label}: use a full .exe path, plain-text arguments and a timeout of 1 to {LaunchRules.MaxTimeoutSeconds} seconds.");
            if (!File.Exists(hook.ExecutablePath)) throw new ArgumentException($"{label}: the program was not found.");
        }
        if (!LaunchRules.IsValid(settings, settings.GameId)) throw new ArgumentException("The launch settings are not valid. Check action names (unique, up to 40 characters), the default action and the limit of " + LaunchRules.MaxActions + " actions.");
        return store.SaveLaunchSettingsAsync(settings);
    }
}
