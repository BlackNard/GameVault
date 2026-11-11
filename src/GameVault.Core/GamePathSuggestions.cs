namespace GameVault.Core;

public static class GamePathSuggestions
{
    public static string WorkingDirectory(string previousExecutable, string currentWorkingDirectory, string selectedExecutable)
    {
        var previousParent = Parent(previousExecutable);
        return string.IsNullOrWhiteSpace(currentWorkingDirectory) ||
            string.Equals(currentWorkingDirectory.Trim().TrimEnd(Path.DirectorySeparatorChar), previousParent, StringComparison.OrdinalIgnoreCase)
            ? Parent(selectedExecutable) : currentWorkingDirectory;
    }

    private static string Parent(string path)
    {
        try { return Path.GetDirectoryName(path.Trim().Trim('"')) ?? ""; }
        catch (ArgumentException) { return ""; }
    }
}
