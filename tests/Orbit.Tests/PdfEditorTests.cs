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
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfPages = Orbit.Core.PdfPages;

internal static partial class Program
{
    private static string PdfFixture(string name)
    {
        var path = Path.Combine(_folder, name);
        using var pdf = new PdfDocument();
        foreach (var color in new[] { XBrushes.Red, XBrushes.Green, XBrushes.Blue })
        {
            var page = pdf.AddPage(); page.Width = XUnit.FromPoint(240); page.Height = XUnit.FromPoint(160);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(color, 0, 0, 240, 160);
        }
        pdf.Save(path); return path;
    }

    private static void CheckPdfAssembly(string source)
    {
        var engine = new DocumentEngine();
        Check("PDF range parsing preserves requested order and rejects invalid pages", () =>
        {
            Assert(PdfPages.Parse("3, 1–2, 2", 3).SequenceEqual(new[] { 2, 0, 1 }), "ordered, unique pages");
            Assert(PdfPages.Parse("", 3).SequenceEqual(new[] { 0, 1, 2 }), "all pages");
            foreach (var range in new[] { "0", "4", "3-1", "1,,2", "1-", "-2", "one", "1-2-3", "2147483647" })
                Throws<ArgumentException>(() => PdfPages.Parse(range, 3));
        });
        Check("PDF merge is reachable from a single PDF and mixed PDF/image selections", () =>
        {
            Assert(ToolCatalog.ForFiles([FileKind.Pdf]).Contains(ToolKind.PdfMerge), "single PDF can open composer");
            Assert(ToolCatalog.ForFiles([FileKind.Pdf, FileKind.Png]).Contains(ToolKind.PdfMerge), "mixed selection");
            Assert(!ToolCatalog.ForFiles([FileKind.Pdf, FileKind.Docx]).Contains(ToolKind.PdfMerge), "unsupported mix");
            Assert(!ToolCatalog.ForFiles([FileKind.Pdf, FileKind.Pdf]).Contains(ToolKind.PdfSplit), "split editor uses one document");
        });
        var pdf = PdfFixture("pdf-editor-colors.pdf"); var original = Hash(pdf); var imageHash = Hash(source);
        Check("Ordered mixed PDF/image composition preserves page dimensions, colors and sources", () =>
        {
            var output = engine.Execute(new(pdf, ToolKind.PdfMerge, Sources: [pdf, source, pdf]), default);
            using var read = PdfReader.Open(output, PdfDocumentOpenMode.Import);
            Assert(read.PageCount == 7, "all PDF and image pages");
            Assert(read.Pages[0].Width.Point == 240 && read.Pages[3].Width.Point == 60, "insertion order, no filename sorting");
            var info = PdfAssembly.Inspect(output);
            foreach (var pair in new[] { (0, 2), (2, 0), (4, 2) })
            {
                var frame = PdfAssembly.Preview(info, pair.Item1, default);
                Assert(frame.PixelWidth == 900 && frame.PixelHeight == 600, "preview keeps landscape aspect ratio");
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
                var center = ((converted.PixelHeight / 2) * converted.PixelWidth + converted.PixelWidth / 2) * 4;
                Assert(pixels[center + pair.Item2] > 240, "rendered page color " + pair.Item1);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
                using var file = File.Create(Path.Combine(_folder, $"pdf-composed-page-{pair.Item1 + 1}.png")); encoder.Save(file);
            }
            Assert(Hash(pdf) == original && Hash(source) == imageHash, "source hashes");
        });
        Check("Multiple images produce one PDF and never overwrite an existing output", () =>
        {
            var first = engine.Execute(new(source, ToolKind.ImagePdf, Sources: [source, source]), default);
            var hash = Hash(first);
            var next = engine.Execute(new(source, ToolKind.ImagePdf, Sources: [source, source]), default);
            using var read = PdfReader.Open(next, PdfDocumentOpenMode.Import);
            Assert(read.PageCount == 2 && first != next && Hash(first) == hash, "two pages, collision safety");
        });
        Check("PDF split exports selected pages into one PDF in entered order", () =>
        {
            var output = engine.Execute(new(pdf, ToolKind.PdfSplit, Pdf: new("3, 1", false)), default);
            using var read = PdfReader.Open(output, PdfDocumentOpenMode.Import);
            Assert(read.PageCount == 2, "selected count");
            var frame = PdfAssembly.Preview(PdfAssembly.Inspect(output), 0, default);
            var bgr = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var bytes = new byte[bgr.PixelWidth * bgr.PixelHeight * 4]; bgr.CopyPixels(bytes, bgr.PixelWidth * 4, 0);
            var center = (bgr.PixelHeight / 2 * bgr.PixelWidth + bgr.PixelWidth / 2) * 4;
            Assert(bytes[center] > 240 && bytes[center + 2] < 10, "page 3 blue comes first");
        });
        Check("PDF split exports separate selected files with original page numbers", () =>
        {
            var folder = engine.Execute(new(pdf, ToolKind.PdfSplit, Pdf: new("1, 3")), default);
            Assert(Directory.GetFiles(folder).Select(Path.GetFileName).Order().SequenceEqual(new[] { "page-0001.pdf", "page-0003.pdf" }), "selected filenames");
            Assert(Hash(pdf) == original, "source remains unchanged");
        });
        Check("PDF assembly rejects unsupported input, page overflow and invalid ranges without partial output", () =>
        {
            var bad = Path.Combine(_folder, "not-an-image.png"); File.WriteAllText(bad, "invalid");
            Throws<NotSupportedException>(() => engine.Execute(new(pdf, ToolKind.PdfMerge, Sources: [pdf, bad]), default));
            Throws<NotSupportedException>(() => engine.Execute(new(pdf, ToolKind.PdfMerge, Sources: Enumerable.Repeat(pdf, 67).ToArray()), default));
            Throws<ArgumentException>(() => engine.Execute(new(pdf, ToolKind.PdfSplit, Pdf: new("4", false)), default));
            Assert(!Directory.EnumerateFileSystemEntries(_folder, ".orbit-*").Any(), "no staging residue");
        });
        Check("PDF assembly cancellation removes staging output and respects configured output folder", () =>
        {
            var destination = Path.Combine(_folder, "pdf-custom-output");
            using var cancel = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => engine.Execute(new(pdf, ToolKind.PdfMerge, Sources: [pdf, source], OutputDirectory: destination),
                cancel.Token, p => { if (p >= 40) cancel.Cancel(); }));
            Assert(!Directory.EnumerateFileSystemEntries(destination).Any(), "cancelled output folder empty");
            var output = engine.Execute(new(pdf, ToolKind.PdfSplit, OutputDirectory: destination, Pdf: new("2", false)), default);
            Assert(Path.GetDirectoryName(output) == destination && Hash(pdf) == original, "configured folder and unchanged source");
        });
    }

    private static async Task CheckPdfEditors(OrbitApplication app)
    {
        var source = WriteImage("pdf-ui-image.png", 400, 300);
        var pdf = PdfFixture("pdf-ui-colors.pdf");
        static Button Button(Window window, string text) => Descendants<Button>(window).Single(b => Equals(b.Content, text));
        static void Click(Window window, string text) => Button(window, text).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        static async Task Finish(ImageJob job)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (job.CanCancel && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert(job.State == JobState.Completed, "UI request completed: " + job.Error);
        }
        static void Screenshot(Window window, string name)
        {
            window.UpdateLayout(); var content = (FrameworkElement)window.Content;
            var shot = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            shot.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(shot));
            using var file = File.Create(Path.Combine(_folder, name)); encoder.Save(file);
        }
        var initial = app.Queue.Jobs.Count;
        app.RunTool(ToolKind.ImagePdf, [source]);
        var choice = app.Windows.OfType<ImagePdfChoiceWindow>().Single();
        Screenshot(choice, "image-pdf-choice-ui.png");
        Assert(app.Queue.Jobs.Count == initial, "choice does not execute automatically");
        Click(choice, "Thêm trang…");
        var editor = app.Windows.OfType<PdfEditorWindow>().Single();
        try
        {
            await editor.Initialization; await editor.AddFilesAsync([pdf]); await editor.PreviewTask;
            var list = Descendants<ListBox>(editor).Single(); list.SelectedIndex = 1;
            Click(editor, "Lên"); await editor.PreviewTask;
            Assert(((PdfInput)list.Items[0]).Path == pdf, "reordered PDF before image");
            Assert(!app.IsIdleForUpdate(true), "editor prevents update");
            var before = list.Items.Count;
            await editor.AddFilesAsync([Path.Combine(_folder, "missing-pdf.pdf")]);
            Assert(list.Items.Count == before && Button(editor, "Xuất PDF").IsEnabled, "invalid add leaves valid selection intact");
            await editor.AddFilesAsync([source]); list.SelectedIndex = 2; Click(editor, "Bỏ khỏi danh sách");
            Assert(list.Items.Count == before && File.Exists(source), "remove affects list only");
            list.SelectedIndex = 0; await editor.PreviewTask;
            Screenshot(editor, "pdf-composer-ui.png");
            Click(editor, "Xuất PDF");
            var job = app.Queue.Jobs.First();
            Assert(job.Request.Sources!.SequenceEqual(new[] { pdf, source }), "saved exact list order");
            await Finish(job);
            using var output = PdfReader.Open(job.Output!, PdfDocumentOpenMode.Import); Assert(output.PageCount == 4, "four page output");
        }
        finally { editor.Close(); }
        var split = new PdfEditorWindow(app, ToolKind.PdfSplit, [pdf]); split.Show();
        try
        {
            await split.Initialization; await split.PreviewTask;
            var pages = Descendants<TextBox>(split).Single(t => t.Name == "PageRange"); pages.Text = "99";
            Assert(!Button(split, "Tách trang").IsEnabled, "out of range blocks save");
            pages.Text = "3, 1"; Descendants<CheckBox>(split).Single().IsChecked = false;
            Screenshot(split, "pdf-split-ui.png");
            Click(split, "Tách trang");
            var job = app.Queue.Jobs.First();
            Assert(job.Request.Pdf == new PdfOptions("3, 1", false), "current values queued");
            await Finish(job);
            using var output = PdfReader.Open(job.Output!, PdfDocumentOpenMode.Import);
            Assert(output.PageCount == 2, "split UI output");
        }
        finally { split.Close(); }
        // Closing while loading must neither enqueue a job nor touch disposed UI state.
        var closed = new PdfEditorWindow(app, ToolKind.PdfMerge, [pdf]); closed.Show(); closed.Close();
        await closed.Initialization; await closed.PreviewTask;
        var jobs = app.Queue.Jobs.Count;
        app.RunTool(ToolKind.ImagePdf, [source]); app.Windows.OfType<ImagePdfChoiceWindow>().Single().Close();
        Assert(app.Queue.Jobs.Count == jobs, "cancel choice queues nothing");
        app.RunTool(ToolKind.ImagePdf, [source]);
        Click(app.Windows.OfType<ImagePdfChoiceWindow>().Single(), "Chuyển ngay");
        await Finish(app.Queue.Jobs.First());
        using (var quick = PdfReader.Open(app.Queue.Jobs.First().Output!, PdfDocumentOpenMode.Import)) Assert(quick.PageCount == 1, "quick convert");
        var home = (HomeWindow)app.MainWindow;
        var image = Engine.Load(source);
        await home.PasteImageAsync(image); await app.Menu.Detection;
        Assert(app.Menu.VisibleTools.Contains(ToolKind.Webp), "clipboard image opens image tools");
        Assert(app.Queue.Jobs.Count == jobs + 1, "paste requires explicit tool selection");
        app.Menu.HideMenu();
        Check("Clipboard snapshot opens tools without running one or changing the clipboard", () => { });
        var pasted = new PdfEditorWindow(app, ToolKind.ImagePdf, [source]); pasted.Show();
        try
        {
            await pasted.Initialization;
            await pasted.PasteImageAsync(image); await pasted.PreviewTask;
            var list = Descendants<ListBox>(pasted).Single();
            Assert(list.Items.Count == 2 && ClipboardImages.IsStoredImage(((PdfInput)list.Items[1]).Path), "paste appends cached image page");
            var cache = ((PdfInput)list.Items[1]).Path;
            Assert(ReadPixels(cache).SequenceEqual(ReadPixels(source)), "clipboard snapshot lossless");
            var settings = new AppSettings { OutputMode = OutputLocation.Beside };
            Assert(ClipboardImages.OutputDirectory(cache, settings) == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Orbit"), "clipboard output is visible Pictures/Orbit");
            settings.OutputMode = OutputLocation.Folder; settings.OutputFolder = _folder;
            Assert(ClipboardImages.OutputDirectory(cache, settings) == _folder, "explicit settings override clipboard default");
            Descendants<TextBox>(pasted).Single(t => t.Name == "OutputName").Text = "clipboard combined.pdf";
            Descendants<TextBox>(pasted).Single(t => t.Name == "OutputFolder").Text = Path.Combine(_folder, "clipboard-export");
            Screenshot(pasted, "pdf-paste-output-ui.png");
            Click(pasted, "Xuất PDF"); await Finish(app.Queue.Jobs.First());
            Assert(app.Queue.Jobs.First().Output == Path.Combine(_folder, "clipboard-export", "clipboard combined.pdf"), "editor uses entered output");
            using var output = PdfReader.Open(app.Queue.Jobs.First().Output!, PdfDocumentOpenMode.Import);
            Assert(output.PageCount == 2, "pasted image exported as page");
        }
        finally { pasted.Close(); }
        Check("Pasted image becomes a PDF page with per-export filename and folder", () => { });
        app.RunTool(ToolKind.PdfCompress, [Path.Combine(_folder, "compression-source.pdf")]);
        var compression = app.Windows.OfType<PdfCompressionWindow>().Single();
        try
        {
            Descendants<Slider>(compression).Single().Value = 45;
            Descendants<TextBox>(compression).Single(t => t.Name == "OutputName").Text = "ui-compressed";
            Descendants<TextBox>(compression).Single(t => t.Name == "OutputFolder").Text = _folder;
            Screenshot(compression, "pdf-compression-ui.png");
            Click(compression, "Nén PDF"); await Finish(app.Queue.Jobs.First());
            var compressed = app.Queue.Jobs.First();
            Assert(compressed.Request.Quality == 45 && compressed.Status.Contains("giảm") && compressed.Status.Contains("→"), "real compression sizes shown");
            var history = Path.Combine(_folder, "compression-history.json");
            using (var queue = new JobQueue(app.Dispatcher, history)) { queue.Jobs.Add(compressed); queue.SaveHistory(); }
            using (var restored = new JobQueue(app.Dispatcher, history)) Assert(restored.Jobs.Single().Status == compressed.Status, "compression details survive restart");
        }
        finally { compression.Close(); }
        Check("Compression WPF flow queues selected quality and persists actual size result", () => { });
        app.Queue.ClearHistory();
        Check("PDF composer and split WPF flows preserve explicit selection, order and validation", () => { });
    }
}
