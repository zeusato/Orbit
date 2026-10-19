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
        if (!first && !verify)
        {
            try { using var signal = EventWaitHandle.OpenExisting("Local\\Orbit.Show." + Environment.UserName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }
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
    public AppSettings Settings { get; } = AppSettings.Load();
    public JobQueue Queue { get; private set; } = null!;
    public RadialWindow Menu { get; private set; } = null!;
    public UpdateService Updates { get; private set; } = null!;
    public bool UpdateInProgress { get; set; }
    public bool IsIdleForUpdate(bool manual) => !Queue.HasPending && !_openingEditor && !Menu.IsVisible &&
        (Native.GetAsyncKeyState(1) & 0x8000) == 0 &&
        !Windows.Cast<Window>().Any(w => w.IsVisible && (!manual || w is not (HomeWindow or SettingsWindow or UpdateWindow)));

    public OrbitApplication(string[] args) { _args = args; ShutdownMode = ShutdownMode.OnExplicitShutdown; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); Theme.Apply(Settings); Theme.InstallStyles();
        Queue = new JobQueue(Dispatcher); Menu = new RadialWindow(this); Updates = new UpdateService(this); _home = new HomeWindow(this);
        MainWindow = _home;
        // Diagnostic mode also lets integration tests exercise the real WPF views
        // without installing global hooks or a notification-area icon.
        if (_args.Contains("--no-integrations")) { ShowHome(); return; }
        if (_args.Length >= 2 && _args[0] == "--render-previews")
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => RenderPreviews(_args[1])); return;
        }
        _icon = CreateIcon();
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "Orbit · Công cụ xử lý file", Visible = true };
        var context = new Forms.ContextMenuStrip();
        context.Items.Add("Mở Orbit", null, (_, _) => Dispatcher.BeginInvoke(ShowHome));
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
        if (!_args.Contains("--background"))
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
    public void RefreshTheme()
    {
        Theme.Apply(Settings); Menu.RefreshBackdrop();
        foreach (Window window in Windows) if (window is GlassWindow glass) glass.RefreshBackdrop();
        _home?.UpdateInstruction();
    }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(RefreshTheme);

    public async void RunTool(ToolKind tool, string[] files)
    {
        if (UpdateInProgress) { _home?.SetStatus("Đang chuẩn bị cập nhật. Hãy thử lại sau."); return; }
        if (files.Length == 0 || files.Length > 128) return;
        if (tool == ToolKind.More) return;
        if (tool == ToolKind.PdfMerge)
        { Queue.Add([new ImageRequest(files[0], tool, Sources: files)]); return; }
        if (tool is not (ToolKind.Resize or ToolKind.Crop or ToolKind.Compress))
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
        Updates?.Dispose(); _hotkey?.Dispose(); _showRegistration?.Unregister(null); _showSignal?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _icon?.Dispose(); Queue?.Dispose(); base.OnExit(e);
    }

    private static System.Drawing.Icon CreateIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(94, 222, 199), 2.5f);
            graphics.DrawEllipse(pen, 5, 5, 22, 22); graphics.DrawEllipse(pen, 2, 11, 28, 10);
        }
        var handle = bitmap.GetHicon();
        try { using var icon = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

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
            var image = new ImageEngine().Load(sample);
            _home!.Show(); SaveWindow(_home, Path.Combine(folder, "home-dark.png"));
            Menu.ShowMenu(new Native.Point(700, 420), [sample]); await Menu.Detection; SaveWindow(Menu, Path.Combine(folder, "menu-dark.png")); Menu.HideMenu();
            var editor = new EditorWindow(this, ToolKind.Crop, [sample], image); editor.Show(); SaveWindow(editor, Path.Combine(folder, "crop-dark.png")); editor.Close();
            Settings.Theme = "Light"; RefreshTheme(); SaveWindow(_home, Path.Combine(folder, "home-light.png"));
            File.WriteAllText(Path.Combine(folder, "render-result.txt"), $"Native WPF renders and WebP/PDF/OCR runtime smoke checks completed. DWM accepted: home={_home.BackdropAvailable}, menu={Menu.BackdropAvailable}");
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
