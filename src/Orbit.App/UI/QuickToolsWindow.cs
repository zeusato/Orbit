using Microsoft.Win32;
using Orbit.App.Imaging;
using Orbit.App.Services;

namespace Orbit.App.UI;

public sealed class QuickToolsWindow : GlassWindow
{
    private readonly QuickPane[] _panes;
    public QuickPane ActivePane => _panes[_navigation.SelectedIndex];
    private readonly ListBox _navigation = new() { Name = "UtilityNavigation", BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    public void SelectTool(int index)
    {
        if (index < 0 || index >= _panes.Length) throw new ArgumentOutOfRangeException(nameof(index));
        _navigation.SelectedIndex = index;
    }

    public QuickToolsWindow(OrbitApplication app) : base(app, "Tiện ích", 1040, 720)
    {
        MinWidth = 880; MinHeight = 620;
        _panes = [new QrPane(), new CodeReaderPane(), new ImageBase64Pane(),
            new TextUtilityPane("Mã hóa văn bản", "UTF-8 · URL xử lý từng thành phần; dấu + giữ nguyên.",
                [("Base64 →", TextAction.Base64Encode), ("← Base64", TextAction.Base64Decode), ("URL →", TextAction.UrlEncode), ("← URL", TextAction.UrlDecode)]),
            new TextUtilityPane("JSON", "Kiểm tra cú pháp, trình bày dễ đọc hoặc thu gọn; giữ thứ tự thuộc tính.",
                [("Định dạng", TextAction.JsonFormat), ("Thu gọn", TextAction.JsonMinify)]),
            new TextUtilityPane("Dọn văn bản", "Chọn thao tác bên dưới. Dùng kết quả làm đầu vào để xử lý tiếp.",
                [("Gọn đầu/cuối dòng", TextAction.TrimLines), ("Bỏ dòng trống", TextAction.RemoveEmptyLines),
                    ("Bỏ dòng trùng", TextAction.UniqueLines), ("CHỮ HOA", TextAction.Uppercase), ("chữ thường", TextAction.Lowercase)])];
        var body = new Grid { Margin = new Thickness(24, 0, 24, 18) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        _navigation.SetResourceReference(Control.ForegroundProperty, "Ink");
        foreach (var label in new[] { "Tạo QR", "Đọc QR / Barcode", "Ảnh ↔ Base64", "Base64 / URL", "JSON", "Dọn văn bản" })
            _navigation.Items.Add(new ListBoxItem { Content = label, Padding = new Thickness(12, 14, 8, 14) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var hint = Ui.Text("Chọn công cụ, nhập hoặc dán nội dung.\nXử lý ngay trên máy.", 12, true);
        hint.Margin = new Thickness(4, 16, 0, 0); DockPanel.SetDock(hint, Dock.Bottom); left.Children.Add(hint); left.Children.Add(_navigation);
        body.Children.Add(left);
        var content = new Grid(); Grid.SetColumn(content, 1); body.Children.Add(content);
        foreach (var pane in _panes) { pane.Visibility = Visibility.Collapsed; content.Children.Add(pane); }
        _navigation.SelectionChanged += (_, _) =>
        {
            for (int i = 0; i < _panes.Length; i++) _panes[i].Visibility = i == _navigation.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        };
        _navigation.SelectedIndex = 0; SetBody(body);
        Closed += (_, _) => { foreach (var pane in _panes) pane.Dispose(); };
    }
}

public abstract class QuickPane : Grid, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Grid _content = new();
    protected readonly TextBlock Status = Ui.Text("", 12, true);
    private bool _disposed;
    public Task Pending { get; private set; } = Task.CompletedTask;
    protected QuickPane(string title, string description)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(Ui.Text(title, 22));
        var subtitle = Ui.Text(description, 13, true); subtitle.Margin = new Thickness(0, 8, 0, 16); heading.Children.Add(subtitle);
        Children.Add(heading); Grid.SetRow(_content, 1); Children.Add(_content);
        Status.Margin = new Thickness(0, 12, 0, 0); Status.MinHeight = 32; Grid.SetRow(Status, 2); Children.Add(Status);
    }
    protected void SetContent(UIElement content)
    {
        _content.Children.Add(content);
        // Native TextBox paste truncates at MaxLength. Reject it explicitly instead of processing a partial payload.
        DataObject.AddPastingHandler(_content, (_, e) =>
        {
            if (e.OriginalSource is TextBox { IsReadOnly: false, MaxLength: > 0 } box &&
                e.DataObject.GetData(DataFormats.UnicodeText) is string text && text.Length + box.Text.Length - box.SelectionLength > box.MaxLength)
            { e.CancelCommand(); Message($"Nội dung vượt giới hạn {box.MaxLength:N0} ký tự; chưa dán vào ô.", true); }
        });
    }
    protected void Message(string text, bool error = false)
    { Status.Text = text; Status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "MutedInk"); }
    protected void Try(Action action)
    { try { action(); } catch (Exception error) { Message(error.Message, true); } }
    protected Task Begin<T>(Func<CancellationToken, T> work, Action<T> apply)
    {
        if (_disposed || !Pending.IsCompleted) return Pending;
        return Pending = Execute(work, apply);
    }
    private async Task Execute<T>(Func<CancellationToken, T> work, Action<T> apply)
    {
        _content.IsEnabled = false; Message("Đang xử lý…");
        try
        {
            var result = await Task.Run(() => work(_lifetime.Token), _lifetime.Token);
            if (!_disposed) { _lifetime.Token.ThrowIfCancellationRequested(); apply(result); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed) Message(error.Message, true); }
        finally { if (!_disposed) _content.IsEnabled = true; }
    }
    protected static TextBox TextArea(string name, bool readOnly = false, int maximum = TextUtilities.MaximumCharacters) => new()
    {
        Name = name, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsReadOnly = readOnly, MaxLength = maximum, Padding = new Thickness(10), MinHeight = 80
    };
    protected static System.Windows.Controls.Image Preview(string name) => new()
    { Name = name, Stretch = Stretch.Uniform, Margin = new Thickness(12), SnapsToDevicePixels = true };
    protected static WrapPanel Actions(params Button[] buttons)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var button in buttons) { button.Margin = new Thickness(0, 0, 8, 8); panel.Children.Add(button); }
        return panel;
    }
    protected static Grid Vertical(UIElement main, UIElement bottom)
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(main); Grid.SetRow(bottom, 1); grid.Children.Add(bottom); return grid;
    }
    protected static Grid Columns(UIElement left, UIElement right)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        ((FrameworkElement)left).Margin = new Thickness(0, 0, 12, 0); grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    protected static DockPanel Labelled(string label, UIElement content)
    {
        var panel = new DockPanel();
        var title = Ui.Text(label, 12, true); title.Margin = new Thickness(0, 0, 0, 6); DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title); panel.Children.Add(content);
        System.Windows.Automation.AutomationProperties.SetName(content, label);
        return panel;
    }
    protected void CopyText(string text)
    { if (text.Length == 0) return; Try(() => { Clipboard.SetText(text); Message("Đã sao chép văn bản."); }); }
    protected void PasteText(TextBox input) => Try(() =>
    {
        if (!Clipboard.ContainsText()) throw new ArgumentException("Clipboard chưa có văn bản.");
        var text = Clipboard.GetText();
        if (text.Length > input.MaxLength) throw new ArgumentException("Nội dung clipboard vượt giới hạn của công cụ.");
        input.Text = text;
    });
    protected void PasteImage(Action<BitmapSource> accept) => Try(() => accept(ClipboardImages.Read()));
    protected void ImagePasteShortcut(Action<BitmapSource> accept)
    {
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control ||
                Keyboard.FocusedElement is TextBox { IsReadOnly: false } or PasswordBox) return;
            e.Handled = true; PasteImage(accept);
        };
    }
    protected void PickImage(Action<string> accept) => Try(() =>
    {
        var dialog = new OpenFileDialog { Title = "Chọn ảnh", Filter = "Ảnh|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.avif;*.heic;*.heif;*.tga|Tất cả file|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) accept(dialog.FileName);
    });
    protected void Save(byte[] bytes, string filename, string extension)
    {
        Try(() =>
        {
            var dialog = new SaveFileDialog { Title = "Lưu bản sao", FileName = filename, DefaultExt = extension,
                Filter = $"{extension} files|*{extension}", AddExtension = true, OverwritePrompt = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            _ = Begin(_ => QuickImageTools.SaveCopy(bytes, dialog.FileName), path => Message("Đã lưu: " + path));
        });
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); }
}

