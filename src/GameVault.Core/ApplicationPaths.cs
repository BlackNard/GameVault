namespace GameVault.Core;

public sealed record ApplicationPaths
{
    private ApplicationPaths(string root) => Root = root;

    public string Root { get; }
    public string Data => Path.Combine(Root, "Data");
    public string Profiles => Path.Combine(Root, "Profiles");

    public static ApplicationPaths FromRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("The application folder must be an absolute path.", nameof(root));
        }

        return new ApplicationPaths(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));
    }
}
