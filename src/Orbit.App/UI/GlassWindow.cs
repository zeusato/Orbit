using System.Windows.Shell;
using Orbit.App.Interop;

namespace Orbit.App.UI;

public class GlassWindow : Window
{
    protected Grid Layout { get; } = new();
    protected OrbitApplication Orbit { get; }
    public bool BackdropAvailable { get; private set; }
    public GlassWindow(OrbitApplication app, string title, double width = 840, double height = 640)
    {
        Orbit = app; Title = "Orbit · " + title; Width = width; Height = height;
        MinWidth = Math.Min(width, 580); MinHeight = 430; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        Background = Brushes.Transparent; SetResourceReference(ForegroundProperty, "Ink");
        FontFamily = new FontFamily("Segoe UI Variable Text"); FontSize = 13;
        // DWM owns the outer silhouette, border and shadow. A second WPF rounded
        // border leaves a visible rectangular backdrop between the two radii.
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false });
        var layers = new Grid();
        var sheen = new Border { IsHitTestVisible = false }; sheen.SetResourceReference(Border.BackgroundProperty, "GlassSheen");
        layers.Children.Add(sheen); layers.Children.Add(Layout);
        var shell = new Border { Child = layers };
        shell.SetResourceReference(Border.BackgroundProperty, "WindowFill");
        Content = shell;
        Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(22, 14, 16, 14), LastChildFill = true };
        header.MouseLeftButtonDown += (_, e) =>
        {
            var target = e.OriginalSource as DependencyObject;
            while (target != null && target != header) { if (target is Button) return; target = VisualTreeHelper.GetParent(target); }
            if (e.ClickCount == 2 && ResizeMode == ResizeMode.CanResize) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.ChangedButton == MouseButton.Left) { try { DragMove(); } catch (InvalidOperationException) { } }
        };
        var close = Ui.Button("✕", Close); close.Padding = new Thickness(11, 4, 11, 4); close.ToolTip = "Đóng cửa sổ"; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var label = Ui.Text(title, 19); label.VerticalAlignment = VerticalAlignment.Center; label.FontWeight = FontWeights.SemiBold; header.Children.Add(label); Layout.Children.Add(header);
        SourceInitialized += (_, _) => RefreshBackdrop();
        Loaded += (_, _) => RefreshBackdrop();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    public void RefreshBackdrop()
    {
        BackdropAvailable = Native.ApplyBackdrop(this, Orbit.Settings.IsDark, Orbit.Settings.Glass);
        if (Content is Border shell)
        {
            if (BackdropAvailable) shell.SetResourceReference(Border.BackgroundProperty, "WindowFill");
            else { var color = ((SolidColorBrush)Theme.Brush("WindowFill")).Color; color.A = 255; shell.Background = new SolidColorBrush(color); }
        }
    }
    protected void SetBody(UIElement body) { Grid.SetRow(body, 1); Layout.Children.Add(body); }
    protected void SetFooter(UIElement footer) { Grid.SetRow(footer, 2); Layout.Children.Add(footer); }
}
