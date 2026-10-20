using Orbit.App.Services;

namespace Orbit.App.UI;

public sealed class PdfCompressionWindow : GlassWindow
{
    public PdfCompressionWindow(OrbitApplication app, string source) : base(app, "Nén PDF", 580, 470)
    {
        MinWidth = 500; MinHeight = 440;
        var body = new StackPanel { Margin = new Thickness(24, 0, 24, 16) };
        body.Children.Add(Ui.Text(Path.GetFileName(source), 16));
        body.Children.Add(Ui.Text("Nén ảnh trong PDF, giữ lớp chữ. Dung lượng trước/sau sẽ hiện trong tác vụ.", 13, true));
        var qualityText = Ui.Text("Chất lượng ảnh: 75", 13);
        qualityText.Margin = new Thickness(0, 16, 0, 6); body.Children.Add(qualityText);
        var quality = new Slider { Minimum = 30, Maximum = 95, Value = 75, TickFrequency = 5, IsSnapToTickEnabled = true };
        quality.ValueChanged += (_, _) => qualityText.Text = $"Chất lượng ảnh: {(int)quality.Value}";
        body.Children.Add(quality);
        body.Children.Add(Ui.Text("Thấp hơn → file nhỏ hơn, ảnh có thể kém nét hơn.", 12, true));
        var output = new PdfOutputControls(app, ToolKind.PdfCompress, source) { Margin = new Thickness(0, 16, 0, 0) };
        body.Children.Add(output); SetBody(body);
        var error = Ui.Text("", 12); error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        var footer = new DockPanel { Margin = new Thickness(24, 0, 24, 20) };
        var buttons = Ui.Row(Ui.Button("Hủy", Close), Ui.Button("Nén PDF", () =>
        {
            try
            {
                var target = output.Read();
                app.Queue.Add([new(source, ToolKind.PdfCompress, Quality: (int)quality.Value, OutputDirectory: target.Directory, OutputStem: target.Stem)]);
                Close(); app.ShowHome();
            }
            catch (Exception failure) { error.Text = failure.Message; }
        }, true));
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); footer.Children.Add(error); SetFooter(footer);
    }
}
