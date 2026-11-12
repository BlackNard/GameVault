using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameVault.Infrastructure.Notes;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
namespace GameVault.App.Notes;

// WPF lays out and shapes text (including fallback fonts and bidi) without a window or printer.
// Pages are embedded as 200-DPI images: visual fidelity rather than searchable PDF text.
internal static class PdfNoteWriter
{
    public static void Write(Stream output, IReadOnlyList<NoteExportEntry> entries)
    {
        if (entries.Count == 0) throw new ArgumentException("There are no saved notes to export.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Render(output, entries); }
            catch (Exception e) { failure = e; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true, Name = "GameVault document export" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new IOException("The PDF could not be generated. " + failure.Message, failure);
    }

    private static void Render(Stream output, IReadOnlyList<NoteExportEntry> entries)
    {
        const double width = 793.7, height = 1122.5, dpi = 200;
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, Global User Interface"), FontSize = 14,
            Foreground = Brushes.Black, Background = Brushes.White, PageWidth = width, PageHeight = height,
            PagePadding = new Thickness(56, 48, 56, 64), ColumnWidth = double.PositiveInfinity
        };
        foreach (var entry in NoteExports.Sort(entries))
        {
            document.Blocks.Add(Paragraph(entry.GameName, 24, true, 0, 10));
            if (!string.IsNullOrWhiteSpace(entry.Note.Title)) document.Blocks.Add(Paragraph(entry.Note.Title, 19, true, 0, 10));
            document.Blocks.Add(Paragraph(NoteExports.Metadata(entry), 11, false, 0, 18));
            document.Blocks.Add(Paragraph(entry.Note.Body, 14, false, 0, 28));
        }
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = new Size(width, height); paginator.ComputePageCount();
        if (paginator.PageCount > 2000) throw new IOException("This export exceeds 2,000 pages. Export smaller groups of notes.");
        using var pdf = new PdfDocument(); pdf.Info.Title = "GameVault notes"; pdf.Info.Author = "GameVault";
        for (var index = 0; index < paginator.PageCount; index++)
        {
            using var page = paginator.GetPage(index);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var rectangle = new Rect(0, 0, width, height);
                drawing.DrawRectangle(Brushes.White, null, rectangle);
                var brush = new VisualBrush(page.Visual) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = rectangle, Stretch = Stretch.Fill };
                drawing.DrawRectangle(brush, null, rectangle);
                var footer = new FormattedText($"GameVault    |    {index + 1} / {paginator.PageCount}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.DimGray, dpi / 96);
                drawing.DrawText(footer, new Point(56, height - 40));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream(); encoder.Save(png); png.Position = 0;
            using var image = XImage.FromStream(png);
            var pdfPage = pdf.AddPage(); pdfPage.Width = XUnit.FromPoint(width * 72 / 96); pdfPage.Height = XUnit.FromPoint(height * 72 / 96);
            using var graphics = XGraphics.FromPdfPage(pdfPage); graphics.DrawImage(image, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
        }
        pdf.Save(output, false);
    }
    private static Paragraph Paragraph(string text, double size, bool heading, double before, double after) => new(new Run(text))
    {
        FontSize = size, FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
        TextAlignment = TextAlignment.Left, Margin = new Thickness(0, before, 0, after), KeepWithNext = heading, LineHeight = size * 1.5
    };
}