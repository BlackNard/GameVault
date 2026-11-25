using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Notes;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class StarRatingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault rating", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService library;
    private readonly string exe;

    public StarRatingTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        paths = ApplicationPaths.FromRoot(root);
        store = new(paths);
        library = new(paths, store);
        exe = Path.Combine(root, "game", "game.exe");
        File.WriteAllText(exe, "fixture only; never executed");
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task<Guid> AddAsync() => (await library.SaveAsync(new(null, "Rated", exe, "", []))).Profile.Id;
    private async Task<int> RatingAsync() => Assert.Single((await library.ListAsync()).Games).Rating;

    [Fact]
    public async Task ARatingIsSavedChangedAndClearedWhateverTheStatus()
    {
        var id = await AddAsync();                       // not started
        Assert.Equal(0, await RatingAsync());
        await store.SaveRatingAsync(id, 7);
        Assert.Equal(7, await RatingAsync());
        await store.SaveRatingAsync(id, 10);
        Assert.Equal(10, await RatingAsync());
        await store.SaveRatingAsync(id, 0);
        Assert.Equal(0, await RatingAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveRatingAsync(id, 11));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveRatingAsync(id, -1));
    }

    [Fact]
    public async Task ANewerRatingFileIsLeftAloneAndTheRestOfTheGameStillWorks()
    {
        var id = await AddAsync();
        var file = Path.Combine(ProfileFolders.Resolve(paths, id), "rating.json");
        var newer = "{\"schemaVersion\":9,\"gameId\":\"" + id + "\",\"halfStars\":4}";
        File.WriteAllText(file, newer);
        Assert.Equal(0, await RatingAsync());
        await Assert.ThrowsAnyAsync<IOException>(() => store.SaveRatingAsync(id, 2));
        Assert.Equal(newer, File.ReadAllText(file));
    }

    [Fact]
    public async Task ARemovedGameCannotBeRated()
    {
        var id = await AddAsync();
        await library.SetArchivedAsync(id, true);
        await Assert.ThrowsAsync<IOException>(() => store.SaveRatingAsync(id, 4));
    }

    [Fact]
    public void StarsWorkInHalves()
    {
        Assert.Equal(1, StarRating.FromPosition(1, 150));
        Assert.Equal(2, StarRating.FromPosition(30, 150));
        Assert.Equal(7, StarRating.FromPosition(100, 150));
        Assert.Equal(10, StarRating.FromPosition(500, 150));
        Assert.Equal(0, StarRating.Toggle(7, 7));
        Assert.Equal(8, StarRating.Toggle(7, 8));
        Assert.Equal([1.0, 1.0, 0.5, 0.0, 0.0], Enumerable.Range(0, 5).Select(i => StarRating.Fill(5, i)));
        Assert.Equal("★★★½☆", StarRating.Text(7));
        Assert.Equal("", StarRating.Text(0));
        Assert.Equal("3.5 of 5 stars", StarRating.Describe(7));
        Assert.Equal("Not rated", StarRating.Describe(0));
    }

    [Fact]
    public void TheLibrarySortsAndFiltersByRating()
    {
        var games = new[] { (Name: "b", Rating: 4), (Name: "a", Rating: 0), (Name: "c", Rating: 10) };
        var sorted = LibrarySortOrder.Apply(games, LibrarySortOrder.Rating, g => g.Name, _ => null, _ => null, g => g.Rating);
        Assert.Equal(["c", "b", "a"], sorted.Select(g => g.Name));
        Assert.Equal("Highest rated", LibrarySortOrder.Label(LibrarySortOrder.Normalize("rating")));
        Assert.True(RatingFilters.Matches(RatingFilters.Any, 0));
        Assert.False(RatingFilters.Matches(RatingFilters.AtLeast4, 7));
        Assert.True(RatingFilters.Matches(RatingFilters.AtLeast4, 8));
        Assert.True(RatingFilters.Matches(RatingFilters.Unrated, 0));
        Assert.False(RatingFilters.Matches(RatingFilters.Rated, 0));
    }

    [Fact]
    public void AReviewExportShowsTheRatingAndSessionNotesDoNot()
    {
        var game = Guid.NewGuid();
        var stamp = DateTimeOffset.UnixEpoch;
        var review = new NoteExportEntry("Game", new(1, game, null, "t", "b", stamp, stamp), null, "Game review", 9);
        Assert.Contains("Rating: 4.5 of 5 stars", NoteExports.Metadata(review));
        var session = review with { Note = review.Note with { SessionId = Guid.NewGuid() } };
        Assert.DoesNotContain("Rating:", NoteExports.Metadata(session));
    }
}
