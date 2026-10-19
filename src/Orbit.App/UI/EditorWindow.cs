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
        var defaults = app.Settings.Defaults;
        _quality.Value = Math.Clamp(defaults.JpegQuality ?? 85, 30, 95); _lock.IsChecked = defaults.ResizeKeepAspect;
        if (tool == ToolKind.Frame) _frame = RememberedFrame(defaults.Frame);
        if (tool == ToolKind.Crop) { _crop = _viewer; BuildCrop(); }
        else if (tool == ToolKind.Resize) BuildResize(); else if (tool == ToolKind.Frame) BuildFrame(); else BuildCompress();
        var caption = Ui.Text($"{Path.GetFileName(files[0])}\nẢnh gốc · {image.PixelWidth} × {image.PixelHeight} px" + (files.Length > 1 ? $" · {files.Length} file" : ""), 12, true);
        caption.TextWrapping = TextWrapping.NoWrap; caption.TextTrimming = TextTrimming.CharacterEllipsis; caption.ToolTip = files[0];
        var captionPanel = new StackPanel { Margin = new Thickness(2, 10, 2, 0) }; captionPanel.Children.Add(caption);
        captionPanel.Children.Add(Ui.Text(tool == ToolKind.Crop ? "Cuộn: zoom · Space + kéo / chuột giữa: di chuyển ảnh" : tool == ToolKind.Frame ? "Xem trước thu nhỏ · Cuộn: zoom · Kéo: di chuyển" : "Cuộn: zoom · Kéo: di chuyển ảnh", 12, true));
        Grid.SetRow(captionPanel, 2); preview.Children.Add(captionPanel);
        body.Children.Add(preview);
        var side = Ui.Panel(new ScrollViewer { Content = _controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 0); Grid.SetColumn(side, 1); body.Children.Add(side); SetBody(body);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); _error.Margin = new Thickness(0, 16, 0, 0); _controls.Children.Add(_error);
        var foot = new DockPanel { Margin = new Thickness(24, 2, 20, 20) };
        var save = Ui.Button(files.Length > 1 ? $"Lưu {files.Length} bản sao" : "Lưu bản sao", Save, true);
        var buttons = Ui.Row(Ui.Button("Hủy", Close), save); DockPanel.SetDock(buttons, Dock.Right); foot.Children.Add(buttons);
        foot.Children.Add(Ui.Text("Lưu cạnh file nguồn\nGiữ bản gốc · Tự thêm số khi trùng tên", 12, true)); SetFooter(foot);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Save(); e.Handled = true; } };
        Closed += (_, _) => { _closed = true; _previewRevision++; _previewTimer.Stop(); _frameTimer.Stop(); };
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
        _controls.Children.Add(_lock); SetLongSide(Orbit.Settings.Defaults.ResizeLongSide is int remembered and > 0 and <= 32768 ? remembered : Math.Min(1920, Math.Max(_image.PixelWidth, _image.PixelHeight)));
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
        var value = Ui.Text($"{(int)_quality.Value}%", 24); _controls.Children.Add(value); _controls.Children.Add(_quality);
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
    private static readonly string[] Palette = ["#FFFFFF", "#F4EFE6", "#1F2328", "#167D72", "#F2C4CE", "#E9C46A", "#8EC5FC", "#B8A9F5"];
    private static readonly (string Name, string From, string To)[] Gradients = [("Bình minh", "#FBC2EB", "#A6C1EE"), ("Bạc hà", "#D4FC79", "#96E6A1"), ("Hoàng hôn", "#FF9A8B", "#FAD0C4"), ("Đêm", "#141E30", "#243B55")];
    private FrameOptions _frame = new();
    private string? _cropAspect;
    private BitmapSource? _frameThumb, _pictureThumb;
    private readonly StackPanel _backgroundPanel = new();
    private readonly System.Windows.Threading.DispatcherTimer _frameTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly List<(Button Button, Func<bool> Selected)> _frameChoices = [];
    public FrameOptions FrameSettings => _frame;
    private void SetFrame(FrameOptions options) { _frame = options; RefreshChoices(); _frameTimer.Stop(); _frameTimer.Start(); }
    private Button Choice(string text, Func<bool> selected, Action apply, WrapPanel panel)
    {
        var button = Ui.Button(text, apply); button.Padding = new Thickness(10, 7, 10, 7); button.Margin = new Thickness(0, 0, 5, 6);
        panel.Children.Add(button); _frameChoices.Add((button, selected)); return button;
    }
    private void RefreshChoices()
    {
        foreach (var (button, selected) in _frameChoices)
        { var on = selected(); button.SetResourceReference(Control.BackgroundProperty, on ? "Accent" : "PanelFill"); button.SetResourceReference(Control.ForegroundProperty, on ? "AccentInk" : "Ink"); }
    }
    private Slider PercentSlider(string name, double max, double value, Action<double> changed)
    {
        var label = Ui.Text($"{name} · {value * 100:0}%", 12, true); label.Margin = new Thickness(0, 10, 0, 0); _controls.Children.Add(label);
        var slider = new Slider { Minimum = 0, Maximum = max * 100, Value = value * 100 }; AutomationProperties.SetName(slider, name);
        slider.ValueChanged += (_, _) => { label.Text = $"{name} · {slider.Value:0}%"; changed(slider.Value / 100); };
        _controls.Children.Add(slider); return slider;
    }
    private void ColorField(Panel parent, string name, Func<string> get, Func<string, FrameOptions> set)
    {
        var label = Ui.Text(name, 12, true); label.Margin = new Thickness(0, 8, 0, 4); parent.Children.Add(label);
        var box = new TextBox { Text = get(), Width = 96 }; AutomationProperties.SetName(box, name);
        var swatch = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0) };
        swatch.SetResourceReference(Border.BorderBrushProperty, "Edge");
        void Paint() { try { swatch.Background = new SolidColorBrush(Imaging.FrameRenderer.ToColor(box.Text)); } catch (FormatException) { } }
        box.TextChanged += (_, _) =>
        {
            try { _ = FrameLayout.ParseColor(box.Text); _error.Text = ""; Paint(); SetFrame(set(box.Text.Trim().StartsWith('#') ? box.Text.Trim().ToUpperInvariant() : "#" + box.Text.Trim().ToUpperInvariant())); }
            catch (FormatException error) { _error.Text = name + ": " + error.Message; }
        };
        Paint(); var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(box); row.Children.Add(swatch); parent.Children.Add(row);
        var swatches = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var color in Palette)
        {
            var chip = new Button { Width = 18, Height = 18, MinHeight = 18, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 3), Background = new SolidColorBrush(Imaging.FrameRenderer.ToColor(color)), ToolTip = color };
            AutomationProperties.SetName(chip, name + " " + color); chip.Click += (_, _) => box.Text = color; swatches.Children.Add(chip);
        }
        parent.Children.Add(swatches);
    }
    private void BuildFrame()
    {
        _frameThumb = new global::Orbit.App.Imaging.ImageEngine().Thumbnail(_image, 1100);
        _frameTimer.Tick += (_, _) => { _frameTimer.Stop(); RefreshFrame(); };
        Heading("Khung");
        PercentSlider("Khoảng viền", .4, _frame.Padding, v => SetFrame(_frame with { Padding = v }));
        var aspects = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (name, ratio) in new (string, double?)[] { ("Theo ảnh", null), ("1:1", 1), ("4:5", .8), ("16:9", 16d / 9), ("9:16", 9d / 16) })
            Choice(name, () => _frame.Aspect == ratio, () => SetFrame(_frame with { Aspect = ratio }), aspects);
        _controls.Children.Add(aspects);
        var heading = Ui.Text("Nền"); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 14, 0, 8); _controls.Children.Add(heading);
        var kinds = new WrapPanel();
        foreach (var (name, kind) in new[] { ("Màu", FrameBackground.Solid), ("Chuyển màu", FrameBackground.Gradient), ("Họa tiết", FrameBackground.Texture), ("Ảnh", FrameBackground.Picture), ("Ảnh gốc mờ", FrameBackground.SourceBlur) })
            Choice(name, () => _frame.Background == kind, () => { SetFrame(_frame with { Background = kind }); BuildBackgroundPanel(); }, kinds);
        _controls.Children.Add(kinds); _controls.Children.Add(_backgroundPanel); BuildBackgroundPanel();
        var photo = Ui.Text("Ảnh"); photo.FontWeight = FontWeights.SemiBold; photo.Margin = new Thickness(0, 14, 0, 0); _controls.Children.Add(photo);
        PercentSlider("Bo góc", .2, _frame.Corner, v => SetFrame(_frame with { Corner = v }));
        var shadow = new CheckBox { Content = "Đổ bóng", IsChecked = _frame.Shadow };
        shadow.Checked += (_, _) => SetFrame(_frame with { Shadow = true }); shadow.Unchecked += (_, _) => SetFrame(_frame with { Shadow = false }); _controls.Children.Add(shadow);
        var formats = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        Choice("PNG", () => !_frame.Jpeg, () => SetFrame(_frame with { Jpeg = false }), formats);
        Choice("JPEG 92%", () => _frame.Jpeg, () => SetFrame(_frame with { Jpeg = true }), formats);
        _controls.Children.Add(formats);
        _summary.Margin = new Thickness(0, 10, 0, 0); _controls.Children.Add(_summary);
        var note = Ui.Text("Ảnh gốc giữ nguyên từng pixel; chỉ phần khung bên ngoài được tạo thêm. Với nhiều file, khung tính theo tỷ lệ của từng ảnh.", 12, true);
        note.Margin = new Thickness(0, 12, 0, 0); _controls.Children.Add(note);
        RefreshChoices(); Loaded += async (_, _) => { RefreshFrame(); if (_frame.Background == FrameBackground.Picture && _frame.Picture != null) await LoadPicture(_frame.Picture); };
    }
    private void BuildBackgroundPanel()
    {
        _backgroundPanel.Children.Clear(); _frameChoices.RemoveAll(c => c.Button.Tag as string == "background");
        void Tagged(WrapPanel panel) { foreach (var button in panel.Children.OfType<Button>()) button.Tag = "background"; _backgroundPanel.Children.Add(panel); }
        switch (_frame.Background)
        {
            case FrameBackground.Solid:
                ColorField(_backgroundPanel, "Màu nền", () => _frame.Color1, c => _frame with { Color1 = c }); break;
            case FrameBackground.Gradient:
                var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var (name, from, to) in Gradients)
                    Choice(name, () => _frame.Color1 == from && _frame.Color2 == to, () => { SetFrame(_frame with { Color1 = from, Color2 = to }); BuildBackgroundPanel(); }, presets);
                Tagged(presets);
                ColorField(_backgroundPanel, "Màu đầu", () => _frame.Color1, c => _frame with { Color1 = c });
                ColorField(_backgroundPanel, "Màu cuối", () => _frame.Color2, c => _frame with { Color2 = c });
                var angleLabel = Ui.Text($"Hướng · {_frame.Angle:0}°", 12, true); angleLabel.Margin = new Thickness(0, 8, 0, 0); _backgroundPanel.Children.Add(angleLabel);
                var angle = new Slider { Minimum = 0, Maximum = 345, TickFrequency = 15, Value = _frame.Angle }; AutomationProperties.SetName(angle, "Hướng chuyển màu");
                angle.ValueChanged += (_, _) => { angleLabel.Text = $"Hướng · {angle.Value:0}°"; SetFrame(_frame with { Angle = angle.Value }); };
                _backgroundPanel.Children.Add(angle); break;
            case FrameBackground.Texture:
                var patterns = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var (name, texture) in new[] { ("Chấm bi", FrameTexture.Dots), ("Sọc chéo", FrameTexture.Stripes), ("Ô lưới", FrameTexture.Grid), ("Giấy", FrameTexture.Paper) })
                    Choice(name, () => _frame.Texture == texture, () => SetFrame(_frame with { Texture = texture }), patterns);
                Tagged(patterns);
                ColorField(_backgroundPanel, "Màu nền", () => _frame.Color1, c => _frame with { Color1 = c });
                ColorField(_backgroundPanel, "Màu họa tiết", () => _frame.Color2, c => _frame with { Color2 = c }); break;
            case FrameBackground.Picture:
                var chosen = Ui.Text(_frame.Picture == null ? "Chưa chọn ảnh nền." : Path.GetFileName(_frame.Picture), 12, true); chosen.Margin = new Thickness(0, 6, 0, 6);
                _backgroundPanel.Children.Add(Ui.Button("Chọn ảnh nền…", PickPicture)); _backgroundPanel.Children.Add(chosen);
                var blur = new CheckBox { Content = "Làm mờ ảnh nền", IsChecked = _frame.BlurPicture };
                blur.Checked += (_, _) => SetFrame(_frame with { BlurPicture = true }); blur.Unchecked += (_, _) => SetFrame(_frame with { BlurPicture = false });
                _backgroundPanel.Children.Add(blur); break;
            default:
                var tip = Ui.Text("Nền là chính ảnh này phóng đầy khung và làm mờ.", 12, true); tip.Margin = new Thickness(0, 6, 0, 0); _backgroundPanel.Children.Add(tip); break;
        }
        RefreshChoices();
    }
    // A remembered picture background may have been moved or deleted since; fall back to a plain colour.
    private static FrameOptions RememberedFrame(FrameOptions? saved)
    {
        if (saved == null) return new();
        try { saved.Validate(); } catch (Exception error) when (error is ArgumentException or FormatException) { return new(); }
        return saved.Background == FrameBackground.Picture && !File.Exists(saved.Picture) ? saved with { Background = FrameBackground.Solid, Picture = null } : saved;
    }
    private void Remember(Action<Services.ToolDefaults> update)
    {
        try { update(Orbit.Settings.Defaults); Orbit.Settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // The job still runs; only the preference is not kept.
    }
    private async void PickPicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Chọn ảnh nền", Filter = "Ảnh|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff;*.gif;*.ico;*.avif;*.heic;*.heif;*.tga" };
        if (dialog.ShowDialog(this) != true) return;
        await LoadPicture(dialog.FileName);
    }
    private async Task LoadPicture(string path)
    {
        _error.Text = "Đang tải ảnh nền…";
        try
        {
            var thumb = await Task.Run(() => { var engine = new global::Orbit.App.Imaging.ImageEngine(); return engine.Thumbnail(engine.Load(path), 1100); });
            if (_closed) return;
            _pictureThumb = thumb; _error.Text = ""; SetFrame(_frame with { Picture = path }); BuildBackgroundPanel();
        }
        catch (Exception error) { if (!_closed) _error.Text = "Không mở được ảnh nền: " + error.Message; }
    }
    private void RefreshFrame()
    {
        if (_closed || _frameThumb == null) return;
        try
        {
            var output = FrameLayout.Compute(_image.PixelWidth, _image.PixelHeight, _frame);
            _summary.Text = $"Đầu ra · {output.CanvasWidth} × {output.CanvasHeight} px" + (_files.Length > 1 ? "\nẢnh khác kích thước dùng cùng tỷ lệ khung." : "");
            if (_frame.Background == FrameBackground.Picture && _pictureThumb == null) { _summary.Text += "\nChọn ảnh nền để xem trước."; return; }
            _viewer.SetImage(Imaging.FrameRenderer.Render(_frameThumb, _frame, _pictureThumb));
        }
        catch (Exception error) when (error is ArgumentException or FormatException) { _summary.Text = error.Message; }
    }
    private void BuildCrop()
    {
        Heading("Tỷ lệ khung");
        var presets = new WrapPanel();
        foreach (var option in new (string Name, double? Ratio)[] { ("Tự do", null), ("Gốc", (double)_image.PixelWidth / _image.PixelHeight), ("1:1", 1), ("4:5", .8), ("16:9", 16d / 9), ("9:16", 9d / 16) })
        {
            var button = Ui.Button(option.Name, () => { _crop!.SetAspect(option.Ratio); _cropAspect = option.Name; RefreshRatios(); });
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
        if (presets.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Content, Orbit.Settings.Defaults.CropAspect)) is { } remembered && !Equals(remembered.Content, "Tự do"))
            remembered.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            if (_tool == ToolKind.Frame)
            {
                _ = FrameLayout.Compute(_image.PixelWidth, _image.PixelHeight, _frame);
                Orbit.Queue.Add(_files.Select(path => new ImageRequest(path, _tool, Quality: 92, Frame: _frame))); Remember(d => d.Frame = _frame); Close(); return;
            }
            Orbit.Queue.Add(_files.Select(path => new ImageRequest(path, _tool, w, h, _lock.IsChecked == true, (int)_quality.Value, _crop?.Region)));
            Remember(d =>
            {
                if (_tool == ToolKind.Resize) { d.ResizeLongSide = Math.Max(w, h); d.ResizeKeepAspect = _lock.IsChecked == true; }
                else if (_tool == ToolKind.Compress) d.JpegQuality = (int)_quality.Value;
                else if (_tool == ToolKind.Crop) d.CropAspect = _cropAspect ?? d.CropAspect;
            });
            Close();
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }
}
