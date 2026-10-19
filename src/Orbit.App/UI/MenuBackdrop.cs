using System.Drawing;
using System.Drawing.Imaging;
using Orbit.App.Interop;

namespace Orbit.App.UI;

public static class MenuBackdrop
{
    // One transient in-memory snapshot before the menu opens. WPF blurs and clips
    // these pixels inside the circle, so no native rectangular blur can escape.
    // Never persisted or sent anywhere; the menu releases it on Hide.
    public static BitmapSource? Capture(Native.Point cursor)
    {
        try
        {
            Native.DwmFlush();
            var b = Native.MenuBounds(cursor);
            using var bitmap = new Bitmap(b.Right - b.Left, b.Bottom - b.Top, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(b.Left, b.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); stream.Position = 0;
            var source = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); source.Freeze(); return source;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or ArgumentException or System.Runtime.InteropServices.ExternalException)
        { return null; }
    }
}
