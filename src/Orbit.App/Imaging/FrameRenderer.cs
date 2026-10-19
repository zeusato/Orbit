using System.Windows.Media.Effects;

namespace Orbit.App.Imaging;

// Expands the canvas around an image and paints a background. The source pixels are
// drawn 1:1 and never altered; only the area outside the image is generated.
public static class FrameRenderer
{
    public static BitmapSource Render(BitmapSource image, FrameOptions options, BitmapSource? picture = null)
    {
        var g = FrameLayout.Compute(image.PixelWidth, image.PixelHeight, options);
        var canvas = new Rect(0, 0, g.CanvasWidth, g.CanvasHeight);
        var target = new Rect(g.X, g.Y, g.Width, g.Height);
        var root = new ContainerVisual();
        root.Children.Add(Background(canvas, options, options.Background == FrameBackground.SourceBlur ? image : picture));
        if (options.Shadow && (g.X > 0 || g.Y > 0))
        {
            // Soft shadow rendered at reduced size: a blur hides the upscale and keeps large canvases fast.
            var radius = Math.Clamp(Math.Max(g.Width, g.Height) * .03, 3, 400);
            var shape = new RectangleGeometry(new Rect(target.X, target.Y + radius * .4, target.Width, target.Height), g.CornerRadius, g.CornerRadius);
            var brush = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
            root.Children.Add(Blurred(canvas, radius, dc => dc.DrawGeometry(brush, null, shape)));
        }
        var photo = new DrawingVisual();
        using (var dc = photo.RenderOpen())
        {
            if (g.CornerRadius > 0) dc.PushClip(new RectangleGeometry(target, g.CornerRadius, g.CornerRadius));
            dc.DrawImage(image, target);
            if (g.CornerRadius > 0) dc.Pop();
        }
        root.Children.Add(photo);
        return Rasterize(root, g.CanvasWidth, g.CanvasHeight);
    }

    private static Visual Background(Rect canvas, FrameOptions options, BitmapSource? picture)
    {
        var c1 = ToColor(options.Color1); var c2 = ToColor(options.Color2);
        if (options.Background is FrameBackground.Picture or FrameBackground.SourceBlur)
        {
            if (picture == null) throw new ArgumentException("Hãy chọn ảnh nền.");
            if (!options.BlurPicture && options.Background == FrameBackground.Picture)
            {
                var sharp = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(sharp, BitmapScalingMode.HighQuality);
                using (var dc = sharp.RenderOpen()) { dc.PushClip(new RectangleGeometry(canvas)); dc.DrawImage(picture, Cover(picture, canvas)); dc.Pop(); }
                return sharp;
            }
            // Overscan by the blur radius so edges do not fade to transparent.
            var radius = Math.Max(canvas.Width, canvas.Height) * .035;
            var area = canvas; area.Inflate(radius * 2, radius * 2);
            return Blurred(canvas, radius, dc => dc.DrawImage(picture, Cover(picture, area)));
        }
        var visual = new DrawingVisual();
        using var draw = visual.RenderOpen();
        switch (options.Background)
        {
            case FrameBackground.Gradient:
                // CSS convention: 90° runs left → right, 180° top → bottom.
                var radians = options.Angle * Math.PI / 180; var direction = new Vector(Math.Sin(radians), -Math.Cos(radians));
                var half = (Math.Abs(canvas.Width * direction.X) + Math.Abs(canvas.Height * direction.Y)) / 2;
                var center = new Point(canvas.Width / 2, canvas.Height / 2);
                draw.DrawRectangle(new LinearGradientBrush(c1, c2, center - direction * half, center + direction * half) { MappingMode = BrushMappingMode.Absolute }, null, canvas);
                break;
            case FrameBackground.Texture:
                draw.DrawRectangle(new SolidColorBrush(c1), null, canvas);
                draw.DrawRectangle(Texture(options.Texture, c2, Math.Max(4, Math.Max(canvas.Width, canvas.Height) / 48)), null, canvas);
                break;
            default:
                draw.DrawRectangle(new SolidColorBrush(c1), null, canvas); break;
        }
        return visual;
    }

    private static Brush Texture(FrameTexture texture, Color ink, double unit)
    {
        var tile = new Rect(0, 0, unit * 2, unit * 2);
        Brush Tiled(Drawing drawing) => new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = tile, ViewportUnits = BrushMappingMode.Absolute, Viewbox = tile, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        var brush = new SolidColorBrush(ink);
        switch (texture)
        {
            case FrameTexture.Dots:
                return Tiled(new DrawingGroup { Children = { new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(tile)), new GeometryDrawing(brush, null, new EllipseGeometry(new Point(unit, unit), unit * .3, unit * .3)) } });
            case FrameTexture.Grid:
                var line = Math.Max(1, unit * .08);
                return Tiled(new DrawingGroup { Children = { new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(tile)), new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, tile.Width, line))), new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, line, tile.Height))) } });
            case FrameTexture.Stripes:
                return new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat, StartPoint = new Point(0, 0), EndPoint = new Point(unit * .8, unit * .8),
                    GradientStops = { new(Colors.Transparent, 0), new(Colors.Transparent, .5), new(ink, .5), new(ink, 1) }
                };
            default:
                // Fixed seed: the same settings always produce the same paper grain.
                const int size = 192; var random = new Random(20261019); var pixels = new byte[size * size * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    var alpha = random.Next(0, 70);
                    pixels[i] = (byte)(ink.B * alpha / 255); pixels[i + 1] = (byte)(ink.G * alpha / 255); pixels[i + 2] = (byte)(ink.R * alpha / 255); pixels[i + 3] = (byte)alpha;
                }
                var noise = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4); noise.Freeze();
                var grain = Math.Max(1, unit / 12);
                return new ImageBrush(noise) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, size * grain, size * grain), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        }
    }

    private static Rect Cover(BitmapSource picture, Rect area)
    {
        var scale = Math.Max(area.Width / picture.PixelWidth, area.Height / picture.PixelHeight);
        var width = picture.PixelWidth * scale; var height = picture.PixelHeight * scale;
        return new Rect(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    // Draws content blurred by `radius` canvas pixels, computed on a canvas at most 640 px long.
    private static Visual Blurred(Rect canvas, double radius, Action<DrawingContext> draw)
    {
        var scale = Math.Min(1, 640 / Math.Max(canvas.Width, canvas.Height));
        int width = Math.Max(1, (int)Math.Ceiling(canvas.Width * scale)), height = Math.Max(1, (int)Math.Ceiling(canvas.Height * scale));
        var content = new DrawingVisual { Effect = new BlurEffect { Radius = Math.Max(.5, radius * scale), KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality } };
        RenderOptions.SetBitmapScalingMode(content, BitmapScalingMode.HighQuality);
        using (var dc = content.RenderOpen()) { dc.PushTransform(new ScaleTransform(scale, scale)); draw(dc); dc.Pop(); }
        var small = Rasterize(content, width, height);
        var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(small, new Rect(0, 0, width / scale, height / scale));
        return visual;
    }

    private static BitmapSource Rasterize(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    public static Color ToColor(string text) { var (r, g, b) = FrameLayout.ParseColor(text); return Color.FromRgb(r, g, b); }
}