public sealed class QrPane : QuickPane
{
    private readonly TextBox _input = TextArea("QrInput", maximum: 2000);
    private readonly System.Windows.Controls.Image _preview = Preview("QrPreview");
    private BitmapSource? _result;
    public QrPane() : base("Tạo QR", "Dán link hoặc văn bản. Giữ nguyên nội dung, kể cả dấu tiếng Việt và xuống dòng.")
    {
        var copy = Ui.Button("Sao chép ảnh", () => Try(() => { Clipboard.SetImage(_result!); Message("Đã sao chép ảnh QR."); }));
        var save = Ui.Button("Lưu PNG…", () => Save(QuickImageTools.Png(_result!), "Orbit-QR.png", ".png"));
        copy.IsEnabled = save.IsEnabled = false;
        _input.TextChanged += (_, _) => { _result = null; _preview.Source = null; copy.IsEnabled = save.IsEnabled = false; Message("Bấm Tạo QR để cập nhật kết quả."); };
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        var left = Vertical(Labelled("Link hoặc nội dung", _input), Actions(Ui.Button("Dán văn bản", () => PasteText(_input)), Ui.Button("Tạo QR", () =>
        {
            var text = _input.Text;
            _ = Begin(_ => QuickImageTools.Qr(text), bitmap =>
            {
                _result = bitmap; _preview.Source = bitmap; copy.IsEnabled = save.IsEnabled = true;
                Message("QR 512 × 512 · có thể sao chép ảnh hoặc lưu PNG.");
            });
        }, true)));
        var panel = Ui.Panel(_preview, 0); panel.Background = Brushes.White;
        SetContent(Columns(left, Vertical(panel, Actions(copy, save))));
    }
}

