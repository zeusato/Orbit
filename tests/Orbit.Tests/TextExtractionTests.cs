using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App.Services;
using Orbit.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

internal static partial class Program
{
    private static void CheckTextExtraction(bool office)
    {
        var engine = new DocumentEngine();
        var textPdf = Path.Combine(_folder, "text-layer.pdf");
        var builder = new PdfDocumentBuilder(); var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Orbit text PDF. Invoice 123456789. Total 1,250,000 VND.", 14, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        File.WriteAllBytes(textPdf, builder.Build());
        var scan = WriteOcrFixture();
        var scanPdf = engine.Execute(new(scan, ToolKind.ImagePdf), CancellationToken.None);
        var mixedPdf = Path.Combine(_folder, "mixed-pages.pdf");
        using (var first = PdfReader.Open(textPdf, PdfDocumentOpenMode.Import))
        using (var second = PdfReader.Open(scanPdf, PdfDocumentOpenMode.Import))
        using (var combined = new PdfDocument()) { combined.AddPage(first.Pages[0]); combined.AddPage(second.Pages[0]); combined.Save(mixedPdf); }

        Check("PDF analysis distinguishes text, scans and mixed pages and filters Word", () =>
        {
            Assert(PdfTextReader.Analyze(textPdf).CanConvertToWord, "text PDF eligible");
            Assert(!PdfTextReader.Analyze(scanPdf).CanConvertToWord, "scan ineligible");
            var mixed = PdfTextReader.Analyze(mixedPdf);
            Assert(!mixed.CanConvertToWord && !mixed.Pages[0].NeedsOcr && mixed.Pages[1].NeedsOcr, "per-page classification");
            Assert(ToolCatalog.ForFiles([FileKind.Pdf], _ => true, true).Contains(ToolKind.PdfWord), "Word on text PDF");
            Assert(ToolCatalog.ForFiles([FileKind.Pdf], _ => false, true).Contains(ToolKind.PdfWord), "DOCX generation does not require Office");
            Assert(!FileToolResolver.Resolve([mixedPdf]).Contains(ToolKind.PdfWord), "mixed PDF hides Word");
            Assert(!FileToolResolver.Resolve([textPdf, scanPdf]).Contains(ToolKind.PdfWord), "batch intersection");
            Assert(FileToolResolver.Resolve([scanPdf]).Contains(ToolKind.PdfText), "scan offers extraction");
            Assert(FileToolResolver.Resolve([scan]).Contains(ToolKind.ImageText), "image offers OCR");
        });
        Check("Text PDF extraction preserves characters and source; collisions keep both outputs", () =>
        {
            var before = Hash(textPdf);
            var first = engine.Execute(new(textPdf, ToolKind.PdfText), CancellationToken.None);
            var second = engine.Execute(new(textPdf, ToolKind.PdfText), CancellationToken.None);
            var text = File.ReadAllText(first);
            Assert(text.Contains("Invoice 123456789") && text.Contains("1,250,000 VND") && !text.Contains("OCR"), "direct text, no recognition");
            Assert(first != second && Hash(textPdf) == before, "no overwrite");
        });
        Check("Local OCR reads Vietnamese accents and numbers from a clean printed image", () =>
        {
            var before = Hash(scan);
            var output = engine.Execute(new(scan, ToolKind.ImageText), CancellationToken.None);
            var text = File.ReadAllText(output).Normalize(NormalizationForm.FormC);
            Assert(text.Contains("Tiếng Việt") && text.Contains("Cộng hòa xã hội chủ nghĩa Việt Nam"), "Vietnamese diacritics");
            Assert(text.Contains("123456789") && text.Contains("1.250.000"), "numeric fields");
            Assert(Hash(scan) == before, "image unchanged");
        });
        Check("Mixed PDF TXT uses direct text and real OCR without dropping scan pages", () =>
        {
            var before = Hash(mixedPdf);
            var output = engine.Execute(new(mixedPdf, ToolKind.PdfText), CancellationToken.None);
            var text = File.ReadAllText(output).Normalize(NormalizationForm.FormC);
            Assert(text.Contains("Trang 1 · lớp chữ") && text.Contains("Trang 2 · OCR"), "page markers");
            Assert(text.Contains("Invoice 123456789") && text.Contains("Tiếng Việt") && text.Contains("1.250.000"), "both page contents");
            Assert(Hash(mixedPdf) == before, "PDF unchanged");
        });
        Check("Scan-to-Word and corrupt PDF are rejected without partial files", () =>
        {
            Throws<NotSupportedException>(() => engine.Execute(new(scanPdf, ToolKind.PdfWord), CancellationToken.None));
            var broken = Path.Combine(_folder, "broken-text.pdf"); File.WriteAllText(broken, "%PDF-1.7\nnot a real file");
            Throws<InvalidDataException>(() => engine.Execute(new(broken, ToolKind.PdfText), CancellationToken.None));
            Assert(!Directory.GetFiles(_folder, ".orbit-*").Any(), "temporary outputs removed");
        });
        Check("PDF text cancellation removes staging output and keeps source intact", () =>
        {
            var before = Hash(mixedPdf); var outputs = Directory.GetFiles(_folder, "mixed-pages_text*.txt").Length;
            using var cancelled = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => engine.Execute(new(mixedPdf, ToolKind.PdfText), cancelled.Token, p => { if (p > 30) cancelled.Cancel(); }));
            Assert(Hash(mixedPdf) == before && Directory.GetFiles(_folder, "mixed-pages_text*.txt").Length == outputs, "no result committed");
            Assert(!Directory.GetFiles(_folder, ".orbit-*").Any(), "no staging remains");
        });
        Check("Text PDF produces an editable DOCX without Office and never overwrites", () =>
        {
            var before = Hash(textPdf);
            var docx = engine.Execute(new(textPdf, ToolKind.PdfWord), CancellationToken.None);
            var second = engine.Execute(new(textPdf, ToolKind.PdfWord), CancellationToken.None);
            Assert(FileTypes.Detect(docx) == FileKind.Docx && docx != second, "DOCX and collision handling");
            var txt = engine.Execute(new(docx, ToolKind.DocxText), CancellationToken.None);
            Assert(File.ReadAllText(txt).Contains("Invoice 123456789") && Hash(textPdf) == before, "editable source text preserved");
        });
        Check("DOCX retains PDF page boundaries and cancellation leaves no partial document", () =>
        {
            var two = Path.Combine(_folder, "two-text-pages.pdf");
            using (var input = PdfReader.Open(textPdf, PdfDocumentOpenMode.Import))
            using (var joined = new PdfDocument()) { joined.AddPage(input.Pages[0]); joined.AddPage(input.Pages[0]); joined.Save(two); }
            var output = engine.Execute(new(two, ToolKind.PdfWord), CancellationToken.None);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(output))
            using (var stream = zip.GetEntry("word/document.xml")!.Open())
            {
                var xml = System.Xml.Linq.XDocument.Load(stream);
                System.Xml.Linq.XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                Assert(xml.Descendants(w + "br").Count(b => (string?)b.Attribute(w + "type") == "page") == 1, "explicit page boundary");
                Assert(xml.Descendants(w + "t").Count(t => t.Value.Contains("Invoice 123456789")) == 2, "both pages editable");
            }
            using var cancelled = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => engine.Execute(new(two, ToolKind.PdfWord), cancelled.Token, p => { if (p > 30) cancelled.Cancel(); }));
            Assert(!Directory.GetFiles(_folder, ".orbit-*").Any() && !File.Exists(Path.Combine(_folder, "two-text-pages (1).docx")), "cancelled DOCX not committed");
        });
        if (office) Check("PDF to DOCX preserves Unicode and Microsoft Word can open the result", () =>
        {
            var originalDocx = Path.Combine(_folder, "document.docx");
            var pdf = engine.Execute(new(originalDocx, ToolKind.OfficePdf), CancellationToken.None);
            Assert(PdfTextReader.Analyze(pdf).CanConvertToWord, "Word PDF has readable text");
            var before = Hash(pdf); var docx = engine.Execute(new(pdf, ToolKind.PdfWord), CancellationToken.None);
            Assert(FileTypes.Detect(docx) == FileKind.Docx, "real DOCX package");
            var txt = engine.Execute(new(docx, ToolKind.DocxText), CancellationToken.None);
            Assert(File.ReadAllText(txt).Contains("Đại ca"), "editable Unicode text survives PDF round trip");
            var roundTrip = engine.Execute(new(docx, ToolKind.OfficePdf), CancellationToken.None);
            Assert(PdfTextReader.Analyze(roundTrip).Pages[0].Text.Contains("Đại ca"), "Word opens and exports generated DOCX");
            Assert(Hash(pdf) == before && docx != originalDocx, "both sources preserved");
        });
    }

    private static string WriteOcrFixture()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1800, 900));
            var lines = new[] { "Tiếng Việt - ORBIT", "Cộng hòa xã hội chủ nghĩa Việt Nam", "Độc lập - Tự do - Hạnh phúc", "Mã tài liệu: 123456789", "Tổng số tiền: 1.250.000 đồng", "Ngày 19 tháng 10 năm 2026" };
            for (int i = 0; i < lines.Length; i++)
                drawing.DrawText(new FormattedText(lines[i], CultureInfo.GetCultureInfo("vi-VN"), FlowDirection.LeftToRight, new Typeface("Arial"), 44, Brushes.Black, 1), new Point(80, 80 + i * 100));
        }
        var bitmap = new RenderTargetBitmap(1800, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(_folder, "ocr-vietnamese.png"); using var stream = File.Create(path); encoder.Save(stream); return path;
    }
}
