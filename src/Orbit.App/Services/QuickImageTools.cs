using Orbit.App.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace Orbit.App.Services;

public sealed record DecodedCode(string Format, string Text);
public sealed record ImagePayload(byte[] Bytes, BitmapSource Bitmap, string Extension, string Mime);

public static class QuickImageTools
{
    public const int MaximumImageBytes = 8 * 1024 * 1024;
    public const int MaximumBase64Characters = 12 * 1024 * 1024;
    public static BitmapSource Qr(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Nhập link hoặc nội dung cần tạo QR.");
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 2000) throw new ArgumentException("Nội dung QR tối đa 2.000 byte UTF-8. Hãy rút gọn nội dung.");
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = 512, Height = 512, Margin = 4, CharacterSet = "UTF-8",
                ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.M
            }
        };
        var pixels = writer.Write(text);
        var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.Pixels, pixels.Width * 4);
        bitmap.Freeze(); return bitmap;
    }

    public static DecodedCode[] ReadCodes(BitmapSource image, CancellationToken token)
    {
        ImageEngine.ValidatePixels(image.PixelWidth, image.PixelHeight);
        token.ThrowIfCancellationRequested();
        var scale = Math.Min(1, Math.Min(4096d / Math.Max(image.PixelWidth, image.PixelHeight),
            Math.Sqrt(16_000_000d / ((long)image.PixelWidth * image.PixelHeight))));
        if (scale < 1) image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        // Decode against white, including clipboard images with transparent backgrounds.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            for (int c = 0; c < 3; c++) pixels[i + c] = (byte)((pixels[i + c] * alpha + 255 * (255 - alpha) + 127) / 255);
            pixels[i + 3] = 255;
        }
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true }
        };
        var results = reader.DecodeMultiple(pixels, bitmap.PixelWidth, bitmap.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
        if (results == null || results.Length == 0)
        {
            token.ThrowIfCancellationRequested();
            var single = reader.Decode(pixels, bitmap.PixelWidth, bitmap.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
            results = single == null ? [] : [single];
        }
        token.ThrowIfCancellationRequested();
        return results.Select(r => new DecodedCode(r.BarcodeFormat.ToString(), r.Text)).Distinct().ToArray();
    }

    public static byte[] Png(BitmapSource bitmap)
    {
        ImageEngine.ValidatePixels(bitmap.PixelWidth, bitmap.PixelHeight);
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        return stream.ToArray();
    }

    public static ImagePayload FromFile(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaximumImageBytes) throw new ArgumentException("Ảnh Base64 tối đa 8 MB.");
        var bytes = new byte[file.Length]; file.ReadExactly(bytes);
        return FromBytes(bytes, Path.GetExtension(path));
    }

    public static ImagePayload FromBase64(string text)
    {
        if (text.Length > MaximumBase64Characters) throw new ArgumentException("Chuỗi Base64 quá dài; ảnh tối đa 8 MB.");
        text = text.Trim();
        string? extensionHint = null;
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = text.IndexOf(',');
            if (comma < 0 || !text[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase) ||
                !text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Chỉ nhận data URI ảnh có mã hóa Base64.");
            if (text.StartsWith("data:image/x-tga;", StringComparison.OrdinalIgnoreCase)) extensionHint = ".tga";
            text = text[(comma + 1)..];
        }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(text); }
        catch (System.FormatException error) { throw new ArgumentException("Chuỗi Base64 không hợp lệ.", error); }
        return FromBytes(bytes, extensionHint);
    }

    public static ImagePayload FromBytes(byte[] bytes, string? extensionHint = null)
    {
        if (bytes.Length is 0 or > MaximumImageBytes) throw new ArgumentException("Ảnh Base64 phải có dữ liệu và tối đa 8 MB.");
        // Reuse the established format/animation/pixel checks; this is our own unique scratch file.
        var extension = string.Equals(extensionHint, ".tga", StringComparison.OrdinalIgnoreCase) ? ".tga" : ".tmp";
        var temporary = Path.Combine(Path.GetTempPath(), $"orbit-base64-{Guid.NewGuid():N}{extension}");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) file.Write(bytes);
            var kind = FileTypes.Detect(temporary);
            var (imageExtension, mime) = kind switch
            {
                FileKind.Png => (".png", "image/png"), FileKind.Jpeg => (".jpg", "image/jpeg"),
                FileKind.Webp => (".webp", "image/webp"), FileKind.Bmp => (".bmp", "image/bmp"),
                FileKind.Tiff => (".tiff", "image/tiff"), FileKind.Gif => (".gif", "image/gif"),
                FileKind.Ico => (".ico", "image/x-icon"), FileKind.Avif => (".avif", "image/avif"),
                FileKind.Heic => (".heic", "image/heic"), FileKind.Tga => (".tga", "image/x-tga"),
                _ => throw new ArgumentException("Dữ liệu không phải ảnh được Orbit hỗ trợ. SVG và file ảnh động chưa được hỗ trợ.")
            };
            var bitmap = new ImageEngine().Load(temporary);
            return new(bytes, bitmap, imageExtension, mime);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string ToBase64(ImagePayload payload, bool dataUri) =>
        (dataUri ? $"data:{payload.Mime};base64," : "") + Convert.ToBase64String(payload.Bytes);

    public static string SaveCopy(byte[] bytes, string requestedPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(requestedPath))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".orbit-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            return OutputFiles.Commit(temporary, directory, Path.GetFileNameWithoutExtension(requestedPath), Path.GetExtension(requestedPath));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
