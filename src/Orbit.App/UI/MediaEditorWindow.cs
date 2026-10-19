using System.Windows.Automation;
using System.Windows.Threading;
using Orbit.App.Services;
using Windows.Media.Playback;
using MediaPlayer = Windows.Media.Playback.MediaPlayer;

namespace Orbit.App.UI;

// Cut, frame, GIF and video compression share one window: a frame preview, a time bar and a
// side panel. Preview frames are decoded stills; "Phát thử" plays the sound of the selection.
public sealed class MediaEditorWindow : GlassWindow
{
    private readonly ToolKind _tool;
    private readonly string[] _files;
    private readonly MediaInfo _info;
    private readonly MediaTimeline _timeline;
    private readonly System.Windows.Controls.Image _frame = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _frameStatus = Ui.Text("", 12, true);
    private readonly StackPanel _controls = new() { Margin = new Thickness(20) };
    private readonly TextBlock _error = Ui.Text("", 12), _summary = Ui.Text("", 12, true), _clock = Ui.Text("0:00.000", 12, true);
    private readonly TextBox _startBox = new(), _endBox = new(), _atBox = new();
    private readonly List<(Button Button, Func<bool> Selected)> _choices = [];
    private readonly DispatcherTimer _scrub = new() { Interval = TimeSpan.FromMilliseconds(140) }, _playTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly MediaEngine.Preview? _preview;
    private readonly Button _play;
    private MediaPlayer? _player;
    private CancellationTokenSource? _frameRequest;
    private DateTime _lastPlayFrame;
    private bool _closed, _updating;
    private int _height, _fps = 12, _gifWidth = 480;
    private MediaQuality _quality = MediaQuality.Medium;
    public MediaTimeline Timeline => _timeline;
    public BitmapSource? PreviewFrame => _frame.Source as BitmapSource;

