using System.Collections.ObjectModel;
using Microsoft.Win32;
using Orbit.App.Services;

namespace Orbit.App.UI;

public sealed class ImagePdfChoiceWindow : GlassWindow
{
    private bool _closing;
    public ImagePdfChoiceWindow(OrbitApplication app, string[] files) : base(app, "Ảnh → PDF", 300, 60)
    {
        MinWidth = MinHeight = 0;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false });
        Layout.Children.Clear(); Layout.RowDefinitions.Clear();
        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        var quick = Ui.Button("Chuyển ngay", () =>
        {
            app.Queue.Add(files.Select(file => new ImageRequest(file, ToolKind.ImagePdf)));
            Close();
        }, true);
        quick.MinWidth = 128; quick.Margin = new Thickness(0, 0, 8, 0);
        quick.ToolTip = files.Length == 1 ? "Tạo PDF một trang." : "Tạo một PDF riêng cho mỗi ảnh.";
        body.Children.Add(quick);
        var add = Ui.Button("Thêm trang…", () =>
        {
            Close();
            new PdfEditorWindow(app, ToolKind.ImagePdf, files).Show();
        });
        add.MinWidth = 128; add.ToolTip = "Thêm ảnh hoặc PDF, sắp xếp rồi xuất chung một PDF.";
        body.Children.Add(add);
        Layout.Children.Add(body);
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => { if (!_closing) Close(); };
    }
}

public sealed class PdfEditorWindow : GlassWindow
{
    private readonly ToolKind _tool;
    private readonly ObservableCollection<PdfInput> _inputs = [];
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly System.Windows.Controls.Image _preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
    private readonly TextBlock _previewLabel = Ui.Text("Chọn file để xem trước", 12, true);
    private readonly TextBlock _summary = Ui.Text("", 13, true);
    private readonly TextBlock _error = Ui.Text("", 12);
    private readonly TextBox _pages = new() { Name = "PageRange", ToolTip = "Để trống = tất cả. Ví dụ: 1-3, 5." };
    private readonly PdfOutputControls _output;
    private readonly CheckBox _separate = new() { Content = "Mỗi trang thành một file PDF", IsChecked = true };
    private readonly Button _save;
    private readonly Button _add;
    private readonly Button _up;
    private readonly Button _down;
    private readonly Button _remove;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _previewGate = new(1, 1);
    private CancellationTokenSource? _previewCancellation;
    private bool _loading, _closed;
    private int _page;
    private bool Split => _tool == ToolKind.PdfSplit;
    public Task Initialization { get; }
    public Task PreviewTask { get; private set; } = Task.CompletedTask;

