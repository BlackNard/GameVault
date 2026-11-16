namespace GameVault.Core;

public sealed record PortraitCoverCandidate(int AppId, string Title, string Context, Uri SourcePage);

public interface IPortraitCoverCatalogue
{
    Task<IReadOnlyList<PortraitCoverCandidate>> SearchAsync(string title, CancellationToken cancellationToken);
    Task<byte[]> DownloadAsync(PortraitCoverCandidate candidate, bool preview, CancellationToken cancellationToken);
}

public static class PortraitCoverPolicy
{
    public static bool IsSuitable(int width, int height) =>
        width >= 600 && height >= 900 && width <= 8192 && height <= 8192 &&
        (long)width * height <= 16_000_000 && Math.Abs((double)width / height / (2d / 3d) - 1) <= .0200000001;
}
