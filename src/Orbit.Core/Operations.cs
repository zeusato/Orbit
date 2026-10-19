using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Orbit.Core;

public enum ToolKind { Png, Jpeg, Webp, Bmp, Tiff, Gif, Ico, Resize, Crop, Rotate, Compress, ImagePdf, PdfPng, PdfJpeg, PdfSplit, PdfMerge, OfficePdf, DocxText, Zip, Sha256, More, PdfWord, PdfText, ImageText, Frame }
public enum JobState { Queued, Running, Completed, Cancelled, Failed }
public sealed record CropRegion(int X, int Y, int Width, int Height);
public sealed record ImageRequest(string Source, ToolKind Tool, int Width = 0, int Height = 0,
    bool KeepAspect = true, int Quality = 85, CropRegion? Crop = null, string[]? Sources = null, FrameOptions? Frame = null);

public static class ToolCatalog
{
    public static readonly ToolKind[] Order = [ToolKind.Png, ToolKind.Jpeg, ToolKind.Resize, ToolKind.Crop, ToolKind.Rotate, ToolKind.Compress];
    public static string Label(ToolKind tool) => tool switch
    {
        ToolKind.Png or ToolKind.PdfPng => "PNG", ToolKind.Jpeg or ToolKind.PdfJpeg => "JPEG", ToolKind.Webp => "WebP",
        ToolKind.Bmp => "BMP", ToolKind.Tiff => "TIFF", ToolKind.Gif => "GIF", ToolKind.Ico => "ICO",
        ToolKind.ImagePdf or ToolKind.OfficePdf => "PDF", ToolKind.PdfSplit => "Tách trang", ToolKind.PdfMerge => "Ghép PDF",
        ToolKind.PdfWord => "Word", ToolKind.PdfText => "Lấy chữ", ToolKind.ImageText => "OCR → TXT",
        ToolKind.DocxText => "Lấy văn bản", ToolKind.Zip => "Tạo ZIP", ToolKind.Sha256 => "SHA-256", ToolKind.More => "Thêm…",
        ToolKind.Resize => "Resize", ToolKind.Crop => "Crop", ToolKind.Rotate => "Xoay 90°", ToolKind.Frame => "Khung nền", _ => "Nén JPEG"
    };
    public static string Description(ToolKind tool) => tool switch
    {
        ToolKind.Png => "Chuyển thành PNG", ToolKind.Jpeg => "Chuyển thành JPEG",
        ToolKind.Webp => "Chuyển thành WebP lossless", ToolKind.Bmp => "Chuyển thành BMP", ToolKind.Tiff => "Chuyển thành TIFF",
        ToolKind.Gif => "Chuyển thành GIF tĩnh (256 màu)", ToolKind.Ico => "Tạo icon ICO (tối đa 256 px)",
        ToolKind.ImagePdf => "Chuyển ảnh thành PDF", ToolKind.PdfPng => "Xuất tất cả trang PDF thành PNG",
        ToolKind.PdfJpeg => "Xuất tất cả trang PDF thành JPEG", ToolKind.PdfSplit => "Tách mỗi trang thành một PDF",
        ToolKind.PdfMerge => "Ghép PDF theo thứ tự tên file", ToolKind.OfficePdf => "Xuất PDF bằng Microsoft Office",
        ToolKind.DocxText => "Trích văn bản DOCX thành TXT", ToolKind.Zip => "Tạo bản nén ZIP", ToolKind.Sha256 => "Lưu mã kiểm tra SHA-256",
        ToolKind.More => "Trang công cụ tiếp theo",
        ToolKind.PdfWord => "PDF có chữ → DOCX sửa được; giữ dòng/trang cơ bản, không phục dựng bảng và hình",
        ToolKind.PdfText => "Lấy chữ ra TXT: đọc lớp chữ, OCR tiếng Việt/Anh cho trang scan; không giữ bố cục",
        ToolKind.ImageText => "OCR ảnh tiếng Việt/Anh ra TXT; không giữ bố cục",
        ToolKind.Resize => "Thay đổi kích thước", ToolKind.Crop => "Cắt khung hình",
        ToolKind.Rotate => "Xoay phải 90 độ", ToolKind.Frame => "Mở rộng khung và thêm nền", _ => "Giảm dung lượng JPEG"
    };
    public static bool SupportsPath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" or ".gif" or ".webp" or ".ico" or ".avif" or ".heic" or ".heif" or ".tga";
    public static bool IsImageTool(ToolKind tool) => tool is ToolKind.Png or ToolKind.Jpeg or ToolKind.Webp or ToolKind.Bmp or ToolKind.Tiff or ToolKind.Gif or ToolKind.Ico or ToolKind.Resize or ToolKind.Crop or ToolKind.Rotate or ToolKind.Compress or ToolKind.Frame;
    public static IReadOnlyList<ToolKind> ForFiles(IReadOnlyList<FileKind> kinds, Func<FileKind, bool>? officeAvailable = null, bool pdfTextOnly = false)
    {
        if (kinds.Count == 0) return [];
        var tools = new List<ToolKind>();
        if (kinds.All(FileTypes.IsImage))
        {
            foreach (var (tool, kind) in new[] { (ToolKind.Png, FileKind.Png), (ToolKind.Jpeg, FileKind.Jpeg), (ToolKind.Webp, FileKind.Webp) })
                if (!kinds.Contains(kind)) tools.Add(tool);
            tools.Add(ToolKind.Resize); if (kinds.Count == 1) tools.Add(ToolKind.Crop);
            tools.Add(ToolKind.Rotate); tools.Add(ToolKind.Compress); tools.Add(ToolKind.Frame);
            foreach (var (tool, kind) in new[] { (ToolKind.Bmp, FileKind.Bmp), (ToolKind.Tiff, FileKind.Tiff), (ToolKind.Gif, FileKind.Gif), (ToolKind.Ico, FileKind.Ico) })
                if (!kinds.Contains(kind)) tools.Add(tool);
            tools.Add(ToolKind.ImagePdf);
            tools.Add(ToolKind.ImageText);
        }
        else if (kinds.All(k => k == FileKind.Pdf))
        {
            if (pdfTextOnly) tools.Add(ToolKind.PdfWord);
            tools.Add(ToolKind.PdfText);
            tools.AddRange([ToolKind.PdfPng, ToolKind.PdfJpeg, ToolKind.PdfSplit]); if (kinds.Count > 1) tools.Add(ToolKind.PdfMerge);
        }
        else if (kinds.All(FileTypes.IsOffice))
        {
            if (officeAvailable != null && kinds.All(officeAvailable)) tools.Add(ToolKind.OfficePdf);
            if (kinds.All(k => k == FileKind.Docx)) tools.Add(ToolKind.DocxText);
        }
        tools.Add(ToolKind.Zip); tools.Add(ToolKind.Sha256); return tools;
    }
    public static ToolKind[] Page(IReadOnlyList<ToolKind> tools, int page)
    {
        if (tools.Count <= 6) return tools.ToArray();
        var pages = (tools.Count + 4) / 5;
        return tools.Skip((page % pages) * 5).Take(5).Append(ToolKind.More).ToArray();
    }
    public static bool SupportsCount(ToolKind tool, int count) => count > 0 && (tool != ToolKind.Crop || count == 1);
    public static (string Stem, string Extension) OutputName(ImageRequest request)
    {
        var stem = Path.GetFileNameWithoutExtension(request.Source);
        return request.Tool switch
        {
            ToolKind.Png => (stem, ".png"), ToolKind.Jpeg => (stem, ".jpg"),
            ToolKind.Webp => (stem, ".webp"), ToolKind.Bmp => (stem, ".bmp"), ToolKind.Tiff => (stem, ".tiff"),
            ToolKind.Gif => (stem, ".gif"), ToolKind.Ico => (stem, ".ico"),
            ToolKind.ImagePdf or ToolKind.OfficePdf => (stem, ".pdf"), ToolKind.PdfMerge => (stem + "_merged", ".pdf"),
            ToolKind.DocxText => (stem, ".txt"), ToolKind.Zip => (stem, ".zip"), ToolKind.Sha256 => (stem, ".sha256"),
            ToolKind.PdfWord => (stem, ".docx"), ToolKind.PdfText or ToolKind.ImageText => (stem + "_text", ".txt"),
            ToolKind.Compress => (stem + "_small", ".jpg"),
            ToolKind.Frame => (stem + "_frame", request.Frame?.Jpeg == true ? ".jpg" : ".png"),
            ToolKind.Resize => (stem + "_resized", ".png"),
            ToolKind.Crop => (stem + "_crop", ".png"),
            _ => (stem + "_rotated", ".png")
        };
    }
}

