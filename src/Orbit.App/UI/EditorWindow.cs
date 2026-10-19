using System.Windows.Automation;

namespace Orbit.App.UI;

public sealed class EditorWindow : GlassWindow
{
    private readonly string[] _files;
    private readonly ToolKind _tool;
    private readonly BitmapSource _image;
    private readonly StackPanel _controls = new() { Margin = new Thickness(20) };
    private readonly TextBlock _error = Ui.Text("", 12);
    private readonly TextBox _width = new(), _height = new();
    private readonly CheckBox _lock = new() { Content = "Giữ tỷ lệ ảnh", IsChecked = true };
    private readonly Slider _quality = new() { Minimum = 30, Maximum = 95, Value = 85 };
    private CropCanvas? _crop;
    private readonly CropCanvas _viewer;
    private readonly TextBlock _summary = Ui.Text("", 12, true);
    private readonly TextBox _x = new(), _y = new();
    private readonly List<(Button Button, double? Ratio)> _ratios = [];
    private readonly System.Windows.Threading.DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private BitmapSource? _jpegSource, _jpegPreview;
    private CheckBox? _showOriginal;
    private bool _closed, _previewBusy;
    private int _previewRevision;
    private bool _updating;

    public EditorWindow(OrbitApplication app, ToolKind tool, string[] files, BitmapSource image) : base(app, ToolCatalog.Description(tool), 860, 650)
    {
        _files = files; _tool = tool; _image = image;
        MinWidth = 760; MinHeight = 560;
        var body = new Grid { Margin = new Thickness(20, 0, 20, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(254) });
        var preview = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _viewer = new CropCanvas(image, tool == ToolKind.Crop);
        Grid.SetRow(_viewer, 1); preview.Children.Add(_viewer);
        var zoomLabel = Ui.Text("", 12, true); zoomLabel.VerticalAlignment = VerticalAlignment.Center; zoomLabel.MinWidth = 45;
        _viewer.ZoomChanged += zoom => zoomLabel.Text = $"{zoom * 100:0.#}%";
        var toolbar = Ui.Row(Ui.Button("−", () => Zoom(.8)), Ui.Button("+", () => Zoom(1.25)), Ui.Button("Vừa khung", _viewer.FitImage), Ui.Button("100%", _viewer.ActualSize), zoomLabel);
        toolbar.Margin = new Thickness(0, 0, 0, 10);
        foreach (var button in toolbar.Children.OfType<Button>()) { button.Padding = new Thickness(9, 5, 9, 5); button.MinHeight = 32; }
        AutomationProperties.SetName((Button)toolbar.Children[0], "Thu nhỏ ảnh"); AutomationProperties.SetName((Button)toolbar.Children[1], "Phóng to ảnh");
        preview.Children.Add(toolbar);
        if (tool == ToolKind.Crop) { _crop = _viewer; BuildCrop(); }
        else if (tool == ToolKind.Resize) BuildResize(); else BuildCompress();
        var caption = Ui.Text($"{Path.GetFileName(files[0])}\nẢnh gốc · {image.PixelWidth} × {image.PixelHeight} px" + (files.Length > 1 ? $" · {files.Length} file" : ""), 12, true);
        caption.TextWrapping = TextWrapping.NoWrap; caption.TextTrimming = TextTrimming.CharacterEllipsis; caption.ToolTip = files[0];
        var captionPanel = new StackPanel { Margin = new Thickness(2, 10, 2, 0) }; captionPanel.Children.Add(caption);
        captionPanel.Children.Add(Ui.Text(tool == ToolKind.Crop ? "Cuộn: zoom · Space + kéo / chuột giữa: di chuyển ảnh" : "Cuộn: zoom · Kéo: di chuyển ảnh", 12, true));
        Grid.SetRow(captionPanel, 2); preview.Children.Add(captionPanel);
        body.Children.Add(preview);
        var side = Ui.Panel(new ScrollViewer { Content = _controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 0); Grid.SetColumn(side, 1); body.Children.Add(side); SetBody(body);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); _error.Margin = new Thickness(0, 16, 0, 0); _controls.Children.Add(_error);
        var foot = new DockPanel { Margin = new Thickness(24, 2, 20, 20) };
        var save = Ui.Button(files.Length > 1 ? $"Lưu {files.Length} bản sao" : "Lưu bản sao", Save, true);
        var buttons = Ui.Row(Ui.Button("Hủy", Close), save); DockPanel.SetDock(buttons, Dock.Right); foot.Children.Add(buttons);
        foot.Children.Add(Ui.Text("Lưu cạnh file nguồn\nGiữ bản gốc · Tự thêm số khi trùng tên", 12, true)); SetFooter(foot);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Save(); e.Handled = true; } };
        Closed += (_, _) => { _closed = true; _previewRevision++; _previewTimer.Stop(); };
    }
    private void Zoom(double factor) => _viewer.ZoomAt(factor, new Point(_viewer.ActualWidth / 2, _viewer.ActualHeight / 2));
    private void Heading(string text)
    { var label = Ui.Text(text); label.FontWeight = FontWeights.SemiBold; label.Margin = new Thickness(0, 0, 0, 10); _controls.Children.Add(label); }
    private void Field(string name, TextBox box)
    {
        var label = Ui.Text(name, 12, true); label.Margin = new Thickness(0, 10, 0, 5); _controls.Children.Add(label);
        AutomationProperties.SetName(box, name); _controls.Children.Add(box);
    }
    private void BuildResize()
    {
        Heading("Kích thước đầu ra");
        var presets = new WrapPanel();
        foreach (var size in new[] { 1080, 1920, 2560 })
        { var button = Ui.Button(size.ToString(), () => SetLongSide(size)); button.Margin = new Thickness(0, 0, 5, 6); button.Padding = new Thickness(10, 7, 10, 7); presets.Children.Add(button); }
        _controls.Children.Add(presets); Field("Rộng (px)", _width); Field("Cao (px)", _height);
        _controls.Children.Add(_lock); SetLongSide(Math.Min(1920, Math.Max(_image.PixelWidth, _image.PixelHeight)));
        _width.TextChanged += (_, _) => SyncSize(true); _height.TextChanged += (_, _) => SyncSize(false);
        _lock.Checked += (_, _) => SyncSize(true);
        _controls.Children.Add(Ui.Button("Kích thước gốc", () => SetLongSide(Math.Max(_image.PixelWidth, _image.PixelHeight))));
        _summary.Margin = new Thickness(0, 12, 0, 8); _controls.Children.Add(_summary);
        _width.TextChanged += (_, _) => UpdateSizeSummary(); _height.TextChanged += (_, _) => UpdateSizeSummary(); UpdateSizeSummary();
        _controls.Children.Add(Ui.Text("Xuất PNG. Với nhiều file, mỗi ảnh được đặt vừa trong kích thước này khi giữ tỷ lệ.", 12, true));
    }
    private void UpdateSizeSummary()
    {
        if (int.TryParse(_width.Text, out var w) && int.TryParse(_height.Text, out var h) && w > 0 && h > 0)
            _summary.Text = $"Đầu ra · {w} × {h} px" + (w > _image.PixelWidth || h > _image.PixelHeight ? "\nPhóng lớn không làm tăng chi tiết ảnh." : "");
        else _summary.Text = "Nhập chiều rộng và chiều cao hợp lệ.";
    }
    private void SetLongSide(int size)
    {
        _updating = true;
        var scale = (double)size / Math.Max(_image.PixelWidth, _image.PixelHeight);
        _width.Text = Math.Max(1, (int)Math.Round(_image.PixelWidth * scale)).ToString();
        _height.Text = Math.Max(1, (int)Math.Round(_image.PixelHeight * scale)).ToString(); _updating = false;
    }
    private void SyncSize(bool fromWidth)
    {
        if (_updating || _lock.IsChecked != true) return;
        if (!int.TryParse((fromWidth ? _width : _height).Text, out var value) || value < 1 || value > 32768) return;
        _updating = true;
        if (fromWidth) _height.Text = Math.Max(1, (int)Math.Round((double)value * _image.PixelHeight / _image.PixelWidth)).ToString();
        else _width.Text = Math.Max(1, (int)Math.Round((double)value * _image.PixelWidth / _image.PixelHeight)).ToString();
        _updating = false;
    }
    private void BuildCompress()
    {
        Heading("Chất lượng JPEG");
        var value = Ui.Text("85%", 24); _controls.Children.Add(value); _controls.Children.Add(_quality);
        AutomationProperties.SetName(_quality, "Chất lượng JPEG"); _quality.ValueChanged += (_, _) => { value.Text = ((int)_quality.Value) + "%"; SchedulePreview(); };
        _controls.Children.Add(Ui.Row(Ui.Button("60%", () => _quality.Value = 60), Ui.Button("85%", () => _quality.Value = 85), Ui.Button("95%", () => _quality.Value = 95)));
        _showOriginal = new CheckBox { Content = "Xem ảnh gốc" };
        _showOriginal.Checked += (_, _) => _viewer.SetPreview(_image);
        _showOriginal.Unchecked += (_, _) => _viewer.SetPreview(_jpegPreview ?? _image);
        _controls.Children.Add(_showOriginal); _controls.Children.Add(_summary);
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RefreshJpegPreview(); };
        Loaded += (_, _) => SchedulePreview();
        var note = Ui.Text("Giữ độ phân giải. Dung lượng được xác định sau khi mã hóa. Vùng trong suốt được ghép lên nền trắng.", 12, true);
        note.Margin = new Thickness(0, 18, 0, 0); _controls.Children.Add(note);
    }
    private void SchedulePreview() { _previewRevision++; _previewTimer.Stop(); _previewTimer.Start(); _summary.Text = "Đang cập nhật xem trước…"; }
    private async Task RefreshJpegPreview()
    {
        if (_closed || _previewBusy) return;
        _previewBusy = true; var revision = _previewRevision; var quality = (int)_quality.Value;
        try
        {
            // Bounded preview; export still encodes the full-resolution source.
            if (_jpegSource == null)
            {
                var thumb = new global::Orbit.App.Imaging.ImageEngine().Thumbnail(_image, 1600);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, thumb.PixelWidth, thumb.PixelHeight)); dc.DrawImage(thumb, new Rect(0, 0, thumb.PixelWidth, thumb.PixelHeight)); }
                var bitmap = new RenderTargetBitmap(thumb.PixelWidth, thumb.PixelHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); _jpegSource = bitmap;
            }
            var source = _jpegSource;
            var result = await Task.Run(() =>
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = quality }; encoder.Frames.Add(BitmapFrame.Create(source));
                using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
                var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; decoded.Freeze(); return decoded;
            });
            if (_closed || revision != _previewRevision) return;
            _jpegPreview = result; if (_showOriginal?.IsChecked != true) _viewer.SetPreview(result);
            _summary.Text = "Xem trước JPEG · tối đa 1600 px\nFile lưu giữ nguyên độ phân giải.";
        }
        catch (Exception error) { if (!_closed && revision == _previewRevision) _summary.Text = "Chưa xem trước được: " + error.Message; }
        finally { _previewBusy = false; if (!_closed && revision != _previewRevision) { _previewTimer.Stop(); _previewTimer.Start(); } }
    }
    private void BuildCrop()
    {
        Heading("Tỷ lệ khung");
        var presets = new WrapPanel();
        foreach (var option in new (string Name, double? Ratio)[] { ("Tự do", null), ("Gốc", (double)_image.PixelWidth / _image.PixelHeight), ("1:1", 1), ("4:5", .8), ("16:9", 16d / 9), ("9:16", 9d / 16) })
        {
            var button = Ui.Button(option.Name, () => { _crop!.SetAspect(option.Ratio); RefreshRatios(); });
            button.Padding = new Thickness(10, 7, 10, 7); button.Margin = new Thickness(0, 0, 5, 6); presets.Children.Add(button); _ratios.Add((button, option.Ratio));
        }
        _controls.Children.Add(presets);
        _x.Text = _y.Text = "0";
        _width.Text = _image.PixelWidth.ToString(); _height.Text = _image.PixelHeight.ToString();
        Field("X (px)", _x); Field("Y (px)", _y); Field("Rộng (px)", _width); Field("Cao (px)", _height);
        foreach (var box in new[] { _x, _y, _width, _height }) box.LostKeyboardFocus += (_, _) => { if (!_updating) ApplyCropFields(box == _height); };
        _crop!.RegionChanged += r => { _updating = true; _x.Text = r.X.ToString(); _y.Text = r.Y.ToString(); _width.Text = r.Width.ToString(); _height.Text = r.Height.ToString(); _updating = false; _error.Text = ""; _summary.Text = $"Vùng cắt · {r.Width} × {r.Height} px"; };
        _controls.Children.Add(Ui.Button("Đặt lại vùng cắt", () => { _crop.ResetSelection(); _crop.FitImage(); RefreshRatios(); }));
        _summary.Margin = new Thickness(0, 10, 0, 0); _summary.Text = $"Vùng cắt · {_image.PixelWidth} × {_image.PixelHeight} px"; _controls.Children.Add(_summary);
        var tip = Ui.Text("Kéo góc/cạnh để chỉnh khung; kéo bên trong để dịch vùng. Shift + kéo để vẽ vùng mới. Mũi tên: 1 px · Shift: 10 px.", 12, true); tip.Margin = new Thickness(0, 12, 0, 0); _controls.Children.Add(tip); RefreshRatios();
    }
    private void RefreshRatios()
    {
        foreach (var (button, ratio) in _ratios)
        { var selected = ratio == _crop!.Aspect; button.SetResourceReference(Control.BackgroundProperty, selected ? "Accent" : "PanelFill"); button.SetResourceReference(Control.ForegroundProperty, selected ? "AccentInk" : "Ink"); }
    }
    private bool ApplyCropFields(bool fromHeight = false)
    {
        if (!int.TryParse(_x.Text, out var x) || !int.TryParse(_y.Text, out var y) || !int.TryParse(_width.Text, out var w) || !int.TryParse(_height.Text, out var h) || w < 1 || h < 1)
        { _error.Text = "Nhập tọa độ và kích thước bằng số nguyên; rộng/cao phải lớn hơn 0."; return false; }
        if (_crop!.Aspect is double ratio)
        {
            var derived = fromHeight ? h * ratio : w / ratio;
            if (!double.IsFinite(derived) || derived > int.MaxValue) { _error.Text = "Kích thước vượt giới hạn ảnh."; return false; }
            if (fromHeight) w = Math.Max(1, (int)Math.Round(derived)); else h = Math.Max(1, (int)Math.Round(derived));
        }
        if (x < 0 || y < 0 || (long)x + w > _image.PixelWidth || (long)y + h > _image.PixelHeight)
        { _error.Text = $"Vùng cắt phải nằm trong ảnh {_image.PixelWidth} × {_image.PixelHeight} px."; return false; }
        _crop.SetRegion(new(x, y, w, h)); return true;
    }
    private void Save()
    {
        try
        {
            if (_closed) return;
            if (_tool == ToolKind.Crop && !ApplyCropFields(_height.IsKeyboardFocused)) return;
            int w = 0, h = 0;
            if (_tool == ToolKind.Resize)
            {
                if (!int.TryParse(_width.Text, out w) || !int.TryParse(_height.Text, out h)) throw new ArgumentException("Hãy nhập kích thước bằng số nguyên.");
                global::Orbit.App.Imaging.ImageEngine.ValidatePixels(w, h);
            }
            Orbit.Queue.Add(_files.Select(path => new ImageRequest(path, _tool, w, h, _lock.IsChecked == true, (int)_quality.Value, _crop?.Region)));
            Close();
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }
}
