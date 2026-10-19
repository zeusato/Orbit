using System.Runtime.InteropServices;

namespace Orbit.App.Services;

public static class OfficeConverter
{
    private static string? ProgId(FileKind kind) => kind switch
    { FileKind.Docx or FileKind.Word => "Word.Application", FileKind.Excel => "Excel.Application", FileKind.PowerPoint => "PowerPoint.Application", _ => null };
    public static bool Available(FileKind kind) => ProgId(kind) is { } id && Type.GetTypeFromProgID(id) != null;

    // Runs on the queue's STA thread. Open a read-only document, export a copy,
    // close only that document, and never quit Office while another document exists.
    public static void ExportPdf(string source, string destination, FileKind kind)
    {
        var id = ProgId(kind) ?? throw new NotSupportedException("Không hỗ trợ loại tài liệu này.");
        var type = Type.GetTypeFromProgID(id) ?? throw new NotSupportedException("Cần cài Microsoft Office tương ứng để xuất PDF.");
        dynamic app = Activator.CreateInstance(type)!;
        object? document = null, collection = null;
        int security = app.AutomationSecurity;
        try
        {
            app.AutomationSecurity = 3; // msoAutomationSecurityForceDisable, restored below.
            if (kind is FileKind.Docx or FileKind.Word)
            {
                collection = app.Documents;
                document = ((dynamic)collection).Open(FileName: source, ConfirmConversions: false, ReadOnly: true,
                    AddToRecentFiles: false, PasswordDocument: "", PasswordTemplate: "", Revert: false,
                    WritePasswordDocument: "", WritePasswordTemplate: "", Visible: false, OpenAndRepair: false, NoEncodingDialog: true);
                ((dynamic)document).ExportAsFixedFormat(OutputFileName: destination, ExportFormat: 17, OpenAfterExport: false);
            }
            else if (kind == FileKind.Excel)
            {
                collection = app.Workbooks;
                document = ((dynamic)collection).Open(Filename: source, UpdateLinks: 0, ReadOnly: true,
                    Password: "", WriteResPassword: "", IgnoreReadOnlyRecommended: true, Notify: false, AddToMru: false);
                ((dynamic)document).ExportAsFixedFormat(Type: 0, Filename: destination, OpenAfterPublish: false);
            }
            else
            {
                collection = app.Presentations;
                document = ((dynamic)collection).Open(FileName: source, ReadOnly: -1, Untitled: 0, WithWindow: 0);
                ((dynamic)document).SaveAs(destination, 32);
            }
        }
        catch (COMException error) { throw new IOException("Office không xuất được PDF. File có thể được bảo vệ, bị hỏng hoặc Office chưa sẵn sàng.", error); }
        finally
        {
            if (document != null)
            {
                try { if (kind == FileKind.PowerPoint) ((dynamic)document).Close(); else ((dynamic)document).Close(false); } catch (COMException) { }
                Marshal.FinalReleaseComObject(document);
            }
            try { app.AutomationSecurity = security; if (collection != null && ((dynamic)collection).Count == 0) app.Quit(); } catch (COMException) { }
            if (collection != null) Marshal.FinalReleaseComObject(collection);
            Marshal.FinalReleaseComObject(app);
        }
    }
}
