using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App.Services;
using Orbit.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

internal static partial class Program
{
    private static void CheckPdfEnhancements()
    {
        var engine = new DocumentEngine();
        var source = Path.Combine(_folder, "compression-source.pdf");
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        page.AddText("Orbit keeps searchable text 12345", 14, new(40, 780), builder.AddStandard14Font(Standard14Font.Helvetica));
        var textBytes = builder.Build();
        File.WriteAllBytes(source, textBytes);
        // RGB noise with a strong color bias: large lossless image, useful reduction from JPEG.
        var pixels = new byte[600 * 400 * 3]; var random = new Random(42);
        for (int i = 0; i < pixels.Length; i += 3)
        { pixels[i] = (byte)random.Next(150, 230); pixels[i + 1] = (byte)random.Next(50, 110); pixels[i + 2] = (byte)random.Next(10, 50); }
        var bitmap = BitmapSource.Create(600, 400, 96, 96, PixelFormats.Rgb24, null, pixels, 1800);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var data = new MemoryStream())
        {
            png.Save(data); data.Position = 0;
            using var pdf = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
            using (var image = XImage.FromStream(data))
            using (var drawing = XGraphics.FromPdfPage(pdf.Pages[0])) drawing.DrawImage(image, 40, 120, 480, 320);
            pdf.Save(source);
        }
        var sourceHash = Hash(source);
        Check("PDF compression reduces embedded RGB images while retaining text, page size and appearance", () =>
        {
            var output = engine.Execute(new(source, ToolKind.PdfCompress, Quality: 55), default);
            Assert(new FileInfo(output).Length < new FileInfo(source).Length / 2, "substantial measured reduction");
            using var before = UglyToad.PdfPig.PdfDocument.Open(source);
            using var after = UglyToad.PdfPig.PdfDocument.Open(output);
            Assert(before.GetPage(1).Text == after.GetPage(1).Text && after.GetPage(1).Text.Contains("searchable"), "actual searchable text retained");
            using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
            Assert(pdf.PageCount == 1 && Math.Abs(pdf.Pages[0].Width.Point - 595) < 2, "page geometry");
            var a = PdfAssembly.Preview(PdfAssembly.Inspect(source), 0, default);
            var b = PdfAssembly.Preview(PdfAssembly.Inspect(output), 0, default);
            var aa = new FormatConvertedBitmap(a, PixelFormats.Bgr24, null, 0);
            var bb = new FormatConvertedBitmap(b, PixelFormats.Bgr24, null, 0);
            Assert(aa.PixelWidth == bb.PixelWidth && aa.PixelHeight == bb.PixelHeight, "render dimensions");
            var pa = new byte[aa.PixelWidth * aa.PixelHeight * 3]; var pb = new byte[pa.Length];
            aa.CopyPixels(pa, aa.PixelWidth * 3, 0); bb.CopyPixels(pb, bb.PixelWidth * 3, 0);
            Assert(pa.Zip(pb, (x, y) => Math.Abs(x - y)).Average() < 8, "render stays close; no lost page content or color inversion");
            Assert(Hash(source) == sourceHash, "original untouched");
        });
        Check("JPEG PDF compression honors quality and never increases output size", () =>
        {
            var jpeg = new JpegBitmapEncoder { QualityLevel = 95 }; jpeg.Frames.Add(BitmapFrame.Create(bitmap));
            var jpegPath = Path.Combine(_folder, "compression-image.jpg");
            using (var file = File.Create(jpegPath)) jpeg.Save(file);
            var jpegPdf = Path.Combine(_folder, "compression-jpeg.pdf");
            using (var pdf = new PdfDocument())
            {
                var p = pdf.AddPage();
                using (var image = XImage.FromFile(jpegPath))
                using (var drawing = XGraphics.FromPdfPage(p)) drawing.DrawImage(image, 0, 0, 480, 320);
                pdf.Save(jpegPdf);
            }
            var low = engine.Execute(new(jpegPdf, ToolKind.PdfCompress, Quality: 30), default);
            var high = engine.Execute(new(jpegPdf, ToolKind.PdfCompress, Quality: 90), default);
            Assert(new FileInfo(low).Length < new FileInfo(high).Length && new FileInfo(high).Length <= new FileInfo(jpegPdf).Length, "quality controls actual bytes");
            var minimal = Path.Combine(_folder, "compression-minimal.pdf");
            File.WriteAllBytes(minimal, textBytes);
            var copy = engine.Execute(new(minimal, ToolKind.PdfCompress), default);
            Assert(new FileInfo(copy).Length <= new FileInfo(minimal).Length, "no growth on text-only document");
            if (new FileInfo(copy).Length == new FileInfo(minimal).Length) Assert(Hash(copy) == Hash(minimal), "unchanged fallback exact");
            Assert(PdfCompression.Describe(1000, 1000).Contains("không giảm"), "honest no-reduction message");
        });
        Check("Compression cancellation, invalid quality and signed PDF leave no partial output", () =>
        {
            var directory = Path.Combine(_folder, "compression-cancel");
            using var cancel = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => engine.Execute(new(source, ToolKind.PdfCompress, OutputDirectory: directory),
                cancel.Token, p => { if (p >= 80) cancel.Cancel(); }));
            Throws<ArgumentException>(() => engine.Execute(new(source, ToolKind.PdfCompress, Quality: 0, OutputDirectory: directory), default));
            var signed = Path.Combine(_folder, "signature-marker.pdf");
            using (var pdf = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
            {
                var signature = new PdfDictionary(pdf); signature.Elements.SetName("/Type", "/Sig");
                pdf.Internals.AddObject(signature); pdf.Internals.Catalog.Elements.SetReference("/TestSignature", signature);
                pdf.Save(signed);
            }
            Throws<NotSupportedException>(() => engine.Execute(new(signed, ToolKind.PdfCompress, OutputDirectory: directory), default));
            Assert(!Directory.EnumerateFileSystemEntries(directory).Any(), "all staging cleaned");
            Assert(Hash(source) == sourceHash, "source preserved");
        });
        Check("PDF compression preserves transparent image data and its separately referenced alpha mask", () =>
        {
            var rgba = new byte[600 * 400 * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            { rgba[i] = pixels[i / 4 * 3 + 2]; rgba[i + 1] = pixels[i / 4 * 3 + 1]; rgba[i + 2] = pixels[i / 4 * 3]; rgba[i + 3] = (byte)random.Next(0, 256); }
            var alpha = BitmapSource.Create(600, 400, 96, 96, PixelFormats.Bgra32, null, rgba, 2400);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(alpha));
            var transparent = Path.Combine(_folder, "compression-alpha.pdf");
            using (var data = new MemoryStream())
            using (var pdf = new PdfDocument())
            {
                encoder.Save(data); data.Position = 0;
                var p = pdf.AddPage();
                using (var image = XImage.FromStream(data))
                using (var drawing = XGraphics.FromPdfPage(p)) drawing.DrawImage(image, 0, 0, 480, 320);
                pdf.Save(transparent);
            }
            static string[] ImageHashes(string path)
            {
                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                return pdf.Internals.GetAllObjects().OfType<PdfDictionary>()
                    .Where(d => d.Elements.GetName("/Subtype") == "/Image")
                    .Select(d => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(d.Stream.Value))).Order().ToArray();
            }
            var hashes = ImageHashes(transparent);
            Assert(hashes.Length == 2, "fixture has image and separate soft mask");
            var result = engine.Execute(new(transparent, ToolKind.PdfCompress, Quality: 30), default);
            Assert(ImageHashes(result).SequenceEqual(hashes), "parent and mask bytes preserved");
        });
        Check("PDF output names reject path escape and Windows reserved names; collision adds suffix", () =>
        {
            foreach (var invalid in new[] { "", " ", "..", "../escape", @"..\escape", "C:escape", "CON", "nul.pdf", "COM1", "name.", "name " })
                Throws<ArgumentException>(() => engine.Execute(new(source, ToolKind.PdfCompress, OutputStem: invalid), default));
            var directory = Path.Combine(_folder, "named-pdf");
            var first = engine.Execute(new(source, ToolKind.PdfSplit, OutputDirectory: directory, OutputStem: "Bản tách", Pdf: new("1", false)), default);
            var hash = Hash(first);
            var second = engine.Execute(new(source, ToolKind.PdfSplit, OutputDirectory: directory, OutputStem: "Bản tách", Pdf: new("1", false)), default);
            Assert(Path.GetFileName(first) == "Bản tách.pdf" && Path.GetFileName(second) == "Bản tách (1).pdf" && Hash(first) == hash, "custom names and collision");
            var folder = engine.Execute(new(source, ToolKind.PdfSplit, OutputDirectory: directory, OutputStem: "Các trang"), default);
            Assert(Path.GetFileName(folder) == "Các trang" && File.Exists(Path.Combine(folder, "page-0001.pdf")), "custom split folder");
        });
    }
}
