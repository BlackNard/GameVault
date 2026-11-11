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
        return store.SaveLaunchSettingsAsync(settings);
    }
}
