using System.Windows.Automation;
using System.Windows.Interop;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;
using System.Windows.Threading;
using Orbit.App.Interop;
using WpfPath = System.Windows.Shapes.Path;
using Orbit.App.Services;
using System.Windows.Media.Effects;

namespace Orbit.App.UI;

public sealed class RadialWindow : Window
{
    private readonly OrbitApplication _app;
    private readonly Canvas _canvas = new() { Width = 400, Height = 400, Background = Brushes.Transparent };
    private readonly Canvas _sectors = new() { Width = 400, Height = 400 };
    private readonly System.Windows.Controls.Image _glassImage = new() { Width = 400, Height = 400, Stretch = Stretch.Fill, IsHitTestVisible = false, Effect = new BlurEffect { Radius = 20 } };
    private readonly Ellipse _glassTint = new() { Width = 380, Height = 380, IsHitTestVisible = false, StrokeThickness = 1 };
    private readonly List<WpfPath> _surfaces = [];
    private readonly List<StackPanel> _labels = [];
    private readonly List<Button> _buttons = [];
    private readonly TextBlock _file = Ui.Text("Kéo file vào", 12);
    private readonly TextBlock _hint = Ui.Text("Di chuyển nhẹ\nđể nhận file", 11, true);
    private readonly Ellipse _hub = new() { Width = 116, Height = 116, StrokeThickness = 1 };
    private string[] _files = [];
    private int _selected = -1;
    private Native.Point _activationPoint;
    private bool _armed;
    private bool _ready;
    private string? _detectionError;
    private string? _notice;
    private bool _moreLatched;
    private readonly DispatcherTimer _moreTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private int _generation, _page;
    private CancellationTokenSource? _detectionCancellation;
    private IReadOnlyList<ToolKind> _tools = [];
    private ToolKind[] _pageTools = [];
    public Task Detection { get; private set; } = Task.CompletedTask;
    public IReadOnlyList<ToolKind> VisibleTools => _pageTools;
    public bool NativeDragActive { get; private set; }
    public string LastDropDiagnostic { get; private set; } = "Chưa nhận phiên kéo thả";
    public bool BackdropAvailable { get; private set; }

    public RadialWindow(OrbitApplication app)
    {
        _app = app; Title = "Orbit · Menu tròn"; Width = Height = 400;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Background = Brushes.Transparent; AllowDrop = true;
        _canvas.Clip = new EllipseGeometry(new Point(200, 200), 190, 190);
        Content = _canvas;
        _canvas.Children.Add(_glassImage);
        Canvas.SetLeft(_glassTint, 10); Canvas.SetTop(_glassTint, 10); _canvas.Children.Add(_glassTint);
        _canvas.Children.Add(_sectors);
        Canvas.SetLeft(_hub, 142); Canvas.SetTop(_hub, 142); _hub.IsHitTestVisible = false; _canvas.Children.Add(_hub);
        var core = new StackPanel { Width = 106, IsHitTestVisible = false };
        var orbit = Ui.Text("◎", 26); orbit.TextAlignment = TextAlignment.Center; orbit.Margin = new Thickness(0, 0, 0, 3);
        _file.TextAlignment = _hint.TextAlignment = TextAlignment.Center;
        _file.TextWrapping = TextWrapping.NoWrap; _file.TextTrimming = TextTrimming.CharacterEllipsis;
        _hint.Margin = new Thickness(0, 5, 0, 0);
        core.Children.Add(orbit); core.Children.Add(_file); core.Children.Add(_hint);
        Canvas.SetLeft(core, 147); Canvas.SetTop(core, 154); _canvas.Children.Add(core);
        SourceInitialized += (_, _) => RefreshBackdrop();
        ContentRendered += (_, _) => Native.SetCircularRegion(this);
        SizeChanged += (_, _) => Native.SetCircularRegion(this);
        PreviewDragEnter += Enter;
        PreviewDragOver += Over;
        PreviewDrop += Dropped;
        PreviewDragLeave += (_, _) => { _moreTimer.Stop(); Select(-1); };
        _moreTimer.Tick += (_, _) =>
        {
            _moreTimer.Stop();
            if (!NativeDragActive || !CanExecute(_selected) || _pageTools[_selected] != ToolKind.More) return;
            _moreLatched = true; _page++; RebuildTools(); Select(-1);
            Native.GetCursorPos(out _activationPoint); _armed = false;
        };
        MouseMove += (_, e) => { if (!NativeDragActive) Select(Hit(e.GetPosition(_canvas))); };
        MouseLeave += (_, _) => { if (!NativeDragActive) Select(-1); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { HideMenu(); e.Handled = true; }
            else if (e.Key is >= Key.D1 and <= Key.D6) { Execute((int)e.Key - (int)Key.D1); e.Handled = true; }
        };
        Deactivated += (_, _) => { if (!NativeDragActive) HideMenu(); };
        RefreshColors();
    }

