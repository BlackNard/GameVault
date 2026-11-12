using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Notes;
namespace GameVault.Tests;

public sealed class NoteExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault exports", Guid.NewGuid().ToString("N"));
    private ApplicationPaths Source => ApplicationPaths.FromRoot(Path.Combine(root, "app"));
    private static NoteExportEntry Entry(string name = "Test / game", Guid? id = null) => new(name,
        new(1, Guid.NewGuid(), id ?? Guid.NewGuid(), "# My title", "مرحبا\n\nCafé 🎮\n<script>text</script>", DateTimeOffset.Parse("2026-11-12T10:00:00Z"), DateTimeOffset.Parse("2026-11-12T11:00:00Z")),
        new(id ?? Guid.NewGuid(), DateTimeOffset.Parse("2026-11-12T08:00:00Z"), DateTimeOffset.Parse("2026-11-12T09:00:00Z"), 3029), "Interrupted · recovered");
    private static void Writer(Stream stream, IReadOnlyList<NoteExportEntry> entries) { var bytes = Encoding.UTF8.GetBytes(NoteExports.Markdown(entries)); stream.Write(bytes); }
    [Fact] public void MarkdownIncludesUsefulContextAndEscapesUserMarkupWithoutPrivatePaths()
    {
        var text = NoteExports.Markdown([Entry()]);
        Assert.Contains("3029", text); Assert.Contains("2026-11-12", text); Assert.Contains("مرحبا", text);
        Assert.Contains("Café 🎮", text); Assert.Contains("Interrupted", text); Assert.DoesNotContain("<script>", text);
        Assert.DoesNotContain("Executable", text); Assert.Contains("Created", text); Assert.Contains("Updated", text);
        Assert.Throws<ArgumentException>(() => NoteExports.Markdown([]));
    }
    [Fact] public async Task SeparateExportUsesSafeUniqueNamesAndNeverOverwrites()
    {
        var entry = Entry("../../CON : Test"); var target = Path.Combine(root, "out");
        var first = await NoteExports.WriteAsync(Source, [entry], target, ".md", true, false, Writer);
        var second = await NoteExports.WriteAsync(Source, [entry], target, ".md", true, false, Writer);
        Assert.Empty(first.Failures); Assert.Empty(second.Failures);
        Assert.NotEqual(first.Paths[0], second.Paths[0]);
        Assert.All(first.Paths.Concat(second.Paths), file => Assert.Equal(target, Path.GetDirectoryName(file)));
        Assert.Equal(File.ReadAllText(first.Paths[0]), File.ReadAllText(second.Paths[0]));
    }
    [Fact] public async Task FailedSingleExportPreservesExistingFileAndCleansTemporaryOutput()
    {
        Directory.CreateDirectory(root); var target = Path.Combine(root, "notes.md"); File.WriteAllText(target, "Keep");
        var result = await NoteExports.WriteAsync(Source, [Entry()], target, ".md", false, true,
            (stream, entries) => { stream.WriteByte(1); throw new IOException("fixture"); });
        Assert.Single(result.Failures); Assert.Empty(result.Paths); Assert.Equal("Keep", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(root));
        var noOverwrite = await NoteExports.WriteAsync(Source, [Entry()], target, ".md", false, false, Writer);
        Assert.Single(noOverwrite.Failures); Assert.Equal("Keep", File.ReadAllText(target));
    }
    [Fact] public async Task PartialFailuresAreReportedAndSourceDirectoriesAreRefused()
    {
        var target = Path.Combine(root, "out"); var calls = 0;
        var result = await NoteExports.WriteAsync(Source, [Entry(), Entry()], target, ".md", true, false,
            (stream, entries) => { if (++calls == 2) throw new IOException("fixture"); Writer(stream, entries); });
        Assert.Single(result.Paths); Assert.Single(result.Failures);
        await Assert.ThrowsAsync<ArgumentException>(() => NoteExports.WriteAsync(Source, [Entry()], Path.Combine(Source.Profiles, "note.md"), ".md", false, false, Writer));
        await Assert.ThrowsAsync<ArgumentException>(() => NoteExports.WriteAsync(Source, [], target, ".md", true, false, Writer));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}