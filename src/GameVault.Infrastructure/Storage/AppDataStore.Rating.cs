using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string RatingFile(Guid id) => Path.Combine(Folder(id), "rating.json");
    private static bool ValidRating(GameRating value, Guid id) => value.SchemaVersion == 1 && value.GameId == id && StarRating.IsValid(value.HalfStars);

    /// <summary>The player's rating of a game. A missing file means not rated; it is its own small document, so older versions ignore it.</summary>
    public Task<StoredDocument<GameRating>> LoadRatingAsync(Guid id) => Run(() =>
        documents.Read<GameRating>(RatingFile(id), value => ValidRating(value, id)));

    /// <summary>Saves or clears (0) the rating. A rating can be given at any time, whatever the game's status.</summary>
    public Task SaveRatingAsync(Guid id, int halfStars) => Run(() =>
    {
        if (!StarRating.IsValid(halfStars)) throw new ArgumentOutOfRangeException(nameof(halfStars));
        var game = LoadProfile(id);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value!.IsArchived)
            throw new IOException("A writable game in your library is required before rating it.");
        var file = RatingFile(id);
        JsonDocuments.RequireWritable(documents.Read<GameRating>(file, value => ValidRating(value, id)));
        documents.Write(file, new GameRating(1, id, halfStars), value => ValidRating(value, id));
    });
}
