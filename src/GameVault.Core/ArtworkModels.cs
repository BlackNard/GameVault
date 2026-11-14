namespace GameVault.Core;

public sealed record ArtworkGame(int Id, string Name, string Context)
{
    public string Display => string.IsNullOrEmpty(Context) ? Name : $"{Name} — {Context}";
}
public sealed record ArtworkCover(int Id, Uri Image, Uri Thumbnail, string Credit)
{
    public string Page => $"https://www.steamgriddb.com/grid/{Id}";
}
public interface IArtworkCredentialStore
{
    string? Read();
    void Write(string key);
    void Delete();
}
public interface IArtworkProvider
{
    Task<IReadOnlyList<ArtworkGame>> SearchAsync(string title, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArtworkCover>> GetCoversAsync(int gameId, CancellationToken cancellationToken);
    Task<byte[]> DownloadAsync(Uri image, CancellationToken cancellationToken);
}
public enum ArtworkFailure { Query, Authentication, RateLimit, Offline, Timeout, InvalidResponse, TooLarge }
public sealed class ArtworkException(ArtworkFailure failure, string message) : Exception(message)
{
    public ArtworkFailure Failure { get; } = failure;
}
