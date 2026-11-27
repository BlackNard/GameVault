using GameVault.Core;
using GameVault.Infrastructure.Notes;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Session notes in an Obsidian vault (ticket #80), on synthetic folders only.</summary>
public sealed class ObsidianExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault obsidian", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly ObsidianVault vault;
    private readonly Guid game = Guid.NewGuid(), session = Guid.NewGuid();

    public ObsidianExportTests()
    {
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        store = new(paths);
        vault = new(paths, store);
        store.SaveProfileAsync(new(1, game, "Alpha: Part 2", Path.Combine(root, "alpha.exe"), root, [], GameStatus.Playing, 20, false, false)).GetAwaiter().GetResult();
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private string DefaultVault => Path.Combine(paths.Root, "Obsidian Vault");

    private NoteExportEntry Session(string title = "First night", string body = "We survived.", Guid? id = null, string name = "Alpha: Part 2")
    {
        var started = new DateTimeOffset(2026, 11, 20, 21, 15, 0, TimeSpan.FromHours(2));
        var stamp = started.AddHours(1);
        return new(name, new(1, game, id ?? session, title, body, stamp, stamp), new(id ?? session, started, stamp, 3600), "Completed session");
    }

    private NoteExportEntry Review() => new("Alpha: Part 2", new(1, game, null, "Verdict", "Great.", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), null, "Game review", 9);

    private async Task<ObsidianExportResult> ExportAsync(ObsidianConflictChoice choice = ObsidianConflictChoice.Cancel, params NoteExportEntry[] entries) =>
        await vault.ExportAsync(entries, _ => Task.FromResult(choice));

    private static string[] Notes(string folder) => Directory.Exists(folder) ? [.. Directory.GetFiles(folder, "*.md", SearchOption.AllDirectories).Order()] : [];

    // ---------- the vault ----------
    [Fact]
    public async Task TheDefaultVaultIsInsideTheGameVaultFolderAndIsCreatedOnce()
    {
        Assert.Equal(ObsidianVaultState.NotLinked, (await vault.StatusAsync()).State);
        Assert.Equal(DefaultVault, vault.DefaultPath);
        var created = await vault.CreateAsync(vault.DefaultPath);
        Assert.True(Directory.Exists(Path.Combine(DefaultVault, ".obsidian")));
        var again = await vault.CreateAsync(vault.DefaultPath);
        Assert.Equal(created, again);
        var status = await vault.StatusAsync();
        Assert.Equal((ObsidianVaultState.Ready, DefaultVault), (status.State, status.Path));
        Assert.Equal("Obsidian Vault", (await store.LoadObsidianSettingsAsync()).Value!.VaultPath);   // relative: survives moving the folder
    }

    [Fact]
    public async Task AnExistingVaultIsLinkedWithoutTouchingItsFilesAndAPlainFolderIsRefused()
    {
        var mine = Path.Combine(root, "mine");
        Directory.CreateDirectory(Path.Combine(mine, ".obsidian"));
        File.WriteAllText(Path.Combine(mine, "own.md"), "mine");
        await Assert.ThrowsAsync<IOException>(() => vault.UseExistingAsync(Path.Combine(root, "app-not-a-vault")));
        await vault.UseExistingAsync(mine);
        await ExportAsync(entries: Session());
        Assert.Equal("mine", File.ReadAllText(Path.Combine(mine, "own.md")));
        Assert.Equal(mine, (await vault.StatusAsync()).Path);
        await vault.UnlinkAsync();
        Assert.Equal(ObsidianVaultState.NotLinked, (await vault.StatusAsync()).State);
        Assert.True(File.Exists(Path.Combine(mine, "own.md")));
    }

    [Fact]
    public async Task UnsafeVaultPlacesAreRefused()
    {
        Directory.CreateDirectory(paths.Profiles);
        await Assert.ThrowsAsync<IOException>(() => vault.CreateAsync(paths.Root));
        await Assert.ThrowsAsync<IOException>(() => vault.CreateAsync(Path.Combine(paths.Profiles, "vault")));
        await Assert.ThrowsAsync<IOException>(() => vault.CreateAsync(Path.Combine(paths.Data, "vault")));
        await Assert.ThrowsAsync<IOException>(() => vault.CreateAsync(Path.GetPathRoot(root)!));
        await Assert.ThrowsAsync<IOException>(() => vault.CreateAsync("relative"));
    }

    [Fact]
    public async Task AMissingVaultIsReportedAndExportWritesNothing()
    {
        await vault.CreateAsync(vault.DefaultPath);
        Directory.Delete(DefaultVault, true);
        Assert.Equal(ObsidianVaultState.Missing, (await vault.StatusAsync()).State);
        await Assert.ThrowsAsync<IOException>(() => ExportAsync(entries: Session()));
        Assert.False(Directory.Exists(DefaultVault));
    }

    // ---------- notes ----------
    [Fact]
    public async Task ASessionNoteBecomesOneReadableNoteInItsGameFolder()
    {
        await vault.CreateAsync(vault.DefaultPath);
        var result = await ExportAsync(entries: [Session(), Review()]);
        Assert.Equal((2, 0, 0), (result.Created, result.Updated, result.Unchanged));
        var folder = Path.Combine(DefaultVault, "Alpha Part 2");
        Assert.Equal(["First night.md", "Review.md"], Notes(folder).Select(Path.GetFileName));   // no date in the name: it is in the properties
        var text = File.ReadAllText(Notes(folder)[0]);
        Assert.StartsWith("---\n", text);
        Assert.Contains($"gamevault-game: {game}", text);
        Assert.Contains($"gamevault-note: {session}", text);
        Assert.Contains("game: \"Alpha: Part 2\"", text);
        Assert.Contains("started: 2026-11-20T21:15:00+02:00", text);
        Assert.Contains("duration-minutes: 60", text);
        Assert.Contains("We survived.", text);
        Assert.Contains("rating: 4.5", File.ReadAllText(Notes(folder)[1]));
    }

    [Fact]
    public async Task ExportingAgainWritesNothingAndAChangedNoteUpdatesTheSameFileEvenAfterRenames()
    {
        await vault.CreateAsync(vault.DefaultPath);
        await ExportAsync(entries: Session());
        var folder = Path.Combine(DefaultVault, "Alpha Part 2");
        var file = Notes(folder).Single();
        var written = File.GetLastWriteTimeUtc(file);
        var repeat = await ExportAsync(entries: Session());
        Assert.Equal((0, 0, 1), (repeat.Created, repeat.Updated, repeat.Unchanged));
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));

        // The player renames the game folder and moves the note inside it; the game is renamed in GameVault.
        var moved = Path.Combine(DefaultVault, "My Alpha");
        Directory.Move(folder, moved);
        Directory.CreateDirectory(Path.Combine(moved, "Nights"));
        var elsewhere = Path.Combine(moved, "Nights", "night one.md");
        File.Move(Notes(moved).Single(), elsewhere);
        var changed = await ExportAsync(entries: Session(body: "We barely survived.", name: "Alpha Remastered"));
        Assert.Equal((0, 1, 0), (changed.Created, changed.Updated, changed.Unchanged));
        Assert.Equal([elsewhere], Notes(DefaultVault));
        Assert.Contains("barely", File.ReadAllText(elsewhere));
    }

    [Theory]
    [InlineData(ObsidianConflictChoice.KeepTheirs)]
    [InlineData(ObsidianConflictChoice.Cancel)]
    public async Task ANoteEditedInObsidianIsNeverOverwrittenUnasked(ObsidianConflictChoice choice)
    {
        await vault.CreateAsync(vault.DefaultPath);
        await ExportAsync(entries: Session());
        var file = Notes(DefaultVault).Single();
        var edited = File.ReadAllText(file) + "\nMy own line.\n";
        File.WriteAllText(file, edited);
        var other = Guid.NewGuid();
        var later = Session("Second", "More.", other);
        later = later with { Session = later.Session! with { StartedAt = later.Session!.StartedAt.AddDays(1) } };   // after the edited one
        var result = await ExportAsync(choice, Session(body: "Changed in GameVault."), later);
        Assert.Equal(edited, File.ReadAllText(file));
        Assert.Equal(choice == ObsidianConflictChoice.Cancel, result.Cancelled);
        Assert.Equal(choice == ObsidianConflictChoice.Cancel ? 1 : 2, Notes(DefaultVault).Length);   // Cancel stops; Keep carries on
    }

    [Fact]
    public async Task ReplacingKeepsACopyOfTheObsidianVersion()
    {
        await vault.CreateAsync(vault.DefaultPath);
        await ExportAsync(entries: Session());
        var file = Notes(DefaultVault).Single();
        File.WriteAllText(file, File.ReadAllText(file) + "\nMy own line.\n");
        ObsidianConflict? asked = null;
        var result = await vault.ExportAsync([Session()], conflict => { asked = conflict; return Task.FromResult(ObsidianConflictChoice.Replace); });
        Assert.Equal(1, result.Updated);
        Assert.Equal(file, asked!.File);
        Assert.DoesNotContain("My own line", File.ReadAllText(file));
        var copy = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.bak"));
        Assert.Contains("My own line", File.ReadAllText(copy));
        Assert.Equal(1, (await ExportAsync(entries: Session())).Unchanged);
    }

    [Fact]
    public async Task WindowsLineEndingsAloneAreNotAnEdit()
    {
        await vault.CreateAsync(vault.DefaultPath);
        await ExportAsync(entries: Session(body: "one\ntwo"));
        var file = Notes(DefaultVault).Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("\n", "\r\n"));
        Assert.Equal(1, (await ExportAsync(entries: Session(body: "one\ntwo"))).Unchanged);
    }

    [Fact]
    public async Task SameNamesGetNumbersAndNothingOfThePlayerIsTaken()
    {
        await vault.CreateAsync(vault.DefaultPath);
        Directory.CreateDirectory(Path.Combine(DefaultVault, "Alpha Part 2"));       // the player's own folder
        var twin = Guid.NewGuid();
        await ExportAsync(entries: [Session(), Session(id: twin)]);                  // two sessions, same minute and title
        var folder = Path.Combine(DefaultVault, "Alpha Part 2 (2)");
        Assert.Equal(2, Notes(folder).Length);
        Assert.Empty(Notes(Path.Combine(DefaultVault, "Alpha Part 2")));
    }

    [Fact]
    public async Task HostileTitlesStayInsideTheVault()
    {
        await vault.CreateAsync(vault.DefaultPath);
        await ExportAsync(entries: Session("..\\..\\evil: <x>|[y]#", name: "..\\..\\CON"));
        var file = Assert.Single(Notes(DefaultVault));
        Assert.StartsWith(DefaultVault + Path.DirectorySeparatorChar, file);
        Assert.Empty(Directory.GetFiles(root, "*.md", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ObsidianIsOfferedOnlyForAVaultItAlreadyKnows()
    {
        var config = Path.Combine(root, "obsidian.json");
        Assert.Null(ObsidianApp.KnownVaultId(DefaultVault, config));
        File.WriteAllText(config, "{\"vaults\":{\"abc123\":{\"path\":" + System.Text.Json.JsonSerializer.Serialize(DefaultVault.ToUpperInvariant()) + ",\"ts\":1}}}");
        Assert.Equal("abc123", ObsidianApp.KnownVaultId(DefaultVault, config));
        Assert.Null(ObsidianApp.KnownVaultId(Path.Combine(root, "other"), config));
        File.WriteAllText(config, "not json");
        Assert.Null(ObsidianApp.KnownVaultId(DefaultVault, config));
        Assert.Equal("obsidian://open?vault=abc%20123", ObsidianApp.OpenUri("abc 123"));
    }

    // ---------- keeping the data ----------
    [Fact]
    public async Task TheDataBackupTakesTheVaultAlongAndRestoresIt()
    {
        await vault.CreateAsync(vault.DefaultPath);
        await ExportAsync(entries: Session());
        var backups = Path.Combine(root, "backups");
        Directory.CreateDirectory(backups);
        await Assert.ThrowsAsync<IOException>(() => DataArchive.CreateAsync(paths, DefaultVault, false, "test"));
        var zip = await DataArchive.CreateAsync(paths, backups, false, "test");
        var file = Notes(DefaultVault).Single();
        File.WriteAllText(file, "changed later");
        var restored = await DataArchive.RestoreAsync(paths, zip);
        Assert.Contains("We survived.", File.ReadAllText(file));
        Assert.Equal("changed later", File.ReadAllText(Path.Combine(restored.ReplacedFolder, Path.GetRelativePath(paths.Root, file))));
    }
}
