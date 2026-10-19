using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App.Services;
using Orbit.Core;

internal static partial class Program
{
    private static void CheckLongOcr()
    {
        Check("Long PDF raster keeps text resolution within a bounded pixel budget", () =>
        {
            var size = TextExtraction.PdfOcrSize(1145.495, 10800);
            Assert(size.Width >= 2000 && size.Height > 20000, "long page is not squeezed to 425 px wide");
            Assert((long)size.Width * size.Height <= 48_000_000 && size.Height <= 30000, "memory and dimensions bounded");
            Assert(Math.Abs((double)size.Width / size.Height - 1145.495 / 10800) < .001, "aspect ratio");
            var normal = TextExtraction.PdfOcrSize(595, 842);
            Assert(normal.Width > 2400 && normal.Height > 3400, "normal A4 retains 300 DPI");
            Throws<InvalidDataException>(() => TextExtraction.PdfOcrSize(double.NaN, 100));
        });
        Check("Long image OCR retains first, last and seam-crossing lines exactly once", () =>
        {
            var drawing = new DrawingVisual();
            using (var canvas = drawing.RenderOpen())
            {
                canvas.DrawRectangle(Brushes.White, null, new Rect(0, 0, 900, 7200));
                foreach (var (label, top) in new[] { ("START 123456", 40), ("BOUNDARY 234567", 2380), ("MIDDLE 345678", 3600), ("BOUNDARY 456789", 4780), ("FINISH 567890", 7100) })
                    canvas.DrawText(new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 38, Brushes.Black, 1), new Point(40, top));
            }
            var bitmap = new RenderTargetBitmap(900, 7200, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(_folder, "long-seams.png"); using (var stream = File.Create(path)) encoder.Save(stream);
            var output = new DocumentEngine().Execute(new(path, ToolKind.ImageText), CancellationToken.None);
            var text = File.ReadAllText(output);
            foreach (var marker in new[] { "123456", "234567", "345678", "456789", "567890" })
                Assert(text.Split(marker).Length == 2, "marker retained exactly once: " + marker);
            using var cancelled = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => new DocumentEngine().Execute(new(path, ToolKind.ImageText), cancelled.Token, p => { if (p > 20) cancelled.Cancel(); }));
            Assert(!File.Exists(Path.Combine(_folder, "long-seams_text (1).txt")), "cancelled strip OCR not committed");
        });
    }
}
