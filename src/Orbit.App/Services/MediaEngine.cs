using ImageMagick;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Orbit.App.Services;

public sealed record MediaInfo(TimeSpan Duration, bool HasVideo, bool HasAudio, int Width, int Height, double Fps, string VideoCodec, string AudioCodec);

// Audio/video through Windows Media Foundation (WinRT transcoder and composition). Formats
// depend on the codecs installed in Windows; nothing is downloaded and no FFmpeg is used.
public static class MediaEngine
{
    private static T Wait<T>(IAsyncOperation<T> operation, CancellationToken token) => operation.AsTask(token).GetAwaiter().GetResult();
    private static void Wait(IAsyncAction action, CancellationToken token) => action.AsTask(token).GetAwaiter().GetResult();
    private static StorageFile File(string path, CancellationToken token) => Wait(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)), token);

    public static MediaInfo Probe(string path, CancellationToken token = default)
    {
        var file = File(path, token);
        MediaEncodingProfile profile;
        try { profile = Wait(MediaEncodingProfile.CreateFromFileAsync(file), token); }
        catch (Exception error) when (error is not OperationCanceledException)
        { throw new NotSupportedException("Windows không đọc được định dạng audio/video này.", error); }
        var duration = TimeSpan.Zero;
        using (var source = MediaSource.CreateFromStorageFile(file))
        {
            try { Wait(source.OpenAsync(), token); duration = source.Duration ?? TimeSpan.Zero; }
            catch (Exception error) when (error is not OperationCanceledException) { }
        }
        if (duration <= TimeSpan.Zero) duration = Wait(file.Properties.GetMusicPropertiesAsync(), token).Duration;
        var video = profile.Video; var audio = profile.Audio;
        var hasVideo = video != null && video.Width > 0 && video.Height > 0;
        var fps = hasVideo && video!.FrameRate.Denominator > 0 ? video.FrameRate.Numerator / (double)video.FrameRate.Denominator : 0;
        return new MediaInfo(duration, hasVideo, audio != null && audio.SampleRate > 0, hasVideo ? (int)video!.Width : 0, hasVideo ? (int)video!.Height : 0, fps,
            hasVideo ? video!.Subtype : "", audio?.Subtype ?? "");
    }

    // Builds the decode/encode chain without running it: fails when a decoder (HEVC, VP9…) is missing.
    public static string? MissingCodec(string path, MediaInfo info, CancellationToken token = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Orbit-media-probe"); Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder, "probe-*").Where(f => System.IO.File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-1)))
            try { System.IO.File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var target = Path.Combine(folder, $"probe-{Guid.NewGuid():N}" + (info.HasVideo ? ".mp4" : ".m4a"));
        try
        {
            var profile = info.HasVideo ? VideoProfile(info, 0, MediaQuality.Medium) : MediaEncodingProfile.CreateM4a(AudioEncodingQuality.Medium);
            var destination = Wait(Wait(StorageFolder.GetFolderFromPathAsync(folder), token).CreateFileAsync(Path.GetFileName(target), CreationCollisionOption.FailIfExists), token);
            var prepared = Wait(new MediaTranscoder().PrepareFileTranscodeAsync(File(path, token), destination, profile), token);
            if (prepared.CanTranscode) return null;
            var codec = info.HasVideo ? info.VideoCodec : info.AudioCodec;
            return prepared.FailureReason == TranscodeFailureReason.CodecNotFound ? CodecHint(codec) : "Windows không xử lý được file này";
        }
        finally { try { System.IO.File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public static string CodecHint(string codec) => codec.ToUpperInvariant() switch
    {
        "HEVC" or "H265" => "Thiếu codec HEVC\nCài HEVC Video Extensions",
        "VP90" or "VP9" or "VP80" => "Thiếu codec VP9\nCài VP9 Video Extensions",
        "AV1" or "AV01" => "Thiếu codec AV1\nCài AV1 Video Extension",
        "" => "Thiếu codec\ncủa Windows",
        var other => $"Thiếu codec {other}"
    };

    public static string Execute(ImageRequest request, CancellationToken token, Action<int>? progress = null)
    {
        token.ThrowIfCancellationRequested(); progress?.Invoke(3);
        var tool = request.Tool;
        var sources = tool == ToolKind.AudioMerge ? (request.Sources ?? throw new ArgumentException("Chọn ít nhất hai file âm thanh để ghép.")).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray() : [request.Source];
        if (tool == ToolKind.AudioMerge && sources.Length is < 2 or > 128) throw new ArgumentException("Chọn từ 2 đến 128 file âm thanh.");
        var infos = new List<(string Path, FileKind Kind, MediaInfo Info)>();
        foreach (var path in sources)
        {
            var full = Path.GetFullPath(path);
            if (!System.IO.File.Exists(full)) throw new FileNotFoundException("Không tìm thấy file nguồn.", full);
            var kind = FileTypes.Detect(full);
            if (!FileTypes.IsMedia(kind)) throw new NotSupportedException("Nội dung file không phải audio/video được hỗ trợ.");
            var info = Probe(full, token); MediaLimits.CheckSource(new FileInfo(full).Length, info.Duration);
            infos.Add((full, kind, info));
        }
        var (source, sourceKind, media) = infos[0];
        var directory = Path.GetDirectoryName(source)!;
        var options = request.Media;
        string? temp = null; var scratch = new List<string>();
        try
        {
            progress?.Invoke(8);
            void Report(double fraction) => progress?.Invoke(10 + (int)(Math.Clamp(fraction, 0, 1) * 80));
            switch (tool)
            {
                case ToolKind.VideoMp4: case ToolKind.VideoCompress:
                {
                    if (!media.HasVideo) throw new NotSupportedException("File không có hình ảnh video.");
                    var profile = VideoProfile(media, tool == ToolKind.VideoCompress ? options?.Height ?? 720 : 0, tool == ToolKind.VideoCompress ? options?.Quality ?? MediaQuality.Medium : MediaQuality.High);
                    EnsureSpace(directory, Estimate(profile, media.Duration));
                    temp = Temp(directory, ".mp4"); Transcode(source, temp, profile, null, token, Report); break;
                }
                case ToolKind.AudioMp3: case ToolKind.AudioM4a: case ToolKind.AudioWav:
                {
                    if (!media.HasAudio) throw new NotSupportedException("File không có âm thanh.");
                    var profile = AudioProfile(tool); EnsureSpace(directory, Estimate(profile, media.Duration));
                    temp = Temp(directory, ToolCatalog.OutputName(request).Extension); Transcode(source, temp, profile, null, token, Report); break;
                }
                case ToolKind.MediaTrim:
                {
                    var range = MediaLimits.Range(options, media.Duration);
                    var extension = media.HasVideo ? ".mp4" : MediaLimits.TrimExtension(sourceKind);
                    request = request with { Media = (options ?? new()) with { Extension = extension } };
                    var profile = media.HasVideo ? VideoProfile(media, 0, MediaQuality.High) : AudioProfile(extension switch { ".mp3" => ToolKind.AudioMp3, ".wav" => ToolKind.AudioWav, _ => ToolKind.AudioM4a });
                    EnsureSpace(directory, Estimate(profile, range.End - range.Start));
                    temp = Temp(directory, extension); Transcode(source, temp, profile, range, token, Report); break;
                }
                case ToolKind.VideoFrame:
                {
                    if (!media.HasVideo) throw new NotSupportedException("File không có hình ảnh video.");
                    var at = options?.Start ?? TimeSpan.Zero;
                    if (at < TimeSpan.Zero || at >= media.Duration) throw new ArgumentException("Thời điểm nằm ngoài thời lượng video.");
                    var frame = Thumbnails(source, [at], media.Width, media.Height, token).Single(); Report(.7);
                    temp = Temp(directory, ".png");
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
                    using (var output = new FileStream(temp, FileMode.CreateNew)) encoder.Save(output);
                    break;
                }
                case ToolKind.VideoGif:
                {
                    if (!media.HasVideo) throw new NotSupportedException("File không có hình ảnh video.");
                    var range = MediaLimits.Range(options ?? new(End: MediaLimits.MaxGif), media.Duration, MediaLimits.MaxGif);
                    var fps = Math.Clamp(options?.GifFps ?? 12, 5, 20);
                    var width = Math.Min(Math.Clamp(options?.GifWidth ?? 480, 120, 720), media.Width);
                    var height = Math.Max(2, (int)Math.Round(width * (double)media.Height / media.Width));
                    var times = new List<TimeSpan>();
                    for (var t = range.Start; t < range.End; t += TimeSpan.FromSeconds(1d / fps)) times.Add(t);
                    var frames = Thumbnails(source, times, width, height, token); Report(.6);
                    temp = Temp(directory, ".gif");
                    using (var gif = new MagickImageCollection())
                    {
                        foreach (var frame in frames)
                        {
                            token.ThrowIfCancellationRequested();
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame)); using var png = new MemoryStream(); encoder.Save(png);
                            var image = new MagickImage(png.ToArray()) { AnimationDelay = (uint)Math.Round(100d / fps), GifDisposeMethod = GifDisposeMethod.None };
                            gif.Add(image);
                        }
                        gif[0].AnimationIterations = 0;
                        gif.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
                        gif.Optimize(); Report(.9);
                        gif.Write(temp, MagickFormat.Gif);
                    }
                    break;
                }
                case ToolKind.AudioMerge:
                {
                    if (infos.Any(i => !i.Info.HasAudio)) throw new NotSupportedException("Có file không chứa âm thanh.");
                    var extension = MediaLimits.MergeExtension(sourceKind);
                    request = request with { Source = source, Media = (options ?? new()) with { Extension = extension } };
                    var total = TimeSpan.FromTicks(infos.Sum(i => i.Info.Duration.Ticks));
                    if (total > MediaLimits.MaxDuration) throw new NotSupportedException("Tổng thời lượng ghép tối đa 1 giờ.");
                    var pcm = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High); pcm.Audio = AudioEncodingProperties.CreatePcm(44100, 2, 16);
                    EnsureSpace(directory, (long)(total.TotalSeconds * 176_400 * 2.2));
                    for (int i = 0; i < infos.Count; i++)
                    {
                        var part = Temp(directory, ".wav"); scratch.Add(part);
                        var index = i; Transcode(infos[i].Path, part, pcm, null, token, p => Report((index + p) / (infos.Count + 1d)));
                    }
                    var joined = Temp(directory, ".wav"); scratch.Add(joined);
                    WavFile.Concat(scratch.ToArray()[..^1], joined);
                    token.ThrowIfCancellationRequested();
                    if (extension == ".wav") { temp = joined; scratch.Remove(joined); }
                    else
                    {
                        temp = Temp(directory, extension);
                        Transcode(joined, temp, AudioProfile(extension == ".mp3" ? ToolKind.AudioMp3 : ToolKind.AudioM4a), null, token, p => Report((infos.Count + p) / (infos.Count + 1d)));
                    }
                    break;
                }
                default: throw new NotSupportedException("Công cụ chưa được hỗ trợ.");
            }
            token.ThrowIfCancellationRequested(); progress?.Invoke(95);
            var name = ToolCatalog.OutputName(request);
            var result = OutputFiles.Commit(temp, directory, name.Stem, name.Extension); temp = null;
            progress?.Invoke(100); return result;
        }
        finally
        {
            foreach (var path in scratch.Append(temp).OfType<string>())
                try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Temp(string directory, string extension) => Path.Combine(directory, $".orbit-{Guid.NewGuid():N}{extension}");

    public static MediaEncodingProfile VideoProfile(MediaInfo info, int targetHeight, MediaQuality quality)
    {
        var (width, height) = MediaLimits.Scale(info.Width, info.Height, targetHeight);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)width; profile.Video.Height = (uint)height;
        var fps = info.Fps is > 0 and <= 120 ? info.Fps : 30;
        profile.Video.FrameRate.Numerator = (uint)Math.Round(fps * 1000); profile.Video.FrameRate.Denominator = 1000;
        profile.Video.PixelAspectRatio.Numerator = 1; profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Video.Bitrate = MediaLimits.VideoBitrate(width, height, fps, quality);
        if (!info.HasAudio) profile.Audio = null;
        else profile.Audio.Bitrate = quality == MediaQuality.Low ? 96_000u : quality == MediaQuality.Medium ? 128_000u : 192_000u;
        return profile;
    }
    private static MediaEncodingProfile AudioProfile(ToolKind tool) => tool switch
    {
        ToolKind.AudioMp3 => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
        ToolKind.AudioWav => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
        _ => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High)
    };
    private static long Estimate(MediaEncodingProfile profile, TimeSpan duration)
    {
        double bitsPerSecond = (profile.Video?.Bitrate ?? 0) + (profile.Audio == null ? 0 : profile.Audio.Subtype == MediaEncodingSubtypes.Pcm ? profile.Audio.SampleRate * profile.Audio.ChannelCount * Math.Max(16u, profile.Audio.BitsPerSample) : Math.Max(profile.Audio.Bitrate, 128_000u));
        return (long)(bitsPerSecond / 8 * duration.TotalSeconds * 1.15);
    }
    private static void EnsureSpace(string directory, long bytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!);
        if (drive.IsReady && drive.AvailableFreeSpace < bytes + 200L * 1024 * 1024)
            throw new IOException($"Ổ đĩa {drive.Name} không đủ chỗ: cần khoảng {bytes / 1048576d:0} MB, còn {drive.AvailableFreeSpace / 1048576d:0} MB.");
    }

    private static void Transcode(string source, string target, MediaEncodingProfile profile, (TimeSpan Start, TimeSpan End)? range, CancellationToken token, Action<double> progress)
    {
        var folder = Wait(StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(target)!), token);
        var destination = Wait(folder.CreateFileAsync(Path.GetFileName(target), CreationCollisionOption.FailIfExists), token);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true, AlwaysReencode = true };
        if (range is var (start, end))
        {
            var duration = Probe(source, token).Duration;
            transcoder.TrimStartTime = start;
            // TrimStopTime is the absolute source time at which output ends (verified by the trim tests), not an amount cut from the end.
            if (end < duration) transcoder.TrimStopTime = end;
        }
        var prepared = Wait(transcoder.PrepareFileTranscodeAsync(File(source, token), destination, profile), token);
        if (!prepared.CanTranscode)
            throw new NotSupportedException(prepared.FailureReason == TranscodeFailureReason.CodecNotFound ? "Thiếu codec của Windows để xử lý file này." : "Windows không chuyển đổi được file này (" + prepared.FailureReason + ").");
        prepared.TranscodeAsync().AsTask(token, new Progress<double>(p => progress(p / 100))).GetAwaiter().GetResult();
    }

    // Editor preview: one composition reused for every scrub, one decode at a time.
    public sealed class Preview(string source)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private MediaComposition? _composition;
        public async Task<BitmapSource> FrameAsync(TimeSpan time, int width, int height, CancellationToken token)
        {
            await _gate.WaitAsync(token);
            try
            {
                return await Task.Run(() =>
                {
                    if (_composition == null)
                    {
                        var composition = new MediaComposition(); composition.Clips.Add(Wait(MediaClip.CreateFromFileAsync(File(source, token)), token)); _composition = composition;
                    }
                    using var stream = Wait(_composition.GetThumbnailAsync(time, width, height, VideoFramePrecision.NearestFrame), token);
                    using var input = stream.AsStreamForRead();
                    var decoded = BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; decoded.Freeze(); return (BitmapSource)decoded;
                }, token);
            }
            finally { _gate.Release(); }
        }
    }

    // Decoded frames at exact times, via a one-clip composition.
    public static IReadOnlyList<BitmapSource> Thumbnails(string source, IReadOnlyList<TimeSpan> times, int width, int height, CancellationToken token)
    {
        var clip = Wait(MediaClip.CreateFromFileAsync(File(source, token)), token);
        var composition = new MediaComposition(); composition.Clips.Add(clip);
        var result = new List<BitmapSource>(times.Count);
        // Batches keep memory bounded for long GIFs.
        foreach (var batch in times.Chunk(30))
        {
            token.ThrowIfCancellationRequested();
            var streams = Wait(composition.GetThumbnailsAsync(batch, width, height, VideoFramePrecision.NearestFrame), token);
            foreach (var stream in streams)
            {
                using var input = stream.AsStreamForRead();
                var decoded = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                decoded.Freeze(); result.Add(decoded);
            }
        }
        return result;
    }
}
