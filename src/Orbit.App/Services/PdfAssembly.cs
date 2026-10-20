using Orbit.App.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfPages = Orbit.Core.PdfPages;

namespace Orbit.App.Services;

public sealed record PdfInput(string Path, int Pages, bool IsPdf)
{
    public override string ToString() => $"{System.IO.Path.GetFileName(Path)}  ·  {Pages} trang";
}

public static class PdfAssembly
{
    public static PdfInput Inspect(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy file.", path);
        if (new FileInfo(path).Length > ImageEngine.MaxFileBytes) throw new NotSupportedException("Mỗi file tối đa 256 MB.");
        var kind = FileTypes.Detect(path);
        if (kind == FileKind.Pdf)
        {
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            if (pdf.PageCount is < 1 or > PdfPages.Maximum) throw new NotSupportedException("Mỗi PDF hỗ trợ từ 1 đến 200 trang.");
            return new(path, pdf.PageCount, true);
        }
        if (!FileTypes.IsImage(kind)) throw new NotSupportedException("Chỉ thêm PDF hoặc ảnh tĩnh được hỗ trợ.");
        _ = new ImageEngine().Load(path); // Verify codec, dimensions and animation before accepting.
        token.ThrowIfCancellationRequested();
        return new(path, 1, false);
    }

    public static void Write(string[] sources, string target, CancellationToken token, Action<int>? progress)
    {
        if (sources.Length is < 1 or > 128) throw new ArgumentException("Chọn từ 1 đến 128 file PDF hoặc ảnh.");
        using var output = new PdfDocument();
        for (var index = 0; index < sources.Length; index++)
        {
            var input = Inspect(sources[index], token);
            if (output.PageCount + input.Pages > PdfPages.Maximum) throw new NotSupportedException("Thành phẩm tối đa 200 trang.");
            if (input.IsPdf)
            {
                using var pdf = PdfReader.Open(input.Path, PdfDocumentOpenMode.Import);
                foreach (var page in pdf.Pages) { token.ThrowIfCancellationRequested(); output.AddPage(page); }
            }
            else AddImage(output, new ImageEngine().Load(input.Path));
            token.ThrowIfCancellationRequested();
            progress?.Invoke(10 + (index + 1) * 75 / sources.Length);
        }
        output.Save(target);
    }

    private static void AddImage(PdfDocument pdf, BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream(); encoder.Save(memory); memory.Position = 0;
        using var image = XImage.FromStream(memory);
        var page = pdf.AddPage();
        var scale = Math.Min(1, 14400d / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        page.Width = XUnit.FromPoint(bitmap.PixelWidth * .75 * scale);
        page.Height = XUnit.FromPoint(bitmap.PixelHeight * .75 * scale);
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
    }

    public static BitmapSource Preview(PdfInput input, int page, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!input.IsPdf)
        {
            var bitmap = new ImageEngine().Load(input.Path);
            var scale = Math.Min(1, 1000d / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
            var preview = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)); preview.Freeze(); return preview;
        }
        using var memory = new MemoryStream();
        // Bounded thumbnail independent of the physical dimensions of a PDF page.
        var bytes = File.ReadAllBytes(input.Path);
        var sizes = PDFtoImage.Conversion.GetPageSizes(bytes).ToArray();
        if (page < 0 || page >= sizes.Length) throw new ArgumentException("Trang xem trước không còn tồn tại.");
        var size = sizes[page];
        var ratio = Math.Min(900d / size.Width, 900d / size.Height);
        var width = Math.Max(1, (int)Math.Round(size.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(size.Height * ratio));
        PDFtoImage.Conversion.SavePng(memory, bytes, page: page,
            options: new PDFtoImage.RenderOptions(Dpi: 72, Width: width, Height: height, WithAspectRatio: false));
        token.ThrowIfCancellationRequested();
        memory.Position = 0;
        var frame = BitmapFrame.Create(memory, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze(); return frame;
    }
}
