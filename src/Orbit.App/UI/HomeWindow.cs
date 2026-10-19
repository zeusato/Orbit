using System.ComponentModel;
using System.Windows.Data;
using Microsoft.Win32;
using Orbit.App.Services;
using Orbit.App.Interop;

namespace Orbit.App.UI;

public sealed class HomeWindow : GlassWindow
{
    private readonly TextBlock _instruction;
    private readonly TextBlock _status = Ui.Text("Sẵn sàng", 12, true);
    private bool _exit;
    public HomeWindow(OrbitApplication app) : base(app, "ORBIT", 900, 680)
    {
        var body = new Grid { Margin = new Thickness(24, 0, 24, 12) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var intro = new StackPanel(); intro.Children.Add(Ui.Text("Công cụ ngay tại file.", 26));
        _instruction = Ui.Text("", 14, true); _instruction.Margin = new Thickness(0, 9, 0, 20); intro.Children.Add(_instruction); body.Children.Add(intro); UpdateInstruction();
        var dropContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var dropTitle = Ui.Text("Thả file vào đây", 20); dropTitle.TextAlignment = TextAlignment.Center; dropContent.Children.Add(dropTitle);
        var types = Ui.Text("Ảnh · WebP · HEIC · PDF · Word · Excel · PowerPoint", 12, true); types.TextAlignment = TextAlignment.Center; types.Margin = new Thickness(0, 6, 0, 12); dropContent.Children.Add(types);
        var pick = Ui.Button("Chọn file…", PickFiles, true); pick.HorizontalAlignment = HorizontalAlignment.Center; dropContent.Children.Add(pick);
        var dropZone = Ui.Panel(dropContent, 20); dropZone.AllowDrop = true;
        void ResetDrop()
        { dropZone.SetResourceReference(Border.BorderBrushProperty, "Line"); dropZone.SetResourceReference(Border.BackgroundProperty, "PanelFill"); dropTitle.Text = "Thả file vào đây"; }
        dropZone.PreviewDragOver += (_, e) =>
        {
            var valid = IsValidDrop(e) && (e.AllowedEffects & DragDropEffects.Copy) != 0;
            e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
            dropZone.SetResourceReference(Border.BorderBrushProperty, valid ? "Accent" : "Danger");
            dropZone.SetResourceReference(Border.BackgroundProperty, "InputFill");
            dropTitle.Text = valid ? "Thả để chọn công cụ" : "Chọn tối đa 128 file, không chọn thư mục";
        };
        dropZone.PreviewDragLeave += (_, _) => ResetDrop();
        dropZone.PreviewDrop += (_, e) =>
        {
            ResetDrop();
            if (IsValidDrop(e) && (e.AllowedEffects & DragDropEffects.Copy) != 0)
            { var files = (string[])e.Data.GetData(DataFormats.FileDrop); e.Effects = DragDropEffects.Copy; Dispatcher.BeginInvoke(() => OpenMenu(files)); }
            else e.Effects = DragDropEffects.None; e.Handled = true;
        };
        Grid.SetRow(dropZone, 1); body.Children.Add(dropZone);
        var jobsHeader = new DockPanel { Margin = new Thickness(0, 20, 0, 10) };
        var settings = Ui.Button("Cài đặt", () => new SettingsWindow(Orbit) { Owner = this }.ShowDialog()); DockPanel.SetDock(settings, Dock.Right); jobsHeader.Children.Add(settings);
        var clear = Ui.Button("Xóa lịch sử", app.Queue.ClearHistory); clear.ToolTip = "Xóa danh sách tác vụ đã xong. Không xóa file kết quả."; clear.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(clear, Dock.Right); jobsHeader.Children.Add(clear);
        var heading = Ui.Text("Tác vụ gần đây", 17); heading.VerticalAlignment = VerticalAlignment.Center; jobsHeader.Children.Add(heading); Grid.SetRow(jobsHeader, 2); body.Children.Add(jobsHeader);
        var items = new ItemsControl { ItemsSource = app.Queue.Jobs, ItemTemplate = JobTemplate() };
        var scroll = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var jobArea = new Grid(); jobArea.Children.Add(scroll);
        var empty = Ui.Text("Chưa có tác vụ.\nChọn file để hiện các công cụ tương thích.", 14, true);
        empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; empty.TextAlignment = TextAlignment.Center;
        void SyncEmpty() => empty.Visibility = app.Queue.Jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        jobArea.Children.Add(empty); app.Queue.Jobs.CollectionChanged += (_, _) => SyncEmpty(); SyncEmpty();
        Grid.SetRow(jobArea, 3); body.Children.Add(jobArea); SetBody(body);
        var footer = new DockPanel { Margin = new Thickness(24, 0, 24, 18) };
        var background = Ui.Button("Thu xuống khay", Hide); DockPanel.SetDock(background, Dock.Right); footer.Children.Add(background);
        footer.Children.Add(_status); SetFooter(footer);
        Closing += (_, e) => { if (!_exit) { e.Cancel = true; Hide(); } };
        app.Queue.Finished += JobFinished;
        Closed += (_, _) => app.Queue.Finished -= JobFinished;
    }
    public void UpdateInstruction() => _instruction.Text = $"Kéo file trong Explorer → bấm {Orbit.Settings.HotkeyLabel} → thả vào một công cụ.";
    public void SetStatus(string message) => _status.Text = message;
    private void JobFinished(ImageJob job) => _status.Text = job.Status;
    public void ExitWindow() { _exit = true; Close(); }
    private static bool IsValidDrop(DragEventArgs e)
    {
        try { return e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length is > 0 and <= 128 && !files.Any(Directory.Exists); }
        catch { return false; }
    }
    private void PickFiles()
    {
        var dialog = new OpenFileDialog { Title = "Chọn file để xử lý", Multiselect = true, Filter = "File được hỗ trợ|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff;*.gif;*.ico;*.avif;*.heic;*.heif;*.tga;*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx|Tất cả file (ZIP / SHA-256)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            var files = dialog.FileNames;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => OpenMenu(files));
        }
    }
    public void OpenMenu(string[] files)
    {
        if (Orbit.UpdateInProgress) { _status.Text = "Đang chuẩn bị cập nhật. Hãy thử lại sau."; return; }
        if (files.Length > 128) { _status.Text = "Mỗi lần tối đa 128 file."; return; }
        Native.GetCursorPos(out var cursor); Orbit.Menu.ShowMenu(cursor, files);
    }
    private DataTemplate JobTemplate()
    {
        var template = new DataTemplate(typeof(ImageJob));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12)); border.SetValue(Border.PaddingProperty, new Thickness(13)); border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 8)); border.SetResourceReference(Border.BackgroundProperty, "PanelFill");
        var dock = new FrameworkElementFactory(typeof(DockPanel)); border.AppendChild(dock);
        var buttons = new FrameworkElementFactory(typeof(StackPanel)); buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); buttons.SetValue(DockPanel.DockProperty, Dock.Right);
        foreach (var pair in new[] { ("Hủy", "CanCancel"), ("Mở", "CanOpen"), ("Thư mục", "CanOpen") })
        {
            var button = new FrameworkElementFactory(typeof(Button)); button.SetValue(ContentControl.ContentProperty, pair.Item1); button.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 0, 0, 0)); button.SetValue(Control.PaddingProperty, new Thickness(10, 7, 10, 7)); button.SetBinding(UIElement.IsEnabledProperty, new Binding(pair.Item2));
            var action = pair.Item1;
            button.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is not FrameworkElement { DataContext: ImageJob job }) return;
                if (action == "Hủy") job.Cancellation.Cancel();
                else if (job.Output != null) Orbit.OpenResult(job.Output, action == "Thư mục");
            })); buttons.AppendChild(button);
        }
        dock.AppendChild(buttons);
        var stack = new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); stack.AppendChild(name);
        var status = new FrameworkElementFactory(typeof(TextBlock)); status.SetBinding(TextBlock.TextProperty, new Binding("Status")); status.SetValue(TextBlock.FontSizeProperty, 12d); status.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); status.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); stack.AppendChild(status);
        var progress = new FrameworkElementFactory(typeof(ProgressBar)); progress.SetValue(FrameworkElement.HeightProperty, 3d); progress.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 7, 0, 0)); progress.SetValue(System.Windows.Controls.Primitives.RangeBase.MaximumProperty, 100d);
        // RangeBase.Value defaults to TwoWay; progress is owned by the worker.
        progress.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding(nameof(ImageJob.Progress)) { Mode = BindingMode.OneWay });
        progress.SetResourceReference(Control.ForegroundProperty, "Accent"); stack.AppendChild(progress); dock.AppendChild(stack);
        template.VisualTree = border; return template;
    }
}