public sealed class CodeReaderPane : QuickPane
{
    private readonly System.Windows.Controls.Image _preview = Preview("CodePreview");
    private readonly TextBox _output = TextArea("CodeOutput", true);
    private readonly Button _copy;
    public CodeReaderPane() : base("Đọc QR / Barcode", "Chọn ảnh hoặc dán ảnh từ clipboard · Ctrl+V. Kết quả là văn bản; link không tự mở.")
    {
        _copy = Ui.Button("Sao chép kết quả", () => CopyText(_output.Text)); _copy.IsEnabled = false;
        var left = Vertical(Ui.Panel(_preview, 0), Actions(
            Ui.Button("Chọn ảnh và đọc…", () => PickImage(path => LoadAndRead(path)), true),
            Ui.Button("Dán ảnh", () => PasteImage(image => ReadImageAsync(image)))));
        SetContent(Columns(left, Vertical(Labelled("Kết quả", _output), Actions(_copy))));
        ImagePasteShortcut(image => ReadImageAsync(image));
    }
    private void ClearResult() { _output.Clear(); _copy.IsEnabled = false; _preview.Source = null; }
    public Task LoadAndRead(string path)
    {
        if (!Pending.IsCompleted) return Pending;
        ClearResult();
        return Begin(token =>
        {
            var image = new ImageEngine().Load(path);
            return (image, codes: QuickImageTools.ReadCodes(image, token));
        }, result => Apply(result.image, result.codes));
    }
    public Task ReadImageAsync(BitmapSource image)
    {
        if (!Pending.IsCompleted) return Pending;
        ClearResult(); var snapshot = image.Clone(); snapshot.Freeze();
        return Begin(token => QuickImageTools.ReadCodes(snapshot, token), codes => Apply(snapshot, codes));
    }
    private void Apply(BitmapSource image, DecodedCode[] codes)
    {
        _preview.Source = image;
        _output.Text = codes.Length == 1 ? codes[0].Text : string.Join("\n\n", codes.Select((c, i) => $"{i + 1}. {c.Format}\n{c.Text}"));
        _copy.IsEnabled = codes.Length > 0;
        Message(codes.Length == 0 ? "Chưa tìm thấy mã. Thử ảnh rõ hơn hoặc cắt sát vùng mã, giữ phần viền trắng." :
            $"Đọc được {codes.Length} mã · {string.Join(", ", codes.Select(c => c.Format).Distinct())}", codes.Length == 0);
    }
}

