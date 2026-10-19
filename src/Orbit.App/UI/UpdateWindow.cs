using Orbit.App.Services;

namespace Orbit.App.UI;

public sealed class UpdateWindow : GlassWindow
{
    public UpdateWindow(OrbitApplication app) : base(app, "Cập nhật Orbit", 620, 450)
    {
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 16) };
        stack.Children.Add(Ui.Text("Orbit " + UpdateService.CurrentVersion, 24));
        stack.Children.Add(Ui.Text("Tác giả: Nguyễn Mạnh Quyết", 13, true));
        var status = Ui.Text(app.Updates.Status, 15); status.Margin = new Thickness(0, 18, 0, 20); stack.Children.Add(status);
        stack.Children.Add(Ui.Text("Tải ZIP portable, kiểm tra chữ ký và SHA-256. Giữ cài đặt và bản cũ để phục hồi khi lỗi khởi động.\n\nTự áp dụng khi Orbit ở khay, không còn tác vụ hoặc cửa sổ chỉnh sửa. Có thể tắt tự cập nhật trong Cài đặt.", 13, true));
        SetBody(stack);
        var check = Ui.Button("Kiểm tra ngay", async () => await app.Updates.CheckAsync());
        var apply = Ui.Button("Cập nhật ngay", async () => await app.Updates.ApplyAsync(true), true);
        var defer = Ui.Button("Để sau", () => { app.Updates.Defer(); Close(); });
        var buttons = Ui.Row(check, apply, defer); buttons.Margin = new Thickness(20); SetFooter(buttons);
        void Refresh() { status.Text = app.Updates.Status; check.IsEnabled = !app.Updates.IsBusy; apply.IsEnabled = app.Updates.CanApply; defer.IsEnabled = !app.UpdateInProgress; }
        app.Updates.Changed += Refresh; Closed += (_, _) => app.Updates.Changed -= Refresh; Refresh();
    }
}
