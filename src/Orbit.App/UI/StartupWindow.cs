using Orbit.App.Services;

namespace Orbit.App.UI;

public sealed class StartupWindow : GlassWindow
{
    public StartupWindow(OrbitApplication app, Action? enableStartup = null) : base(app, "Sẵn sàng khi cần", 560, 430)
    {
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(24, 12, 24, 0) };
        stack.Children.Add(Ui.Text("Chạy Orbit cùng Windows?", 23));
        var description = Ui.Text("Orbit tự chạy ở khay hệ thống sau khi đăng nhập, để công cụ luôn sẵn sàng khi kéo file. Cửa sổ chính sẽ không tự bật lên.", 14, true);
        description.Margin = new Thickness(0, 16, 0, 16); stack.Children.Add(description);
        stack.Children.Add(Ui.Text("Có thể bật hoặc tắt lại trong Cài đặt. Không cần quyền administrator. Giữ thư mục Orbit tại vị trí đang dùng.", 12, true));
        var error = Ui.Text("", 12); error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); error.Margin = new Thickness(0, 12, 0, 0); stack.Children.Add(error);
        SetBody(stack);
        var completed = false;
        void Choose(bool enable)
        {
            try
            {
                app.Settings.CompleteStartupPrompt(enable ? enableStartup ?? (() => AppSettings.StartupEnabled = true) : null);
                completed = true; Close();
            }
            catch (Exception ex) { error.Text = "Chưa lưu được lựa chọn: " + ex.Message; }
        }
        var buttons = Ui.Row(Ui.Button("Không bật", () => Choose(false)), Ui.Button("Bật cùng Windows", () => Choose(true), true));
        buttons.HorizontalAlignment = HorizontalAlignment.Right; buttons.Margin = new Thickness(20); SetFooter(buttons);
        // Dismissing with X/Esc is a decline, never an implicit opt-in.
        Closing += (_, _) =>
        {
            if (completed) return;
            try { app.Settings.CompleteStartupPrompt(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Không lưu được lựa chọn startup: " + ex.Message); }
        };
    }
}
