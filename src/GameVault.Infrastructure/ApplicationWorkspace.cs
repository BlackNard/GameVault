using GameVault.Core;

namespace GameVault.Infrastructure;

public sealed class ApplicationWorkspace : IDisposable
{
    private FileStream? lease;

    private ApplicationWorkspace(FileStream lease) => this.lease = lease;

    public static ApplicationWorkspace Open(ApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!Directory.Exists(paths.Root))
        {
            throw new DirectoryNotFoundException("The application folder is unavailable.");
        }

        var lease = new FileStream(Path.Combine(paths.Root, ".gamevault.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            // Keep the coordination file in place: deleting a lock pathname can
            // let another process lock a different file while a lease is alive.
            using var probe = new FileStream(
                Path.Combine(paths.Root, $".gamevault-probe-{Guid.NewGuid():N}"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            probe.WriteByte(0);
            probe.Flush(flushToDisk: true);
            return new ApplicationWorkspace(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public void Dispose() => Interlocked.Exchange(ref lease, null)?.Dispose();
}
