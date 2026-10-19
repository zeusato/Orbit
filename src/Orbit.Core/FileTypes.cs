using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;

namespace Orbit.Core;

// Mp4 = MPEG-4 video other than QuickTime; Video = MOV, MKV/WebM, AVI, WMV. Audio = WMA, FLAC, Ogg, AAC and other decodable audio.
public enum FileKind { Unknown, Jpeg, Png, Webp, Bmp, Tiff, Gif, Ico, Avif, Heic, Tga, Pdf, Docx, Word, Excel, PowerPoint, Mp4, Video, Mp3, M4a, Wav, Audio }

public static class FileTypes
{
    public static bool IsImage(FileKind kind) => kind is >= FileKind.Jpeg and <= FileKind.Tga;
    public static bool IsOffice(FileKind kind) => kind is >= FileKind.Docx and <= FileKind.PowerPoint;
    public static bool IsVideo(FileKind kind) => kind is FileKind.Mp4 or FileKind.Video;
    public static bool IsAudio(FileKind kind) => kind is FileKind.Mp3 or FileKind.M4a or FileKind.Wav or FileKind.Audio;
    public static bool IsMedia(FileKind kind) => IsVideo(kind) || IsAudio(kind);
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
            // Major brand first, then compatible brands: generic HEIF (mif1/msf1) may carry AV1 or HEVC.
            var boxSize = (int)Math.Min(b.Length, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(b));
            var brands = new List<string>();
            for (int i = 8; i + 4 <= boxSize; i += i == 8 ? 8 : 4) brands.Add(System.Text.Encoding.ASCII.GetString(b.Slice(i, 4)));
            static bool Av1(string brand) => brand is "avif" or "avis";
            static bool Hevc(string brand) => brand is "heic" or "heix" or "heim" or "heis" or "hevc" or "hevx";
            if (brands.Count == 0) return FileKind.Unknown;
            if (Av1(brands[0])) return FileKind.Avif;
            if (Hevc(brands[0])) return FileKind.Heic;
            if (brands.Any(Av1)) return FileKind.Avif;
            if (brands.Any(x => Hevc(x) || x is "mif1" or "msf1")) return FileKind.Heic;
            if (brands.Any(x => x is "M4A " or "M4B " or "M4P " or "F4A ")) return FileKind.M4a;
            if (brands[0] is "qt  ") return FileKind.Video;
            // Windows and many tools write audio-only MP4 with generic brands; the extension then decides.
            if (Path.GetExtension(path).ToLowerInvariant() is ".m4a" or ".m4b" or ".aac") return FileKind.M4a;
            if (brands.Any(x => x is "isom" or "iso2" or "iso4" or "iso5" or "iso6" or "mp41" or "mp42" or "avc1" or "M4V " or "M4VH" or "3gp4" or "3gp5" or "3gp6" or "3g2a" or "dash" or "MSNV" or "mmp4" or "XAVC" or "f4v ")) return FileKind.Mp4;
        }
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8))
        {
            if (b.Slice(8, 4).SequenceEqual("WAVE"u8)) return FileKind.Wav;
            if (b.Slice(8, 4).SequenceEqual("AVI "u8)) return FileKind.Video;
        }
        if (b.StartsWith(new byte[] {0x1a,0x45,0xdf,0xa3})) return FileKind.Video; // Matroska / WebM (EBML)
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (b.StartsWith(new byte[] {0x30,0x26,0xb2,0x75,0x8e,0x66,0xcf,0x11})) return extension == ".wma" ? FileKind.Audio : FileKind.Video; // ASF
        if (b.StartsWith("fLaC"u8) || b.StartsWith("OggS"u8)) return FileKind.Audio;
        if (b.StartsWith("ID3"u8)) return extension is ".aac" or ".m4a" ? FileKind.Audio : FileKind.Mp3;
        // Bare MPEG audio frames have only an 11-bit sync word; trust it only with a matching extension.
        if (b.Length >= 2 && b[0] == 0xff && (b[1] & 0xe0) == 0xe0 && extension is ".mp3" or ".aac")
            return (b[1] & 0x06) == 0 ? FileKind.Audio : FileKind.Mp3;
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
