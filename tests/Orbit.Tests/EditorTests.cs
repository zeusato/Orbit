using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App;
using Orbit.App.UI;
using Orbit.Core;

internal static partial class Program
{
    private static async Task CheckEditors(OrbitApplication app)
    {
        var source = WriteImage("editor-source.png", 1000, 800); var hash = Hash(source); var image = Engine.Load(source);
        Check("Crop zoom, pan and handle drags preserve source pixel coordinates", () =>
        {
            var editor = new EditorWindow(app, ToolKind.Crop, [source], image);
            try
            {
                editor.Show(); editor.UpdateLayout();
                Assert(!app.IsIdleForUpdate(true) && !app.IsIdleForUpdate(false), "open editor blocks manual and automatic updates");
                var canvas = Descendants<CropCanvas>(editor).Single(); canvas.FitImage();
                canvas.SetRegion(new(200, 200, 400, 300));
                var before = canvas.Region; canvas.ActualSize();
                Assert(canvas.Zoom == 1 && canvas.Region == before, "100% keeps selection");
                var point = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
                var pixel = canvas.ToPixel(point); canvas.ZoomAt(2, point);
                Assert((canvas.ToPixel(point) - pixel).Length < .01, "zoom anchored on cursor");
                canvas.BeginDrag(point, true); canvas.DragTo(point + new Vector(-60, -40)); canvas.EndDrag();
                Assert(canvas.Region == before, "pan cannot alter selection");
                var r = canvas.SelectionBounds; var right = new Point(r.Right, r.Top + r.Height / 2);
                canvas.BeginDrag(right); canvas.DragTo(right + new Vector(100, 0)); canvas.EndDrag();
                Assert(canvas.Region == new CropRegion(200, 200, 450, 300), "edge resize maps through 200% zoom");
                var center = new Point(canvas.SelectionBounds.Left + 100, canvas.SelectionBounds.Top + 100);
                canvas.BeginDrag(center); canvas.DragTo(center + new Vector(40, 20)); canvas.EndDrag();
                Assert(canvas.Region == new CropRegion(220, 210, 450, 300), "move uses source pixels after pan");
                var output = Engine.Execute(new(source, ToolKind.Crop, Crop: canvas.Region), default);
                Assert(Size(output) == (450, 300), "encoded crop dimensions");
                var expected = new CroppedBitmap(image, new Int32Rect(220, 210, 450, 300)); var pixels = new byte[450 * 300 * 4];
                new FormatConvertedBitmap(expected, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 450 * 4, 0);
                Assert(ReadPixels(output).SequenceEqual(pixels), "encoded pixels exactly match visual selection");
                Assert(Hash(source) == hash, "source unchanged");
            }
            finally { editor.Close(); }
        });
        Check("Crop aspect edges stay in bounds; free aspect does not reset selection", () =>
        {
            var editor = new EditorWindow(app, ToolKind.Crop, [source], image);
            try
            {
                editor.Show(); editor.UpdateLayout(); var canvas = Descendants<CropCanvas>(editor).Single(); canvas.ActualSize();
                foreach (var ratio in new[] { 1d, .8, 16d / 9 })
                {
                    canvas.SetRegion(new(200, 200, 400, 300)); canvas.SetAspect(ratio);
                    var r = canvas.SelectionBounds; var p = new Point(r.Right, r.Top + r.Height / 2);
                    canvas.BeginDrag(p); canvas.DragTo(p + new Vector(9000, 0)); canvas.EndDrag();
                    var result = canvas.Region;
                    Assert(result.X >= 0 && result.Y >= 0 && result.X + result.Width <= 1000 && result.Y + result.Height <= 800, "bounded ratio selection");
                    Assert(Math.Abs(result.Width - result.Height * ratio) <= 2, "ratio within pixel rounding");
                    canvas.SetAspect(null); Assert(canvas.Region == result, "unlock keeps current region");
                }
                canvas.ResetSelection(); Assert(canvas.Region == new CropRegion(0, 0, 1000, 800) && canvas.Aspect == null, "reset selection and aspect");
            }
            finally { editor.Close(); }
        });
        Check("Crop rejects invalid fields and commits latest edit before Save", () =>
        {
            var editor = new EditorWindow(app, ToolKind.Crop, [source], image);
            try
            {
                editor.Show(); editor.UpdateLayout();
                var fields = Descendants<TextBox>(editor).ToDictionary(AutomationProperties.GetName);
                var save = Descendants<Button>(editor).Single(b => Equals(b.Content, "Lưu bản sao")); var count = app.Queue.Jobs.Count;
                fields["Rộng (px)"].Text = "not-a-number"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(editor.IsVisible && app.Queue.Jobs.Count == count, "invalid input does not silently save old crop");
                fields["Rộng (px)"].Text = "120"; fields["Cao (px)"].Text = "90"; fields["X (px)"].Text = "50"; fields["Y (px)"].Text = "40";
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(!editor.IsVisible && app.Queue.Jobs[0].Request.Crop == new CropRegion(50, 40, 120, 90), "save commits even without focus change");
            }
            finally { if (editor.IsVisible) editor.Close(); }
        });
        Check("Resize original preset and unlocked dimensions; JPEG viewer provides zoom", () =>
        {
            var editor = new EditorWindow(app, ToolKind.Resize, [source], image);
            try
            {
                editor.Show(); editor.UpdateLayout();
                var fields = Descendants<TextBox>(editor).ToDictionary(AutomationProperties.GetName);
                fields["Rộng (px)"].Text = "500"; Assert(fields["Cao (px)"].Text == "400", "locked ratio");
                Descendants<CheckBox>(editor).Single().IsChecked = false;
                fields["Cao (px)"].Text = "123"; Assert(fields["Rộng (px)"].Text == "500", "unlocked dimensions");
                Descendants<Button>(editor).Single(b => Equals(b.Content, "Kích thước gốc")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(fields["Rộng (px)"].Text == "1000" && fields["Cao (px)"].Text == "800", "reset dimensions");
                var viewer = Descendants<CropCanvas>(editor).Single(); viewer.ActualSize(); Assert(viewer.Zoom == 1, "resize preview zoom");
            }
            finally { editor.Close(); }
        });
        var compress = new EditorWindow(app, ToolKind.Compress, [source], image);
        try
        {
            compress.Show(); compress.UpdateLayout();
            var slider = Descendants<Slider>(compress).Single(); slider.Value = 60; slider.Value = 95;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!Descendants<TextBlock>(compress).Any(t => t.Text.StartsWith("Xem trước JPEG")) && DateTime.UtcNow < deadline) await Task.Delay(50);
            Check("JPEG preview completes after rapid quality changes", () => Assert(Descendants<TextBlock>(compress).Any(t => t.Text.StartsWith("Xem trước JPEG")), "latest JPEG preview is displayed"));
        }
        finally { compress.Close(); }
        var finish = DateTime.UtcNow.AddSeconds(10);
        while (app.Queue.HasPending && DateTime.UtcNow < finish) await Task.Delay(30);
        Check("Editor Save runs actual crop job with latest numeric selection", () => Assert(app.Queue.Jobs.Single().State == JobState.Completed && Size(app.Queue.Jobs.Single().Output!) == (120, 90), "actual crop export"));
        app.Queue.Jobs.Clear();
    }
}