    public PdfEditorWindow(OrbitApplication app, ToolKind tool, string[] files)
        : base(app, tool == ToolKind.PdfSplit ? "Tách trang PDF" : "Ghép PDF và ảnh", 980, 700)
    {
        _tool = tool;
        MinWidth = 820; MinHeight = 700;
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        _list.SetResourceReference(Control.ForegroundProperty, "Ink");
        _list.ItemsSource = _inputs;
        _list.SelectionChanged += (_, _) => { _page = 0; Refresh(); PreviewTask = ShowPreview(); };
        var body = new Grid { Margin = new Thickness(24, 4, 24, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition());
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var intro = Ui.Text(Split ? "Chọn các trang cần giữ trong bản tách." : "Thêm PDF hoặc ảnh. Thứ tự từ trên xuống là thứ tự trong PDF thành phẩm.", 13, true);
        intro.Margin = new Thickness(0, 0, 0, 12); left.Children.Add(intro);
        var listPanel = Ui.Panel(_list, 8); Grid.SetRow(listPanel, 1); left.Children.Add(listPanel);
        var commands = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _add = Ui.Button("Thêm PDF / ảnh…", PickFiles);
        _add.HorizontalAlignment = HorizontalAlignment.Left;
        commands.Children.Add(_add);
        if (!Split)
        {
            var paste = Ui.Button("Dán ảnh · Ctrl+V", PasteClipboard);
            paste.HorizontalAlignment = HorizontalAlignment.Left; paste.Margin = new Thickness(0, 8, 0, 0);
            commands.Children.Add(paste);
            PreviewKeyDown += (_, e) => { if (ClipboardImages.IsPaste(e)) { e.Handled = true; PasteClipboard(); } };
        }
        _up = Ui.Button("Lên", () => Move(-1));
        _down = Ui.Button("Xuống", () => Move(1));
        _remove = Ui.Button("Bỏ khỏi danh sách", Remove);
        var order = Ui.Row(_up, _down, _remove); order.Margin = new Thickness(0, 8, 0, 0); commands.Children.Add(order);
        if (Split)
        {
            _add.Visibility = order.Visibility = Visibility.Collapsed;
            commands.Children.Add(Ui.Text("Trang cần tách · ví dụ 1-3, 5"));
            _pages.Margin = new Thickness(0, 6, 0, 4); commands.Children.Add(_pages);
            commands.Children.Add(Ui.Text("Để trống để lấy tất cả trang. Giữ thứ tự đã nhập.", 12, true));
            commands.Children.Add(_separate);
            commands.Children.Add(Ui.Text("Bỏ chọn để lưu các trang đã chọn vào một PDF.", 12, true));
        }
        _output = new PdfOutputControls(app, tool, files[0]) { Margin = new Thickness(0, 14, 0, 0) };
        commands.Children.Add(_output);
        Grid.SetRow(commands, 2); left.Children.Add(commands);
        _summary.Margin = new Thickness(0, 14, 0, 0); Grid.SetRow(_summary, 3); left.Children.Add(_summary);
        _pages.TextChanged += (_, _) => Refresh();
        _separate.Checked += (_, _) => Refresh(); _separate.Unchecked += (_, _) => Refresh();
        body.Children.Add(left);
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var previewPanel = Ui.Panel(_preview, 0); right.Children.Add(previewPanel);
        _previous = Ui.Button("‹", () => ChangePage(-1)); _previous.ToolTip = "Trang trước";
        _next = Ui.Button("›", () => ChangePage(1)); _next.ToolTip = "Trang sau";
        _previewLabel.VerticalAlignment = VerticalAlignment.Center;
        var navigation = Ui.Row(_previous, _next, _previewLabel); navigation.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(navigation, 1); right.Children.Add(navigation);
        Grid.SetColumn(right, 1); body.Children.Add(right);
        SetBody(body);
        var footer = new DockPanel { Margin = new Thickness(24, 0, 24, 20) };
        _save = Ui.Button(Split ? "Tách trang" : "Xuất PDF", Save, true);
        var buttons = Ui.Row(Ui.Button("Hủy", Close), _save); DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons); footer.Children.Add(_error); SetFooter(footer);
        if (!Split)
        {
            body.AllowDrop = true;
            body.PreviewDragOver += (_, e) =>
            {
                e.Effects = !_loading && e.Data.GetDataPresent(DataFormats.FileDrop) && (e.AllowedEffects & DragDropEffects.Copy) != 0
                    ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
            };
            body.PreviewDrop += async (_, e) =>
            {
                e.Handled = true; e.Effects = DragDropEffects.None;
                if (_loading || (e.AllowedEffects & DragDropEffects.Copy) == 0 || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
                e.Effects = DragDropEffects.Copy; await AddFilesAsync(paths);
            };
        }
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _previewCancellation?.Cancel(); };
        Refresh();
        Initialization = AddFilesAsync(files);
    }

