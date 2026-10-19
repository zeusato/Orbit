namespace Orbit.App.UI;

// The Orbit mark, drawn as vectors so every size is rendered natively: a teal planet, a tilted
// orbit ring passing behind and in front of it, and an amber satellite. Small sizes drop the
// fine detail and thicken the ring so the shape still reads at 16 px.
public static class OrbitIcon
{
    public static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    public static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 256d, size / 256d));
            Draw(dc, size);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static void Draw(DrawingContext dc, int size)
    {
        var small = size <= 24; var tiny = size <= 16;
        var center = new Point(128, 128);
        // Ring: an ellipse tilted -28°, split into the half behind the planet and the half in front.
        var tilt = new RotateTransform(-24, 128, 128);
        var ringWidth = tiny ? 20 : small ? 17 : size <= 48 ? 12 : 10;
        var ringRadius = new Size(small ? 116 : 114, small ? 44 : 40);
        var ring = new LinearGradientBrush(Color.FromRgb(0xF4, 0xFF, 0xFC), Color.FromRgb(0x9F, 0xF2, 0xE3), 0) { MappingMode = BrushMappingMode.RelativeToBoundingBox };
        var ringPen = new Pen(ring, ringWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        // A navy outline keeps the pale ring visible on light and dark backgrounds alike.
        var ringEdge = new Pen(new SolidColorBrush(Color.FromRgb(0x12, 0x30, 0x4F)), ringWidth + (small ? 9 : 7)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        Geometry Arc(bool front)
        {
            var figure = new PathFigure { StartPoint = new Point(128 - ringRadius.Width, 128) };
            figure.Segments.Add(new ArcSegment(new Point(128 + ringRadius.Width, 128), ringRadius, 0, false, front ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
            var geometry = new PathGeometry([figure]) { Transform = tilt }; return geometry;
        }

        // Soft drop shadow under the planet (skipped when it would only blur a few pixels).
        if (!small)
        {
            var shadow = new RadialGradientBrush(Color.FromArgb(0x55, 0x05, 0x20, 0x2A), Color.FromArgb(0, 0x05, 0x20, 0x2A));
            dc.DrawEllipse(shadow, null, new Point(128, 214), 78, 18);
        }
        // Whole ring behind the planet; only the stretch that crosses the planet is drawn again on top.
        var wholeRing = new EllipseGeometry(new Point(128, 128), ringRadius.Width, ringRadius.Height) { Transform = tilt };
        dc.DrawGeometry(null, ringEdge, wholeRing); dc.DrawGeometry(null, ringPen, wholeRing);

        var planetRadius = small ? 88 : 80;
        var planet = new LinearGradientBrush
        {
            StartPoint = new Point(.2, .1), EndPoint = new Point(.85, .95),
            GradientStops = { new(Color.FromRgb(0x5F, 0xF2, 0xD6), 0), new(Color.FromRgb(0x16, 0xA5, 0x93), .42), new(Color.FromRgb(0x10, 0x5F, 0x86), .78), new(Color.FromRgb(0x1C, 0x2E, 0x63), 1) }
        };
        dc.DrawEllipse(planet, tiny ? null : new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x0A, 0x24, 0x36)), 3), center, planetRadius, planetRadius);
        if (!small)
        {
            // Terminator shading and a glass highlight give the sphere depth.
            var shade = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x48, 0x06, 0x12, 0x2C)) { Center = new Point(.35, .3), GradientOrigin = new Point(.35, .3), RadiusX = .8, RadiusY = .8 };
            dc.DrawEllipse(shade, null, center, planetRadius, planetRadius);
            var highlight = new RadialGradientBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF));
            dc.DrawEllipse(highlight, null, new Point(98, 92), 34, 24);
        }

        dc.PushClip(new EllipseGeometry(center, planetRadius + 1, planetRadius + 1));
        dc.DrawGeometry(null, ringEdge, Arc(front: true)); dc.DrawGeometry(null, ringPen, Arc(front: true));
        dc.Pop();
        // Satellite on the front of the ring, lower right.
        var satellite = tilt.Transform(new Point(128 + ringRadius.Width * Math.Cos(.55), 128 + ringRadius.Height * Math.Sin(.55)));
        var satelliteRadius = tiny ? 22 : small ? 20 : 17;
        var amber = new RadialGradientBrush(Color.FromRgb(0xFF, 0xE3, 0xA1), Color.FromRgb(0xF2, 0x9E, 0x2E)) { GradientOrigin = new Point(.35, .3) };
        dc.DrawEllipse(amber, tiny ? null : new Pen(new SolidColorBrush(Color.FromRgb(0x1C, 0x2E, 0x63)), small ? 6 : 4), satellite, satelliteRadius, satelliteRadius);
    }

    // PNG-compressed ICO (Windows Vista and later), one image per size.
    public static void WriteIco(string path)
    {
        var images = Sizes.Select(size => { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(size))); using var stream = new MemoryStream(); encoder.Save(stream); return (size, data: stream.ToArray()); }).ToArray();
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)images.Length);
        var offset = 6 + 16 * images.Length;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); writer.Write((byte)(size >= 256 ? 0 : size)); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(data.Length); writer.Write(offset); offset += data.Length;
        }
        foreach (var (_, data) in images) writer.Write(data);
    }

    // Tray icon at the exact system size for the current DPI.
    public static System.Drawing.Icon TrayIcon(int size)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(size)));
        using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(stream);
        var handle = bitmap.GetHicon();
        try { using var icon = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
