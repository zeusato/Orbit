using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;
using Orbit.App.Imaging;
using Orbit.App.Interop;
using Orbit.App.Services;
using Orbit.App.UI;
using Forms = System.Windows.Forms;

namespace Orbit.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var verify = args.Length >= 2 && args[0] == "--render-previews";
        using var mutex = new Mutex(true, "Local\\Orbit.Native." + Environment.UserName, out var first);
        if (!first && !verify && ShellCommand.TryParse(args, out var shell))
            return ShellPipe.Send(shell) ? 0 : 1;
        if (!first && !verify)
        {
            try { using var signal = EventWaitHandle.OpenExisting("Local\\Orbit.Show." + Environment.UserName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }
        if (!verify) ShellInbox.Start();
        var app = new OrbitApplication(args);
        try { return app.Run(); }
        catch (Exception error)
        {
            try { Directory.CreateDirectory(AppSettings.DataDirectory); File.WriteAllText(Path.Combine(AppSettings.DataDirectory, "startup-error.txt"), error.ToString()); } catch { }
            MessageBox.Show("Orbit gặp lỗi và cần đóng.\n" + error.Message + "\n\nChi tiết được lưu trong thư mục dữ liệu Orbit.", "Orbit", MessageBoxButton.OK, MessageBoxImage.Error); return 1;
        }
    }
}

public sealed class OrbitApplication : Application
{
    private readonly string[] _args;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _icon;
    private DragHotkey? _hotkey;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showRegistration;
    private bool _openingEditor;
    private HomeWindow? _home;
    public ProgressToast? Progress { get; private set; }
    public AppSettings Settings { get; } = AppSettings.Load();
    public JobQueue Queue { get; private set; } = null!;
    public RadialWindow Menu { get; private set; } = null!;
    public UpdateService Updates { get; private set; } = null!;
    public bool UpdateInProgress { get; set; }
    public bool IsIdleForUpdate(bool manual) => !Queue.HasPending && !_openingEditor && _home?.IsPasting != true && !Menu.IsVisible &&
        (Native.GetAsyncKeyState(1) & 0x8000) == 0 &&
        !Windows.Cast<Window>().Any(w => w.IsVisible && (!manual || w is not (HomeWindow or SettingsWindow or UpdateWindow)));

