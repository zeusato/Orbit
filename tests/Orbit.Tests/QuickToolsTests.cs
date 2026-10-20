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
using Orbit.App.Services;
using Orbit.App.UI;
using Orbit.Core;

internal static partial class Program
{
    private static void CheckQuickTools(string source)
    {
        Check("QR preserves Vietnamese, emoji, newlines and literal URLs with a quiet zone", () =>
        {
            foreach (var text in new[] { "https://example.com/?q=Việt%20Nam&n=1", "Đại ca thử QR 😊\nDòng hai: 12345", "  khoảng trắng  " })
            {
                var image = QuickImageTools.Qr(text);
                Assert(image.PixelWidth == 512 && image.PixelHeight == 512, "size");
                var decoded = QuickImageTools.ReadCodes(image, default);
                Assert(decoded.Single().Text == text && decoded[0].Format == "QR_CODE", "exact payload");
                var bytes = new byte[512 * 512 * 4]; image.CopyPixels(bytes, 2048, 0);
                Assert(bytes.Take(512 * 4).All(b => b == 255), "white quiet zone");
            }
            Throws<ArgumentException>(() => QuickImageTools.Qr(""));
            Throws<ArgumentException>(() => QuickImageTools.Qr(new string('ấ', 1000)));
            var png = QuickImageTools.Png(QuickImageTools.Qr("Orbit QR sample"));
            File.WriteAllBytes(Path.Combine(_folder, "quick-qr.png"), png);
        });
        Check("Reader decodes Code128, EAN13 and rotated barcode images", () =>
        {
            foreach (var (format, value) in new[] { (ZXing.BarcodeFormat.CODE_128, "ORBIT-12345"), (ZXing.BarcodeFormat.EAN_13, "5901234123457") })
            {
                var writer = new ZXing.BarcodeWriterPixelData
                {
                    Format = format,
                    Options = new ZXing.Common.EncodingOptions { Width = 600, Height = 220, Margin = 20, PureBarcode = true }
                };
                var data = writer.Write(value);
                var image = BitmapSource.Create(data.Width, data.Height, 96, 96, PixelFormats.Bgra32, null, data.Pixels, data.Width * 4); image.Freeze();
                Assert(QuickImageTools.ReadCodes(image, default).Any(r => r.Text == value), "barcode value");
                var rotated = new TransformedBitmap(image, new RotateTransform(90)); rotated.Freeze();
                Assert(QuickImageTools.ReadCodes(rotated, default).Any(r => r.Text == value), "rotation");
            }
        });
        Check("Reader handles multiple QR codes, inverted QR and missing codes without inventing text", () =>
        {
            var first = QuickImageTools.Qr("first"); var second = QuickImageTools.Qr("second");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1100, 560));
                dc.DrawImage(first, new Rect(10, 20, 512, 512)); dc.DrawImage(second, new Rect(570, 20, 512, 512));
            }
            var combined = new RenderTargetBitmap(1100, 560, 96, 96, PixelFormats.Pbgra32); combined.Render(visual); combined.Freeze();
            Assert(QuickImageTools.ReadCodes(combined, default).Select(r => r.Text).Order().SequenceEqual(new[] { "first", "second" }), "two codes");
            var pixels = new byte[512 * 512 * 4]; first.CopyPixels(pixels, 2048, 0);
            for (int i = 0; i < pixels.Length; i += 4) for (int c = 0; c < 3; c++) pixels[i + c] = (byte)(255 - pixels[i + c]);
            var inverse = BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, pixels, 2048); inverse.Freeze();
            Assert(QuickImageTools.ReadCodes(inverse, default).Single().Text == "first", "inverse");
            var empty = BitmapSource.Create(80, 80, 96, 96, PixelFormats.Bgra32, null, new byte[80 * 80 * 4], 320);
            Assert(QuickImageTools.ReadCodes(empty, default).Length == 0, "blank");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Throws<OperationCanceledException>(() => QuickImageTools.ReadCodes(first, cancelled.Token));
        });
        Check("Image Base64 round-trips original bytes, data URI, alpha and JPEG format", () =>
        {
            var originalHash = Hash(source);
            foreach (var path in new[] { source, Engine.Execute(new(source, ToolKind.Jpeg), default) })
            {
                var payload = QuickImageTools.FromFile(path);
                foreach (var dataUri in new[] { false, true })
                {
                    var encoded = QuickImageTools.ToBase64(payload, dataUri);
                    var decoded = QuickImageTools.FromBase64(encoded);
                    Assert(decoded.Bytes.SequenceEqual(File.ReadAllBytes(path)), "exact original file bytes");
                    Assert(decoded.Extension == payload.Extension && decoded.Bitmap.PixelWidth == 80, "format and dimensions");
                }
            }
            var alpha = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 3, 4, 5, 0, 6, 7, 8, 120 }, 8);
            var bytes = QuickImageTools.Png(alpha);
            var roundtrip = QuickImageTools.FromBase64(Convert.ToBase64String(bytes));
            Assert(roundtrip.Bytes.SequenceEqual(bytes), "alpha PNG byte preservation");
            var tga = QuickImageTools.FromFile(Path.Combine(_folder, "extended.tga"));
            Assert(QuickImageTools.FromBase64(QuickImageTools.ToBase64(tga, true)).Bytes.SequenceEqual(tga.Bytes), "TGA data URI keeps format hint");
            Assert(Hash(source) == originalHash, "original source unchanged");
        });
        Check("Image Base64 rejects malformed, non-image and excessive input; saves never overwrite", () =>
        {
            Throws<ArgumentException>(() => QuickImageTools.FromBase64("not base64!"));
            Throws<ArgumentException>(() => QuickImageTools.FromBase64("data:text/plain;base64,SGVsbG8="));
            Throws<ArgumentException>(() => QuickImageTools.FromBase64("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4="));
            Throws<ArgumentException>(() => QuickImageTools.FromBytes(new byte[QuickImageTools.MaximumImageBytes + 1]));
            var payload = QuickImageTools.FromFile(source);
            var hash = Hash(source);
            var output = QuickImageTools.SaveCopy(payload.Bytes, source);
            Assert(source != output && Hash(source) == hash && Hash(output) == hash, "same-name export adds suffix");
        });
        Check("Text Base64 and URL preserve Unicode and literal plus, reject invalid inputs", () =>
        {
            const string value = "Tiếng Việt 😊 + / ?=\nsecond";
            Assert(TextUtilities.Transform(TextUtilities.Transform(value, TextAction.Base64Encode), TextAction.Base64Decode) == value, "UTF8 roundtrip");
            Assert(TextUtilities.Transform(TextUtilities.Transform(value, TextAction.UrlEncode), TextAction.UrlDecode) == value, "URL component roundtrip");
            Assert(TextUtilities.Transform("a+b", TextAction.UrlDecode) == "a+b", "plus is not space");
            Throws<ArgumentException>(() => TextUtilities.Transform("/w==", TextAction.Base64Decode));
            Throws<ArgumentException>(() => TextUtilities.Transform("%GG", TextAction.UrlDecode));
            Throws<ArgumentException>(() => TextUtilities.Transform(new string('a', TextUtilities.MaximumCharacters + 1), TextAction.JsonFormat));
        });
        Check("JSON formatting preserves numeric precision and text; cleanup preserves order", () =>
        {
            const string json = "{\"n\":123456789012345678901234567890,\"text\":\"Việt Nam\",\"a\":[true,null]}";
            var formatted = TextUtilities.Transform(json, TextAction.JsonFormat);
            Assert(formatted.Contains('\n') && TextUtilities.Transform(formatted, TextAction.JsonMinify) == json, "no numeric rounding");
            Throws<ArgumentException>(() => TextUtilities.Transform("{\"x\":", TextAction.JsonFormat));
            Assert(TextUtilities.Transform(" a \r\n\r\nb ", TextAction.TrimLines) == $"a{Environment.NewLine}{Environment.NewLine}b", "trim each line");
            Assert(TextUtilities.Transform("a\n\nb\n \na", TextAction.RemoveEmptyLines) == string.Join(Environment.NewLine, "a", "b", "a"), "remove empties");
            Assert(TextUtilities.Transform("b\na\nb\nA", TextAction.UniqueLines) == string.Join(Environment.NewLine, "b", "a", "A"), "case-sensitive stable distinct");
        });
    }

    private static async Task CheckQuickToolsUi(OrbitApplication app)
    {
        static Button Button(DependencyObject root, string label) => Descendants<Button>(root).Single(b => Equals(b.Content, label));
        static void Click(DependencyObject root, string label) => Button(root, label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        static void Shot(Window window, string name)
        {
            window.UpdateLayout(); var content = (FrameworkElement)window.Content;
            var render = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            render.Render(content); File.WriteAllBytes(Path.Combine(_folder, name), QuickImageTools.Png(render));
        }
        var jobs = app.Queue.Jobs.Count;
        Click(app.MainWindow, "Tiện ích…");
        var window = app.Windows.OfType<QuickToolsWindow>().Single();
        try
        {
            app.ShowQuickTools();
            Assert(app.Windows.OfType<QuickToolsWindow>().Count() == 1 && !app.IsIdleForUpdate(true), "reuse window and block update");
            var navigation = Descendants<ListBox>(window).Single(l => l.Name == "UtilityNavigation");
            var qr = (QrPane)window.ActivePane;
            var qrInput = Descendants<TextBox>(qr).Single();
            var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, new string('x', 2001)), false, DataFormats.UnicodeText);
            qrInput.RaiseEvent(paste);
            Assert(paste.CommandCancelled && qrInput.Text == "", "overlong clipboard paste is rejected rather than truncated");
            qrInput.Text = "https://example.com/orbit?chào=Đại%20ca";
            Assert(!Button(qr, "Lưu PNG…").IsEnabled, "no automatic generation");
            Click(qr, "Tạo QR"); await qr.Pending;
            Assert(Button(qr, "Lưu PNG…").IsEnabled, "generated image can be saved");
            var bitmap = (BitmapSource)Descendants<System.Windows.Controls.Image>(qr).Single().Source;
            Shot(window, "utilities-qr-ui.png");
            navigation.SelectedIndex = 1;
            var reader = (CodeReaderPane)window.ActivePane;
            await reader.ReadImageAsync(bitmap);
            Assert(Descendants<TextBox>(reader).Single().Text == qrInput.Text, "clipboard bitmap scan displays exact text");
            Shot(window, "utilities-reader-ui.png");
            navigation.SelectedIndex = 2;
            var images = (ImageBase64Pane)window.ActivePane;
            await images.LoadImageAsync(bitmap);
            var text = Descendants<TextBox>(images).Single();
            Assert(text.Text.StartsWith("data:image/png;base64,") && Button(images, "Lưu ảnh…").IsEnabled, "clipboard snapshot to data URI");
            var encoded = text.Text;
            text.Text = "broken";
            Assert(!Button(images, "Lưu ảnh…").IsEnabled, "editing clears stale image");
            Click(images, "Giải mã → ảnh"); await images.Pending;
            Assert(!Button(images, "Lưu ảnh…").IsEnabled, "invalid Base64 cannot export");
            text.Text = encoded; Click(images, "Giải mã → ảnh"); await images.Pending;
            Assert(Button(images, "Lưu ảnh…").IsEnabled, "valid Base64 displays image");
            Shot(window, "utilities-base64-ui.png");
            navigation.SelectedIndex = 4;
            var json = (TextUtilityPane)window.ActivePane;
            var input = Descendants<TextBox>(json).Single(t => !t.IsReadOnly);
            var output = Descendants<TextBox>(json).Single(t => t.IsReadOnly);
            input.Text = "{\"name\":\"Orbit\",\"pages\":3}"; Click(json, "Định dạng"); await json.Pending;
            Assert(output.Text.Contains('\n') && Button(json, "Sao chép kết quả").IsEnabled, "formatted output");
            Shot(window, "utilities-json-ui.png");
            input.Text = "{bad"; Click(json, "Định dạng"); await json.Pending;
            Assert(output.Text == "" && !Button(json, "Sao chép kết quả").IsEnabled, "invalid JSON clears stale output");
            navigation.SelectedIndex = 0;
            Assert(qrInput.Text.Contains("example.com") && Button(qr, "Lưu PNG…").IsEnabled, "switch retains working data");
            qrInput.Text = "changed";
            Assert(!Button(qr, "Sao chép ảnh").IsEnabled && Descendants<System.Windows.Controls.Image>(qr).Single().Source == null, "stale QR invalidated");
            Assert(app.Queue.Jobs.Count == jobs, "utilities do not enqueue file jobs implicitly");
        }
        finally { window.Close(); }
        app.ShowQuickTools(); var closed = app.Windows.OfType<QuickToolsWindow>().Single();
        var newQr = (QrPane)closed.ActivePane;
        Descendants<TextBox>(newQr).Single().Text = "Close while generating";
        Click(newQr, "Tạo QR"); closed.Close(); await newQr.Pending;
        Check("Utilities WPF flows keep input on navigation, invalidate stale output and close safely", () => { });
        app.MainWindow.Hide();
        var cursor = new Orbit.App.Interop.Native.Point(700, 420);
        app.ToggleQuickMenu(cursor);
        Assert(app.Menu.IsVisible && app.Menu.QuickMode && !app.Menu.NativeDragActive && !app.Menu.AllowDrop, "idle launcher is click mode");
        Assert(!app.MainWindow.IsVisible && app.Menu.VisibleTools.SequenceEqual(ToolCatalog.QuickTools), "six tools without opening Home");
        Assert(Orbit.App.Interop.Native.HasCircularRegion(app.Menu), "native circular boundary");
        Shot(app.Menu, "utilities-radial-ui.png");
        app.ToggleQuickMenu(cursor);
        Assert(!app.Menu.IsVisible && app.Queue.Jobs.Count == jobs, "toggle cancels without executing");
        // Late file detection must never replace the utility ring.
        app.Menu.ShowMenu(cursor, [Path.Combine(_folder, "text-layer.pdf")]);
        var detection = app.Menu.Detection;
        app.ToggleQuickMenu(cursor); await detection;
        Assert(app.Menu.VisibleTools.SequenceEqual(ToolCatalog.QuickTools), "stale detection ignored");
        for (int index = 0; index < ToolCatalog.QuickTools.Length; index++)
        {
            app.Menu.ShowQuickMenu(cursor);
            Descendants<Button>(app.Menu).ElementAt(index).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!app.Windows.OfType<QuickToolsWindow>().Any() && DateTime.UtcNow < deadline) await Task.Delay(10);
            var selected = app.Windows.OfType<QuickToolsWindow>().Single();
            Assert(Descendants<ListBox>(selected).Single(l => l.Name == "UtilityNavigation").SelectedIndex == index, "direct target " + index);
            Assert(!app.MainWindow.IsVisible && app.Queue.Jobs.Count == jobs, "no Home or implicit processing");
            if (index == 5) Assert(!Descendants<TextBlock>(selected).Any(t => t.Text.Contains("Đại ca")), "neutral product copy");
            selected.Close();
        }
        app.Menu.ShowMenu(cursor);
        Assert(app.Menu.NativeDragActive && app.Menu.AllowDrop && !app.Menu.QuickMode, "drag mode restored after utility ring");
        app.Menu.HideMenu(); app.ShowHome();
        Check("Idle radial launcher toggles, opens all six targets without Home and isolates drag detection", () => { });
    }
}
