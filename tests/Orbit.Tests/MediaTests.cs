using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using Orbit.App;
using Orbit.App.Services;
using Orbit.App.UI;
using Orbit.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

internal static partial class Program
{
    private static string? _video, _tone;
    private static bool _mediaAvailable;

    // Fixtures come from Windows itself: 1 s red + 1 s blue at 320×240 with a 440 Hz tone.
    private static string MakeVideo(string name, double redSeconds, double blueSeconds, string? audio)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 255, 0, 0), TimeSpan.FromSeconds(redSeconds)));
        composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 0, 0, 255), TimeSpan.FromSeconds(blueSeconds)));
        if (audio != null) composition.BackgroundAudioTracks.Add(BackgroundAudioTrack.CreateFromFileAsync(StorageFile.GetFileFromPathAsync(audio).AsTask().GetAwaiter().GetResult()).AsTask().GetAwaiter().GetResult());
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
        profile.Video.Width = 320; profile.Video.Height = 240; profile.Video.FrameRate.Numerator = 30; profile.Video.FrameRate.Denominator = 1; profile.Video.Bitrate = 800_000;
        if (audio == null) profile.Audio = null;
        var folder = StorageFolder.GetFolderFromPathAsync(_folder).AsTask().GetAwaiter().GetResult();
        var file = folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting).AsTask().GetAwaiter().GetResult();
        composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, profile).AsTask().GetAwaiter().GetResult();
        return Path.Combine(_folder, name);
    }
    private static string WriteTone(string name, double seconds, double hz = 440)
    {
        var path = Path.Combine(_folder, name); var samples = (int)(44100 * seconds);
        using var writer = new BinaryWriter(File.Create(path));
        WavFile.WriteHeader(writer, 2, 44100, 16, (uint)(samples * 4));
        for (int i = 0; i < samples; i++) { var value = (short)(Math.Sin(2 * Math.PI * hz * i / 44100) * 8000); writer.Write(value); writer.Write(value); }
        return path;
    }
    private static bool Close(TimeSpan actual, double seconds, double tolerance = .2) => Math.Abs(actual.TotalSeconds - seconds) <= tolerance;
    private static (byte R, byte G, byte B) Center(string png)
    { var p = Pixel(png, Size(png).Item1 / 2, Size(png).Item2 / 2); return (p.R, p.G, p.B); }

    private static void CheckMedia()
    {
        Check("Media detection, time fields, limits and menus", () =>
        {
            static string Bytes(string name, byte[] head) { var path = Path.Combine(_folder, name); File.WriteAllBytes(path, head.Concat(new byte[64]).ToArray()); return path; }
            Assert(FileTypes.Detect(Bytes("clip.mkv", [0x1a, 0x45, 0xdf, 0xa3])) == FileKind.Video, "Matroska");
            Assert(FileTypes.Detect(Bytes("clip.avi", "RIFF\0\0\0\0AVI "u8.ToArray())) == FileKind.Video, "AVI");
            Assert(FileTypes.Detect(Bytes("song.mp3", "ID3"u8.ToArray())) == FileKind.Mp3, "MP3 with ID3");
            Assert(FileTypes.Detect(Bytes("raw.mp3", [0xff, 0xfb, 0x90])) == FileKind.Mp3 && FileTypes.Detect(Bytes("raw.bin", [0xff, 0xfb, 0x90])) == FileKind.Unknown, "bare MPEG frames need .mp3");
            Assert(FileTypes.Detect(Bytes("lossless.flac", "fLaC"u8.ToArray())) == FileKind.Audio && FileTypes.Detect(Bytes("voice.wma", [0x30, 0x26, 0xb2, 0x75, 0x8e, 0x66, 0xcf, 0x11])) == FileKind.Audio, "FLAC and WMA");
            Assert(FileTypes.Detect(Bytes("voice.m4a", [0, 0, 0, 0x18, .. "ftypM4A "u8.ToArray(), 0, 0, 0, 0, .. "isom"u8.ToArray()])) == FileKind.M4a, "M4A brand");
            Assert(FileTypes.Detect(Bytes("movie.mov", [0, 0, 0, 0x14, .. "ftypqt  "u8.ToArray(), 0, 0, 0, 0, .. "qt  "u8.ToArray()])) == FileKind.Video, "QuickTime");
            Assert(MediaTime.TryParse("1:05.5", out var t) && t == TimeSpan.FromSeconds(65.5) && MediaTime.TryParse("65,25", out t) && t == TimeSpan.FromSeconds(65.25), "time parsing");
            Assert(!MediaTime.TryParse("1:75", out _) && !MediaTime.TryParse("abc", out _) && MediaTime.Format(TimeSpan.FromSeconds(65.5)) == "1:05.500", "time validation and format");
            Assert(MediaLimits.Scale(1920, 1080, 720) == (1280, 720) && MediaLimits.Scale(1080, 1920, 720) == (720, 1280) && MediaLimits.Scale(640, 360, 720) == (640, 360), "short-side scaling never upscales");
            Throws<NotSupportedException>(() => MediaLimits.CheckSource(MediaLimits.MaxBytes + 1, TimeSpan.FromSeconds(1)));
            Throws<NotSupportedException>(() => MediaLimits.CheckSource(1, TimeSpan.FromMinutes(61)));
            Throws<ArgumentException>(() => MediaLimits.Range(new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(60), MediaLimits.MaxGif));
            var video = ToolCatalog.ForFiles([FileKind.Mp4]);
            Assert(!video.Contains(ToolKind.VideoMp4) && video.Contains(ToolKind.MediaTrim) && video.Contains(ToolKind.VideoGif) && video.Contains(ToolKind.AudioMp3), "single MP4 menu");
            Assert(ToolCatalog.ForFiles([FileKind.Video]).Contains(ToolKind.VideoMp4), "MOV/MKV offers MP4");
            var songs = ToolCatalog.ForFiles([FileKind.Mp3, FileKind.Wav]);
            Assert(songs.Contains(ToolKind.AudioMerge) && songs.Contains(ToolKind.AudioM4a) && !songs.Contains(ToolKind.AudioMp3) && !songs.Contains(ToolKind.MediaTrim), "audio batch menu");
            Assert(ToolCatalog.ForFiles([FileKind.Video], mediaReady: false).SequenceEqual([ToolKind.Zip, ToolKind.Sha256]), "missing codec keeps only generic tools");
        });
        Check("WAV concatenation keeps PCM data and rejects mixed formats", () =>
        {
            var a = WriteTone("tone-a.wav", .5); var b = WriteTone("tone-b.wav", .25, 660); var joined = Path.Combine(_folder, "tone-joined.wav");
            WavFile.Concat([a, b], joined);
            Assert(Close(WavFile.Read(joined).Duration, .75, .001), "0.5 s + 0.25 s");
            var mono = Path.Combine(_folder, "mono.wav");
            using (var writer = new BinaryWriter(File.Create(mono))) { WavFile.WriteHeader(writer, 1, 44100, 16, 200); writer.Write(new byte[200]); }
            Throws<InvalidDataException>(() => WavFile.Concat([a, mono], Path.Combine(_folder, "mixed.wav")));
        });
        try { _tone = WriteTone("tone-2s.wav", 2); _video = MakeVideo("red-blue.mp4", 1, 1, _tone); _mediaAvailable = File.Exists(_video) && new FileInfo(_video).Length > 1000; }
        catch (Exception error) { Console.WriteLine("SKIP Windows media pipeline unavailable: " + error.Message); }
        if (!_mediaAvailable) return;
        var video = _video!; var before = Hash(video);
        MediaInfo info = null!;
        Check("Windows decodes the generated fixture: duration, size, tracks", () =>
        {
            Assert(FileTypes.Detect(video) == FileKind.Mp4, "MP4 detected");
            info = MediaEngine.Probe(video);
            Assert(info.HasVideo && info.HasAudio && info.Width == 320 && info.Height == 240 && Close(info.Duration, 2), $"probe {info}");
            Assert(MediaEngine.MissingCodec(video, info) == null, "H.264/AAC chain available");
            var resolved = FileToolResolver.ResolveWithNotice([video]);
            Assert(resolved.Notice == null && resolved.Tools.Contains(ToolKind.VideoCompress), "menu offers video tools");
            var bogus = Path.Combine(_folder, "not-really.mkv"); File.WriteAllBytes(bogus, new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }.Concat(new byte[4096]).ToArray());
            var refused = FileToolResolver.ResolveWithNotice([bogus]);
            Assert(refused.Notice != null && refused.Tools.SequenceEqual([ToolKind.Zip, ToolKind.Sha256]), "undecodable media shows a notice and generic tools only");
        });
        Check("Video converts to MP4 and compresses to the chosen short side", () =>
        {
            var mp4 = MediaEngine.Execute(new(video, ToolKind.VideoMp4), CancellationToken.None);
            var converted = MediaEngine.Probe(mp4);
            Assert(Path.GetFileName(mp4) == "red-blue (1).mp4" && converted.Width == 320 && Close(converted.Duration, 2), "same size, numbered name, full length");
            var small = MediaEngine.Execute(new(video, ToolKind.VideoCompress, Media: new(Height: 180, Quality: MediaQuality.Low)), CancellationToken.None);
            var compressed = MediaEngine.Probe(small);
            Assert(small.EndsWith("_small.mp4") && compressed.Width == 240 && compressed.Height == 180 && compressed.HasAudio, "240×180 with audio");
        });
        Check("Audio is extracted or converted to M4A, MP3 and WAV", () =>
        {
            var m4a = MediaEngine.Execute(new(video, ToolKind.AudioM4a), CancellationToken.None);
            var extracted = MediaEngine.Probe(m4a);
            Assert(FileTypes.Detect(m4a) == FileKind.M4a && !extracted.HasVideo && extracted.HasAudio && Close(extracted.Duration, 2), $"M4A from video: {FileTypes.Detect(m4a)} {extracted}");
            var mp3 = MediaEngine.Execute(new(_tone!, ToolKind.AudioMp3), CancellationToken.None);
            Assert(FileTypes.Detect(mp3) == FileKind.Mp3 && Close(MediaEngine.Probe(mp3).Duration, 2), "MP3 from WAV");
            var wav = MediaEngine.Execute(new(mp3, ToolKind.AudioWav), CancellationToken.None);
            Assert(FileTypes.Detect(wav) == FileKind.Wav && Close(WavFile.Read(wav).Duration, 2, .15), "WAV from MP3");
            Throws<NotSupportedException>(() => MediaEngine.Execute(new(m4a, ToolKind.VideoCompress), CancellationToken.None));
        });
        Check("Frames: PNG at the requested time, trim keeps the selected seconds", () =>
        {
            var red = MediaEngine.Execute(new(video, ToolKind.VideoFrame, Media: new(Start: TimeSpan.FromSeconds(.5))), CancellationToken.None);
            var blue = MediaEngine.Execute(new(video, ToolKind.VideoFrame, Media: new(Start: TimeSpan.FromSeconds(1.5))), CancellationToken.None);
            Assert(Size(red) == (320, 240) && FileTypes.Detect(red) == FileKind.Png, "full-size PNG");
            var r = Center(red); var b = Center(blue);
            Assert(r.R > 200 && r.B < 60 && b.B > 200 && b.R < 60, $"red at 0.5 s {r}, blue at 1.5 s {b}");
            var cut = MediaEngine.Execute(new(video, ToolKind.MediaTrim, Media: new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))), CancellationToken.None);
            var kept = MediaEngine.Probe(cut);
            Assert(cut.EndsWith("_cut.mp4") && Close(kept.Duration, 1), $"one second kept: {kept.Duration}");
            var first = MediaEngine.Execute(new(cut, ToolKind.VideoFrame, Media: new(Start: TimeSpan.FromSeconds(.3))), CancellationToken.None);
            Assert(Center(first).B > 200, "cut starts in the blue half");
            var song = MediaEngine.Execute(new(_tone!, ToolKind.MediaTrim, Media: new(TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(1.5))), CancellationToken.None);
            Assert(song.EndsWith("_cut.wav") && Close(WavFile.Read(song).Duration, 1, .1), "WAV trim keeps format");
            Throws<ArgumentException>(() => MediaEngine.Execute(new(video, ToolKind.MediaTrim, Media: new(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1.55))), CancellationToken.None));
        });
        Check("GIF holds the chosen range, size and frame rate", () =>
        {
            var gif = MediaEngine.Execute(new(video, ToolKind.VideoGif, Media: new(TimeSpan.Zero, TimeSpan.FromSeconds(2), GifFps: 10, GifWidth: 160)), CancellationToken.None);
            using var frames = new MagickImageCollection(gif);
            Assert(FileTypes.Detect(gif) == FileKind.Gif && frames.Count is >= 18 and <= 20 && frames[0].Width == 160, $"{frames.Count} frames");
            frames.Coalesce();
            var start = frames[0].GetPixels().GetPixel(80, 60).ToColor()!; var end = frames[^1].GetPixels().GetPixel(80, 60).ToColor()!;
            Assert(start.R > 200 && start.B < 80 && end.B > 200 && end.R < 80, "red to blue");
        });
        Check("Audio merge joins in file-name order and keeps the first format", () =>
        {
            var a = WriteTone("merge-1.wav", 1); var b = WriteTone("merge-2.wav", .5, 660);
            var wav = MediaEngine.Execute(new(b, ToolKind.AudioMerge, Sources: [b, a]), CancellationToken.None);
            Assert(Path.GetFileName(wav) == "merge-1_merged.wav" && Close(WavFile.Read(wav).Duration, 1.5, .05), "WAV merge 1 + 0.5 s");
            var m1 = MediaEngine.Execute(new(a, ToolKind.AudioMp3), CancellationToken.None); var m2 = MediaEngine.Execute(new(b, ToolKind.AudioMp3), CancellationToken.None);
            var mp3 = MediaEngine.Execute(new(m1, ToolKind.AudioMerge, Sources: [m1, m2]), CancellationToken.None);
            Assert(mp3.EndsWith("_merged.mp3") && Close(MediaEngine.Probe(mp3).Duration, 1.5, .25), "MP3 merge");
        });
        Check("Cancelled media job leaves no partial file; sources unchanged", () =>
        {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { MediaEngine.Execute(new(video, ToolKind.VideoMp4), cancel.Token); throw new Exception("expected cancellation"); } catch (OperationCanceledException) { }
            using var soon = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            try { MediaEngine.Execute(new(video, ToolKind.VideoCompress, Media: new(Height: 120)), soon.Token); } catch (OperationCanceledException) { }
            Assert(!Directory.EnumerateFiles(_folder, ".orbit-*").Any(), "no staging files");
            Assert(Hash(video) == before, "source video unchanged");
        });
    }

    private static async Task CheckMediaEditor(OrbitApplication app)
    {
        if (!_mediaAvailable) { Console.WriteLine("SKIP Media editor (Windows media pipeline unavailable)"); return; }
        var info = MediaEngine.Probe(_video!);
        var editor = new MediaEditorWindow(app, ToolKind.MediaTrim, [_video!], info);
        editor.Show(); editor.UpdateLayout();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (editor.PreviewFrame == null && DateTime.UtcNow < deadline) await Task.Delay(50);
            editor.Timeline.SetRange(TimeSpan.FromSeconds(.4), TimeSpan.FromSeconds(1.6)); editor.Timeline.SetPosition(TimeSpan.FromSeconds(1.2)); await Task.Delay(500); editor.UpdateLayout();
            { var content = (FrameworkElement)editor.Content; var shot = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); shot.Render(content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(shot)); using var file = File.Create(Path.Combine(_folder, "media-editor.png")); png.Save(file); }
            editor.Timeline.SetRange(TimeSpan.Zero, info.Duration);
            Check("Media editor shows a decoded frame and a full-length range", () =>
                Assert(editor.PreviewFrame != null && editor.Timeline.Start == TimeSpan.Zero && Close(editor.Timeline.End, 2), "preview frame and default range"));
            var fields = Descendants<TextBox>(editor).ToDictionary(System.Windows.Automation.AutomationProperties.GetName);
            fields["Bắt đầu"].Text = "0:00.5"; fields["Kết thúc"].Text = "nope";
            fields["Kết thúc"].RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(fields["Kết thúc"])!, 0, System.Windows.Input.Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
            Check("Media editor rejects invalid times", () => Assert(Descendants<TextBlock>(editor).Any(t => t.Text.StartsWith("Kết thúc: nhập thời gian")), "error shown"));
            fields["Kết thúc"].Text = "1.5";
            fields["Kết thúc"].RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(fields["Kết thúc"])!, 0, System.Windows.Input.Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
            Check("Typed times move the range handles", () => Assert(editor.Timeline.Start == TimeSpan.FromSeconds(.5) && editor.Timeline.End == TimeSpan.FromSeconds(1.5), "0.5–1.5 s"));
            Descendants<Button>(editor).First(b => Equals(b.Content, "Lưu bản sao")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { if (editor.IsVisible) editor.Close(); }
        var finish = DateTime.UtcNow.AddSeconds(30);
        while (app.Queue.HasPending && DateTime.UtcNow < finish) await Task.Delay(50);
        Check("Media editor Save runs the trim on the media worker", () =>
        {
            var job = app.Queue.Jobs.Single(); Assert(job.State == JobState.Completed, "trim job: " + job.Status);
            Assert(Close(MediaEngine.Probe(job.Output!).Duration, 1), "one second exported: " + MediaEngine.Probe(job.Output!).Duration);
        });
        app.Queue.Jobs.Clear();
        var compress = new MediaEditorWindow(app, ToolKind.VideoCompress, [_video!], info);
        compress.Show(); compress.UpdateLayout();
        Check("Compress editor estimates output from the preset", () => Assert(Descendants<TextBlock>(compress).Any(t => t.Text.StartsWith("Đầu ra · 320 × 240 px")), "source below 480p keeps size by default"));
        compress.Close();
    }
}
