using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App;
using Orbit.App.UI;
using Orbit.Core;

internal static partial class Program
{
    private static (byte B, byte G, byte R, byte A) Pixel(string path, int x, int y)
    {
        var image = new FormatConvertedBitmap(Engine.Load(path), PixelFormats.Bgra32, null, 0); var bytes = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0); return (bytes[0], bytes[1], bytes[2], bytes[3]);
    }
    private static bool Near((byte B, byte G, byte R, byte A) p, byte r, byte g, byte b, int tolerance = 2) =>
        Math.Abs(p.R - r) <= tolerance && Math.Abs(p.G - g) <= tolerance && Math.Abs(p.B - b) <= tolerance && p.A == 255;

    private static void CheckFrames(string source)
    {
        var plain = new FrameOptions(Padding: .25, Corner: 0, Shadow: false, Color1: "#112233");
        Check("Frame layout grows the canvas by padding then to the target aspect", () =>
        {
            Assert(FrameLayout.Compute(100, 50, plain with { Padding = .1 }) == new FrameGeometry(120, 70, 10, 10, 100, 50, 0), "uniform padding");
            Assert(FrameLayout.Compute(100, 50, plain with { Padding = .1, Aspect = 1 }) == new FrameGeometry(120, 120, 10, 35, 100, 50, 0), "square canvas");
            Assert(FrameLayout.Compute(100, 50, plain with { Padding = .1, Aspect = 16d / 9 }).CanvasWidth == 125, "16:9 extends width");
            Assert(FrameLayout.Compute(50, 100, plain with { Padding = 0, Aspect = 16d / 9 }) is { CanvasWidth: 178, CanvasHeight: 100, X: 64 }, "portrait into landscape");
            Assert(FrameLayout.Compute(200, 100, plain with { Corner = .1 }).CornerRadius == 10, "corner relative to short side");
            Throws<ArgumentException>(() => FrameLayout.Compute(30000, 1000, plain with { Padding = .1 }));
            Throws<ArgumentException>(() => FrameLayout.Compute(100, 100, plain with { Padding = .6 }));
            Throws<FormatException>(() => FrameLayout.Compute(100, 100, plain with { Color2 = "teal" }));
            Throws<ArgumentException>(() => FrameLayout.Compute(100, 100, plain with { Background = FrameBackground.Picture }));
        });
        Check("Solid frame keeps every source pixel and paints only the border", () =>
        {
            var before = Hash(source);
            var output = Engine.Execute(new(source, ToolKind.Frame, Frame: plain), CancellationToken.None);
            Assert(Path.GetFileName(output) == Path.GetFileNameWithoutExtension(source) + "_frame.png" && Size(output) == (120, 100), "PNG canvas 80×60 + 25% of 80 on each side");
            Assert(Near(Pixel(output, 0, 0), 0x11, 0x22, 0x33, 0) && Near(Pixel(output, 119, 99), 0x11, 0x22, 0x33, 0), "border color");
            var framed = new FormatConvertedBitmap(Engine.Load(output), PixelFormats.Bgra32, null, 0); var inner = new byte[80 * 60 * 4];
            framed.CopyPixels(new Int32Rect(20, 20, 80, 60), inner, 80 * 4, 0);
            Assert(inner.SequenceEqual(ReadPixels(source)), "source pixels copied 1:1");
            Assert(Hash(source) == before, "source unchanged");
        });
        Check("Gradient, textures and JPEG output render distinct backgrounds", () =>
        {
            var gradient = Engine.Execute(new(source, ToolKind.Frame, Frame: plain with { Background = FrameBackground.Gradient, Color1 = "#FF0000", Color2 = "#0000FF", Angle = 90 }), CancellationToken.None);
            var left = Pixel(gradient, 0, 50); var right = Pixel(gradient, 119, 50);
            Assert(left.R > 200 && left.B < 60 && right.B > 200 && right.R < 60, "left-to-right red to blue");
            foreach (var texture in Enum.GetValues<FrameTexture>())
            {
                var output = Engine.Execute(new(source, ToolKind.Frame, Frame: plain with { Padding = .5, Background = FrameBackground.Texture, Texture = texture, Color1 = "#FFFFFF", Color2 = "#000000" }), CancellationToken.None);
                var border = new FormatConvertedBitmap(Engine.Load(output), PixelFormats.Bgra32, null, 0); var strip = new byte[border.PixelWidth * 30 * 4];
                border.CopyPixels(new Int32Rect(0, 0, border.PixelWidth, 30), strip, border.PixelWidth * 4, 0);
                var shades = Enumerable.Range(0, strip.Length / 4).Select(i => strip[i * 4]).Distinct().Count();
                Assert(shades >= 2, "texture pattern visible: " + texture);
            }
            var jpeg = Engine.Execute(new(source, ToolKind.Frame, Quality: 92, Frame: plain with { Jpeg = true }), CancellationToken.None);
            Assert(jpeg.EndsWith("_frame.jpg") && FileTypes.Detect(jpeg) == FileKind.Jpeg && Size(jpeg) == (120, 100), "JPEG frame");
        });
        Check("Picture, blurred picture and blurred source backgrounds fill the canvas", () =>
        {
            var red = WriteSolid("frame-red.png", 10, 10, 255, 0, 0);
            foreach (var blur in new[] { false, true })
            {
                var output = Engine.Execute(new(source, ToolKind.Frame, Frame: plain with { Background = FrameBackground.Picture, Picture = red, BlurPicture = blur }), CancellationToken.None);
                Assert(Near(Pixel(output, 0, 0), 255, 0, 0, 3) && Near(Pixel(output, 119, 99), 255, 0, 0, 3), "picture covers corners, blur=" + blur);
            }
            var green = WriteSolid("frame-green.png", 40, 30, 0, 200, 0);
            var own = Engine.Execute(new(green, ToolKind.Frame, Frame: plain with { Background = FrameBackground.SourceBlur }), CancellationToken.None);
            Assert(Near(Pixel(own, 0, 0), 0, 200, 0, 3), "source blurred behind itself, opaque at edges");
            Throws<FileNotFoundException>(() => Engine.Execute(new(source, ToolKind.Frame, Frame: plain with { Background = FrameBackground.Picture, Picture = Path.Combine(_folder, "missing.png") }), CancellationToken.None));
        });
        Check("Rounded corners show the background; shadow darkens below the image", () =>
        {
            var white = WriteSolid("frame-white.png", 80, 60, 255, 255, 255);
            var output = Engine.Execute(new(white, ToolKind.Frame, Frame: plain with { Corner = .25, Shadow = true, Color1 = "#FFFFFF" }), CancellationToken.None);
            var corner = Pixel(output, 21, 21); var center = Pixel(output, 60, 50); var below = Pixel(output, 60, 82);
            Assert(Near(center, 255, 255, 255, 0), "image interior untouched");
            Assert(below.R < 250, "shadow under the image");
            Assert(corner.R >= 250 || corner.R < below.R + 10, "clipped corner is background or shadow, not image edge");
        });
    }

    // Diagnostic: writes one framed copy per background style into `folder` for visual review.
    private static int FrameSamples(string image, string folder)
    {
        Directory.CreateDirectory(folder);
        var styles = new (string Name, FrameOptions Options)[]
        {
            ("solid", new()), ("gradient", new(Background: FrameBackground.Gradient, Color1: "#FBC2EB", Color2: "#A6C1EE", Aspect: 1)),
            ("dots", new(Background: FrameBackground.Texture, Color1: "#F4EFE6", Color2: "#167D72", Padding: .12)),
            ("stripes", new(Background: FrameBackground.Texture, Texture: FrameTexture.Stripes, Color1: "#1F2328", Color2: "#2B323A", Aspect: 4d / 5)),
            ("paper", new(Background: FrameBackground.Texture, Texture: FrameTexture.Paper, Color1: "#F4EFE6", Color2: "#5A4632")),
            ("source-blur", new(Background: FrameBackground.SourceBlur, Aspect: 9d / 16, Corner: .05))
        };
        foreach (var (name, options) in styles)
        {
            var copy = Path.Combine(folder, name + Path.GetExtension(image)); File.Copy(image, copy, true);
            Console.WriteLine(Engine.Execute(new(copy, ToolKind.Frame, Quality: 92, Frame: options with { Jpeg = true }), CancellationToken.None));
        }
        return 0;
    }

    private static async Task CheckHistory(OrbitApplication app)
    {
        var file = Path.Combine(_folder, "history", "history.json"); var source = WriteImage("history-source.png", 20, 10);
        var first = new Orbit.App.Services.JobQueue(app.Dispatcher, file);
        first.Add([new ImageRequest(source, ToolKind.Jpeg), new ImageRequest(Path.Combine(_folder, "history-missing.png"), ToolKind.Png)]);
        var finish = DateTime.UtcNow.AddSeconds(10);
        while (first.HasPending && DateTime.UtcNow < finish) await Task.Delay(30);
        first.Dispose();
        var second = new Orbit.App.Services.JobQueue(app.Dispatcher, file);
        Check("Finished jobs persist across restarts and can be cleared", () =>
        {
            Assert(second.Jobs.Count == 2 && second.Jobs.Any(j => j.State == JobState.Completed && j.CanOpen && j.Progress == 100 && File.Exists(j.Output)), "completed row restored with output");
            Assert(second.Jobs.Any(j => j.State == JobState.Failed && j.Status.StartsWith("Lỗi")) && !second.HasPending, "failed row restored, nothing re-runs");
            second.ClearHistory();
            Assert(second.Jobs.Count == 0 && new Orbit.App.Services.JobQueue(app.Dispatcher, file).Jobs.Count == 0, "cleared on disk");
            File.WriteAllText(file, "{ broken"); Assert(new Orbit.App.Services.JobQueue(app.Dispatcher, file).Jobs.Count == 0, "corrupt history ignored");
        });
        second.Dispose();
    }

    private static async Task CheckProgressToast(OrbitApplication app)
    {
        var toast = app.Progress!; var pdf = Path.Combine(_folder, "ocr-vietnamese.pdf");
        app.MainWindow.Hide();
        app.Queue.Add([new ImageRequest(pdf, ToolKind.PdfText)]);
        var seen = false; double percent = 0; var activated = false; var deadline = DateTime.UtcNow.AddSeconds(30);
        while (app.Queue.HasPending && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            if (toast.IsVisible) { seen = true; percent = Math.Max(percent, toast.Bar.Value); activated |= toast.IsActive; }
        }
        await Task.Delay(400);
        Check("Long job shows a non-activating progress panel in the notification corner", () =>
        {
            Assert(seen, "progress panel appeared for a multi-second OCR job");
            Assert(!activated, "panel never takes focus");
            var area = SystemParameters.WorkArea;
            Assert(Math.Abs(toast.Left + toast.ActualWidth - area.Right) < 12 && Math.Abs(toast.Top + toast.ActualHeight - area.Bottom) < 12, "bottom-right of work area");
            Assert(!toast.IsVisible && app.Queue.Jobs.First().State == JobState.Completed, "panel hides after completion");
        });
        app.Queue.Jobs.Clear();
        app.ShowHome(); app.MainWindow.Activate(); await Task.Delay(100);
        if (app.MainWindow.IsActive)
        {
            app.Queue.Add([new ImageRequest(pdf, ToolKind.PdfText)]);
            var shown = false; deadline = DateTime.UtcNow.AddSeconds(30);
            while (app.Queue.HasPending && DateTime.UtcNow < deadline) { await Task.Delay(50); shown |= toast.IsVisible && app.MainWindow.IsActive; }
            Check("Progress panel stays hidden while the Orbit window is in front", () => Assert(!shown, "main window list already shows progress"));
            app.Queue.Jobs.Clear();
        }
        else Console.WriteLine("SKIP Progress panel suppression (test window could not take foreground)");
    }

    private static string WriteSolid(string name, int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(_folder, name); using var file = File.Create(path); encoder.Save(file); return path;
    }

    private static async Task CheckFrameEditor(OrbitApplication app)
    {
        var source = WriteImage("frame-editor.png", 400, 200); var image = Engine.Load(source);
        var editor = new EditorWindow(app, ToolKind.Frame, [source], image);
        editor.Show(); editor.UpdateLayout();
        var viewer = Descendants<CropCanvas>(editor).Single();
        Button Find(string text) => Descendants<Button>(editor).First(b => Equals(b.Content, text));
        await Task.Delay(300);
        try
        {
            Check("Frame editor previews padding and aspect from the side panel", () =>
                Assert(Math.Abs(viewer.ImageBounds.Width / viewer.ImageBounds.Height - 464d / 264) < .02, "default 8% padding preview of a 2:1 image (464 × 264)"));
            Find("1:1").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find("Chuyển màu").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find("Đêm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(400);
            Check("Frame editor switches aspect and gradient preset with live preview", () =>
            {
                Assert(Math.Abs(viewer.ImageBounds.Width - viewer.ImageBounds.Height) < 2, "square preview");
                Assert(editor.FrameSettings is { Aspect: 1, Background: FrameBackground.Gradient, Color1: "#141E30", Color2: "#243B55" }, "options follow buttons");
                Assert(Descendants<TextBlock>(editor).Any(t => t.Text.StartsWith("Đầu ra · 464 × 464 px")), "summary uses full-resolution geometry");
            });
            var hex = Descendants<TextBox>(editor).First(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Màu đầu");
            hex.Text = "nope"; Check("Frame editor rejects invalid hex without losing the last valid color", () => Assert(editor.FrameSettings.Color1 == "#141E30", "invalid color ignored"));
            hex.Text = "#ff8800";
            Find("Lưu bản sao").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { if (editor.IsVisible) editor.Close(); }
        var finish = DateTime.UtcNow.AddSeconds(15);
        while (app.Queue.HasPending && DateTime.UtcNow < finish) await Task.Delay(30);
        Check("Frame editor Save exports the full-resolution framed image", () =>
        {
            var job = app.Queue.Jobs.Single(); Assert(job.State == JobState.Completed, "frame job completed: " + job.Status);
            Assert(Size(job.Output!) == (464, 464) && Near(Pixel(job.Output!, 0, 0), 0xFF, 0x88, 0x00, 6), "square canvas starting with the edited color");
        });
        app.Queue.Jobs.Clear();
    }
}
