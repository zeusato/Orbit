using System.IO.Compression;
using Orbit.App.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Orbit.App.Services;

public static class PdfCompression
{
    public static void Write(string source, string target, int quality, CancellationToken token, Action<int>? progress = null)
    {
        if (quality is < 30 or > 95) throw new ArgumentException("Chất lượng PDF phải từ 30 đến 95.");
        using (var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
        {
            if (document.PageCount is < 1 or > Orbit.Core.PdfPages.Maximum)
                throw new NotSupportedException("Mỗi tác vụ hỗ trợ từ 1 đến 200 trang PDF.");
            // A rewrite invalidates signatures. Refuse signed documents instead of silently stripping validity.
            var objects = document.Internals.GetAllObjects().OfType<PdfDictionary>().ToArray();
            if (objects.Any(d => d.Elements.GetName("/Type") == "/Sig" || d.Elements.ContainsKey("/ByteRange")))
                throw new NotSupportedException("PDF có chữ ký số. Orbit không nén để tránh làm mất hiệu lực chữ ký.");
            var masks = objects.SelectMany(d => new[] { Resolve(d.Elements["/SMask"]), Resolve(d.Elements["/Mask"]) })
                .OfType<PdfDictionary>().ToHashSet();
            var images = objects.Where(d => d.Elements.GetName("/Subtype") == "/Image" && d.Stream != null && !masks.Contains(d)).ToArray();
            for (int i = 0; i < images.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                Reencode(images[i], quality, token);
                progress?.Invoke(10 + (i + 1) * 70 / Math.Max(1, images.Length));
            }
            token.ThrowIfCancellationRequested();
            document.Options.CompressContentStreams = true;
            document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            document.Save(target);
        }
        token.ThrowIfCancellationRequested();
        // Rewriting a small/already optimized PDF can grow it. Return an unchanged copy in that case.
        if (new FileInfo(target).Length >= new FileInfo(source).Length)
            File.Copy(source, target, true);
    }

    private static PdfItem? Resolve(PdfItem? item) => item is PdfSharp.Pdf.Advanced.PdfReference reference ? reference.Value : item;

    private static void Reencode(PdfDictionary image, int quality, CancellationToken token)
    {
        var e = image.Elements;
        // Keep unusual color spaces, masks and decode transforms untouched. No page rasterization.
        if (e["/ColorSpace"] is not PdfName color || color.Value is not ("/DeviceRGB" or "/DeviceGray") ||
            e.GetInteger("/BitsPerComponent") != 8 ||
            new[] { "/Mask", "/SMask", "/ImageMask", "/Decode", "/DecodeParms", "/F" }.Any(e.ContainsKey))
            return;
        var width = e.GetInteger("/Width"); var height = e.GetInteger("/Height");
        try { ImageEngine.ValidatePixels(width, height); }
        catch (ArgumentException) { return; }
        if (width < 1 || height < 1) return;
        var data = image.Stream.Value;
        BitmapSource bitmap;
        try
        {
            if (e["/Filter"] is PdfName { Value: "/DCTDecode" })
            {
                using var input = new MemoryStream(data, false);
                var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                bitmap = decoder.Frames[0];
                if (bitmap.PixelWidth != width || bitmap.PixelHeight != height) return;
            }
            else if (e["/Filter"] == null || e["/Filter"] is PdfName { Value: "/FlateDecode" })
            {
                var channels = color.Value == "/DeviceRGB" ? 3 : 1;
                var length = checked(width * height * channels);
                byte[] pixels;
                if (e["/Filter"] == null) pixels = data;
                else
                {
                    pixels = new byte[length];
                    using var input = new MemoryStream(data, false);
                    using var inflate = new ZLibStream(input, CompressionMode.Decompress);
                    inflate.ReadExactly(pixels);
                    if (inflate.ReadByte() != -1) return;
                }
                if (pixels.Length != length) return;
                bitmap = BitmapSource.Create(width, height, 96, 96, channels == 3 ? PixelFormats.Rgb24 : PixelFormats.Gray8,
                    null, pixels, width * channels);
            }
            else return;
            token.ThrowIfCancellationRequested();
            // Always encode RGB so the new color space matches the actual JPEG.
            var rgb = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(rgb));
            using var output = new MemoryStream(); encoder.Save(output);
            token.ThrowIfCancellationRequested();
            if (output.Length >= data.Length) return;
            image.Stream.Value = output.ToArray();
            e.SetName("/Filter", "/DCTDecode"); e.SetName("/ColorSpace", "/DeviceRGB");
            e.SetInteger("/Length", (int)output.Length);
        }
        catch (Exception error) when (error is FileFormatException or NotSupportedException or InvalidDataException or EndOfStreamException)
        {
            // An unsupported image remains byte-for-byte intact; other images can still be optimized.
        }
    }

    public static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576d:0.##} MB" : $"{bytes / 1024d:0.##} KB";
    public static string Describe(long before, long after) =>
        $"{Size(before)} → {Size(after)} · " + (after < before
            ? $"giảm {(before - after) * 100d / before:0.#}%"
            : "không giảm thêm; giữ nguyên nội dung bản gốc");
}
