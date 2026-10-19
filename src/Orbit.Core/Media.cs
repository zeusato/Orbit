using System;
using System.Linq;

namespace Orbit.Core;

public enum MediaQuality { High, Medium, Low }

// Start/End select a range (trim, GIF); Start alone is the frame time. Height 0 keeps the source height.
public sealed record MediaOptions(TimeSpan? Start = null, TimeSpan? End = null, int Height = 0, MediaQuality Quality = MediaQuality.Medium,
    int GifFps = 12, int GifWidth = 480, string? Extension = null);

public static class MediaTime
{
    public static string Format(TimeSpan time) => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture) : time.ToString(@"m\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture);
    // Accepts 12.5 (seconds), 1:02, 1:02.5 or 1:02:03.250; comma also works as the decimal mark.
    public static bool TryParse(string text, out TimeSpan time)
    {
        time = TimeSpan.Zero; var parts = (text ?? "").Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 1 or > 3 || parts.Any(p => p.Length == 0)) return false;
        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var value)) return false;
            if (!last && value != Math.Floor(value)) return false;
            if (i > 0 && value >= 60) return false;
            total = total * 60 + value;
        }
        if (!double.IsFinite(total) || total > TimeSpan.MaxValue.TotalSeconds / 2) return false;
        time = TimeSpan.FromMilliseconds(Math.Round(total * 1000)); return true;
    }
}

public static class MediaLimits
{
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxGif = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan MinClip = TimeSpan.FromMilliseconds(100);

    public static void CheckSource(long bytes, TimeSpan duration)
    {
        if (bytes > MaxBytes) throw new NotSupportedException("Mỗi file audio/video tối đa 2 GB.");
        if (duration <= TimeSpan.Zero) throw new NotSupportedException("Không đọc được thời lượng của file.");
        if (duration > MaxDuration) throw new NotSupportedException("Mỗi file audio/video tối đa 1 giờ.");
    }
    // Validates a range against the source duration; returns the clamped range.
    public static (TimeSpan Start, TimeSpan End) Range(MediaOptions? options, TimeSpan duration, TimeSpan? maximum = null)
    {
        var start = options?.Start ?? TimeSpan.Zero; var end = options?.End ?? duration;
        if (start < TimeSpan.Zero || start >= duration) throw new ArgumentException("Điểm đầu nằm ngoài thời lượng file.");
        if (end > duration) end = duration;
        if (end - start < MinClip) throw new ArgumentException("Đoạn chọn phải dài ít nhất 0,1 giây.");
        if (maximum is TimeSpan max && end - start > max) throw new ArgumentException($"Đoạn chọn tối đa {max.TotalSeconds:0} giây.");
        return (start, end);
    }

    // H.264 target bitrate from pixels per second; preset quality scales bits per pixel.
    public static uint VideoBitrate(int width, int height, double fps, MediaQuality quality)
    {
        var bpp = quality switch { MediaQuality.High => .10, MediaQuality.Low => .045, _ => .07 };
        var rate = width * (double)height * Math.Clamp(double.IsFinite(fps) && fps > 0 ? fps : 30, 1, 120) * bpp;
        return (uint)Math.Clamp(rate, 250_000, 40_000_000);
    }
    // Scales to the target height (never upscales), keeping aspect; H.264 needs even sizes.
    public static (int Width, int Height) Scale(int width, int height, int targetHeight)
    {
        if (width < 2 || height < 2) throw new ArgumentException("Kích thước video không hợp lệ.");
        // Portrait video: the preset applies to the shorter side, so a phone clip at "720p" stays 720 wide.
        var shortSide = Math.Min(width, height);
        var scale = targetHeight <= 0 || targetHeight >= shortSide ? 1 : (double)targetHeight / shortSide;
        static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
        return (Even(width * scale), Even(height * scale));
    }
    public static string TrimExtension(FileKind kind) => kind switch
    {
        FileKind.Mp3 => ".mp3", FileKind.Wav => ".wav", FileKind.M4a or FileKind.Audio => ".m4a", _ => ".mp4"
    };
    public static string MergeExtension(FileKind first) => first switch { FileKind.Mp3 => ".mp3", FileKind.Wav => ".wav", _ => ".m4a" };
}
