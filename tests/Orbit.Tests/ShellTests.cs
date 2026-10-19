using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Orbit.App;
using Orbit.App.Services;
using Orbit.App.UI;
using Orbit.Core;
using System.Windows.Media.Imaging;

internal static partial class Program
{
    private static void CheckShell(string source)
    {
        Check("Shell commands parse, round-trip and merge per tool", () =>
        {
            Assert(ShellCommand.TryParse(["--tool", "Png", "a.jpg"], out var png) && png.Tool == ToolKind.Png && png.Files.SequenceEqual(["a.jpg"]), "tool command");
            Assert(ShellCommand.TryParse(["--open", "a.jpg", "b.jpg"], out var open) && open.Tool == null && open.Files.Length == 2, "open command");
            Assert(!ShellCommand.TryParse(["--tool", "More", "a.jpg"], out _) && !ShellCommand.TryParse(["--tool", "Nope", "a"], out _) && !ShellCommand.TryParse(["--background"], out _), "rejects invalid");
            Assert(ShellCommand.TryParse(png.ToArgs(), out var again) && again == png with { Files = again.Files } && again.Files.SequenceEqual(png.Files), "round trip");
            var merged = ShellCommand.Merge([new(ToolKind.Png, [Path.Combine(_folder, "a.jpg")]), new(ToolKind.Png, [Path.Combine(_folder, "b.jpg")]), new(ToolKind.Png, [Path.Combine(_folder, "A.JPG")]), new(null, [Path.Combine(_folder, "c.jpg")])]);
            Assert(merged.Count == 2 && merged.Single(c => c.Tool == ToolKind.Png).Files.Length == 2, "one batch per tool, duplicates removed");
        });
        Check("Context submenus follow the file type", () =>
        {
            var jpeg = ShellMenuLayout.Tools(FileKind.Jpeg);
            Assert(!jpeg.Contains(ToolKind.Jpeg) && jpeg.Contains(ToolKind.Png) && jpeg.Contains(ToolKind.Crop) && jpeg.Contains(ToolKind.Frame) && jpeg[^1] == ToolKind.Sha256, "JPEG menu");
            var pdf = ShellMenuLayout.Tools(FileKind.Pdf).ToList();
            Assert(pdf.Contains(ToolKind.PdfWord) && pdf.Contains(ToolKind.PdfText) && pdf.IndexOf(ToolKind.PdfMerge) == pdf.IndexOf(ToolKind.Zip) - 1, "PDF menu with merge before generic tools");
            var mp3 = ShellMenuLayout.Tools(FileKind.Mp3);
            Assert(!mp3.Contains(ToolKind.AudioMp3) && mp3.Contains(ToolKind.MediaTrim) && mp3.Contains(ToolKind.AudioMerge), "MP3 menu");
            Assert(ShellMenuLayout.Tools(FileKind.Mp4).Contains(ToolKind.VideoGif) && ShellMenuLayout.Label(ToolKind.Png) == "Chuyển sang PNG" && ShellMenuLayout.Label(ToolKind.Crop) == "Crop", "video menu and labels");
            Assert(ShellMenuLayout.Extensions[".HEIC"] == FileKind.Heic && ShellMenuLayout.Extensions[".mkv"] == FileKind.Video, "extension map");
        });
        Check("Context menu registry: per-type submenus, quoted commands, foreign verbs untouched", () =>
        {
            var path = @"Software\OrbitTests\" + Guid.NewGuid().ToString("N");
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey(path);
                using (var foreign = root.CreateSubKey(@"SystemFileAssociations\.jpg\shell\OtherApp")) foreign.SetValue("MUIVerb", "Other");
                using (var lookalike = root.CreateSubKey(@"SystemFileAssociations\.png\shell\Orbit")) lookalike.SetValue("ExtendedSubCommandsKey", "SomeoneElse.Menu");
                const string exe = @"C:\Program Files\Orbit Test\Orbit.exe";
                ShellMenu.Register(exe, root);
                using (var verb = root.OpenSubKey(@"SystemFileAssociations\.jpg\shell\Orbit")!)
                    Assert(Equals(verb.GetValue("ExtendedSubCommandsKey"), "Orbit.Menu.Jpeg") && Equals(verb.GetValue("MultiSelectModel"), "Player") && ((string)verb.GetValue("Icon")!).StartsWith("\"C:\\Program Files"), "verb points at the JPEG submenu");
                using (var menu = root.OpenSubKey(@"Orbit.Menu.Jpeg\shell")!)
                {
                    var names = menu.GetSubKeyNames();
                    Assert(names.Contains("00open") && names.Any(n => n.EndsWith("Png")) && !names.Any(n => n.EndsWith("Jpeg")) && names.Any(n => n.EndsWith("Crop")), "JPEG submenu entries");
                    using var command = menu.OpenSubKey(names.First(n => n.EndsWith("Png")) + @"\command")!;
                    Assert(Equals(command.GetValue(""), $"\"{exe}\" --tool Png \"%1\""), "quoted command line");
                }
                using (var mp3 = root.OpenSubKey(@"Orbit.Menu.Mp3\shell")!) Assert(mp3.GetSubKeyNames().Any(n => n.EndsWith("AudioMerge")), "audio submenu has merge");
                ShellMenu.Unregister(root);
                Assert(root.OpenSubKey(@"SystemFileAssociations\.jpg\shell\Orbit") == null && !root.GetSubKeyNames().Any(n => n.StartsWith("Orbit.Menu.")), "removed");
                Assert(root.OpenSubKey(@"SystemFileAssociations\.jpg\shell\OtherApp") != null && root.OpenSubKey(@"SystemFileAssociations\.png\shell\Orbit") != null, "foreign verbs kept");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        });
        Check("Second process hands its request to the running Orbit over the user pipe", () =>
        {
            var name = "Orbit.Shell.Test." + Guid.NewGuid().ToString("N"); var received = new System.Collections.Concurrent.ConcurrentQueue<ShellCommand>();
            using var stop = new CancellationTokenSource();
            var listener = ShellPipe.Listen(received.Enqueue, stop.Token, name);
            Assert(ShellPipe.Send(new(ToolKind.Png, ["a.jpg"]), name) && ShellPipe.Send(new(null, ["b.pdf"]), name), "sent");
            var deadline = DateTime.UtcNow.AddSeconds(5); while (received.Count < 2 && DateTime.UtcNow < deadline) Thread.Sleep(20);
            stop.Cancel(); listener.Wait(2000);
            Assert(received.Count == 2 && received.Any(c => c.Tool == ToolKind.Png) && received.Any(c => c.Tool == null), "both requests received");
            Assert(!ShellPipe.Send(new(ToolKind.Png, ["a.jpg"]), "Orbit.Shell.Nobody." + Guid.NewGuid().ToString("N"), TimeSpan.FromMilliseconds(400)), "no server: gives up");
        });
        Check("Output location: beside, Orbit subfolder without nesting, or fixed folder", () =>
        {
            var dir = Path.GetDirectoryName(source)!;
            Assert(OutputLocation.For(source, OutputLocation.Beside, null) == dir, "beside");
            var sub = OutputLocation.For(source, OutputLocation.Subfolder, null);
            Assert(sub == Path.Combine(dir, "Orbit") && OutputLocation.For(Path.Combine(sub, "x.png"), OutputLocation.Subfolder, null) == sub, "subfolder never nests");
            Assert(OutputLocation.For(source, OutputLocation.Folder, Path.Combine(_folder, "fixed")) == Path.Combine(_folder, "fixed") && OutputLocation.For(source, OutputLocation.Folder, null) == dir, "fixed folder, empty falls back");
            var output = Engine.Execute(new(source, ToolKind.Webp, OutputDirectory: sub), CancellationToken.None);
            Assert(Path.GetDirectoryName(output) == sub && File.Exists(output), "engine writes into the created subfolder");
            var pdf = new DocumentEngine().Execute(new(source, ToolKind.ImagePdf, OutputDirectory: Path.Combine(_folder, "fixed")), CancellationToken.None);
            Assert(Path.GetDirectoryName(pdf) == Path.Combine(_folder, "fixed"), "document engine too");
            Assert(!Directory.EnumerateFiles(dir, ".orbit-*").Any() && !Directory.EnumerateFiles(sub, ".orbit-*").Any(), "no staging left");
        });
        Check("Icon renders every size and the committed ICO holds all of them", () =>
        {
            foreach (var size in OrbitIcon.Sizes) { var image = OrbitIcon.Render(size); Assert(image.PixelWidth == size && image.PixelHeight == size, "size " + size); }
            var corner = new byte[4]; OrbitIcon.Render(64).CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
            Assert(corner[3] == 0, "transparent corner");
            var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir != null && !File.Exists(Path.Combine(dir.FullName, "assets", "orbit.ico"))) dir = dir.Parent;
            Assert(dir != null, "assets/orbit.ico found");
            var bytes = File.ReadAllBytes(Path.Combine(dir!.FullName, "assets", "orbit.ico"));
            Assert(BitConverter.ToUInt16(bytes, 2) == 1 && BitConverter.ToUInt16(bytes, 4) == OrbitIcon.Sizes.Length, "ICO directory lists every size");
            var frames = new IconBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
            Assert(frames.Count == OrbitIcon.Sizes.Length && frames.Any(f => f.PixelWidth == 256) && frames.Any(f => f.PixelWidth == 16), "Windows icon decoder reads 16 to 256 px");
        });
    }

    private static async Task CheckShellUi(OrbitApplication app)
    {
        var source = WriteImage("shell-source.png", 120, 90); var image = Engine.Load(source);
        // Remembered tool settings.
        var compress = new EditorWindow(app, ToolKind.Compress, [source], image); compress.Show();
        Descendants<Slider>(compress).Single().Value = 60;
        Descendants<Button>(compress).First(b => Equals(b.Content, "Lưu bản sao")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var again = new EditorWindow(app, ToolKind.Compress, [source], image); again.Show();
        Check("JPEG quality is remembered and persisted", () => Assert(app.Settings.Defaults.JpegQuality == 60 && AppSettings.Load().Defaults.JpegQuality == 60 && Descendants<Slider>(again).Single().Value == 60, "60% restored"));
        again.Close();
        var frame = new EditorWindow(app, ToolKind.Frame, [source], image); frame.Show();
        Descendants<Button>(frame).First(b => Equals(b.Content, "4:5")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Descendants<Button>(frame).First(b => Equals(b.Content, "Họa tiết")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Descendants<Button>(frame).First(b => Equals(b.Content, "Lưu bản sao")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var frameAgain = new EditorWindow(app, ToolKind.Frame, [source], image); frameAgain.Show();
        Check("Frame options are remembered", () => Assert(frameAgain.FrameSettings is { Aspect: .8, Background: FrameBackground.Texture }, "4:5 texture restored"));
        frameAgain.Close();
        app.Settings.Defaults.Frame = new(Background: FrameBackground.Picture, Picture: Path.Combine(_folder, "deleted-background.png"), Padding: .2);
        var missing = new EditorWindow(app, ToolKind.Frame, [source], image); missing.Show();
        Check("A remembered picture that no longer exists falls back to a colour", () => Assert(missing.FrameSettings is { Background: FrameBackground.Solid, Picture: null, Padding: .2 }, "solid with other options kept"));
        missing.Close();
        var resize = new EditorWindow(app, ToolKind.Resize, [source], image); resize.Show();
        var fields = Descendants<TextBox>(resize).ToDictionary(System.Windows.Automation.AutomationProperties.GetName);
        fields["Rộng (px)"].Text = "60";
        Descendants<Button>(resize).First(b => Equals(b.Content, "Lưu bản sao")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var resizeAgain = new EditorWindow(app, ToolKind.Resize, [source], image); resizeAgain.Show();
        Check("Resize long side is remembered", () => Assert(Descendants<TextBox>(resizeAgain).First(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Rộng (px)").Text == "60", "60 px restored"));
        resizeAgain.Close();
        var finish = DateTime.UtcNow.AddSeconds(15);
        while (app.Queue.HasPending && DateTime.UtcNow < finish) await Task.Delay(30);
        app.Queue.Jobs.Clear();

        // Output folder chosen at queue time.
        app.Settings.OutputMode = OutputLocation.Subfolder;
        app.Queue.Add([new ImageRequest(source, ToolKind.Webp)]);
        app.Settings.OutputMode = OutputLocation.Beside;
        finish = DateTime.UtcNow.AddSeconds(15);
        while (app.Queue.HasPending && DateTime.UtcNow < finish) await Task.Delay(30);
        Check("Queued jobs keep the output folder chosen when they were added", () =>
            Assert(Path.GetDirectoryName(app.Queue.Jobs.Single().Output) == Path.Combine(Path.GetDirectoryName(source)!, "Orbit"), "written to Orbit subfolder"));
        app.Queue.Jobs.Clear();

        // Commands from the context menu.
        app.ShowHome();
        app.RunShellCommand(new(ToolKind.Webp, [source]));
        finish = DateTime.UtcNow.AddSeconds(15);
        await Task.Delay(300);
        while (app.Queue.HasPending && DateTime.UtcNow < finish) await Task.Delay(30);
        bool Status(string text) => Descendants<TextBlock>(app.MainWindow).Any(t => t.Text.Contains(text));
        app.RunShellCommand(new(ToolKind.PdfWord, [source])); await Task.Delay(500); var wrongType = Status("không dùng được");
        app.RunShellCommand(new(ToolKind.Crop, [source, source + ".missing", WriteImage("shell-second.png", 10, 10)])); await Task.Delay(200); var tooMany = Status("chỉ nhận một file");
        Check("Context menu commands run matching tools and explain the rest", () =>
        {
            Assert(app.Queue.Jobs.Count == 1 && app.Queue.Jobs[0].Request.Tool == ToolKind.Webp && app.Queue.Jobs[0].State == JobState.Completed, "WebP ran once: " + string.Join(",", app.Queue.Jobs.Select(j => j.Request.Tool + ":" + j.Status)));
            Assert(wrongType && tooMany, $"mismatch explained (type {wrongType}, count {tooMany})");
        });
        app.Queue.Jobs.Clear();
        app.Settings.Defaults = new();
    }
}
