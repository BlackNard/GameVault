using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class ProgressiveLookupTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-progress-" + Guid.NewGuid().ToString("N"));
    private ApplicationPaths paths = null!;
    private AppDataStore store = null!;
    private GameProfile profile = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        paths = ApplicationPaths.FromRoot(root);
        store = new(paths);
        await store.InitializeAsync();
        var exe = Path.Combine(root, "game", "game.exe");
        File.WriteAllBytes(exe, new byte[100]);
        profile = (await new LibraryService(paths, store).SaveAsync(new(null, "Signalis", exe, Path.Combine(root, "game"), []))).Profile;
    }

    public Task DisposeAsync() { Directory.Delete(root, true); return Task.CompletedTask; }

    private sealed class Fake(string id, ProviderData? data, List<string> log, params MetadataField[] fields) : IGameMetadataProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public IReadOnlySet<MetadataField> AvailableFields { get; } = fields.ToHashSet();
        public Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken) { log.Add("ask " + id); return Task.FromResult(data); }
    }

    private sealed class NoStore : IStoreGameLauncher
    {
        public StoreLaunchPlan? Resolve(string selectedExecutable, string? targetOverride) => null;
        public void RequestLaunch(StoreLaunchPlan plan) { }
    }

    private MetadataRequest Request() => new(profile.Id, "Signalis", profile.ExecutablePath, null, null, null, null, true, false);

    [Fact]
    public async Task What_the_first_source_found_is_reported_before_waiting_for_the_next_source()
    {
        var log = new List<string>();
        var first = new Fake("store", new ProviderData(Description: "A story", Genres: ["Horror"]), log, MetadataField.Description, MetadataField.Genres);
        var slow = new Fake("slow", new ProviderData(InstallSizeBytes: 123), log, MetadataField.InstallSize);
        var result = await MetadataResolver.ResolveAsync(Request(), [first, slow], MetadataSettings.Default, GameMetadataDocument.Empty(profile.Id), CancellationToken.None, null, partial =>
        {
            log.Add($"partial description={partial.Get(MetadataField.Description)?.Text} size={partial.Get(MetadataField.InstallSize)?.Bytes}");
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { "ask store", "partial description=A story size=", "ask slow" }, log);
        Assert.Equal(123, result.Document.Get(MetadataField.InstallSize)!.Bytes);                      // the final result still has everything
    }

    [Fact]
    public async Task Nothing_is_reported_when_nothing_was_found_yet_or_no_further_source_is_needed()
    {
        var empty = new List<string>();
        var nothing = new Fake("a", null, empty, MetadataField.Description);
        var next = new Fake("b", new ProviderData(Description: "x"), empty, MetadataField.Description);
        var reported = 0;
        await MetadataResolver.ResolveAsync(Request(), [nothing, next], MetadataSettings.Default, GameMetadataDocument.Empty(profile.Id), CancellationToken.None, null, _ => { reported++; return Task.CompletedTask; });
        Assert.Equal(0, reported);
        var single = new Fake("only", new ProviderData(Description: "x", Genres: ["g"]), [], MetadataField.Description, MetadataField.Genres);
        await MetadataResolver.ResolveAsync(Request(), [single], MetadataSettings.Default, GameMetadataDocument.Empty(profile.Id), CancellationToken.None, null, _ => { reported++; return Task.CompletedTask; });
        Assert.Equal(0, reported);
    }

    [Fact]
    public async Task A_failing_progress_report_never_breaks_the_lookup()
    {
        var log = new List<string>();
        var first = new Fake("store", new ProviderData(Description: "x"), log, MetadataField.Description);
        var second = new Fake("slow", new ProviderData(InstallSizeBytes: 5), log, MetadataField.InstallSize);
        var result = await MetadataResolver.ResolveAsync(Request(), [first, second], MetadataSettings.Default, GameMetadataDocument.Empty(profile.Id), CancellationToken.None, null, _ => throw new IOException("disk full"));
        Assert.Equal(5, result.Document.Get(MetadataField.InstallSize)!.Bytes);
    }

    [Fact]
    public async Task The_service_saves_each_stage_tells_the_page_and_only_the_last_stage_marks_the_lookup_complete()
    {
        var log = new List<string>();
        var first = new Fake("store", new ProviderData(Description: "A story"), log, MetadataField.Description);
        var slow = new Fake("slow", new ProviderData(InstallSizeBytes: 123), log, MetadataField.InstallSize);
        var service = new GameMetadataService(store, [first, slow], new NoStore());
        var stages = new List<(int Fields, int? Version)>();
        service.FactsUpdated += (_, _) =>
        {
            var document = store.LoadMetadataAsync(profile.Id).GetAwaiter().GetResult().Value!;
            stages.Add((document.Fields.Count, document.LookupVersion));
        };
        await service.RefreshAsync(profile.Id, false, CancellationToken.None);
        Assert.Equal(2, stages.Count);
        Assert.Equal((1, (int?)null), stages[0]);                                                      // description only, lookup not yet complete
        Assert.Equal((2, (int?)GameMetadataService.CurrentLookupVersion), stages[1]);                  // everything, complete
    }
}