public static class RadialGeometry
{
    public const double Diameter = 400;
    public const double Center = Diameter / 2;
    public const double InnerRadius = 65;
    public const double OuterRadius = 190;
    public const double HalfSector = 28.5;
    public static int HitTest(double x, double y, int count = 6)
    {
        var dx = x - Center; var dy = y - Center;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (count < 2 || radius < InnerRadius || radius > OuterRadius) return -1;
        var angle = (Math.Atan2(dy, dx) * 180 / Math.PI + 450) % 360;
        var step = 360d / count;
        var sector = (int)Math.Floor((angle + step / 2) / step) % count;
        var distance = Math.Abs((angle - sector * step + 540) % 360 - 180);
        return distance <= step / 2 - 1.5 ? sector : -1;
    }
    public static (double X, double Y) Point(double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return (Center + radius * Math.Cos(radians), Center + radius * Math.Sin(radians));
    }
}

public static class OutputFiles
{
    // Rename without replacement is the commit step. Race collisions choose the next name.
    public static string Commit(string temporary, string directory, string stem, string extension)
    {
        for (int suffix = 0; suffix < 10000; suffix++)
        {
            var candidate = Path.Combine(directory, stem + (suffix == 0 ? "" : $" ({suffix})") + extension);
            try { File.Move(temporary, candidate, false); return candidate; }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
        }
        throw new IOException("Đã có quá nhiều file trùng tên. Hãy đổi tên file nguồn.");
    }
}
