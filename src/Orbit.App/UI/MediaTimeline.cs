using System.Windows.Automation;

namespace Orbit.App.UI;

// Time bar with a playhead and, in range mode, start/end handles. Values are source time.
public sealed class MediaTimeline : FrameworkElement
{
    private const double Pad = 12, TrackTop = 18, TrackHeight = 8;
    private int _drag = -1; // 0 start, 1 end, 2 playhead
    public TimeSpan Duration { get; }
    public bool RangeMode { get; }
    public TimeSpan Start { get; private set; }
    public TimeSpan End { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan? MaxRange { get; set; }
    public event Action? RangeChanged;
    public event Action? PositionChanged;

    public MediaTimeline(TimeSpan duration, bool rangeMode)
    {
        Duration = duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(1); RangeMode = rangeMode; End = Duration;
        Height = 46; Focusable = true; Cursor = Cursors.Hand;
        AutomationProperties.SetName(this, rangeMode ? "Thanh thời gian: kéo hai tay nắm để chọn đoạn" : "Thanh thời gian: kéo để chọn thời điểm");
    }
    private double Width0 => Math.Max(1, ActualWidth - 2 * Pad);
    private double X(TimeSpan time) => Pad + Width0 * Math.Clamp(time.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);
    private TimeSpan Time(double x) => TimeSpan.FromMilliseconds(Math.Round(Duration.TotalMilliseconds * Math.Clamp((x - Pad) / Width0, 0, 1)));

    public void SetRange(TimeSpan start, TimeSpan end)
    {
        start = TimeSpan.FromTicks(Math.Clamp(start.Ticks, 0, Duration.Ticks)); end = TimeSpan.FromTicks(Math.Clamp(end.Ticks, 0, Duration.Ticks));
        if (end < start) (start, end) = (end, start);
        if (MaxRange is TimeSpan max && end - start > max) end = start + max;
        if (end - start < MediaLimits.MinClip) { end = TimeSpan.FromTicks(Math.Min(Duration.Ticks, (start + MediaLimits.MinClip).Ticks)); start = end - MediaLimits.MinClip < TimeSpan.Zero ? TimeSpan.Zero : end - MediaLimits.MinClip; }
        Start = start; End = end; InvalidateVisual(); RangeChanged?.Invoke();
    }
    public void SetPosition(TimeSpan position)
    {
        Position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Math.Max(0, Duration.Ticks - 1)));
        InvalidateVisual(); PositionChanged?.Invoke();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var track = new Rect(Pad, TrackTop, Width0, TrackHeight);
        dc.DrawRoundedRectangle(Theme.Brush("InputFill"), new Pen(Theme.Brush("Line"), 1), track, 4, 4);
        if (RangeMode)
        {
            var selected = new Rect(X(Start), TrackTop, Math.Max(2, X(End) - X(Start)), TrackHeight);
            dc.DrawRoundedRectangle(Theme.Brush("Accent"), null, selected, 4, 4);
            foreach (var t in new[] { Start, End })
                dc.DrawRoundedRectangle(Brushes.White, new Pen(Theme.Brush("Accent"), 2), new Rect(X(t) - 5, TrackTop - 7, 10, TrackHeight + 14), 3, 3);
        }
        var x = X(Position);
        dc.DrawLine(new Pen(Theme.Brush("Ink"), 2), new Point(x, 4), new Point(x, TrackTop + TrackHeight + 6));
        dc.DrawEllipse(Theme.Brush("Ink"), null, new Point(x, 5), 4, 4);
        var label = new FormattedText(MediaTime.Format(Duration), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Text"), 11, Theme.Brush("MutedInk"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(ActualWidth - Pad - label.Width, TrackTop + TrackHeight + 6));
        var zero = new FormattedText("0:00", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Text"), 11, Theme.Brush("MutedInk"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(zero, new Point(Pad, TrackTop + TrackHeight + 6));
    }

    // Shared by mouse input and tests: grab the nearest handle in range mode, otherwise the playhead.
    public void BeginDrag(Point point)
    {
        if (RangeMode && Math.Abs(point.X - X(Start)) <= 9 && Math.Abs(point.X - X(Start)) <= Math.Abs(point.X - X(End))) _drag = 0;
        else if (RangeMode && Math.Abs(point.X - X(End)) <= 9) _drag = 1;
        else _drag = 2;
        DragTo(point);
    }
    public void DragTo(Point point)
    {
        var time = Time(point.X);
        if (_drag == 0) { var end = End; if (MaxRange is TimeSpan max && end - time > max) end = time + max; SetRange(time, end); SetPosition(Start); }
        else if (_drag == 1) { var start = Start; if (MaxRange is TimeSpan max && time - start > max) start = time - max; SetRange(start, time); SetPosition(End); }
        else if (_drag == 2) SetPosition(time);
    }
    public void EndDrag() => _drag = -1;
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); BeginDrag(e.GetPosition(this)); CaptureMouse(); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) DragTo(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { EndDrag(); ReleaseMouseCapture(); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) => EndDrag();
}
