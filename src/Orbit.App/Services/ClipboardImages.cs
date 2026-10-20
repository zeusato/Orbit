using Orbit.App.Imaging;

namespace Orbit.App.Services;

public static class ClipboardImages
{
    public static string DirectoryPath => Path.Combine(AppSettings.DataDirectory, "clipboard");

    public static BitmapSource Read()
    {
        // Clipboard access stays on the UI's STA. Never clear or change the user's clipboard.
        var bitmap = Clipboard.GetImage() ?? throw new InvalidOperationException("Clipboard chưa có ảnh. Hãy chụp hoặc sao chép ảnh trước.");
        ImageEngine.ValidatePixels(bitmap.PixelWidth, bitmap.PixelHeight);
        var snapshot = bitmap.Clone(); snapshot.Freeze(); return snapshot;
    }

    public static string Save(BitmapSource snapshot)
    {
        ImageEngine.ValidatePixels(snapshot.PixelWidth, snapshot.PixelHeight);
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, $"Clipboard-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(snapshot)); encoder.Save(file);
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    public static bool IsStoredImage(string source) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(source)), Path.GetFullPath(DirectoryPath), StringComparison.OrdinalIgnoreCase);

    public static string OutputDirectory(string source, AppSettings settings)
    {
        if (IsStoredImage(source) && settings.OutputMode != OutputLocation.Folder)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Orbit");
        return OutputLocation.For(source, settings.OutputMode, settings.OutputFolder);
    }

    public static bool IsPaste(KeyEventArgs e) => e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control &&
        Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase && Keyboard.FocusedElement is not PasswordBox;
}