public sealed class ImageBase64Pane : QuickPane
{
    private readonly TextBox _text = TextArea("ImageBase64Text", maximum: QuickImageTools.MaximumBase64Characters);
    private readonly System.Windows.Controls.Image _preview = Preview("Base64Preview");
    private readonly CheckBox _dataUri = new() { Content = "Kèm data:image/…;base64,", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _save;
    private ImagePayload? _payload;
    private bool _setting;
    public ImageBase64Pane() : base("Ảnh ↔ Base64", "Ảnh tối đa 8 MB. Nhận Base64 thuần hoặc data URI. File giải mã giữ nguyên dữ liệu ảnh.")
    {
        _text.TextWrapping = TextWrapping.NoWrap; _text.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _save = Ui.Button("Lưu ảnh…", () => Save(_payload!.Bytes, "Orbit-image" + _payload.Extension, _payload.Extension)); _save.IsEnabled = false;
        _text.TextChanged += (_, _) =>
        {
            if (_setting) return;
            _payload = null; _preview.Source = null; _save.IsEnabled = false; Message("Bấm Giải mã → ảnh để xem và lưu.");
        };
        void UpdateEncoding(object sender, RoutedEventArgs e)
        {
            if (_payload == null) return;
            SetText(QuickImageTools.ToBase64(_payload, _dataUri.IsChecked == true));
        }
        _dataUri.Checked += UpdateEncoding; _dataUri.Unchecked += UpdateEncoding;
        var leftButtons = new StackPanel();
        leftButtons.Children.Add(_dataUri);
        leftButtons.Children.Add(Actions(Ui.Button("Dán Base64", () => PasteText(_text)),
            Ui.Button("Giải mã → ảnh", Decode, true), Ui.Button("Sao chép", () => CopyText(_text.Text))));
        var rightButtons = Actions(Ui.Button("Ảnh → Base64…", () => PickImage(path => LoadFileAsync(path))),
            Ui.Button("Dán ảnh", () => PasteImage(image => LoadImageAsync(image))), _save);
        SetContent(Columns(Vertical(Labelled("Chuỗi Base64", _text), leftButtons), Vertical(Ui.Panel(_preview, 0), rightButtons)));
        ImagePasteShortcut(image => LoadImageAsync(image));
    }
    private void SetText(string text) { _setting = true; try { _text.Text = text; } finally { _setting = false; } }
    private void Apply(ImagePayload payload, bool encode)
    {
        _payload = payload; _preview.Source = payload.Bitmap; _save.IsEnabled = true;
        // TGA has no mandatory signature, so retain its format hint in the data URI.
        _dataUri.IsEnabled = payload.Extension != ".tga";
        if (payload.Extension == ".tga") _dataUri.IsChecked = true;
        if (encode) SetText(QuickImageTools.ToBase64(payload, _dataUri.IsChecked == true));
        Message($"{payload.Bitmap.PixelWidth} × {payload.Bitmap.PixelHeight} · {PdfCompression.Size(payload.Bytes.Length)} · {payload.Mime}");
    }
    public Task LoadFileAsync(string path)
    {
        if (!Pending.IsCompleted) return Pending;
        _payload = null; _preview.Source = null; _save.IsEnabled = false; SetText("");
        return Begin(_ => QuickImageTools.FromFile(path), payload => Apply(payload, true));
    }
    public Task LoadImageAsync(BitmapSource image)
    {
        if (!Pending.IsCompleted) return Pending;
        _payload = null; _preview.Source = null; _save.IsEnabled = false; SetText("");
        var snapshot = image.Clone(); snapshot.Freeze();
        return Begin(_ => QuickImageTools.FromBytes(QuickImageTools.Png(snapshot)), payload => Apply(payload, true));
    }
    private void Decode()
    {
        var text = _text.Text; _payload = null; _preview.Source = null; _save.IsEnabled = false;
        _ = Begin(_ => QuickImageTools.FromBase64(text), payload => Apply(payload, false));
    }
}

public sealed class TextUtilityPane : QuickPane
{
    private readonly TextBox _input = TextArea("TextInput");
    private readonly TextBox _output = TextArea("TextOutput", true);
    public TextUtilityPane(string title, string description, (string Label, TextAction Action)[] actions) : base(title, description)
    {
        var copy = Ui.Button("Sao chép kết quả", () => CopyText(_output.Text)); copy.IsEnabled = false;
        var use = Ui.Button("Dùng làm đầu vào", () => _input.Text = _output.Text); use.IsEnabled = false;
        _input.TextChanged += (_, _) => { _output.Clear(); copy.IsEnabled = use.IsEnabled = false; Message("Chọn thao tác để xử lý nội dung."); };
        var commands = new StackPanel();
        commands.Children.Add(Actions(Ui.Button("Dán văn bản", () => PasteText(_input)), Ui.Button("Xóa", () => _input.Clear())));
        commands.Children.Add(Actions(actions.Select(pair => Ui.Button(pair.Label, () =>
        {
            var text = _input.Text; _output.Clear(); copy.IsEnabled = use.IsEnabled = false;
            _ = Begin(_ =>
            {
                var output = TextUtilities.Transform(text, pair.Action);
                return (output, statistics: TextUtilities.Statistics(output));
            }, result =>
            {
                _output.Text = result.output; copy.IsEnabled = result.output.Length > 0; use.IsEnabled = result.output.Length is > 0 and <= TextUtilities.MaximumCharacters;
                Message(result.statistics);
            });
        }, actions.Length <= 2)).ToArray()));
        SetContent(Columns(Vertical(Labelled("Đầu vào", _input), commands), Vertical(Labelled("Kết quả", _output), Actions(copy, use))));
    }
}
