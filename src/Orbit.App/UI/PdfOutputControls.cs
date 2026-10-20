using Microsoft.Win32;
using Orbit.App.Services;

namespace Orbit.App.UI;

public sealed class PdfOutputControls : StackPanel
{
    private readonly TextBox _name = new() { Name = "OutputName", ToolTip = "Tên kết quả, không cần đuôi .pdf" };
    private readonly TextBox _folder = new() { Name = "OutputFolder" };
    public PdfOutputControls(OrbitApplication app, ToolKind tool, string source)
    {
        Children.Add(Ui.Text(tool == ToolKind.PdfSplit ? "Tên PDF / thư mục tách trang" : "Tên PDF", 12, true));
        _name.Text = ToolCatalog.OutputName(new(source, tool)).Stem;
        _name.Margin = new Thickness(0, 4, 0, 8); Children.Add(_name);
        Children.Add(Ui.Text("Lưu tại", 12, true));
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var browse = Ui.Button("Chọn…", () =>
        {
            var picker = new OpenFolderDialog { Title = "Chọn thư mục lưu PDF" };
            if (Directory.Exists(_folder.Text)) picker.InitialDirectory = _folder.Text;
            if (picker.ShowDialog(Window.GetWindow(this)) == true) _folder.Text = picker.FolderName;
        });
        browse.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
        _folder.Text = ClipboardImages.OutputDirectory(source, app.Settings); row.Children.Add(_folder); Children.Add(row);
    }

    public (string Stem, string Directory) Read(bool folderOutput = false)
    {
        var name = _name.Text;
        if (!folderOutput && name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        PdfOutput.ValidateStem(name);
        if (string.IsNullOrWhiteSpace(_folder.Text) || !Path.IsPathFullyQualified(_folder.Text))
            throw new ArgumentException("Hãy chọn đường dẫn đầy đủ đến thư mục lưu.");
        return (name, Path.GetFullPath(_folder.Text));
    }
}
