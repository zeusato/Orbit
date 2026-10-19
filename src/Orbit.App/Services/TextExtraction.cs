using System.Text;
using ImageMagick;
using Orbit.App.Imaging;
using Tesseract;

namespace Orbit.App.Services;

public sealed class LocalOcr : IDisposable
{
    private readonly TesseractEngine _engine;
    public LocalOcr()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (!File.Exists(Path.Combine(data, "vie.traineddata")) || !File.Exists(Path.Combine(data, "eng.traineddata")))
            throw new NotSupportedException("Thiếu mô hình OCR. Giải nén đầy đủ thư mục tessdata đi kèm Orbit.");
        try { _engine = new TesseractEngine(data, "vie+eng", EngineMode.LstmOnly); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new NotSupportedException("Không khởi tạo được OCR. Giữ đầy đủ thư viện đi kèm và cài Microsoft Visual C++ Redistributable x64 (2015–2022).", error); }
        _engine.SetVariable("user_defined_dpi", "300");
    }
    public (string Text, float Confidence) Read(byte[] png, CancellationToken token, Action<double>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        using var image = new MagickImage(png);
        ImageEngine.ValidatePixels((int)image.Width, (int)image.Height);
        image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove);
        image.ColorSpace = ColorSpace.Gray;
        if (image.Height > 6000 && image.Height > image.Width * 2)
        {
            // Long infographics are not a single page of paragraphs. Keep their
            // pixels and OCR strips with context; assign each line by its center
            // to one strip so a seam neither truncates nor duplicates the line.
            const int coreHeight = 2400, overlap = 200;
            var combined = new StringBuilder(); double weightedConfidence = 0; int characters = 0;
            for (int top = 0; top < image.Height; top += coreHeight)
            {
                token.ThrowIfCancellationRequested();
                int start = Math.Max(0, top - overlap), end = Math.Min((int)image.Height, top + coreHeight + overlap);
                using var strip = image.Clone();
                strip.Crop(new MagickGeometry(0, start, image.Width, (uint)(end - start))); strip.ResetPage();
                using var pixels = Pix.LoadFromMemory(strip.ToByteArray(MagickFormat.Png));
                using var recognized = _engine.Process(pixels, PageSegMode.Auto);
                using var lines = recognized.GetIterator(); lines.Begin();
                do
                {
                    if (!lines.TryGetBoundingBox(PageIteratorLevel.TextLine, out var box)) continue;
                    var center = start + (box.Y1 + box.Y2) / 2;
                    if (center < top || center >= Math.Min((int)image.Height, top + coreHeight)) continue;
                    var line = lines.GetText(PageIteratorLevel.TextLine)?.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    combined.AppendLine(line);
                    weightedConfidence += recognized.GetMeanConfidence() * line.Length; characters += line.Length;
                } while (lines.Next(PageIteratorLevel.TextLine));
                progress?.Invoke(Math.Min(1, (top + coreHeight) / (double)image.Height));
            }
            token.ThrowIfCancellationRequested();
            return (combined.ToString().Trim(), characters == 0 ? 0 : (float)(weightedConfidence / characters));
        }
        image.Deskew(new Percentage(40));
        using var pix = Pix.LoadFromMemory(image.ToByteArray(MagickFormat.Png));
        using var page = _engine.Process(pix, PageSegMode.Auto);
        var text = page.GetText().Trim(); var confidence = page.GetMeanConfidence();
        token.ThrowIfCancellationRequested();
        progress?.Invoke(1);
        return (text, confidence);
    }
    public void Dispose() => _engine.Dispose();
}

public static class TextExtraction
{
    public static (int Width, int Height) PdfOcrSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new InvalidDataException("Kích thước trang PDF không hợp lệ.");
        // Budget by area, not longest edge: a 10:1 infographic must not be
        // squeezed to ~400 px wide just because it is very tall.
        var scale = Math.Min(300d / 72, Math.Min(Math.Sqrt(48_000_000d / (width * height)), 30000d / Math.Max(width, height)));
        return (Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
    }
    private const string OcrNotice = "[Văn bản OCR tự động; cần đối chiếu dấu tiếng Việt, tên riêng và số liệu với bản gốc.]";
    public static void FromImage(string source, string target, CancellationToken token, Action<int>? progress = null)
    {
        var bitmap = new ImageEngine().Load(source);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream(); encoder.Save(memory);
        using var ocr = new LocalOcr(); var result = ocr.Read(memory.ToArray(), token, p => progress?.Invoke(10 + (int)(p * 75)));
        if (string.IsNullOrWhiteSpace(result.Text)) throw new NotSupportedException("Không nhận diện được chữ trong ảnh. Hãy dùng ảnh rõ và đúng chiều.");
        File.WriteAllText(target, OcrNotice + Environment.NewLine + Warning(result.Confidence) + result.Text + Environment.NewLine, new UTF8Encoding(false));
    }

    public static void FromPdf(string source, string target, CancellationToken token, Action<int>? progress)
    {
        var analysis = PdfTextReader.Analyze(source, token);
        byte[]? bytes = null; LocalOcr? ocr = null;
        try
        {
            using var writer = new StreamWriter(target, false, new UTF8Encoding(false));
            if (analysis.Pages.Any(p => p.NeedsOcr)) writer.WriteLine(OcrNotice);
            foreach (var page in analysis.Pages)
            {
                token.ThrowIfCancellationRequested();
                writer.WriteLine($"--- Trang {page.Number} · {(page.Blank ? "trống" : page.NeedsOcr ? "OCR" : "lớp chữ")} ---");
                if (page.NeedsOcr)
                {
                    bytes ??= File.ReadAllBytes(source); ocr ??= new LocalOcr();
                    var size = PDFtoImage.Conversion.GetPageSizes(bytes).ElementAt(page.Number - 1);
                    var (width, height) = PdfOcrSize(size.Width, size.Height);
                    ImageEngine.ValidatePixels(width, height);
                    using var rendered = PDFtoImage.Conversion.ToImage(bytes, page: page.Number - 1, options: new PDFtoImage.RenderOptions(Dpi: 300, Width: width, Height: height, WithAspectRatio: false));
                    ImageEngine.ValidatePixels(rendered.Width, rendered.Height);
                    using var encoded = rendered.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    var result = ocr.Read(encoded.ToArray(), token, p => progress?.Invoke(10 + (int)((page.Number - 1 + p) * 75 / analysis.Pages.Count)));
                    writer.Write(Warning(result.Confidence));
                    writer.WriteLine(result.Text.Length == 0 ? "[Không nhận diện được chữ; cần kiểm tra trang gốc.]" : result.Text);
                }
                else writer.WriteLine(page.Text);
                writer.WriteLine(); progress?.Invoke(10 + page.Number * 75 / analysis.Pages.Count);
            }
        }
        finally { ocr?.Dispose(); }
    }
    private static string Warning(float confidence) => confidence < .7f ? "[OCR độ tin cậy thấp — cần kiểm tra.]" + Environment.NewLine : "";
}
