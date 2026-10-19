using System;
using System.Globalization;

namespace Orbit.Core;

public enum FrameBackground { Solid, Gradient, Texture, Picture, SourceBlur }
public enum FrameTexture { Dots, Stripes, Grid, Paper }

// Proportions, not pixels: one setting gives the same look across a batch of differently sized images.
public sealed record FrameOptions(
    double Padding = .08, double? Aspect = null, FrameBackground Background = FrameBackground.Solid,
    string Color1 = "#F1F4F3", string Color2 = "#167D72", double Angle = 135, FrameTexture Texture = FrameTexture.Dots,
    string? Picture = null, bool BlurPicture = true, double Corner = .03, bool Shadow = true, bool Jpeg = false)
{
    public const double MaxPadding = .5, MaxCorner = .25;
    public void Validate()
    {
        if (!double.IsFinite(Padding) || Padding is < 0 or > MaxPadding) throw new ArgumentException("Khoảng viền phải từ 0 đến 50% cạnh dài.");
        if (Aspect is double a && (!double.IsFinite(a) || a < .05 || a > 20)) throw new ArgumentException("Tỷ lệ khung không hợp lệ.");
        if (!double.IsFinite(Corner) || Corner is < 0 or > MaxCorner) throw new ArgumentException("Bo góc phải từ 0 đến 25% cạnh ngắn.");
        if (!double.IsFinite(Angle)) throw new ArgumentException("Góc chuyển màu không hợp lệ.");
        if (!Enum.IsDefined(Background) || !Enum.IsDefined(Texture)) throw new ArgumentException("Kiểu nền không hợp lệ.");
        _ = FrameLayout.ParseColor(Color1); _ = FrameLayout.ParseColor(Color2);
        if (Background == FrameBackground.Picture && string.IsNullOrWhiteSpace(Picture)) throw new ArgumentException("Hãy chọn ảnh nền.");
    }
}

public readonly record struct FrameGeometry(int CanvasWidth, int CanvasHeight, int X, int Y, int Width, int Height, int CornerRadius);

public static class FrameLayout
{
    // The canvas only grows: padding first, then the shorter dimension extends to the target aspect.
    public static FrameGeometry Compute(int width, int height, FrameOptions options)
    {
        if (width < 1 || height < 1) throw new ArgumentException("Kích thước ảnh không hợp lệ.");
        options.Validate();
        var pad = (long)Math.Round(options.Padding * Math.Max(width, height));
        long canvasWidth = width + 2 * pad, canvasHeight = height + 2 * pad;
        if (options.Aspect is double aspect)
        {
            if ((double)canvasWidth / canvasHeight < aspect) canvasWidth = (long)Math.Ceiling(canvasHeight * aspect - 1e-9);
            else canvasHeight = (long)Math.Ceiling(canvasWidth / aspect - 1e-9);
        }
        if (canvasWidth > 32768 || canvasHeight > 32768 || canvasWidth * canvasHeight > 60_000_000)
            throw new ArgumentException($"Khung {canvasWidth} × {canvasHeight} px vượt giới hạn 60 megapixel hoặc 32768 px/cạnh. Hãy giảm khoảng viền hoặc resize ảnh trước.");
        var radius = (int)Math.Round(options.Corner * Math.Min(width, height));
        return new((int)canvasWidth, (int)canvasHeight, (int)((canvasWidth - width) / 2), (int)((canvasHeight - height) / 2), width, height, radius);
    }

    public static (byte R, byte G, byte B) ParseColor(string text)
    {
        var hex = (text ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Màu phải có dạng #RRGGBB.");
        return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
