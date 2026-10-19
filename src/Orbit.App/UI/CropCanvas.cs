using System.Windows.Automation;

namespace Orbit.App.UI;

// Selection stays in source pixels; zoom and pan only change the viewport.
public sealed class CropCanvas : FrameworkElement
{
    private BitmapSource _image;
    private BitmapSource _preview;
    private readonly bool _editable;
    private double _scale;
    private Vector _offset, _initialOffset;
    private Point _start;
    private CropRegion _initial;
    private int _mode = -1;
    private bool _fit = true;
    public CropRegion Region { get; private set; }
    public double? Aspect { get; private set; }
    public double Zoom => _scale;
    public Rect ImageBounds => new(_offset.X, _offset.Y, _image.PixelWidth * _scale, _image.PixelHeight * _scale);
    public Rect SelectionBounds => new(_offset.X + Region.X * _scale, _offset.Y + Region.Y * _scale, Region.Width * _scale, Region.Height * _scale);
    public event Action<CropRegion>? RegionChanged;
    public event Action<double>? ZoomChanged;

    public CropCanvas(BitmapSource image, bool editable = true)
    {
        _image = _preview = image; _editable = editable;
        Region = _initial = new(0, 0, image.PixelWidth, image.PixelHeight);
        Focusable = true; ClipToBounds = true; MinHeight = 180; Cursor = editable ? Cursors.Cross : Cursors.Hand;
        AutomationProperties.SetName(this, editable ? "Vùng cắt ảnh: cuộn để zoom, Space và kéo để di chuyển ảnh." : "Xem ảnh: cuộn để zoom, kéo để di chuyển.");
        SizeChanged += (_, e) =>
        {
            if (_fit) FitImage();
            else { _offset += new Vector((e.NewSize.Width - e.PreviousSize.Width) / 2, (e.NewSize.Height - e.PreviousSize.Height) / 2); ClampPan(); InvalidateVisual(); }
        };
    }
    public void SetPreview(BitmapSource image) { _preview = image; InvalidateVisual(); }
    // Viewer-only: replaces the displayed image (e.g. a rendered frame) and refits when its size changes.
    public void SetImage(BitmapSource image)
    {
        var resized = image.PixelWidth != _image.PixelWidth || image.PixelHeight != _image.PixelHeight;
        _image = _preview = image; Region = new(0, 0, image.PixelWidth, image.PixelHeight);
        if (resized && _fit) FitImage(); else if (resized) { _scale = Math.Min(_scale, Math.Max(8, FitScale)); ClampPan(); InvalidateVisual(); ZoomChanged?.Invoke(_scale); } else InvalidateVisual();
    }
    private double FitScale => Math.Min(Math.Max(1, ActualWidth - 32) / _image.PixelWidth, Math.Max(1, ActualHeight - 32) / _image.PixelHeight);
    public void FitImage()
    {
        _fit = true; _scale = FitScale;
        _offset = new((ActualWidth - _image.PixelWidth * _scale) / 2, (ActualHeight - _image.PixelHeight * _scale) / 2);
        InvalidateVisual(); ZoomChanged?.Invoke(_scale);
    }
    public void ActualSize() => SetZoom(1, new Point(ActualWidth / 2, ActualHeight / 2));
    public void ZoomAt(double factor, Point anchor) => SetZoom((_scale > 0 ? _scale : FitScale) * factor, anchor);
    private void SetZoom(double scale, Point anchor)
    {
        if (_mode != -1 || !double.IsFinite(scale)) return;
        if (_scale <= 0) FitImage();
        var pixel = ToPixel(anchor);
        _scale = Math.Clamp(scale, Math.Min(FitScale, 1) * .25, Math.Max(8, FitScale)); _fit = false;
        _offset = new(anchor.X - pixel.X * _scale, anchor.Y - pixel.Y * _scale);
        ClampPan(); InvalidateVisual(); ZoomChanged?.Invoke(_scale);
    }
    private void ClampPan()
    {
        static double Axis(double offset, double size, double viewport) => size <= viewport - 32 ? (viewport - size) / 2 : Math.Clamp(offset, viewport - size - 16, 16);
        _offset = new(Axis(_offset.X, _image.PixelWidth * _scale, ActualWidth), Axis(_offset.Y, _image.PixelHeight * _scale, ActualHeight));
    }
    public Point ToPixel(Point point) => new((point.X - _offset.X) / Math.Max(.00001, _scale), (point.Y - _offset.Y) / Math.Max(.00001, _scale));
    public void SetRegion(CropRegion region)
    {
        var w = Math.Clamp(region.Width, 1, _image.PixelWidth); var h = Math.Clamp(region.Height, 1, _image.PixelHeight);
        Region = new(Math.Clamp(region.X, 0, _image.PixelWidth - w), Math.Clamp(region.Y, 0, _image.PixelHeight - h), w, h);
        InvalidateVisual(); RegionChanged?.Invoke(Region);
    }
    public void ResetSelection() { Aspect = null; SetRegion(new(0, 0, _image.PixelWidth, _image.PixelHeight)); }
    public void SetAspect(double? ratio)
    {
        if (ratio.HasValue && (!double.IsFinite(ratio.Value) || ratio <= 0)) throw new ArgumentOutOfRangeException(nameof(ratio));
        Aspect = ratio;
        if (!ratio.HasValue) return;
        double w = Region.Width, h = Region.Height;
        if (w / h > ratio.Value) w = h * ratio.Value; else h = w / ratio.Value;
        SetRegion(new(Region.X + (int)((Region.Width - w) / 2), Region.Y + (int)((Region.Height - h) / 2), Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h))));
    }
    private Point[] Handles()
    {
        var r = SelectionBounds;
        return [r.TopLeft, new(r.Left + r.Width / 2, r.Top), r.TopRight, new(r.Right, r.Top + r.Height / 2), r.BottomRight, new(r.Left + r.Width / 2, r.Bottom), r.BottomLeft, new(r.Left, r.Top + r.Height / 2)];
    }
    private int HitHandle(Point point)
    {
        var handles = Handles();
        foreach (var i in new[] { 0, 2, 4, 6, 1, 3, 5, 7 }) if ((handles[i] - point).Length <= 10) return i;
        return -1;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize), 12, 12));
        dc.DrawRectangle(Theme.Brush("InputFill"), null, new Rect(RenderSize));
        if (_scale <= 0) { dc.Pop(); return; }
        dc.DrawImage(_preview, ImageBounds);
        if (_editable)
        {
            var r = SelectionBounds;
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(140, 5, 20, 25)), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(ImageBounds), new RectangleGeometry(r)));
            dc.DrawRectangle(null, new Pen(Brushes.Black, 3), r); dc.DrawRectangle(null, new Pen(Brushes.White, 1), r);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1);
            for (int i = 1; i < 3; i++)
            {
                dc.DrawLine(pen, new(r.Left + r.Width * i / 3, r.Top), new(r.Left + r.Width * i / 3, r.Bottom));
                dc.DrawLine(pen, new(r.Left, r.Top + r.Height * i / 3), new(r.Right, r.Top + r.Height * i / 3));
            }
            foreach (var p in Handles()) dc.DrawRoundedRectangle(Brushes.White, new Pen(Theme.Brush("Accent"), 1.5), new Rect(p.X - 4, p.Y - 4, 8, 8), 2, 2);
        }
        dc.Pop();
    }
    // Shared by actual mouse input and interaction regression tests.
    public bool BeginDrag(Point point, bool pan = false, bool newSelection = false)
    {
        if (_scale <= 0) return false;
        _initial = Region; _initialOffset = _offset;
        if (pan || !_editable) { _mode = 10; _start = point; Cursor = Cursors.ScrollAll; return true; }
        if (newSelection && ImageBounds.Contains(point)) { _mode = 9; _start = ToPixel(point); return true; }
        _mode = HitHandle(point);
        if (_mode < 0)
        {
            if (!ImageBounds.Contains(point)) return false;
            _mode = SelectionBounds.Contains(point) ? 8 : 9;
        }
        _start = ToPixel(point); return true;
    }
    public void DragTo(Point point)
    {
        if (_mode < 0) return;
        if (_mode == 10) { _fit = false; _offset = _initialOffset + (point - _start); ClampPan(); InvalidateVisual(); return; }
        var raw = ToPixel(point);
        if (_mode == 8) { SetRegion(_initial with { X = _initial.X + (int)Math.Round(raw.X - _start.X), Y = _initial.Y + (int)Math.Round(raw.Y - _start.Y) }); return; }
        var p = new Point(Math.Clamp(raw.X, 0, _image.PixelWidth), Math.Clamp(raw.Y, 0, _image.PixelHeight));
        double l = _initial.X, t = _initial.Y, r = l + _initial.Width, b = t + _initial.Height;
        if (_mode == 9) { l = Math.Min(_start.X, p.X); r = Math.Max(_start.X, p.X); t = Math.Min(_start.Y, p.Y); b = Math.Max(_start.Y, p.Y); }
        else
        {
            if (_mode is 0 or 6 or 7) l = Math.Min(p.X, r - 1);
            if (_mode is 2 or 3 or 4) r = Math.Max(p.X, l + 1);
            if (_mode is 0 or 1 or 2) t = Math.Min(p.Y, b - 1);
            if (_mode is 4 or 5 or 6) b = Math.Max(p.Y, t + 1);
        }
        if (Aspect is double aspect)
        {
            // Keep the opposite corner or edge midpoint fixed, including at image bounds.
            double ax = _mode is 0 or 6 or 7 ? r : _mode is 1 or 5 ? (l + r) / 2 : l;
            double ay = _mode is 0 or 1 or 2 ? b : _mode is 3 or 7 ? (t + b) / 2 : t;
            double fx = _mode is 0 or 6 or 7 ? 1 : _mode is 1 or 5 ? .5 : 0;
            double fy = _mode is 0 or 1 or 2 ? 1 : _mode is 3 or 7 ? .5 : 0;
            if (_mode == 9) { ax = _start.X; ay = _start.Y; fx = p.X < ax ? 1 : 0; fy = p.Y < ay ? 1 : 0; }
            var w = _mode is 1 or 5 ? (b - t) * aspect : r - l;
            var maxW = fx == 0 ? _image.PixelWidth - ax : fx == 1 ? ax : 2 * Math.Min(ax, _image.PixelWidth - ax);
            var maxH = fy == 0 ? _image.PixelHeight - ay : fy == 1 ? ay : 2 * Math.Min(ay, _image.PixelHeight - ay);
            w = Math.Min(w, Math.Min(maxW, maxH * aspect)); var h = w / aspect;
            l = ax - fx * w; t = ay - fy * h; r = l + w; b = t + h;
        }
        SetRegion(new((int)Math.Round(l), (int)Math.Round(t), Math.Max(1, (int)Math.Round(r - l)), Math.Max(1, (int)Math.Round(b - t))));
    }
    public void EndDrag() { _mode = -1; Cursor = _editable ? Cursors.Cross : Cursors.Hand; }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;
        Focus();
        if (BeginDrag(e.GetPosition(this), e.ChangedButton == MouseButton.Middle || Keyboard.IsKeyDown(Key.Space), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) { CaptureMouse(); e.Handled = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var p = e.GetPosition(this);
        if (IsMouseCaptured) { DragTo(p); return; }
        if (!_editable || Keyboard.IsKeyDown(Key.Space)) { Cursor = Cursors.Hand; return; }
        Cursor = HitHandle(p) switch { 0 or 4 => Cursors.SizeNWSE, 2 or 6 => Cursors.SizeNESW, 1 or 5 => Cursors.SizeNS, 3 or 7 => Cursors.SizeWE, _ => SelectionBounds.Contains(p) ? Cursors.SizeAll : Cursors.Cross };
    }
    protected override void OnMouseUp(MouseButtonEventArgs e) { base.OnMouseUp(e); if (e.ChangedButton is MouseButton.Left or MouseButton.Middle) { EndDrag(); ReleaseMouseCapture(); e.Handled = true; } }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); EndDrag(); }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { base.OnMouseWheel(e); ZoomAt(Math.Pow(1.2, e.Delta / 120d), e.GetPosition(this)); e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space) { Cursor = Cursors.Hand; e.Handled = true; return; }
        if (e.Key == Key.D0) { FitImage(); e.Handled = true; return; }
        if (e.Key == Key.D1) { ActualSize(); e.Handled = true; return; }
        if (!_editable) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
        var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
        if (dx != 0 || dy != 0) { SetRegion(Region with { X = Region.X + dx, Y = Region.Y + dy }); e.Handled = true; }
    }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.Key == Key.Space) Cursor = _editable ? Cursors.Cross : Cursors.Hand; }
}