    public MediaEditorWindow(OrbitApplication app, ToolKind tool, string[] files, MediaInfo info) : base(app, ToolCatalog.Description(tool), 900, 640)
    {
        _tool = tool; _files = files; _info = info; MinWidth = 780; MinHeight = 560;
        _timeline = new MediaTimeline(info.Duration, tool is ToolKind.MediaTrim or ToolKind.VideoGif) { Margin = new Thickness(0, 10, 0, 0) };
        if (info.HasVideo) _preview = new MediaEngine.Preview(files[0]);
        var body = new Grid { Margin = new Thickness(20, 0, 20, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
        var left = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var stage = new Grid(); stage.Children.Add(_frame);
        if (!info.HasVideo)
        {
            var note = Ui.Text("♪\nFile chỉ có âm thanh\nDùng Phát thử để nghe đoạn đã chọn", 14, true); note.TextAlignment = TextAlignment.Center;
            note.HorizontalAlignment = HorizontalAlignment.Center; note.VerticalAlignment = VerticalAlignment.Center; stage.Children.Add(note);
        }
        _frameStatus.HorizontalAlignment = HorizontalAlignment.Right; _frameStatus.VerticalAlignment = VerticalAlignment.Bottom; _frameStatus.Margin = new Thickness(0, 0, 10, 8); stage.Children.Add(_frameStatus);
        var screen = Ui.Panel(stage, 8); screen.MinHeight = 220; left.Children.Add(screen);
        Grid.SetRow(_timeline, 1); left.Children.Add(_timeline);
        _play = Ui.Button("▶ Phát thử", TogglePlay); AutomationProperties.SetName(_play, "Phát thử đoạn đã chọn");
        var transport = Ui.Row(_play, _clock); _clock.VerticalAlignment = VerticalAlignment.Center; transport.Margin = new Thickness(0, 8, 0, 0);
        if (_timeline.RangeMode)
        {
            transport.Children.Add(Ui.Button("Đầu = tại đây", () => { _timeline.SetRange(_timeline.Position, _timeline.End); }));
            transport.Children.Add(Ui.Button("Cuối = tại đây", () => { _timeline.SetRange(_timeline.Start, _timeline.Position); }));
        }
        foreach (var button in transport.Children.OfType<Button>()) { button.Padding = new Thickness(10, 5, 10, 5); button.MinHeight = 32; }
        Grid.SetRow(transport, 2); left.Children.Add(transport);
        var caption = Ui.Text($"{Path.GetFileName(files[0])}\n{Describe(info)}" + (files.Length > 1 ? $" · {files.Length} file" : ""), 12, true);
        caption.Margin = new Thickness(2, 10, 2, 0); caption.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetRow(caption, 3); left.Children.Add(caption);
        body.Children.Add(left);
        var side = Ui.Panel(new ScrollViewer { Content = _controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 0); Grid.SetColumn(side, 1); body.Children.Add(side);
        SetBody(body);
        switch (tool)
        {
            case ToolKind.MediaTrim: BuildRange("Đoạn cần giữ", null); break;
            case ToolKind.VideoGif: BuildRange("Đoạn GIF", MediaLimits.MaxGif); BuildGif(); break;
            case ToolKind.VideoFrame: BuildFrame(); break;
            default: BuildCompress(); break;
        }
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); _error.Margin = new Thickness(0, 12, 0, 0); _error.TextWrapping = TextWrapping.Wrap;
        _summary.Margin = new Thickness(0, 12, 0, 0); _controls.Children.Add(_summary); _controls.Children.Add(_error);
        var foot = new DockPanel { Margin = new Thickness(24, 2, 20, 20) };
        var buttons = Ui.Row(Ui.Button("Hủy", Close), Ui.Button(files.Length > 1 ? $"Lưu {files.Length} bản sao" : "Lưu bản sao", Save, true)); DockPanel.SetDock(buttons, Dock.Right); foot.Children.Add(buttons);
        foot.Children.Add(Ui.Text("Lưu cạnh file nguồn\nGiữ bản gốc · Tự thêm số khi trùng tên", 12, true)); SetFooter(foot);
        _timeline.PositionChanged += () => { _clock.Text = MediaTime.Format(_timeline.Position); if (_tool == ToolKind.VideoFrame) SetBox(_atBox, _timeline.Position); if (_player == null || _player.PlaybackSession.PlaybackState != MediaPlaybackState.Playing) { _scrub.Stop(); _scrub.Start(); } };
        _timeline.RangeChanged += () => { SetBox(_startBox, _timeline.Start); SetBox(_endBox, _timeline.End); UpdateSummary(); };
        _timeline.PreviewMouseLeftButtonDown += (_, _) => Pause();
        _scrub.Tick += async (_, _) => { _scrub.Stop(); await ShowFrame(_timeline.Position); };
        _playTimer.Tick += async (_, _) => await PlayTick();
        PreviewKeyDown += KeyPressed;
        Loaded += async (_, _) => { UpdateSummary(); await ShowFrame(_timeline.Position); };
        Closed += (_, _) => { _closed = true; _scrub.Stop(); _playTimer.Stop(); _frameRequest?.Cancel(); _player?.Dispose(); _player = null; };
    }

    private static string Describe(MediaInfo info) =>
        (info.HasVideo ? $"{info.Width} × {info.Height} px · {info.Fps:0.##} fps · " : "Âm thanh · ") + MediaTime.Format(info.Duration);
    private void Heading(string text) { var label = Ui.Text(text); label.FontWeight = FontWeights.SemiBold; label.Margin = new Thickness(0, 6, 0, 8); _controls.Children.Add(label); }
    private Button Choice(WrapPanel panel, string text, Func<bool> selected, Action apply, bool enabled = true)
    {
        var button = Ui.Button(text, () => { apply(); RefreshChoices(); UpdateSummary(); }); button.Padding = new Thickness(10, 7, 10, 7); button.Margin = new Thickness(0, 0, 5, 6); button.IsEnabled = enabled;
        panel.Children.Add(button); _choices.Add((button, selected)); return button;
    }
    private void RefreshChoices()
    {
        foreach (var (button, selected) in _choices)
        { var on = selected(); button.SetResourceReference(Control.BackgroundProperty, on ? "Accent" : "PanelFill"); button.SetResourceReference(Control.ForegroundProperty, on ? "AccentInk" : "Ink"); }
    }
    private void Field(string name, TextBox box, Action apply)
    {
        var label = Ui.Text(name, 12, true); label.Margin = new Thickness(0, 8, 0, 4); _controls.Children.Add(label);
        AutomationProperties.SetName(box, name); box.ToolTip = "Ví dụ 1:05.5 hoặc 65.5 (giây)"; _controls.Children.Add(box);
        box.LostKeyboardFocus += (_, _) => { if (!_updating) apply(); };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { apply(); e.Handled = true; } };
    }
    private void SetBox(TextBox box, TimeSpan time) { _updating = true; box.Text = MediaTime.Format(time); _updating = false; }
    private bool Read(TextBox box, string name, out TimeSpan time)
    {
        if (MediaTime.TryParse(box.Text, out time) && time >= TimeSpan.Zero && time <= _info.Duration) return true;
        _error.Text = $"{name}: nhập thời gian trong khoảng 0 đến {MediaTime.Format(_info.Duration)}, ví dụ 1:05.5."; return false;
    }

    private void BuildRange(string title, TimeSpan? max)
    {
        Heading(title);
        _timeline.MaxRange = max;
        _timeline.SetRange(TimeSpan.Zero, max is TimeSpan limit && _info.Duration > limit ? TimeSpan.FromSeconds(Math.Min(5, limit.TotalSeconds)) : _info.Duration);
        Field("Bắt đầu", _startBox, () => ApplyRange());
        Field("Kết thúc", _endBox, () => ApplyRange());
        SetBox(_startBox, _timeline.Start); SetBox(_endBox, _timeline.End);
        var tip = Ui.Text("Kéo hai tay nắm trên thanh thời gian, hoặc đặt tại vị trí đầu phát. [ và ]: đặt đầu/cuối · Mũi tên: 0,1 giây · Shift: 1 giây · Space: phát/dừng.", 12, true);
        tip.Margin = new Thickness(0, 10, 0, 0); _controls.Children.Add(tip);
        if (_tool == ToolKind.MediaTrim)
        {
            var note = Ui.Text(_info.HasVideo ? "Xuất MP4 (H.264/AAC), giữ độ phân giải; video được mã hóa lại." : "Giữ định dạng MP3/M4A/WAV; định dạng khác xuất M4A.", 12, true);
            note.Margin = new Thickness(0, 10, 0, 0); _controls.Children.Add(note);
        }
    }
    private bool ApplyRange()
    {
        _error.Text = "";
        if (!Read(_startBox, "Bắt đầu", out var start) || !Read(_endBox, "Kết thúc", out var end)) return false;
        if (end - start < MediaLimits.MinClip) { _error.Text = "Kết thúc phải sau bắt đầu ít nhất 0,1 giây."; return false; }
        if (_timeline.MaxRange is TimeSpan max && end - start > max) { _error.Text = $"Đoạn tối đa {max.TotalSeconds:0} giây."; return false; }
        _timeline.SetRange(start, end); _timeline.SetPosition(start); return true;
    }
    private void BuildGif()
    {
        Heading("GIF");
        var fps = new WrapPanel();
        foreach (var value in new[] { 10, 12, 15 }) Choice(fps, $"{value} fps", () => _fps == value, () => _fps = value);
        _controls.Children.Add(fps);
        var widths = new WrapPanel();
        foreach (var value in new[] { 320, 480, 640 }) Choice(widths, $"{value} px", () => _gifWidth == value, () => _gifWidth = value, value <= _info.Width);
        _fps = Orbit.Settings.Defaults.GifFps is int f && f is 10 or 12 or 15 ? f : 12;
        _gifWidth = Orbit.Settings.Defaults.GifWidth is int w && w is 320 or 480 or 640 && w <= _info.Width ? w : Math.Min(480, _info.Width - _info.Width % 2);
        _controls.Children.Add(widths); RefreshChoices();
    }
    private void BuildFrame()
    {
        Heading("Thời điểm");
        Field("Khung hình tại", _atBox, () => { _error.Text = ""; if (Read(_atBox, "Thời điểm", out var at)) _timeline.SetPosition(at); });
        SetBox(_atBox, TimeSpan.Zero);
        var tip = Ui.Text($"Lưu PNG đúng độ phân giải video ({_info.Width} × {_info.Height} px). Mũi tên: 0,1 giây · Shift: 1 giây.", 12, true); tip.Margin = new Thickness(0, 10, 0, 0); _controls.Children.Add(tip);
    }
    private void BuildCompress()
    {
        Heading("Độ phân giải");
        var shortSide = Math.Min(_info.Width, _info.Height);
        var sizes = new WrapPanel();
        Choice(sizes, "Gốc", () => _height == 0, () => _height = 0);
        foreach (var value in new[] { 1080, 720, 480 }) Choice(sizes, $"{value}p", () => _height == value, () => _height = value, value < shortSide);
        var remembered = Orbit.Settings.Defaults.VideoHeight;
        _height = remembered is int h && (h == 0 || h < shortSide) ? h : shortSide > 720 ? 720 : 0;
        _quality = Orbit.Settings.Defaults.VideoQuality ?? MediaQuality.Medium;
        _controls.Children.Add(sizes);
        Heading("Chất lượng");
        var qualities = new WrapPanel();
        foreach (var (name, value) in new[] { ("Cao", MediaQuality.High), ("Vừa", MediaQuality.Medium), ("Thấp", MediaQuality.Low) }) Choice(qualities, name, () => _quality == value, () => _quality = value);
        _controls.Children.Add(qualities); RefreshChoices();
        var note = Ui.Text("Xuất MP4 (H.264/AAC). Video dọc giữ cạnh ngắn theo mức đã chọn. Dung lượng là ước tính theo bitrate mục tiêu, file thật có thể khác." + (_files.Length > 1 ? " Mỗi file dùng cùng thiết lập theo kích thước riêng." : ""), 12, true);
        note.Margin = new Thickness(0, 10, 0, 0); _controls.Children.Add(note);
    }
    private void UpdateSummary()
    {
        if (_tool == ToolKind.VideoCompress && _info.HasVideo)
        {
            var (w, h) = MediaLimits.Scale(_info.Width, _info.Height, _height);
            var rate = MediaLimits.VideoBitrate(w, h, _info.Fps, _quality) + (_info.HasAudio ? 128_000 : 0);
            _summary.Text = $"Đầu ra · {w} × {h} px · {rate / 1e6:0.0} Mbps\nƯớc tính khoảng {rate / 8d * _info.Duration.TotalSeconds / 1048576:0} MB";
        }
        else if (_tool == ToolKind.VideoGif)
        {
            var seconds = (_timeline.End - _timeline.Start).TotalSeconds; var height = (int)Math.Round(_gifWidth * (double)_info.Height / Math.Max(1, _info.Width));
            _summary.Text = $"GIF · {_gifWidth} × {height} px · {seconds:0.0} giây · {Math.Ceiling(seconds * _fps):0} khung hình";
        }
        else if (_tool == ToolKind.MediaTrim) _summary.Text = $"Đoạn giữ lại · {MediaTime.Format(_timeline.End - _timeline.Start)}";
        else if (_tool == ToolKind.VideoFrame) _summary.Text = $"PNG · {_info.Width} × {_info.Height} px";
    }

    private async Task ShowFrame(TimeSpan time)
    {
        if (_preview == null || _closed) return;
        _frameRequest?.Cancel(); var request = _frameRequest = new CancellationTokenSource();
        var width = Math.Min(960, _info.Width); var height = Math.Max(2, (int)Math.Round(width * (double)_info.Height / _info.Width));
        try
        {
            var frame = await _preview.FrameAsync(time, width, height, request.Token);
            if (!_closed && request == _frameRequest) { _frame.Source = frame; _frameStatus.Text = MediaTime.Format(time); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) _frameStatus.Text = "Chưa xem trước được: " + error.Message; }
    }

    private void TogglePlay()
    {
        if (_player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) { Pause(); return; }
        try
        {
            _player ??= new MediaPlayer { Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(Path.GetFullPath(_files[0]))), AutoPlay = false };
            var from = _timeline.RangeMode && (_timeline.Position < _timeline.Start || _timeline.Position >= _timeline.End) ? _timeline.Start : _timeline.Position;
            _player.PlaybackSession.Position = from; _player.Play(); _playTimer.Start(); _play.Content = "❚❚ Dừng";
        }
        catch (Exception error) { _error.Text = "Không phát được: " + error.Message; }
    }
    private void Pause()
    {
        _playTimer.Stop(); _play.Content = "▶ Phát thử";
        try { _player?.Pause(); } catch (Exception) { }
    }
    private async Task PlayTick()
    {
        if (_player == null) return;
        var position = _player.PlaybackSession.Position;
        var stop = _timeline.RangeMode ? _timeline.End : _info.Duration;
        if (position >= stop || _player.PlaybackSession.PlaybackState is MediaPlaybackState.Paused or MediaPlaybackState.None && position > TimeSpan.Zero && DateTime.UtcNow - _lastPlayFrame > TimeSpan.FromSeconds(1))
        { Pause(); _timeline.SetPosition(TimeSpan.FromTicks(Math.Min(position.Ticks, stop.Ticks))); return; }
        _timeline.SetPosition(position);
        if (DateTime.UtcNow - _lastPlayFrame > TimeSpan.FromMilliseconds(300)) { _lastPlayFrame = DateTime.UtcNow; await ShowFrame(position); }
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        var step = TimeSpan.FromSeconds(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : .1);
        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Left: Pause(); _timeline.SetPosition(_timeline.Position - step); break;
            case Key.Right: Pause(); _timeline.SetPosition(_timeline.Position + step); break;
            case Key.OemOpenBrackets when _timeline.RangeMode: _timeline.SetRange(_timeline.Position, _timeline.End); break;
            case Key.OemCloseBrackets when _timeline.RangeMode: _timeline.SetRange(_timeline.Start, _timeline.Position); break;
            case Key.Enter: Save(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Save()
    {
        if (_closed) return;
        _error.Text = "";
        try
        {
            MediaOptions options;
            switch (_tool)
            {
                case ToolKind.MediaTrim: case ToolKind.VideoGif:
                    if ((_startBox.IsKeyboardFocused || _endBox.IsKeyboardFocused) && !ApplyRange()) return;
                    var range = MediaLimits.Range(new(_timeline.Start, _timeline.End), _info.Duration, _tool == ToolKind.VideoGif ? MediaLimits.MaxGif : null);
                    options = new(range.Start, range.End, GifFps: _fps, GifWidth: _gifWidth); break;
                case ToolKind.VideoFrame:
                    if (_atBox.IsKeyboardFocused && !Read(_atBox, "Thời điểm", out _)) return;
                    if (_atBox.IsKeyboardFocused && MediaTime.TryParse(_atBox.Text, out var at)) _timeline.SetPosition(at);
                    options = new(Start: _timeline.Position); break;
                default: options = new(Height: _height, Quality: _quality); break;
            }
            Pause();
            try
            {
                var d = Orbit.Settings.Defaults;
                if (_tool == ToolKind.VideoGif) { d.GifFps = _fps; d.GifWidth = _gifWidth; }
                else if (_tool == ToolKind.VideoCompress) { d.VideoHeight = _height; d.VideoQuality = _quality; }
                if (_tool is ToolKind.VideoGif or ToolKind.VideoCompress) Orbit.Settings.Save();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            Orbit.Queue.Add(_files.Select(path => new ImageRequest(path, _tool, Media: options)));
            Close();
        }
        catch (ArgumentException error) { _error.Text = error.Message; }
    }
}
