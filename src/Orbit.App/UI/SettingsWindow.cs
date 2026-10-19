using Orbit.App.Services;

namespace Orbit.App.UI;

public sealed class SettingsWindow : GlassWindow
{
    public SettingsWindow(OrbitApplication app) : base(app, "Cài đặt Orbit", 600, 640)
    {
        ResizeMode = ResizeMode.NoResize;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 20) };
        var choices = Enum.GetValues<MenuHotkey>();
        var hotkey = new ComboBox { ItemsSource = choices.Select(MenuHotkeys.Label).ToArray(), SelectedIndex = Array.IndexOf(choices, app.Settings.EffectiveHotkey) };
        System.Windows.Automation.AutomationProperties.SetName(hotkey, "Phím gọi menu khi đang kéo");
        stack.Children.Add(Ui.Text("Phím gọi menu khi đang kéo", 14)); hotkey.Margin = new Thickness(0, 7, 0, 18); stack.Children.Add(hotkey);
        var theme = new ComboBox { ItemsSource = new[] { "Theo hệ thống", "Sáng", "Tối" }, SelectedIndex = app.Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 } };
        stack.Children.Add(Ui.Text("Giao diện", 14)); theme.Margin = new Thickness(0, 7, 0, 14); stack.Children.Add(theme);
        var glass = new CheckBox { Content = "Hiệu ứng kính mờ", IsChecked = app.Settings.Glass }; stack.Children.Add(glass);
        var startup = new CheckBox { Content = "Chạy ở khay hệ thống khi đăng nhập Windows", IsChecked = AppSettings.StartupEnabled }; stack.Children.Add(startup);
        var updates = new CheckBox { Content = "Tự tải và cập nhật khi Orbit ở khay, không có tác vụ", IsChecked = app.Settings.AutoUpdate }; stack.Children.Add(updates);
        stack.Children.Add(Ui.Button("Cập nhật Orbit…", () => new UpdateWindow(app).Show()));
        var note = Ui.Text($"Orbit {UpdateService.CurrentVersion} · Ảnh / WebP / PDF / Office\nTác giả: Nguyễn Mạnh Quyết\nĐầu ra lưu cạnh nguồn; giữ bản gốc. Office → PDF cần Microsoft Office.\nKiểm tra cập nhật mỗi 12 giờ. Kính menu lấy nền tại lúc mở.", 12, true); note.Margin = new Thickness(0, 16, 0, 0); stack.Children.Add(note);
        var error = Ui.Text("", 12); error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); error.Margin = new Thickness(0, 10, 0, 0); stack.Children.Add(error); SetBody(stack);
        var buttons = Ui.Row(Ui.Button("Hủy", Close), Ui.Button("Lưu cài đặt", () =>
        {
            try
            {
                app.Settings.SetHotkey(choices[hotkey.SelectedIndex]);
                app.Settings.Theme = theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
                app.Settings.Glass = glass.IsChecked == true;
                app.Settings.AutoUpdate = updates.IsChecked == true;
                if (AppSettings.StartupEnabled != (startup.IsChecked == true)) AppSettings.StartupEnabled = startup.IsChecked == true;
                app.Settings.CompleteStartupPrompt(); app.RefreshTheme(); Close();
            }
            catch (Exception ex) { error.Text = "Chưa lưu được cài đặt: " + ex.Message; }
        }, true)); buttons.HorizontalAlignment = HorizontalAlignment.Right; buttons.Margin = new Thickness(20); SetFooter(buttons);
    }
}
