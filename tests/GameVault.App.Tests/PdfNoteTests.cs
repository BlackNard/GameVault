using System.IO;
using GameVault.App.Notes;
using GameVault.Core;
using GameVault.Infrastructure.Notes;
using PdfSharp.Pdf.IO;
namespace GameVault.App.Tests;
public sealed class PdfNoteTests
{
    [Fact] public void LongUnicodeNotesGenerateMultipleFullPagesWithoutAWindowOrPrinter()
    {
        var stamp = DateTimeOffset.Parse("2026-11-12T10:00:00Z");
        var text = "مرحبا بالعالم — Café 日本語 🎮\n\n" + string.Join("\n\n", Enumerable.Range(1, 65).Select(i => $"Paragraph {i}: The police station was quiet. " + new string('W', 130))) + "\n\nEND OF NOTE";
        var entry = new NoteExportEntry("Resident Evil 2", new(1, Guid.NewGuid(), Guid.NewGuid(), "A long session journal", text, stamp, stamp), new(Guid.NewGuid(), stamp.AddHours(-1), stamp, 3029), "Interrupted · recovered");
        using var output = new MemoryStream(); PdfNoteWriter.Write(output, [entry]);
        output.Position = 0; using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.InRange(pdf.PageCount, 4, 30);
        foreach (var page in pdf.Pages) { Assert.InRange(page.Width.Point, 594, 596); Assert.InRange(page.Height.Point, 841, 843); }
        var directory = Environment.GetEnvironmentVariable("GAMEVAULT_PDF_QA");
        if (directory is not null) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "synthetic-notes.pdf"), output.ToArray()); }
    }
}