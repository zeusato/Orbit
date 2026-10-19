using System.Windows.Threading;
using Orbit.App.Interop;
using Orbit.App.Services;

namespace Orbit.App.UI;

// Small, non-activating panel in the notification corner while jobs run. It appears only
// after a job has been busy for `Delay`, so quick conversions keep just the completion toast.
public sealed class ProgressToast : Window
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(700);
    private readonly JobQueue _queue;
    private readonly Func<bool> _suppressed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly HashSet<ImageJob> _batch = [];
    private readonly TextBlock _title = Ui.Text("", 13), _name = Ui.Text("", 12), _status = Ui.Text("", 12, true);
    private readonly ProgressBar _bar = new() { Height = 4, Maximum = 100, Margin = new Thickness(0, 9, 0, 9) };
    private readonly Button _cancel;
    private DateTime _busySince;
    public ImageJob? Current { get; private set; }
    public ProgressBar Bar => _bar;

    public ProgressToast(JobQueue queue, Action openOrbit, Func<bool> suppressed)
    {
        _queue = queue; _suppressed = suppressed;
        Title = "Orbit · Đang xử lý"; Width = 340; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; Focusable = false;
        FontFamily = new FontFamily("Segoe UI Variable Text");
        _title.FontWeight = FontWeights.SemiBold;
        _name.TextTrimming = TextTrimming.CharacterEllipsis; _name.TextWrapping = TextWrapping.NoWrap; _name.Margin = new Thickness(0, 6, 0, 0);
        _status.TextTrimming = TextTrimming.CharacterEllipsis; _status.TextWrapping = TextWrapping.NoWrap;
        _bar.SetResourceReference(Control.ForegroundProperty, "Accent");
        _cancel = Ui.Button("Hủy", () => { foreach (var job in _batch.Where(j => j.CanCancel)) job.Cancellation.Cancel(); });
        var open = Ui.Button("Mở Orbit", openOrbit);
        foreach (var button in new[] { _cancel, open }) { button.Padding = new Thickness(10, 4, 10, 4); button.MinHeight = 28; button.Focusable = false; }
        var buttons = Ui.Row(open, _cancel); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var stack = new StackPanel(); stack.Children.Add(_title); stack.Children.Add(_name); stack.Children.Add(_bar);
        var foot = new DockPanel(); DockPanel.SetDock(buttons, Dock.Right); foot.Children.Add(buttons); _status.VerticalAlignment = VerticalAlignment.Center; foot.Children.Add(_status);
        stack.Children.Add(foot);
        // Opaque fill: a layered toast cannot use the DWM backdrop, and text must stay readable.
        var card = new Border { Child = stack, Padding = new Thickness(16, 13, 14, 13), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(8) };
        card.SetResourceReference(Border.BorderBrushProperty, "Edge");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = .25 };
        Content = card; RefreshTheme();
        System.Windows.Automation.AutomationProperties.SetName(_bar, "Tiến độ tác vụ");
        SourceInitialized += (_, _) => Native.SetNoActivate(this, true);
        SizeChanged += (_, _) => Place();
        _timer.Tick += (_, _) => Refresh();
        _queue.Jobs.CollectionChanged += (_, _) => { if (_queue.HasPending && !_timer.IsEnabled) { _busySince = DateTime.UtcNow; _timer.Start(); } };
    }
    public void RefreshTheme()
    {
        if (Content is not Border card) return;
        var fill = ((SolidColorBrush)Theme.Brush("WindowFill")).Color; fill.A = 255;
        card.Background = new SolidColorBrush(fill);
    }
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8; Top = area.Bottom - ActualHeight - 8;
    }
    // Polled only while jobs are pending; the timer stops when the queue is idle.
    public void Refresh()
    {
        var pending = _queue.Jobs.Where(j => j.CanCancel).ToArray();
        if (pending.Length == 0) { _timer.Stop(); _batch.Clear(); Current = null; if (IsVisible) Hide(); return; }
        foreach (var job in pending) _batch.Add(job);
        Current = pending.FirstOrDefault(j => j.State == JobState.Running) ?? pending.Last();
        var done = _batch.Count(j => !j.CanCancel);
        _title.Text = _batch.Count > 1 ? $"Orbit · Đang xử lý {Math.Min(done + 1, _batch.Count)}/{_batch.Count}" : "Orbit · Đang xử lý";
        _name.Text = Current.Name; _name.ToolTip = Current.Request.Source;
        _bar.IsIndeterminate = Current.State != JobState.Running || Current.Progress <= 5;
        _bar.Value = Current.Progress;
        _status.Text = Current.State == JobState.Running ? $"{ToolCatalog.Label(Current.Request.Tool)} · {Current.Progress}%" : "Đang xếp hàng";
        _cancel.Content = _batch.Count(j => j.CanCancel) > 1 ? "Hủy tất cả" : "Hủy";
        var due = DateTime.UtcNow - _busySince >= Delay;
        if (due && !_suppressed()) { if (!IsVisible) { Show(); Place(); } }
        else if (IsVisible && _suppressed()) Hide();
    }
}