    private void BuildSector(int index)
    {
        var step = 360d / _pageTools.Length;
        var angle = -90 + step * index;
        var half = step / 2 - 1.5;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var outerStart = P(RadialGeometry.OuterRadius, angle - half);
            context.BeginFigure(outerStart, true, true);
            context.ArcTo(P(RadialGeometry.OuterRadius, angle + half), new Size(190, 190), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(P(RadialGeometry.InnerRadius, angle + half), true, false);
            context.ArcTo(P(RadialGeometry.InnerRadius, angle - half), new Size(65, 65), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        var path = new WpfPath { Data = geometry, StrokeThickness = 1 };
        var sectorCanvas = new Canvas { Width = 400, Height = 400 };
        sectorCanvas.Children.Add(path); _surfaces.Add(path);
        var position = P(130, angle);
        var label = new StackPanel { Width = 94, IsHitTestVisible = false };
        label.Children.Add(ToolIcon(_pageTools[index]));
        var name = Ui.Text(ToolCatalog.Label(_pageTools[index]), 14); name.TextAlignment = TextAlignment.Center; name.Margin = new Thickness(0, 8, 0, 0); label.Children.Add(name);
        Canvas.SetLeft(label, position.X - 47); Canvas.SetTop(label, position.Y - 26); sectorCanvas.Children.Add(label); _labels.Add(label);
        var button = new Button { Width = 400, Height = 400, Clip = geometry, Content = sectorCanvas, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        template.VisualTree = presenter; button.Template = template;
        AutomationProperties.SetName(button, ToolCatalog.Description(_pageTools[index]));
        button.Click += (_, _) => { if (!NativeDragActive) Execute(index); };
        button.GotKeyboardFocus += (_, _) => Select(index);
        _buttons.Add(button); _sectors.Children.Add(button);
    }

    private int Hit(Point point) => RadialGeometry.HitTest(point.X, point.Y, _pageTools.Length);
    private void RebuildTools()
    {
        _sectors.Children.Clear(); _buttons.Clear(); _surfaces.Clear(); _labels.Clear(); _selected = -1;
        _pageTools = ToolCatalog.Page(_tools, _page);
        for (int i = 0; i < _pageTools.Length; i++) BuildSector(i);
        RefreshColors();
    }

    private async Task DetectFiles(string[] files)
    {
        var generation = ++_generation;
        _detectionCancellation?.Cancel(); _detectionCancellation?.Dispose();
        _detectionCancellation = new CancellationTokenSource();
        var token = _detectionCancellation.Token;
        _files = files.ToArray(); _ready = false; _detectionError = null; _notice = null; _tools = []; _page = 0; RebuildTools();
        _file.Text = files.Length == 1 ? Path.GetFileName(files[0]) : $"{files.Length} file";
        _hint.Text = "Đang nhận diện…";
        try
        {
            var detected = await Task.Run(() =>
            {
                if (files.Length is < 1 or > 128 || files.Any(Directory.Exists)) throw new NotSupportedException("Chọn file, không chọn thư mục.");
                return FileToolResolver.ResolveWithNotice(files, token);
            });
            if (generation != _generation) return;
            _tools = detected.Tools; _notice = detected.Notice; _ready = true; RebuildTools(); Select(-1);
            // Keep the original activation point: detection must not demand a second gesture.
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        { if (generation == _generation) { _detectionError = "Không đọc được file\nEsc để hủy"; _hint.Text = _detectionError; _hint.ToolTip = error.Message; LastDropDiagnostic = error.Message; } }
    }

    private static System.Windows.Point P(double radius, double angle)
    { var value = RadialGeometry.Point(radius, angle); return new System.Windows.Point(value.X, value.Y); }

    private static FrameworkElement ToolIcon(ToolKind kind)
    {
        var data = kind switch
        {
            ToolKind.Png or ToolKind.Jpeg or ToolKind.Webp or ToolKind.Bmp or ToolKind.Gif or ToolKind.Tiff or ToolKind.Ico or ToolKind.PdfPng or ToolKind.PdfJpeg => "M3,3 L21,3 21,21 3,21 Z M3,17 L9,11 14,16 17,13 21,17 M16,7 A1,1 0 1 1 16,9 A1,1 0 1 1 16,7",
            ToolKind.More => "M5,12 L19,12 M13,6 L19,12 13,18",
            ToolKind.OfficePdf or ToolKind.ImagePdf or ToolKind.DocxText or ToolKind.PdfSplit or ToolKind.PdfMerge or ToolKind.PdfWord or ToolKind.PdfText or ToolKind.ImageText => "M5,2 L15,2 20,7 20,22 5,22 Z M15,2 L15,7 20,7 M8,12 L17,12 M8,16 L17,16",
            ToolKind.Resize => "M3,9 L3,3 9,3 M15,21 L21,21 21,15 M4,4 L10,10 M20,20 L14,14 M3,15 L3,21 9,21 M15,3 L21,3 21,9",
            ToolKind.Crop => "M7,2 L7,17 22,17 M2,7 L17,7 17,22",
            ToolKind.Rotate => "M20,8 A9,9 0 1 0 21,15 M20,2 L20,8 14,8",
            ToolKind.Frame => "M2,3 L22,3 22,21 2,21 Z M7,8 L17,8 17,16 7,16 Z",
            ToolKind.VideoMp4 or ToolKind.VideoCompress or ToolKind.VideoGif => "M2,5 L16,5 16,19 2,19 Z M16,10 L22,6 22,18 16,14 M7,9 L12,12 7,15 Z",
            ToolKind.AudioMp3 or ToolKind.AudioM4a or ToolKind.AudioWav or ToolKind.AudioMerge => "M9,18 L9,5 20,3 20,16 M9,18 A3,3 0 1 1 9,17.9 M20,16 A3,3 0 1 1 20,15.9",
            ToolKind.MediaTrim => "M6,6 A3,3 0 1 1 6,5.9 M6,18 A3,3 0 1 1 6,17.9 M8,8 L20,18 M8,16 L20,6",
            ToolKind.VideoFrame => "M3,7 L8,7 10,4 14,4 16,7 21,7 21,20 3,20 Z M12,10 A3.5,3.5 0 1 1 12,17 A3.5,3.5 0 1 1 12,10",
            _ => "M3,3 L9,9 M9,3 L9,9 3,9 M21,21 L15,15 M15,21 L15,15 21,15"
        };
        var icon = new WpfPath { Data = Geometry.Parse(data), Width = 24, Height = 24, StrokeThickness = 1.7, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(Shape.StrokeProperty, "Ink"); return icon;
    }

    public void RefreshBackdrop()
    {
        // No full-HWND blur: it paints outside even a layered window's alpha mask.
        if (!_app.Settings.Glass || SystemParameters.HighContrast || !Native.TransparencyEnabled()) _glassImage.Source = null;
        BackdropAvailable = _glassImage.Source != null;
        Native.SetCircularRegion(this);
        RefreshColors();
    }
    private void RefreshColors()
    {
        var fill = ((SolidColorBrush)Theme.Brush("WindowFill")).Color;
        fill.A = BackdropAvailable ? (byte)120 : (byte)255;
        _glassTint.Fill = new SolidColorBrush(fill); _glassTint.Stroke = Theme.Brush("Edge");
        for (int i = 0; i < _surfaces.Count; i++)
        {
            _surfaces[i].Fill = i == _selected ? Theme.Brush("Accent") : Theme.Brush("PanelFill");
            _surfaces[i].Stroke = Theme.Brush("Edge");
            _labels[i].Opacity = CanExecute(i) ? 1 : .45;
            foreach (var child in _labels[i].Children)
            {
                if (child is TextBlock text) text.Foreground = i == _selected ? Theme.Brush("AccentInk") : Theme.Brush("Ink");
                if (child is WpfPath path) path.Stroke = i == _selected ? Theme.Brush("AccentInk") : Theme.Brush("Ink");
            }
        }
        _hub.Fill = Theme.Brush("WindowFill"); _hub.Stroke = Theme.Brush("Edge");
    }
    private bool CanExecute(int sector) => _ready && sector >= 0 && sector < _pageTools.Length;
    private void Select(int sector)
    {
        var previous = _selected;
        _selected = CanExecute(sector) ? sector : -1;
        _hint.Text = _detectionError ?? (!_ready ? (_files.Length == 0 ? "Di chuyển nhẹ\nđể nhận file" : "Đang nhận diện…\nCó thể thả để chờ") : _selected >= 0 ? (_pageTools[_selected] == ToolKind.More && NativeDragActive ? "Giữ để sang trang" : NativeDragActive ? "Thả để chọn" : "Bấm để chọn") : "Chọn công cụ\nEsc để hủy");
        if (_ready && _selected < 0 && _notice != null) _hint.Text = _notice;
        if (previous != _selected) RefreshColors();
    }
    public void ShowMenu(Native.Point cursor, string[]? files = null)
    {
        if (IsVisible) HideMenu();
        _glassImage.Source = _app.Settings.Glass && !SystemParameters.HighContrast && Native.TransparencyEnabled() ? MenuBackdrop.Capture(cursor) : null;
        BackdropAvailable = _glassImage.Source != null;
        NativeDragActive = files == null;
        _activationPoint = cursor; _armed = files != null;
        _files = []; _ready = false; _detectionError = null; _hint.ToolTip = null; _moreLatched = false; _tools = []; _page = 0; RebuildTools();
        _file.Text = _files.Length switch { 0 => "Kéo file vào", 1 => Path.GetFileName(_files[0]), _ => $"{_files.Length} file" };
        LastDropDiagnostic = NativeDragActive ? "Đã gọi menu, đang chờ OLE DragEnter" : "Chọn file trực tiếp";
        Select(-1); ShowActivated = !NativeDragActive; Native.SetNoActivate(this, NativeDragActive);
        Native.PositionMenu(this, cursor); Show(); Native.PositionMenu(this, cursor);
        if (files != null) Detection = DetectFiles(files);
        if (!NativeDragActive) Activate();
    }
    public void HideMenu() { ++_generation; _detectionCancellation?.Cancel(); _moreTimer.Stop(); NativeDragActive = false; Hide(); _glassImage.Source = null; BackdropAvailable = false; _files = []; _ready = false; Select(-1); }

    private void Enter(object sender, DragEventArgs e)
    {
        NativeDragActive = true;
        try
        {
            var incoming = e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length is > 0 and <= 128 ? paths : [];
            if (!_files.SequenceEqual(incoming)) Detection = DetectFiles(incoming);
            LastDropDiagnostic = $"OLE DragEnter · {_files.Length} file hợp lệ";
        }
        catch (Exception ex) { _files = []; LastDropDiagnostic = "Không đọc được dữ liệu kéo thả: " + ex.Message; }
        Over(sender, e);
    }
    private void Over(object sender, DragEventArgs e)
    {
        if (!_armed)
        {
            Native.GetCursorPos(out var cursor);
            var threshold = 12 * VisualTreeHelper.GetDpi(this).DpiScaleX;
            _armed = Math.Abs(cursor.X - _activationPoint.X) >= threshold || Math.Abs(cursor.Y - _activationPoint.Y) >= threshold;
        }
        var point = e.GetPosition(_canvas); Select(_armed ? Hit(point) : -1);
        var onMore = _selected >= 0 && _pageTools[_selected] == ToolKind.More;
        if (!onMore) { _moreTimer.Stop(); _moreLatched = false; }
        else if (!_moreLatched && !_moreTimer.IsEnabled) _moreTimer.Start();
        e.Effects = (_selected >= 0 || (!_ready && _files.Length > 0 && _detectionError == null)) && (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Dropped(object sender, DragEventArgs e)
    {
        _moreTimer.Stop();
        // A slow PDF may still be analysing. Keep its menu open for an explicit click.
        if (!_ready && _files.Length > 0 && _detectionError == null && (e.AllowedEffects & DragDropEffects.Copy) != 0)
        {
            e.Effects = DragDropEffects.Copy; e.Handled = true; NativeDragActive = false; _armed = true;
            Native.SetNoActivate(this, false); Activate(); return;
        }
        var point = e.GetPosition(_canvas); var sector = Hit(point);
        e.Effects = _armed && CanExecute(sector) && (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (e.Effects == DragDropEffects.Copy) { LastDropDiagnostic = "OLE Drop · Copy · " + ToolCatalog.Label(_pageTools[sector]); Execute(sector); }
        else HideMenu();
    }
    private void Execute(int sector)
    {
        if (!CanExecute(sector)) return;
        var files = _files.ToArray(); var tool = _pageTools[sector];
        if (tool == ToolKind.More)
        {
            _page++; RebuildTools(); NativeDragActive = false; _armed = true;
            Native.SetNoActivate(this, false); Activate(); Select(-1); return;
        }
        HideMenu();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _app.RunTool(tool, files));
    }
}
