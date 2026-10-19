using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Orbit.App.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Orbit.App.Services;

public sealed class DocumentEngine
{
    public string Execute(ImageRequest request, CancellationToken token, Action<int>? progress = null)
    {
        token.ThrowIfCancellationRequested(); progress?.Invoke(5);
        var source = Path.GetFullPath(request.Source);
        if (!File.Exists(source)) throw new FileNotFoundException("Không tìm thấy file nguồn.", source);
        if (new FileInfo(source).Length > ImageEngine.MaxFileBytes) throw new NotSupportedException("Mỗi file tối đa 256 MB.");
        var kind = FileTypes.Detect(source);
        var dir = Path.GetDirectoryName(source)!;
        var folderOutput = request.Tool is ToolKind.PdfPng or ToolKind.PdfJpeg or ToolKind.PdfSplit;
        var temp = Path.Combine(dir, $".orbit-{Guid.NewGuid():N}" + (folderOutput ? "" : request.Tool == ToolKind.PdfWord ? ".tmp.docx" : ".tmp.pdf"));
        try
        {
            if (folderOutput) Directory.CreateDirectory(temp);
            switch (request.Tool)
            {
                case ToolKind.PdfWord:
                    RequirePdf(kind);
                    PdfWordWriter.Write(source, temp, token, progress);
                    if (FileTypes.Detect(temp) != FileKind.Docx) throw new InvalidDataException("Word chưa tạo được DOCX hợp lệ.");
                    break;
                case ToolKind.PdfText:
                    RequirePdf(kind); TextExtraction.FromPdf(source, temp, token, progress); break;
                case ToolKind.ImageText:
                    if (!FileTypes.IsImage(kind)) throw new NotSupportedException("OCR ảnh chỉ nhận ảnh tĩnh được hỗ trợ.");
                    TextExtraction.FromImage(source, temp, token, progress); break;
                case ToolKind.ImagePdf:
                    var bitmap = new ImageEngine().Load(source);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var memory = new MemoryStream())
                    {
                        encoder.Save(memory); memory.Position = 0;
                        using var image = XImage.FromStream(memory);
                        using var pdf = new PdfDocument(); var page = pdf.AddPage();
                        var scale = Math.Min(1, 14400d / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
                        page.Width = XUnit.FromPoint(bitmap.PixelWidth * .75 * scale); page.Height = XUnit.FromPoint(bitmap.PixelHeight * .75 * scale);
                        using (var g = XGraphics.FromPdfPage(page)) g.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
                        pdf.Save(temp);
                    }
                    break;
                case ToolKind.PdfPng: case ToolKind.PdfJpeg:
                    RequirePdf(kind);
                    var bytes = File.ReadAllBytes(source);
                    var sizes = PDFtoImage.Conversion.GetPageSizes(bytes).ToArray();
                    CheckPages(sizes.Length);
                    foreach (var size in sizes) ImageEngine.ValidatePixels((int)Math.Ceiling(size.Width * 150 / 72), (int)Math.Ceiling(size.Height * 150 / 72));
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var name = Path.Combine(temp, $"page-{i + 1:D4}" + (request.Tool == ToolKind.PdfPng ? ".png" : ".jpg"));
                        var options = new PDFtoImage.RenderOptions(Dpi: 150,
                            Width: (int)Math.Ceiling(sizes[i].Width * 150 / 72), Height: (int)Math.Ceiling(sizes[i].Height * 150 / 72), WithAspectRatio: false);
                        if (request.Tool == ToolKind.PdfPng) PDFtoImage.Conversion.SavePng(name, bytes, page: i, options: options);
                        else PDFtoImage.Conversion.SaveJpeg(name, bytes, page: i, options: options);
                        progress?.Invoke(10 + (i + 1) * 75 / sizes.Length);
                    }
                    break;
                case ToolKind.PdfSplit:
                    RequirePdf(kind);
                    using (var pdf = PdfReader.Open(source, PdfDocumentOpenMode.Import))
                    {
                        CheckPages(pdf.PageCount);
                        for (int i = 0; i < pdf.PageCount; i++)
                        {
                            token.ThrowIfCancellationRequested(); using var single = new PdfDocument(); single.AddPage(pdf.Pages[i]);
                            single.Save(Path.Combine(temp, $"page-{i + 1:D4}.pdf")); progress?.Invoke(10 + (i + 1) * 75 / pdf.PageCount);
                        }
                    }
                    break;
                case ToolKind.PdfMerge:
                    var sources = request.Sources ?? throw new ArgumentException("Chọn ít nhất hai PDF để ghép.");
                    if (sources.Length is < 2 or > 128) throw new ArgumentException("Chọn từ 2 đến 128 PDF.");
                    using (var merged = new PdfDocument())
                    {
                        foreach (var input in sources.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                        {
                            token.ThrowIfCancellationRequested(); RequirePdf(FileTypes.Detect(input));
                            if (new FileInfo(input).Length > ImageEngine.MaxFileBytes) throw new NotSupportedException("Mỗi PDF tối đa 256 MB.");
                            using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Import);
                            CheckPages(merged.PageCount + pdf.PageCount);
                            foreach (var page in pdf.Pages) merged.AddPage(page);
                        }
                        merged.Save(temp);
                    }
                    break;
                case ToolKind.OfficePdf:
                    if (!FileTypes.IsOffice(kind)) throw new NotSupportedException("File không phải tài liệu Office.");
                    OfficeConverter.ExportPdf(source, temp, kind); break;
                case ToolKind.DocxText:
                    if (kind != FileKind.Docx) throw new NotSupportedException("Lấy văn bản chỉ hỗ trợ DOCX.");
                    using (var zip = ZipFile.OpenRead(source))
                    {
                        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("DOCX thiếu nội dung.");
                        using var input = entry.Open();
                        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
                        var doc = XDocument.Load(reader); XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        var paragraphs = doc.Descendants(w + "p").Select(p => string.Concat(p.Descendants().Select(e => e.Name == w + "t" ? e.Value : e.Name == w + "tab" ? "\t" : e.Name == w + "br" ? "\n" : "")));
                        File.WriteAllText(temp, string.Join(Environment.NewLine, paragraphs), new UTF8Encoding(false));
                    }
                    break;
                case ToolKind.Zip:
                    using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
                    using (var output = archive.CreateEntry(Path.GetFileName(source), CompressionLevel.Optimal).Open())
                    using (var input = File.OpenRead(source)) Copy(input, output, token);
                    break;
                case ToolKind.Sha256:
                    using (var input = File.OpenRead(source))
                    using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        var buffer = new byte[81920]; int read;
                        while ((read = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
                        File.WriteAllText(temp, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() + "  " + Path.GetFileName(source) + Environment.NewLine);
                    }
                    break;
                default: throw new NotSupportedException("Công cụ chưa được hỗ trợ.");
            }
            progress?.Invoke(90); token.ThrowIfCancellationRequested();
            string result;
            if (folderOutput) result = CommitFolder(temp, dir, Path.GetFileNameWithoutExtension(source) + (request.Tool == ToolKind.PdfSplit ? "_pages" : "_images"));
            else { var name = ToolCatalog.OutputName(request); result = OutputFiles.Commit(temp, dir, name.Stem, name.Extension); }
            progress?.Invoke(100); return result;
        }
        finally
        {
            // Only this operation's GUID staging path is eligible for cleanup.
            if (File.Exists(temp)) File.Delete(temp);
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }
    private static void RequirePdf(FileKind kind) { if (kind != FileKind.Pdf) throw new InvalidDataException("Nội dung file không phải PDF."); }
    private static void CheckPages(int count) { if (count is < 1 or > 200) throw new NotSupportedException("Mỗi tác vụ hỗ trợ từ 1 đến 200 trang PDF."); }
    private static void Copy(Stream input, Stream output, CancellationToken token)
    { var buffer = new byte[81920]; int read; while ((read = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); } }
    private static string CommitFolder(string temp, string directory, string stem)
    {
        for (int i = 0; i < 10000; i++)
        {
            var target = Path.Combine(directory, stem + (i == 0 ? "" : $" ({i})"));
            try { Directory.Move(temp, target); return target; }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { }
        }
        throw new IOException("Quá nhiều thư mục trùng tên.");
    }
}