    public OrbitApplication(string[] args) { _args = args; ShutdownMode = ShutdownMode.OnExplicitShutdown; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); Theme.Apply(Settings); Theme.InstallStyles();
        // Diagnostic and test sessions keep no history on disk.
        var history = _args.Contains("--no-integrations") || _args.Contains("--render-previews") ? null : Path.Combine(AppSettings.DataDirectory, "history.json");
        Queue = new JobQueue(Dispatcher, history) { OutputFor = source => ClipboardImages.OutputDirectory(source, Settings) }; Menu = new RadialWindow(this); Updates = new UpdateService(this); _home = new HomeWindow(this);
        MainWindow = _home;
        // Progress for long jobs in the notification corner; hidden while the main window is in front.
        if (!_args.Contains("--render-previews"))
            Progress = new ProgressToast(Queue, ShowHome, () => UpdateInProgress || (_home.IsVisible && _home.IsActive && _home.WindowState != WindowState.Minimized));
        // Diagnostic mode also lets integration tests exercise the real WPF views
        // without installing global hooks or a notification-area icon.
        if (_args.Contains("--no-integrations")) { ShowHome(); return; }
        if (_args.Length >= 2 && _args[0] == "--render-previews")
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => RenderPreviews(_args[1])); return;
        }
        if (!_args.Contains("--update-health") && TryRecoverInterruptedUpdate()) return;
        _icon = OrbitIcon.TrayIcon(Math.Max(16, Forms.SystemInformation.SmallIconSize.Width));
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "Orbit · Công cụ xử lý file", Visible = true };
        var context = new Forms.ContextMenuStrip();
        context.Items.Add("Mở Orbit", null, (_, _) => Dispatcher.BeginInvoke(ShowHome));
        context.Items.Add("Tiện ích · QR, Base64, văn bản", null, (_, _) => Dispatcher.BeginInvoke(ShowQuickTools));
        context.Items.Add("Cài đặt", null, (_, _) => Dispatcher.BeginInvoke(() => new SettingsWindow(this).ShowDialog()));
        context.Items.Add("Cập nhật Orbit", null, (_, _) => Dispatcher.BeginInvoke(() => new UpdateWindow(this).Show()));
        context.Items.Add(new Forms.ToolStripSeparator());
        context.Items.Add("Thoát Orbit", null, (_, _) => Dispatcher.BeginInvoke(RequestExit));
        _tray.ContextMenuStrip = context; _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowHome);
        _tray.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(ShowHome);
        Queue.Finished += job =>
        {
            if (_home.IsVisible) return;
            _tray.BalloonTipTitle = job.State == JobState.Completed ? "Orbit · Đã lưu bản sao" : "Orbit · Tác vụ";
            _tray.BalloonTipText = job.State == JobState.Completed ? Path.GetFileName(job.Output) ?? "Đã xử lý ảnh" : job.Status;
            _tray.ShowBalloonTip(2500);
        };
        try
        {
            _hotkey = new DragHotkey(() => Settings.EffectiveHotkey);
            _hotkey.Requested += cursor => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (!UpdateInProgress) Menu.ShowMenu(cursor); });
            _hotkey.QuickRequested += cursor => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ToggleQuickMenu(cursor));
            _hotkey.Cancelled += () => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (Menu.IsVisible) Menu.HideMenu(); });
            _hotkey.Released += () => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (!Menu.NativeDragActive || !Menu.IsVisible) return;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                timer.Tick += (_, _) => { timer.Stop(); if (Menu.NativeDragActive && Menu.IsVisible && (Native.GetAsyncKeyState(1) & 0x8000) == 0) Menu.HideMenu(); };
                timer.Start();
            });
        }
        catch (Exception error) { _home.SetStatus("Phím gọi chưa hoạt động: " + error.Message + " · Có thể chọn hoặc thả ảnh vào cửa sổ này."); }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Orbit.Show." + Environment.UserName);
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.BeginInvoke(ShowHome), null, Timeout.Infinite, false);
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        _shellBatch.Tick += (_, _) => FlushShell();
        ShellInbox.Attach(command => Dispatcher.BeginInvoke(() => QueueShell(command)));
        SyncShellMenu();
        var fromShell = ShellCommand.TryParse(_args, out var launch);
        if (fromShell) QueueShell(launch);
        if (!_args.Contains("--background") && !fromShell)
        {
            ShowHome();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, OfferStartup);
        }
        Updates.Start();
        var healthIndex = Array.IndexOf(_args, "--update-health");
        if (healthIndex >= 0 && healthIndex + 1 < _args.Length && Guid.TryParseExact(_args[healthIndex + 1], "N", out _))
        {
            var healthId = _args[healthIndex + 1];
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                try { using var health = EventWaitHandle.OpenExisting("Local\\Orbit.Update.Health." + healthId); health.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
            });
        }
    }

    // An update helper killed mid-replacement leaves a journal without a result marker. This
    // process runs from the install folder, so a copied helper restores the backup after we exit.
    private bool TryRecoverInterruptedUpdate()
    {
        Process? helper = null;
        try
        {
            var root = AppContext.BaseDirectory;
            var backup = UpdateTransaction.FindIncomplete(root).LastOrDefault();
            if (backup == null) return false;
            if (File.Exists(Path.Combine(backup, "recovery-attempted.txt")))
            {
                _home?.SetStatus("Bản cập nhật trước bị gián đoạn và chưa tự khôi phục được. Xem thư mục .orbit-backup trong thư mục Orbit.");
                return false;
            }
            try { using var probe = new FileStream(Path.Combine(root, ".orbit-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return false; } // A helper is still working on it.
            var id = Path.GetFileName(backup); var runner = Path.Combine(AppSettings.DataDirectory, "updates", Guid.NewGuid().ToString("N"), "runner");
            UpdatePackage.CopyRunner(root, runner);
            using var self = Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(runner)!, "request.json"), System.Text.Json.JsonSerializer.Serialize(new UpdatePlan(root, self.Id, self.StartTime.ToUniversalTime().Ticks, id, !_args.Contains("--background"))));
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\Orbit.Update.Ready." + id);
            helper = Process.Start(new ProcessStartInfo(Path.Combine(runner, "Orbit.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, Arguments = "--recover", WorkingDirectory = runner });
            if (helper == null || !ready.WaitOne(TimeSpan.FromSeconds(30))) { if (helper is { HasExited: false }) helper.Kill(); return false; }
            Shutdown(); return true;
        }
        catch (Exception error) { _home?.SetStatus("Chưa khôi phục được bản cập nhật bị gián đoạn: " + error.Message); return false; }
        finally { helper?.Dispose(); }
    }

    private readonly List<ShellCommand> _shellPending = [];
    private readonly DispatcherTimer _shellBatch = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Requests from Explorer arrive one process per selected file; wait briefly and run them as one batch.
    public void QueueShell(ShellCommand command) { _shellPending.Add(command); _shellBatch.Stop(); _shellBatch.Start(); }
    private void FlushShell()
    {
        _shellBatch.Stop();
        var batch = ShellCommand.Merge(_shellPending); _shellPending.Clear();
        foreach (var command in batch) RunShellCommand(command);
    }
    public async void RunShellCommand(ShellCommand command)
    {
        var files = command.Files.Where(File.Exists).ToArray();
        if (files.Length == 0) { Notify("Không tìm thấy file được chọn."); return; }
        if (command.Tool is not ToolKind tool)
        {
            // Ring menu at the pointer, in click mode: the right-click already ended any drag.
            Native.GetCursorPos(out var cursor); Menu.ShowMenu(cursor, files); return;
        }
        try
        {
            if (!ToolCatalog.SupportsCount(tool, files.Length))
            { Notify(tool is ToolKind.PdfMerge or ToolKind.AudioMerge ? $"{ToolCatalog.Label(tool)}: chọn từ 2 file trở lên." : $"{ToolCatalog.Label(tool)} chỉ nhận một file mỗi lần."); return; }
            // The context menu is chosen by extension; check the content before running.
            var resolved = await Task.Run(() => FileToolResolver.ResolveWithNotice(files));
            if (!resolved.Tools.Contains(tool))
            { Notify($"{ToolCatalog.Label(tool)} không dùng được cho file này." + (resolved.Notice == null ? "" : " " + resolved.Notice.Replace('\n', ' '))); return; }
            RunTool(tool, files);
        }
        catch (Exception error) { Notify("Không đọc được file: " + error.Message); }
    }
    private void Notify(string message)
    {
        if (_tray != null && _home?.IsVisible != true) { _tray.BalloonTipTitle = "Orbit"; _tray.BalloonTipText = message; _tray.ShowBalloonTip(4000); }
        else { ShowHome(); _home?.SetStatus(message); }
    }
    public void SyncShellMenu()
    {
        if (_args.Contains("--no-integrations")) return;
        var enabled = Settings.ShellMenu; var exe = Environment.ProcessPath!;
        Task.Run(() =>
        {
            try { if (enabled) ShellMenu.Register(exe); else ShellMenu.Unregister(); }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            { Dispatcher.BeginInvoke(() => _home?.SetStatus("Chưa cập nhật được menu chuột phải: " + error.Message)); }
        });
    }

    private void OfferStartup()
    {
        if (Settings.StartupPromptCompleted || Dispatcher.HasShutdownStarted) return;
        try
        {
            var enabled = AppSettings.StartupEnabled;
            if (Settings.ShouldOfferStartup(_args.Contains("--background"), enabled))
                new StartupWindow(this) { Owner = _home }.ShowDialog();
            else if (enabled) Settings.CompleteStartupPrompt();
        }
        catch (Exception error) { _home?.SetStatus("Chưa thiết lập được chạy cùng Windows: " + error.Message); }
    }

    public void ShowHome() { if (_home == null) return; _home.Show(); if (_home.WindowState == WindowState.Minimized) _home.WindowState = WindowState.Normal; _home.Activate(); }
    public void ShowQuickTools()
        => ShowQuickTools(null);
    public void ToggleQuickMenu(Native.Point cursor)
    {
        if (UpdateInProgress) return;
        if (Menu.IsVisible && Menu.QuickMode) Menu.HideMenu();
        else Menu.ShowQuickMenu(cursor);
    }
    private void ShowQuickTools(int? index)
    {
        if (UpdateInProgress) { _home?.SetStatus("Đang cập nhật. Hãy thử lại sau."); return; }
        var window = Windows.OfType<QuickToolsWindow>().FirstOrDefault() ?? new QuickToolsWindow(this);
        if (index.HasValue) window.SelectTool(index.Value);
        window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate();
    }
    public void RefreshTheme()
    {
        Theme.Apply(Settings); Menu.RefreshBackdrop();
        Progress?.RefreshTheme();
        foreach (Window window in Windows) if (window is GlassWindow glass) glass.RefreshBackdrop();
        _home?.UpdateInstruction();
    }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(RefreshTheme);

    public async void RunTool(ToolKind tool, string[] files)
    {
        if (UpdateInProgress) { _home?.SetStatus("Đang chuẩn bị cập nhật. Hãy thử lại sau."); return; }
        var quickIndex = Array.IndexOf(ToolCatalog.QuickTools, tool);
        if (quickIndex >= 0) { ShowQuickTools(quickIndex); return; }
        if (files.Length == 0 || files.Length > 128) return;
        if (tool == ToolKind.More) return;
        if (tool == ToolKind.ImagePdf)
        { new ImagePdfChoiceWindow(this, files).Show(); return; }
        if (tool is ToolKind.PdfMerge or ToolKind.PdfSplit)
        { new PdfEditorWindow(this, tool, files).Show(); return; }
        if (tool == ToolKind.PdfCompress)
        { if (files.Length == 1) new PdfCompressionWindow(this, files[0]).Show(); return; }
        if (tool == ToolKind.AudioMerge)
        { Queue.Add([new ImageRequest(files[0], tool, Sources: files)]); return; }
        if (tool is ToolKind.VideoCompress or ToolKind.MediaTrim or ToolKind.VideoFrame or ToolKind.VideoGif)
        {
            if (_openingEditor) { ShowHome(); _home!.SetStatus("Đang mở trình chỉnh sửa. Hãy thử lại sau."); return; }
            _openingEditor = true;
            try
            {
                var info = await Task.Run(() => MediaEngine.Probe(files[0]));
                if (Dispatcher.HasShutdownStarted) return;
                var media = new MediaEditorWindow(this, tool, files, info); media.Show(); media.Activate();
            }
            catch (Exception error) { ShowHome(); _home!.SetStatus("Không mở được file: " + error.Message); }
            finally { _openingEditor = false; }
            return;
        }
        if (tool is not (ToolKind.Resize or ToolKind.Crop or ToolKind.Compress or ToolKind.Frame))
        { Queue.Add(files.Select(path => new ImageRequest(path, tool))); return; }
        if (_openingEditor) { ShowHome(); _home!.SetStatus("Đang mở trình chỉnh sửa. Hãy thử lại sau khi ảnh được tải."); return; }
        _openingEditor = true;
        try
        {
            var image = await Task.Run(() => new ImageEngine().Load(files[0]));
            if (Dispatcher.HasShutdownStarted) return;
            var editor = new EditorWindow(this, tool, files, image); editor.Show(); editor.Activate();
        }
        catch (Exception error) { ShowHome(); _home!.SetStatus("Không mở được ảnh: " + error.Message); }
        finally { _openingEditor = false; }
    }

    public void OpenResult(string path, bool folder)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("File kết quả đã được di chuyển hoặc xóa.");
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (folder) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error) { ShowHome(); _home!.SetStatus(error.Message); }
    }
    private void RequestExit()
    {
        if (Queue.HasPending)
        {
            var answer = MessageBox.Show("Còn tác vụ chưa hoàn tất. Hủy các tác vụ và thoát Orbit?", "Orbit", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            Queue.CancelAll();
            _home!.SetStatus("Đang hủy tác vụ trước khi thoát…");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => { if (!Queue.HasPending) { timer.Stop(); Shutdown(); } }; timer.Start(); return;
        }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        ShellInbox.Stop.Cancel(); Updates?.Dispose(); _hotkey?.Dispose(); _showRegistration?.Unregister(null); _showSignal?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _icon?.Dispose(); Queue?.Dispose(); base.OnExit(e);
    }


    private async void RenderPreviews(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder); Settings.Theme = "Dark"; RefreshTheme();
            var sample = PreviewData.CreateSample(Path.Combine(folder, "sample.jpg"));
            var webp = new ImageEngine().Execute(new ImageRequest(sample, ToolKind.Webp), CancellationToken.None);
            _ = new ImageEngine().Load(webp);
            var pdf = new DocumentEngine().Execute(new ImageRequest(sample, ToolKind.ImagePdf), CancellationToken.None);
            var pages = new DocumentEngine().Execute(new ImageRequest(pdf, ToolKind.PdfPng), CancellationToken.None);
            _ = new ImageEngine().Load(Path.Combine(pages, "page-0001.png"));
            // Exercise the published native OCR libraries and language data too.
            _ = new DocumentEngine().Execute(new ImageRequest(pdf, ToolKind.PdfText), CancellationToken.None);
            // Media pipeline in the published app: build a 1 s clip with Windows, then compress it and grab a frame.
            var clip = await global::Windows.Media.Editing.MediaClip.CreateFromImageFileAsync(await global::Windows.Storage.StorageFile.GetFileFromPathAsync(sample), TimeSpan.FromSeconds(1));
            var composition = new global::Windows.Media.Editing.MediaComposition(); composition.Clips.Add(clip);
            var clipProfile = global::Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(global::Windows.Media.MediaProperties.VideoEncodingQuality.Vga); clipProfile.Audio = null;
            var clipFile = await (await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(folder)).CreateFileAsync("sample.mp4", global::Windows.Storage.CreationCollisionOption.ReplaceExisting);
            await composition.RenderToFileAsync(clipFile, global::Windows.Media.Editing.MediaTrimmingPreference.Precise, clipProfile);
            var video = Path.Combine(folder, "sample.mp4");
            _ = await Task.Run(() => MediaEngine.Execute(new ImageRequest(video, ToolKind.VideoCompress, Media: new(Height: 240)), CancellationToken.None));
            _ = await Task.Run(() => MediaEngine.Execute(new ImageRequest(video, ToolKind.VideoFrame, Media: new(Start: TimeSpan.FromSeconds(.5))), CancellationToken.None));
            var image = new ImageEngine().Load(sample);
            _home!.Show(); SaveWindow(_home, Path.Combine(folder, "home-dark.png"));
            Menu.ShowMenu(new Native.Point(700, 420), [sample]); await Menu.Detection; SaveWindow(Menu, Path.Combine(folder, "menu-dark.png")); Menu.HideMenu();
            var editor = new EditorWindow(this, ToolKind.Crop, [sample], image); editor.Show(); SaveWindow(editor, Path.Combine(folder, "crop-dark.png")); editor.Close();
            var frame = new EditorWindow(this, ToolKind.Frame, [sample], image); frame.Show(); await Task.Delay(400); SaveWindow(frame, Path.Combine(folder, "frame-dark.png")); frame.Close();
            var toast = new ProgressToast(Queue, ShowHome, () => false);
            Queue.Add(Enumerable.Range(0, 8).Select(_ => new ImageRequest(pdf, ToolKind.PdfText)));
            for (int i = 0; i < 200 && !(toast.IsVisible && Queue.Jobs.Count(j => !j.CanCancel) >= 2); i++) await Task.Delay(50);
            if (toast.IsVisible) SaveWindow(toast, Path.Combine(folder, "progress-dark.png"));
            Queue.CancelAll(); while (Queue.HasPending) await Task.Delay(50); toast.Close();
            Settings.Theme = "Light"; RefreshTheme(); SaveWindow(_home, Path.Combine(folder, "home-light.png"));
            File.WriteAllText(Path.Combine(folder, "render-result.txt"), $"Native WPF renders and WebP/PDF/OCR/media runtime smoke checks completed. DWM accepted: home={_home.BackdropAvailable}, menu={Menu.BackdropAvailable}");
            Shutdown(0);
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(folder, "render-error.txt"), error.ToString()); Shutdown(1); }
    }
    private static void SaveWindow(Window window, string filename)
    {
        window.UpdateLayout();
        var source = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(source.ActualWidth), (int)Math.Ceiling(source.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(source); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(filename); encoder.Save(stream);
    }
}

public static class PreviewData
{
    public static string CreateSample(string path)
    {
        var width = 800; var height = 1000; var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var index = (y * width + x) * 4; var bottle = x is > 280 and < 520 && y is > 270 and < 830;
            pixels[index] = (byte)(bottle ? 195 + (x - 280) / 10 : 138 + y / 24);
            pixels[index + 1] = (byte)(bottle ? 216 + (x - 280) / 14 : 160 + y / 25);
            pixels[index + 2] = (byte)(bottle ? 204 + (x - 280) / 13 : 153 + x / 22); pixels[index + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze();
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream); return path;
    }
}
