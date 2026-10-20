using Orbit.App.Services;

namespace Orbit.App.UI;

public sealed class SettingsWindow : GlassWindow
{
    public SettingsWindow(OrbitApplication app) : base(app, "Cài đặt Orbit", 620, 720)
    {
        ResizeMode = ResizeMode.NoResize;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 20) };
        var choices = Enum.GetValues<MenuHotkey>();
        var hotkey = new ComboBox { ItemsSource = choices.Select(MenuHotkeys.Label).ToArray(), SelectedIndex = Array.IndexOf(choices, app.Settings.EffectiveHotkey) };
        System.Windows.Automation.AutomationProperties.SetName(hotkey, "Phím gọi menu khi đang kéo");
        stack.Children.Add(Ui.Text("Phím gọi menu khi đang kéo", 14)); hotkey.Margin = new Thickness(0, 7, 0, 18); stack.Children.Add(hotkey);
        var quickHint = Ui.Text("Không kéo file: Ctrl + Space mở vòng tiện ích.", 12, true);
        quickHint.Margin = new Thickness(0, 0, 0, 12); stack.Children.Add(quickHint);
        var theme = new ComboBox { ItemsSource = new[] { "Theo hệ thống", "Sáng", "Tối" }, SelectedIndex = app.Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 } };
        stack.Children.Add(Ui.Text("Giao diện", 14)); theme.Margin = new Thickness(0, 7, 0, 14); stack.Children.Add(theme);

        // Output location: beside the source (default), an "Orbit" folder next to it, or one fixed folder.
        var modes = new[] { OutputLocation.Beside, OutputLocation.Subfolder, OutputLocation.Folder };
        var output = new ComboBox { ItemsSource = new[] { "Cạnh file nguồn", "Thư mục con \"Orbit\" cạnh file nguồn", "Một thư mục cố định" }, SelectedIndex = Math.Max(0, Array.IndexOf(modes, app.Settings.OutputMode)) };
        System.Windows.Automation.AutomationProperties.SetName(output, "Nơi lưu kết quả");
        var folder = new TextBox { Text = app.Settings.OutputFolder ?? "", IsReadOnly = true }; System.Windows.Automation.AutomationProperties.SetName(folder, "Thư mục lưu cố định");
        var browse = Ui.Button("Chọn…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn thư mục lưu kết quả của Orbit", InitialDirectory = Directory.Exists(folder.Text) ? folder.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
            if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName;
        });
        browse.Margin = new Thickness(8, 0, 0, 0); browse.Padding = new Thickness(12, 5, 12, 5);
        var folderRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse); folderRow.Children.Add(folder);
        void SyncFolder() => folderRow.Visibility = output.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        output.SelectionChanged += (_, _) => SyncFolder(); SyncFolder();
        stack.Children.Add(Ui.Text("Nơi lưu kết quả", 14)); output.Margin = new Thickness(0, 7, 0, 0); stack.Children.Add(output); stack.Children.Add(folderRow);
        var outputNote = Ui.Text("Không bao giờ ghi đè: trùng tên thì thêm (1), (2)…", 12, true); outputNote.Margin = new Thickness(0, 6, 0, 14); stack.Children.Add(outputNote);

        var glass = new CheckBox { Content = "Hiệu ứng kính mờ", IsChecked = app.Settings.Glass }; stack.Children.Add(glass);
        var startup = new CheckBox { Content = "Chạy ở khay hệ thống khi đăng nhập Windows", IsChecked = AppSettings.StartupEnabled }; stack.Children.Add(startup);
        var shell = new CheckBox { Content = "Thêm Orbit vào menu chuột phải của file", IsChecked = app.Settings.ShellMenu, ToolTip = "Windows 11: nằm trong \"Hiển thị thêm tùy chọn\" (Shift + chuột phải)." }; stack.Children.Add(shell);
        var updates = new CheckBox { Content = "Tự tải và cập nhật khi Orbit ở khay, không có tác vụ", IsChecked = app.Settings.AutoUpdate }; stack.Children.Add(updates);
        var actions = Ui.Row(Ui.Button("Cập nhật Orbit…", () => new UpdateWindow(app).Show()));
        var reset = Ui.Button("Đặt lại thiết lập công cụ", () => { }); actions.Children.Add(reset);
        reset.ToolTip = "Khung nền, mức nén, kích thước, tỷ lệ crop, GIF… trở về mặc định ở lần mở sau.";
        actions.Margin = new Thickness(0, 8, 0, 0); stack.Children.Add(actions);
        var resetPending = false; reset.Click += (_, _) => { resetPending = true; reset.Content = "Sẽ đặt lại khi lưu"; reset.IsEnabled = false; };
        var note = Ui.Text($"Orbit {UpdateService.CurrentVersion} · Ảnh / PDF / Office / Âm thanh / Video\nTác giả: Nguyễn Mạnh Quyết\nGiữ bản gốc. Office → PDF cần Microsoft Office; audio/video dùng codec của Windows.\nKiểm tra cập nhật mỗi 12 giờ. Kính menu lấy nền tại lúc mở.", 12, true); note.Margin = new Thickness(0, 16, 0, 0); stack.Children.Add(note);
        var error = Ui.Text("", 12); error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); error.Margin = new Thickness(0, 10, 0, 0); stack.Children.Add(error);
        SetBody(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var buttons = Ui.Row(Ui.Button("Hủy", Close), Ui.Button("Lưu cài đặt", () =>
        {
            try
            {
                var mode = modes[Math.Max(0, output.SelectedIndex)];
                if (mode == OutputLocation.Folder && (string.IsNullOrWhiteSpace(folder.Text) || !Path.IsPathFullyQualified(folder.Text)))
                { error.Text = "Chọn thư mục lưu cố định trước khi lưu."; return; }
                app.Settings.SetHotkey(choices[hotkey.SelectedIndex]);
                app.Settings.Theme = theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
                app.Settings.Glass = glass.IsChecked == true;
                app.Settings.AutoUpdate = updates.IsChecked == true;
                app.Settings.OutputMode = mode; app.Settings.OutputFolder = folder.Text.Length > 0 ? folder.Text : null;
                var shellChanged = app.Settings.ShellMenu != (shell.IsChecked == true); app.Settings.ShellMenu = shell.IsChecked == true;
                if (resetPending) app.Settings.Defaults = new();
                if (AppSettings.StartupEnabled != (startup.IsChecked == true)) AppSettings.StartupEnabled = startup.IsChecked == true;
                app.Settings.CompleteStartupPrompt(); app.RefreshTheme();
                if (shellChanged) app.SyncShellMenu();
                Close();
            }
            catch (Exception ex) { error.Text = "Chưa lưu được cài đặt: " + ex.Message; }
        }, true)); buttons.HorizontalAlignment = HorizontalAlignment.Right; buttons.Margin = new Thickness(20); SetFooter(buttons);
    }
}
