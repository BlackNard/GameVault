using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

internal sealed class ManagedPaths(ApplicationPaths paths)
{
    public void Check(string target)
    {
        var relative = Path.GetRelativePath(paths.Root, Path.GetFullPath(target));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new IOException("Storage must remain inside the application folder.");

        var current = paths.Root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked files or folders are not supported inside managed app data.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public void EnsureDirectory(string directory)
    {
        if (!Directory.Exists(paths.Root)) throw new DirectoryNotFoundException("The application folder is unavailable.");
        Check(directory);
        Directory.CreateDirectory(directory);
        Check(directory);
    }
}
