using System;
using System.IO;
using System.IO.Compression;

namespace Orbit.Core;

public enum FileKind { Unknown, Jpeg, Png, Webp, Bmp, Tiff, Gif, Ico, Avif, Heic, Tga, Pdf, Docx, Word, Excel, PowerPoint }

public static class FileTypes
{
    public static bool IsImage(FileKind kind) => kind is >= FileKind.Jpeg and <= FileKind.Tga && kind != FileKind.Heic;
    public static bool IsOffice(FileKind kind) => kind is >= FileKind.Docx and <= FileKind.PowerPoint;
    public static FileKind Detect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> bytes = stackalloc byte[512]; var length = stream.Read(bytes); var b = bytes[..length];
        if (b.StartsWith(new byte[] {255,216,255})) return FileKind.Jpeg;
        if (b.StartsWith(new byte[] {137,80,78,71,13,10,26,10})) return FileKind.Png;
        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)) return FileKind.Gif;
        if (b.StartsWith("BM"u8)) return FileKind.Bmp;
        if (b.StartsWith("II\x2a\0"u8) || b.StartsWith("MM\0\x2a"u8)) return FileKind.Tiff;
        if (b.StartsWith(new byte[] {0,0,1,0})) return FileKind.Ico;
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8,4).SequenceEqual("WEBP"u8)) return FileKind.Webp;
        if (b.Length >= 16 && b.Slice(4,4).SequenceEqual("ftyp"u8))
        {
            if (b.Slice(8,4).SequenceEqual("avif"u8) || b.Slice(8,4).SequenceEqual("avis"u8)) return FileKind.Avif;
            if (b.Slice(8,4).SequenceEqual("heic"u8) || b.Slice(8,4).SequenceEqual("heix"u8) || b.Slice(8,4).SequenceEqual("mif1"u8)) return FileKind.Heic;
        }
        if (b.StartsWith("%PDF-"u8)) return FileKind.Pdf;
        if (b.StartsWith("PK\x03\x04"u8))
        {
            stream.Position = 0;
            try
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
                if (zip.GetEntry("[Content_Types].xml") == null) return FileKind.Unknown;
                if (zip.GetEntry("word/document.xml") != null) return FileKind.Docx;
                if (zip.GetEntry("xl/workbook.xml") != null) return FileKind.Excel;
                if (zip.GetEntry("ppt/presentation.xml") != null) return FileKind.PowerPoint;
            }
            catch (InvalidDataException) { }
        }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (b.StartsWith(new byte[] {0xd0,0xcf,0x11,0xe0,0xa1,0xb1,0x1a,0xe1}))
            return ext switch { ".doc" or ".docx" => FileKind.Word, ".xls" or ".xlsx" => FileKind.Excel, ".ppt" or ".pptx" => FileKind.PowerPoint, _ => FileKind.Unknown };
        // TGA has no mandatory magic. Validate its header before offering image tools.
        if (ext == ".tga" && b.Length >= 18 && b[2] is 2 or 3 or 10 or 11 && b[16] is 8 or 16 or 24 or 32) return FileKind.Tga;
        return FileKind.Unknown;
    }
}
