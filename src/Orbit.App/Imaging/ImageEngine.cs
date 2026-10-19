using ImageMagick;

namespace Orbit.App.Imaging;

public sealed class ImageEngine
{
    public const long MaxPixels = 60_000_000;
    public const long MaxFileBytes = 256L * 1024 * 1024;

    public BitmapSource Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Không tìm thấy file nguồn.", path);
        var kind = FileTypes.Detect(path);
        if (!FileTypes.IsImage(kind)) throw new NotSupportedException("Nội dung file không thuộc định dạng ảnh được hỗ trợ.");
        if (info.Length > MaxFileBytes) throw new NotSupportedException("File vượt giới hạn 256 MB của bản đầu.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (kind == FileKind.Webp) RejectAnimatedWebp(stream);
        if (kind is FileKind.Webp or FileKind.Avif or FileKind.Heic or FileKind.Tga or FileKind.Ico)
            return LoadExtended(stream, kind);
        RejectAnimatedPng(stream);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1) throw new NotSupportedException("Ảnh nhiều frame hoặc nhiều trang chưa được hỗ trợ. File gốc được giữ nguyên.");
        var frame = decoder.Frames[0];
        ValidatePixels(frame.PixelWidth, frame.PixelHeight);
        var orientation = ReadOrientation(frame.Metadata as BitmapMetadata);
        frame.Freeze();
        return Orient(frame, orientation);
    }

    private static ushort ReadOrientation(BitmapMetadata? metadata)
    {
        if (metadata == null) return 1;
        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try { if (metadata.ContainsQuery(query)) return Convert.ToUInt16(metadata.GetQuery(query)); }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or InvalidCastException or FormatException) { }
        }
        return 1;
    }

    private static void RejectAnimatedWebp(Stream stream)
    {
        Span<byte> chunk = stackalloc byte[8];
        try
        {
            stream.Position = 12;
            while (stream.Position + 8 <= stream.Length)
            {
                stream.ReadExactly(chunk);
                var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                if (chunk[..4].SequenceEqual("ANIM"u8) || chunk[..4].SequenceEqual("ANMF"u8)) throw new NotSupportedException("WebP động chưa được hỗ trợ.");
                if ((long)size + (size % 2) > stream.Length - stream.Position) break;
                stream.Seek((long)size + (size % 2), SeekOrigin.Current);
            }
        }
        finally { stream.Position = 0; }
    }

    private static void RejectAnimatedPng(Stream stream)
    {
        // WIC can expose APNG as one static frame, so inspect its animation control chunk.
        Span<byte> header = stackalloc byte[8];
        try
        {
            if (stream.Read(header) != 8 || !header.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return;
            while (stream.Position + 8 <= stream.Length)
            {
                stream.ReadExactly(header);
                var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
                if (header[4] == 'a' && header[5] == 'c' && header[6] == 'T' && header[7] == 'L')
                    throw new NotSupportedException("Ảnh PNG động chưa được hỗ trợ. File gốc được giữ nguyên.");
                if ((long)length + 4 > stream.Length - stream.Position) return;
                stream.Seek((long)length + 4, SeekOrigin.Current);
            }
        }
        finally { stream.Position = 0; }
    }

    public static BitmapSource Orient(BitmapSource image, ushort orientation)
    {
        var group = new TransformGroup();
        switch (orientation)
        {
            case 2: group.Children.Add(new ScaleTransform(-1, 1)); break;
            case 3: group.Children.Add(new RotateTransform(180)); break;
            case 4: group.Children.Add(new ScaleTransform(1, -1)); break;
            case 5: group.Children.Add(new RotateTransform(90)); group.Children.Add(new ScaleTransform(-1, 1)); break;
            case 6: group.Children.Add(new RotateTransform(90)); break;
            case 7: group.Children.Add(new RotateTransform(90)); group.Children.Add(new ScaleTransform(1, -1)); break;
            case 8: group.Children.Add(new RotateTransform(270)); break;
            default: return image;
        }
        var result = new TransformedBitmap(image, group); result.Freeze(); return result;
    }

    public BitmapSource Thumbnail(BitmapSource image, int maxSide = 1100)
    {
        var scale = Math.Min(1, (double)maxSide / Math.Max(image.PixelWidth, image.PixelHeight));
        if (scale >= 1) return image;
        var thumbnail = new TransformedBitmap(image, new ScaleTransform(scale, scale)); thumbnail.Freeze(); return thumbnail;
    }

    public string Execute(ImageRequest request, CancellationToken cancellation, Action<int>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested(); progress?.Invoke(5);
        var image = Load(request.Source);
        cancellation.ThrowIfCancellationRequested(); progress?.Invoke(30);
        switch (request.Tool)
        {
            case ToolKind.Resize:
                ValidatePixels(request.Width, request.Height);
                var sx = (double)request.Width / image.PixelWidth;
                var sy = (double)request.Height / image.PixelHeight;
                if (request.KeepAspect) sx = sy = Math.Min(sx, sy);
                image = new TransformedBitmap(image, new ScaleTransform(sx, sy)); image.Freeze(); break;
            case ToolKind.Crop:
                var crop = request.Crop ?? throw new ArgumentException("Chưa chọn vùng cắt.");
                if (crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1 ||
                    (long)crop.X + crop.Width > image.PixelWidth || (long)crop.Y + crop.Height > image.PixelHeight)
                    throw new ArgumentException("Vùng cắt nằm ngoài ảnh nguồn.");
                image = new CroppedBitmap(image, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height)); image.Freeze(); break;
            case ToolKind.Rotate:
                image = new TransformedBitmap(image, new RotateTransform(90)); image.Freeze(); break;
            case ToolKind.Frame:
                var frame = request.Frame ?? throw new ArgumentException("Chưa chọn thông số khung.");
                frame.Validate();
                var picture = frame.Background == FrameBackground.Picture ? Load(frame.Picture!) : null;
                cancellation.ThrowIfCancellationRequested();
                image = FrameRenderer.Render(image, frame, picture); break;
        }
        cancellation.ThrowIfCancellationRequested(); progress?.Invoke(55);
        BitmapEncoder encoder;
        if (request.Tool is ToolKind.Jpeg or ToolKind.Compress || (request.Tool == ToolKind.Frame && request.Frame?.Jpeg == true))
        {
            if (request.Quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request.Quality));
            image = FlattenWhite(image);
            encoder = new JpegBitmapEncoder { QualityLevel = request.Quality };
        }
        else encoder = request.Tool switch
        { ToolKind.Bmp => new BmpBitmapEncoder(), ToolKind.Tiff => new TiffBitmapEncoder(), ToolKind.Gif => new GifBitmapEncoder(), _ => new PngBitmapEncoder() };
        // Normalized pixels only: never copy stale EXIF orientation or location metadata.
        encoder.Frames.Add(BitmapFrame.Create(image));
        var source = Path.GetFullPath(request.Source);
        var directory = OutputLocation.Prepare(request);
        var temporary = Path.Combine(directory, $".orbit-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (request.Tool is ToolKind.Webp or ToolKind.Ico)
                {
                    using var pixels = new MemoryStream(); encoder.Save(pixels); pixels.Position = 0;
                    using var converted = new MagickImage(pixels);
                    converted.Strip();
                    if (request.Tool == ToolKind.Webp)
                    { converted.Settings.SetDefine(MagickFormat.WebP, "lossless", true); converted.Write(output, MagickFormat.WebP); }
                    else
                    { if (converted.Width > 256 || converted.Height > 256) converted.Resize(256, 256); converted.Write(output, MagickFormat.Ico); }
                }
                else encoder.Save(output);
                output.Flush(true);
            }
            progress?.Invoke(90); cancellation.ThrowIfCancellationRequested();
            var name = ToolCatalog.OutputName(request);
            var result = OutputFiles.Commit(temporary, directory, name.Stem, name.Extension);
            progress?.Invoke(100); return result;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static BitmapSource LoadExtended(Stream source, FileKind kind)
    {
        var format = kind switch { FileKind.Webp => MagickFormat.WebP, FileKind.Avif => MagickFormat.Avif, FileKind.Heic => MagickFormat.Heic, FileKind.Tga => MagickFormat.Tga, _ => MagickFormat.Ico };
        var settings = new MagickReadSettings { Format = format };
        if (kind == FileKind.Heic)
        {
            // HEIF image sequences are animations. Still HEIC may hold extra top-level items
            // (bursts, edits); viewers show the primary image, which libheif decodes first.
            Span<byte> brand = stackalloc byte[12];
            if (source.Read(brand) == 12 && (brand[8..].SequenceEqual("msf1"u8) || brand[8..].SequenceEqual("hevs"u8)))
                throw new NotSupportedException("HEIF dạng chuỗi ảnh/ảnh động chưa được hỗ trợ.");
            source.Position = 0; settings.FrameIndex = 0; settings.FrameCount = 1;
        }
        using var headers = new MagickImageCollection(); headers.Ping(source, settings);
        if (headers.Count == 0 || (headers.Count != 1 && kind != FileKind.Ico))
            throw new NotSupportedException("Ảnh động hoặc nhiều frame chưa được hỗ trợ.");
        foreach (var header in headers) ValidatePixels(checked((int)header.Width), checked((int)header.Height));
        source.Position = 0;
        using var frames = new MagickImageCollection(); frames.Read(source, settings);
        var image = frames.OrderByDescending(f => (long)f.Width * f.Height).First();
        image.AutoOrient(); image.Strip();
        using var png = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        var decoded = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); decoded.Freeze(); return decoded;
    }

    public static void ValidatePixels(int width, int height)
    {
        if (width < 1 || height < 1 || width > 32768 || height > 32768 || (long)width * height > MaxPixels)
            throw new ArgumentException("Kích thước phải lớn hơn 0, mỗi cạnh tối đa 32768 px và tổng tối đa 60 megapixel.");
    }

    private static BitmapSource FlattenWhite(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            drawing.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }
        var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        var result = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0); result.Freeze(); return result;
    }
}
