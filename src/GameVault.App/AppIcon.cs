namespace GameVault.App;

internal static class AppIcon
{
    internal static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("/GameVault;component/Assets/GameVault.ico", UriKind.Relative))
            ?? throw new InvalidOperationException("GameVault icon resource is missing.");
        using var stream = resource.Stream;
        using var original = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)original.Clone();
    }
}