    public async Task AddFilesAsync(string[] files)
    {
        if (_closed || _loading) return;
        if (files.Length == 0) return;
        _loading = true; _error.Text = ""; Refresh();
        try
        {
            if (Split && (files.Length != 1 || _inputs.Count != 0)) throw new ArgumentException("Chọn một PDF để tách trang.");
            if (_inputs.Count + files.Length > 128) throw new ArgumentException("Tối đa 128 file trong một PDF.");
            var added = await Task.Run(() => files.Select(path => PdfAssembly.Inspect(path, _lifetime.Token)).ToArray(), _lifetime.Token);
            if (_closed) return;
            if (Split && !added[0].IsPdf) throw new ArgumentException("Tách trang chỉ nhận PDF.");
            if (_inputs.Sum(input => input.Pages) + added.Sum(input => input.Pages) > PdfPages.Maximum)
                throw new ArgumentException("Thành phẩm tối đa 200 trang. Hãy bớt file.");
            foreach (var input in added) _inputs.Add(input);
            if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) _error.Text = error.Message; }
        finally { _loading = false; if (!_closed) Refresh(); }
    }

    private async void PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Thêm trang từ PDF hoặc ảnh", Multiselect = true,
            Filter = "PDF và ảnh|*.pdf;*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff;*.gif;*.ico;*.avif;*.heic;*.heif;*.tga|Tất cả file|*.*"
        };
        if (dialog.ShowDialog(this) == true) await AddFilesAsync(dialog.FileNames);
    }

    private async void PasteClipboard()
    {
        if (_closed || _loading || Split) return;
        try { await PasteImageAsync(ClipboardImages.Read()); }
        catch (Exception error) { if (!_closed) _error.Text = "Không dán được ảnh: " + error.Message; }
    }

    public async Task PasteImageAsync(BitmapSource image)
    {
        if (_closed || _loading || Split) return;
        _loading = true; Refresh();
        string source;
        try
        {
            var snapshot = image.Clone(); snapshot.Freeze();
            source = await Task.Run(() => ClipboardImages.Save(snapshot), _lifetime.Token);
        }
        finally { _loading = false; if (!_closed) Refresh(); }
        if (!_closed) await AddFilesAsync([source]);
    }

    private void Move(int delta)
    {
        var index = _list.SelectedIndex; var target = index + delta;
        if (_loading || index < 0 || target < 0 || target >= _inputs.Count) return;
        _inputs.Move(index, target); _list.SelectedIndex = target; Refresh();
    }

    private void Remove()
    {
        var index = _list.SelectedIndex;
        if (_loading || index < 0) return;
        _inputs.RemoveAt(index); _list.SelectedIndex = Math.Min(index, _inputs.Count - 1); Refresh();
    }

    private void ChangePage(int delta)
    {
        if (_list.SelectedItem is not PdfInput input) return;
        _page = Math.Clamp(_page + delta, 0, input.Pages - 1); Refresh(); PreviewTask = ShowPreview();
    }

    private async Task ShowPreview()
    {
        _previewCancellation?.Cancel();
        var cancellation = _previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var input = _list.SelectedItem as PdfInput; var page = _page;
        _preview.Source = null;
        if (input == null) { _previewLabel.Text = "Chưa có file"; _previewCancellation = null; cancellation.Dispose(); return; }
        _previewLabel.Text = $"Trang {page + 1}/{input.Pages} · Đang tải…";
        try
        {
            await _previewGate.WaitAsync(cancellation.Token);
            try
            {
                var bitmap = await Task.Run(() => PdfAssembly.Preview(input, page, cancellation.Token), cancellation.Token);
                if (!_closed && !cancellation.IsCancellationRequested)
                { _preview.Source = bitmap; _previewLabel.Text = $"Trang {page + 1}/{input.Pages}"; }
            }
            finally { _previewGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_closed && !cancellation.IsCancellationRequested) _previewLabel.Text = "Không xem trước được: " + error.Message;
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            cancellation.Dispose();
        }
    }

    private void Refresh()
    {
        var valid = _inputs.Count > 0;
        var count = _inputs.Sum(input => input.Pages);
        if (Split && valid)
        {
            try
            {
                count = PdfPages.Parse(_pages.Text, _inputs[0].Pages).Length;
                _summary.Text = $"{count}/{_inputs[0].Pages} trang → " + (_separate.IsChecked == true ? $"{count} file PDF" : "1 file PDF");
            }
            catch (ArgumentException error) { valid = false; _summary.Text = error.Message; }
        }
        else _summary.Text = $"{_inputs.Count} file · {count}/200 trang → 1 PDF\nMỗi ảnh là một trang, giữ tỷ lệ gốc.";
        if (_loading) _summary.Text = "Đang đọc file…";
        _save.IsEnabled = valid && !_loading;
        _add.IsEnabled = !_loading;
        var index = _list.SelectedIndex;
        _up.IsEnabled = !_loading && index > 0;
        _down.IsEnabled = !_loading && index >= 0 && index < _inputs.Count - 1;
        _remove.IsEnabled = !_loading && index >= 0;
        _previous.IsEnabled = _page > 0 && index >= 0;
        _next.IsEnabled = index >= 0 && _page + 1 < _inputs[index].Pages;
    }

    private void Save()
    {
        if (_closed || _loading || _inputs.Count == 0) return;
        try
        {
            var sources = _inputs.Select(input => input.Path).ToArray();
            PdfOptions? options = null;
            if (Split)
            {
                _ = PdfPages.Parse(_pages.Text, _inputs[0].Pages);
                options = new(_pages.Text, _separate.IsChecked == true);
            }
            var output = _output.Read(Split && _separate.IsChecked == true);
            Orbit.Queue.Add([new ImageRequest(sources[0], _tool, Sources: Split ? null : sources, Pdf: options,
                OutputDirectory: output.Directory, OutputStem: output.Stem)]);
            Close();
        }
        catch (Exception error) { _error.Text = error.Message; }
    }
}
