using System.IO;
using GameVault.InstallerFixture;
namespace GameVault.App.Tests;

public sealed class InstallerLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault installer " + Guid.NewGuid().ToString("N"));
    private readonly Guid token = Guid.NewGuid();
    private string App => Path.Combine(root, "App with spaces");
    public InstallerLibraryTests() { Directory.CreateDirectory(App); File.WriteAllText(Path.Combine(App, ".installer-fixture-token"), token.ToString("N")); }
    [Fact] public async Task Seed_and_reopen_verify_real_profile_session_note_and_pin_documents_without_changes()
    {
        var seeded = await InstallerLibrary.SeedAsync(App, token);
        var before = InstallerLibrary.Fingerprint(App);
        var reopened = await InstallerLibrary.InspectAsync(App, token);
        Assert.Equal(2, seeded.Games); Assert.Equal(2, reopened.Sessions); Assert.Equal(7200, reopened.Seconds);
        Assert.Equal(2, reopened.Pinned); Assert.Equal(2, reopened.Unpinned); Assert.Equal(2, reopened.Notes);
        Assert.Equal(before, InstallerLibrary.Fingerprint(App));
    }
    [Fact] public async Task Seed_refuses_existing_data_and_preserves_it()
    {
        await InstallerLibrary.SeedAsync(App, token); var before = InstallerLibrary.Fingerprint(App);
        await Assert.ThrowsAsync<IOException>(() => InstallerLibrary.SeedAsync(App, token));
        Assert.Equal(before, InstallerLibrary.Fingerprint(App));
    }
    [Fact] public async Task Missing_or_wrong_token_refuses_reads_and_writes()
    {
        await Assert.ThrowsAsync<IOException>(() => InstallerLibrary.SeedAsync(App, Guid.NewGuid()));
        File.Delete(Path.Combine(App, ".installer-fixture-token"));
        await Assert.ThrowsAsync<IOException>(() => InstallerLibrary.InspectAsync(App, token));
        Assert.False(Directory.Exists(Path.Combine(App,"Data")));
    }
    [Fact] public async Task Damaged_payload_is_not_reported_as_a_valid_backup()
    {
        await InstallerLibrary.SeedAsync(App, token);
        var payload = Directory.GetFiles(Path.Combine(App,"Profiles"),"slot.sav",SearchOption.AllDirectories).First();
        File.WriteAllText(payload,"damaged");
        await Assert.ThrowsAsync<IOException>(() => InstallerLibrary.InspectAsync(App, token));
    }
    public void Dispose() { if(Directory.Exists(root)) Directory.Delete(root,true); }
}
