using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.App.Imaging;
using Orbit.App.Services;
using Orbit.Core;
using Orbit.App;
using System.Windows.Controls;

internal static partial class Program
{
    private static int _passed, _failed;
    private static string _folder = "";
    private static readonly ImageEngine Engine = new();
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--editor-preview")
        {
            Environment.SetEnvironmentVariable("ORBIT_DATA_DIR", Path.Combine(Path.GetTempPath(), "Orbit-editor-preview"));
            var app = new OrbitApplication(["--no-integrations"]);
            app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                app.MainWindow.Hide();
                var editor = new Orbit.App.UI.EditorWindow(app, Enum.Parse<ToolKind>(args[2], true), [args[1]], Engine.Load(args[1]));
                editor.Closed += (_, _) => { if (!app.Queue.HasPending) app.Shutdown(); else app.ShowHome(); };
                editor.Show(); editor.Activate();
            });
            return app.Run();
        }
        if (args.Length == 3 && args[0] == "--frame-samples") return FrameSamples(args[1], args[2]);
        if (args.Length == 3 && args[0] == "--ocr-file") return DiagnoseOcr(args[1], args[2]);
        if (args.Length == 3 && args[0] == "--ocr-variants") return DiagnoseOcrVariants(args[1], args[2]);
        _folder = Path.Combine(args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.CurrentDirectory, "artifacts", "tests"), DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(_folder);
        var source = WriteImage("ảnh nguồn.png", 80, 60);
        var hash = Hash(source);
        Check("Radial sectors, neutral center, gaps and bounds", () =>
        {
            Assert(RadialGeometry.HitTest(200, 200) == -1, "center");
            Assert(RadialGeometry.HitTest(200, 0) == -1, "outside");
            for (int i = 0; i < 6; i++)
            {
                var p = RadialGeometry.Point(130, -90 + i * 60); Assert(RadialGeometry.HitTest(p.X, p.Y) == i, "sector " + i);
                p = RadialGeometry.Point(130, -60 + i * 60); Assert(RadialGeometry.HitTest(p.X, p.Y) == -1, "gap " + i);
            }
        });
        Check("PNG does not overwrite a same-format source", () =>
        {
            var output = Engine.Execute(new(source, ToolKind.Png), CancellationToken.None);
            Assert(output != source && File.Exists(output), "unique output");
            Assert(Hash(output).Length > 0, "readable output"); Assert(Size(output) == (80, 60), "dimensions");
            Assert(ReadPixels(source).SequenceEqual(ReadPixels(output)), "lossless pixel preservation");
        });
        Check("JPEG to PNG preserves decoded pixels", () =>
        {
            var jpeg = Engine.Execute(new(source, ToolKind.Jpeg, Quality: 90), CancellationToken.None);
            var png = Engine.Execute(new(jpeg, ToolKind.Png), CancellationToken.None);
            Assert(ReadPixels(jpeg).SequenceEqual(ReadPixels(png)), "JPEG decoded values unchanged");
            Assert(File.ReadAllBytes(png).Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}), "PNG signature");
        });
        Check("Resize fits bounds and keeps aspect ratio", () =>
        { var output = Engine.Execute(new(source, ToolKind.Resize, 40, 40), CancellationToken.None); Assert(Size(output) == (40, 30), "fit size"); });
        Check("Resize supports explicit width and height", () =>
        { var output = Engine.Execute(new(source, ToolKind.Resize, 23, 31, false), CancellationToken.None); Assert(Size(output) == (23, 31), "exact size"); });
        Check("Crop uses source pixel coordinates", () =>
        {
            var output = Engine.Execute(new(source, ToolKind.Crop, Crop: new(7, 9, 20, 13)), CancellationToken.None);
            Assert(Size(output) == (20, 13), "crop dimensions");
            Assert(ReadPixels(output).Take(4).SequenceEqual(ReadPixels(source).Skip((9 * 80 + 7) * 4).Take(4)), "crop origin");
        });
        Check("Crop rejects out-of-bounds and integer overflow", () =>
        {
            Throws<ArgumentException>(() => Engine.Execute(new(source, ToolKind.Crop, Crop: new(70, 50, 20, 20)), CancellationToken.None));
            Throws<ArgumentException>(() => Engine.Execute(new(source, ToolKind.Crop, Crop: new(int.MaxValue, 0, 20, 20)), CancellationToken.None));
        });
        Check("Rotation swaps dimensions", () =>
        { var output = Engine.Execute(new(source, ToolKind.Rotate), CancellationToken.None); Assert(Size(output) == (60, 80), "rotation"); });
        Check("EXIF orientation is normalized and not reapplied", () =>
        {
            var image = Engine.Load(source); var metadata = new BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
            var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
            var oriented = Path.Combine(_folder, "oriented.jpg"); using (var file = File.Create(oriented)) encoder.Save(file);
            Assert(Size(oriented) == (60, 80), "EXIF read");
            var output = Engine.Execute(new(oriented, ToolKind.Png), CancellationToken.None); Assert(Size(output) == (60, 80), "normalized once");
        });
        Check("JPEG flattens transparency onto white", () =>
        {
            var transparent = WriteImage("transparent.png", 30, 20, true);
            var output = Engine.Execute(new(transparent, ToolKind.Jpeg, Quality: 95), CancellationToken.None);
            Assert(ReadPixels(output).Take(3).All(c => c >= 248), "white matte");
        });
        Check("All mirrored EXIF orientations map pixels correctly", () =>
        {
            byte[] pixels = [0,0,10,255, 0,0,20,255, 0,0,30,255, 0,0,40,255, 0,0,50,255, 0,0,60,255];
            var bitmap = BitmapSource.Create(2, 3, 96, 96, PixelFormats.Bgra32, null, pixels, 8); bitmap.Freeze();
            var expected = new Dictionary<ushort, byte[]>
            {
                [2] = [20,10,40,30,60,50], [3] = [60,50,40,30,20,10], [4] = [50,60,30,40,10,20],
                [5] = [10,30,50,20,40,60], [6] = [50,30,10,60,40,20], [7] = [60,40,20,50,30,10], [8] = [20,40,60,10,30,50]
            };
            foreach (var item in expected)
            {
                var oriented = ImageEngine.Orient(bitmap, item.Key); var bytes = new byte[24]; oriented.CopyPixels(bytes, oriented.PixelWidth * 4, 0);
                var red = Enumerable.Range(0, 6).Select(i => bytes[i * 4 + 2]); Assert(red.SequenceEqual(item.Value), "orientation " + item.Key);
            }
        });
        Check("APNG is rejected even when WIC exposes one frame", () =>
        {
            var png = File.ReadAllBytes(source); var animated = Path.Combine(_folder, "animated.png");
            using (var output = File.Create(animated))
            {
                output.Write(png.AsSpan(0, 33));
                WriteChunk(output, "acTL", [0,0,0,1,0,0,0,0]);
                byte[] control = new byte[26]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(4, 4), 80); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(8, 4), 60); control[21] = 1; control[23] = 10;
                WriteChunk(output, "fcTL", control); output.Write(png.AsSpan(33));
            }
            Throws<NotSupportedException>(() => Engine.Load(animated));
        });
        Check("Compression produces JPEG and valid dimensions", () =>
        {
            var output = Engine.Execute(new(source, ToolKind.Compress, Quality: 40), CancellationToken.None);
            Assert(Size(output) == (80, 60), "compression dimensions");
            Assert(File.ReadAllBytes(output).Take(2).SequenceEqual(new byte[] {255,216}), "JPEG signature");
        });
        Check("Multi-frame images are rejected explicitly", () =>
        {
            var encoder = new TiffBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Engine.Load(source))); encoder.Frames.Add(BitmapFrame.Create(Engine.Load(source)));
            var multi = Path.Combine(_folder, "multi.tiff"); using (var output = File.Create(multi)) encoder.Save(output);
            Throws<NotSupportedException>(() => Engine.Load(multi));
        });
        Check("Cancellation leaves no output or temporary file", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var before = Directory.GetFiles(_folder).Length;
            Throws<OperationCanceledException>(() => Engine.Execute(new(source, ToolKind.Png), cancelled.Token));
            Assert(Directory.GetFiles(_folder).Length == before, "nothing written");
            using var during = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => Engine.Execute(new(source, ToolKind.Png), during.Token, p => { if (p == 90) during.Cancel(); }));
            Assert(!Directory.GetFiles(_folder, ".orbit-*.tmp").Any(), "temporary cleanup");
        });
        Check("Atomic commit handles concurrent name collisions", () =>
        {
            Parallel.For(0, 12, i => { var temp = Path.Combine(_folder, $"commit-{i}.tmp"); File.WriteAllText(temp, i.ToString()); OutputFiles.Commit(temp, _folder, "race", ".txt"); });
            var files = Directory.GetFiles(_folder, "race*.txt"); Assert(files.Length == 12, "all outputs exist");
            Assert(files.Select(File.ReadAllText).Distinct().Count() == 12, "all data retained");
        });
        Check("Corrupt input fails without writing a result", () =>
        {
            var bad = Path.Combine(_folder, "corrupt.jpg"); File.WriteAllText(bad, "This is not an image");
            Throws<Exception>(() => Engine.Execute(new(bad, ToolKind.Png), CancellationToken.None));
            Assert(!File.Exists(Path.ChangeExtension(bad, ".png")), "no false output");
        });
        Check("Queue continues after an invalid image", () =>
        {
            using var queue = new JobQueue(Dispatcher.CurrentDispatcher);
            queue.Add([new(Path.Combine(_folder, "missing.jpg"), ToolKind.Png), new(source, ToolKind.Png)]);
            var frame = new DispatcherFrame(); var start = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { if (!queue.HasPending || DateTime.UtcNow - start > TimeSpan.FromSeconds(10)) { timer.Stop(); frame.Continue = false; } };
            timer.Start(); Dispatcher.PushFrame(frame);
            Assert(queue.Jobs.Count(j => j.State == JobState.Completed) == 1, "valid job completed");
            Assert(queue.Jobs.Count(j => j.State == JobState.Failed) == 1, "invalid job reported");
        });
        Check("Original source remains byte-identical", () => Assert(Hash(source) == hash, "original hash"));
        Check("Invalid and excessive resize dimensions are rejected", () =>
        {
            Throws<ArgumentException>(() => Engine.Execute(new(source, ToolKind.Resize, 0, 100), CancellationToken.None));
            Throws<ArgumentException>(() => Engine.Execute(new(source, ToolKind.Resize, 32768, 32768), CancellationToken.None));
        });
        CheckExtended(source, args.Contains("--office"));
        CheckFrames(source);
        CheckTextExtraction(args.Contains("--office"));
        CheckLongOcr();
        CheckUpdates();
        Check("Live WPF menu detection, circular alpha and PNG completion", () => CheckLiveUi(source));
        var report = $"Passed: {_passed}; Failed: {_failed}\nArtifacts: {_folder}";
        File.WriteAllText(Path.Combine(_folder, "results.txt"), report); Console.WriteLine(report); return _failed == 0 ? 0 : 1;
    }
    private static void Check(string name, Action check) { try { check(); _passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { _failed++; Console.WriteLine("FAIL " + name + "\n" + e); } }
    private static void CheckLiveUi(string source)
    {
        var previousData = Environment.GetEnvironmentVariable("ORBIT_DATA_DIR");
        Environment.SetEnvironmentVariable("ORBIT_DATA_DIR", Path.Combine(_folder, "ui-settings"));
        var app = new OrbitApplication(["--no-integrations"]);
        Exception? failure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        app.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; timer.Stop(); app.Shutdown(1); };
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                Check("First-run startup choice persists, can retry errors and respects decline", () => CheckStartupUi(app));
                Check("Hotkeys require exact modifiers and dragging; legacy preferences migrate", CheckHotkeyRules);
                Check("Settings saves Shift+Space, updates instructions and preserves cancel", () => CheckHotkeySettingsUi(app));
                await CheckEditors(app);
                await CheckFrameEditor(app);
                await CheckHistory(app);
                app.Menu.ShowMenu(new Orbit.App.Interop.Native.Point(700, 420), [Path.Combine(_folder, "text-layer.pdf")]);
                await app.Menu.Detection;
                Assert(app.Menu.VisibleTools.Contains(ToolKind.PdfWord), "text PDF offers Word in real WPF menu");
                app.Menu.HideMenu();
                app.Menu.ShowMenu(new Orbit.App.Interop.Native.Point(700, 420), [Path.Combine(_folder, "ocr-vietnamese.pdf")]);
                await app.Menu.Detection;
                Assert(!app.Menu.VisibleTools.Contains(ToolKind.PdfWord) && app.Menu.VisibleTools.Contains(ToolKind.PdfText), "scan offers TXT, hides Word");
                app.Menu.HideMenu();
                var jpeg = Engine.Execute(new(source, ToolKind.Jpeg), CancellationToken.None);
                app.Menu.ShowMenu(new Orbit.App.Interop.Native.Point(700, 420), [jpeg]);
                await app.Menu.Detection;
                app.Menu.UpdateLayout();
                Assert(Orbit.App.Interop.Native.HasCircularRegion(app.Menu), "native region excludes all menu corners");
                Assert(!app.Menu.VisibleTools.Contains(ToolKind.Jpeg) && app.Menu.VisibleTools.Contains(ToolKind.Webp), "JPEG-adaptive menu");
                var render = new RenderTargetBitmap(400, 400, 96, 96, PixelFormats.Pbgra32); render.Render((Visual)app.Menu.Content);
                var alpha = new byte[400 * 400 * 4]; render.CopyPixels(alpha, 1600, 0);
                for (int y = 0; y < 400; y++) for (int x = 0; x < 400; x++)
                    if (Math.Sqrt((x - 199.5) * (x - 199.5) + (y - 199.5) * (y - 199.5)) > 192)
                        Assert(alpha[(y * 400 + x) * 4 + 3] == 0, "no painted pixels outside circle");
                Descendants<Button>(app.Menu).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                app.RunTool(ToolKind.Png, [Path.Combine(_folder, "missing-ui.jpg")]);
                app.MainWindow.UpdateLayout();
                timer.Start();
            }
            catch (Exception error) { failure = error; app.Shutdown(1); }
        }));
        timer.Tick += (_, _) =>
        {
            try
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("UI conversion did not complete");
                if (app.Queue.HasPending) return;
                app.MainWindow.UpdateLayout();
                var completed = app.Queue.Jobs.Single(j => j.State == JobState.Completed);
                Assert(File.Exists(completed.Output) && Size(completed.Output!) == (80, 60), "real PNG output");
                Assert(app.Queue.Jobs.Single(j => j.State == JobState.Failed).Status.StartsWith("Lỗi"), "failed row displayed");
                var bars = Descendants<ProgressBar>(app.MainWindow).ToArray();
                Assert(bars.Length == 2, "both WPF job templates materialized");
                Assert(bars.Single(b => ReferenceEquals(b.DataContext, completed)).Value == 100, "completed UI progress");
                timer.Stop(); app.Shutdown();
            }
            catch (Exception error) { failure = error; timer.Stop(); app.Shutdown(1); }
        };
        try { app.Run(); if (failure != null) throw new InvalidOperationException("WPF integration failed", failure); }
        finally { timer.Stop(); Environment.SetEnvironmentVariable("ORBIT_DATA_DIR", previousData); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length); stream.Write(length);
        var bytes = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); stream.Write(bytes);
        uint crc = 0xffffffff;
        foreach (var b in bytes) { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u); }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); stream.Write(length);
    }
    private static (int, int) Size(string path) { var image = Engine.Load(path); return (image.PixelWidth, image.PixelHeight); }
    private static byte[] ReadPixels(string path)
    { var image = new FormatConvertedBitmap(Engine.Load(path), PixelFormats.Bgra32, null, 0); var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes; }
    private static string WriteImage(string name, int width, int height, bool transparent = false)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        { var p = (y * width + x) * 4; pixels[p] = (byte)(x * 3); pixels[p + 1] = (byte)(y * 4); pixels[p + 2] = (byte)(x + y); pixels[p + 3] = transparent ? (byte)0 : (byte)255; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(_folder, name); using var file = File.Create(path); encoder.Save(file); return path;
    }
}
