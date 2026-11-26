using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string CompletionFile(Guid id) => Path.Combine(Folder(id), "completion.json");
    private static bool ValidCompletion(GameCompletion value, Guid id) => value is { SchemaVersion: 1 } && value.GameId == id;

    /// <summary>When the game became Completed. A missing file means it is not completed, or it was before dates were recorded.</summary>
    public Task<StoredDocument<GameCompletion>> LoadCompletionAsync(Guid id) => Run(() =>
        documents.Read<GameCompletion>(CompletionFile(id), value => ValidCompletion(value, id)));

    /// <summary>Notes the date when a game becomes Completed and forgets it when that is undone. It never stops the status change itself.</summary>
    private void TrackCompletion(Guid id, GameStatus before, GameStatus after)
    {
        if ((before == GameStatus.Completed) == (after == GameStatus.Completed)) return;
        try
        {
            var file = CompletionFile(id);
            if (after == GameStatus.Completed) documents.Write(file, new GameCompletion(1, id, DateTimeOffset.UtcNow), value => ValidCompletion(value, id));
            else documents.Delete<GameCompletion>(file, value => ValidCompletion(value, id));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
