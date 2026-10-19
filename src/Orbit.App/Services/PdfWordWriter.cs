using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Orbit.App.Services;

// Editable text only. Source page boundaries and line breaks are retained;
// a PDF's visual layout is not an editable Word document structure.
public static class PdfWordWriter
{
    public static void Write(string source, string target, CancellationToken token, Action<int>? progress)
    {
        var analysis = PdfTextReader.Analyze(source, token);
        if (!analysis.CanConvertToWord)
            throw new NotSupportedException("PDF có trang scan hoặc không có lớp chữ dùng được. Chọn Lấy chữ để trích TXT bằng OCR.");
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = new XElement(w + "body");
        foreach (var page in analysis.Pages)
        {
            token.ThrowIfCancellationRequested();
            if (page.Number > 1)
                body.Add(new XElement(w + "p", new XElement(w + "r", new XElement(w + "br", new XAttribute(w + "type", "page")))));
            foreach (var line in page.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                token.ThrowIfCancellationRequested();
                // Invalid XML control characters occasionally occur in PDF text maps.
                var clean = string.Concat(line.EnumerateRunes().Where(r => r.Value > 0xffff || XmlConvert.IsXmlChar((char)r.Value)).Select(r => r.ToString()));
                body.Add(new XElement(w + "p",
                    new XElement(w + "pPr", new XElement(w + "spacing", new XAttribute(w + "after", "0"))),
                    new XElement(w + "r", new XElement(w + "rPr",
                        new XElement(w + "rFonts", new XAttribute(w + "ascii", "Arial"), new XAttribute(w + "hAnsi", "Arial")),
                        new XElement(w + "sz", new XAttribute(w + "val", "22"))),
                        new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), clean))));
            }
            progress?.Invoke(10 + page.Number * 75 / analysis.Pages.Count);
        }
        body.Add(new XElement(w + "sectPr",
            new XElement(w + "pgSz", new XAttribute(w + "w", "11906"), new XAttribute(w + "h", "16838")),
            new XElement(w + "pgMar", new XAttribute(w + "top", "1134"), new XAttribute(w + "right", "1134"),
                new XAttribute(w + "bottom", "1134"), new XAttribute(w + "left", "1134"))));
        token.ThrowIfCancellationRequested();
        using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        Save(zip, "[Content_Types].xml", new XElement(ct + "Types",
            new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))));
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        Save(zip, "_rels/.rels", new XElement(rel + "Relationships", new XElement(rel + "Relationship",
            new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "word/document.xml"))));
        Save(zip, "word/document.xml", new XElement(w + "document", new XAttribute(XNamespace.Xmlns + "w", w), body));
    }

    private static void Save(ZipArchive zip, string name, XElement root)
    {
        using var stream = zip.CreateEntry(name).Open();
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(stream);
    }
}
