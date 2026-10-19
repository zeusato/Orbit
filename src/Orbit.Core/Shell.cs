using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Orbit.Core;

// A request from the Explorer context menu: run Tool on Files, or open the ring menu (Tool null).
public sealed record ShellCommand(ToolKind? Tool, string[] Files)
{
    public static bool TryParse(string[] args, out ShellCommand command)
    {
        command = new(null, []);
        if (args.Length >= 2 && args[0] == "--open") { command = new(null, args[1..]); return true; }
        if (args.Length >= 3 && args[0] == "--tool" && Enum.TryParse<ToolKind>(args[1], false, out var tool) && Enum.IsDefined(tool) && tool != ToolKind.More)
        { command = new(tool, args[2..]); return true; }
        return false;
    }
    public string[] ToArgs() => Tool is ToolKind tool ? ["--tool", tool.ToString(), .. Files] : ["--open", .. Files];

    // Explorer may start one process per selected file; requests arriving together become one batch per tool.
    public static IReadOnlyList<ShellCommand> Merge(IEnumerable<ShellCommand> commands) =>
        commands.GroupBy(c => c.Tool).Select(g => new ShellCommand(g.Key, g.SelectMany(c => c.Files).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(128).ToArray())).ToArray();
}

public static class ShellMenuLayout
{
    // Extension → representative kind; the menu is static, so content is checked again when a tool runs.
    public static readonly IReadOnlyDictionary<string, FileKind> Extensions = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = FileKind.Jpeg, [".jpeg"] = FileKind.Jpeg, [".png"] = FileKind.Png, [".webp"] = FileKind.Webp, [".bmp"] = FileKind.Bmp,
        [".tif"] = FileKind.Tiff, [".tiff"] = FileKind.Tiff, [".gif"] = FileKind.Gif, [".ico"] = FileKind.Ico, [".avif"] = FileKind.Avif,
        [".heic"] = FileKind.Heic, [".heif"] = FileKind.Heic, [".tga"] = FileKind.Tga, [".pdf"] = FileKind.Pdf,
        [".docx"] = FileKind.Docx, [".doc"] = FileKind.Word, [".xls"] = FileKind.Excel, [".xlsx"] = FileKind.Excel, [".ppt"] = FileKind.PowerPoint, [".pptx"] = FileKind.PowerPoint,
        [".mp4"] = FileKind.Mp4, [".m4v"] = FileKind.Mp4, [".mov"] = FileKind.Video, [".mkv"] = FileKind.Video, [".webm"] = FileKind.Video, [".avi"] = FileKind.Video, [".wmv"] = FileKind.Video,
        [".mp3"] = FileKind.Mp3, [".m4a"] = FileKind.M4a, [".wav"] = FileKind.Wav, [".wma"] = FileKind.Audio, [".flac"] = FileKind.Audio, [".ogg"] = FileKind.Audio, [".aac"] = FileKind.Audio
    };

    // Tools for one kind, including the batch-only ones (merge) that appear when several files are selected.
    public static IReadOnlyList<ToolKind> Tools(FileKind kind, Func<FileKind, bool>? officeAvailable = null)
    {
        var tools = ToolCatalog.ForFiles([kind], officeAvailable, pdfTextOnly: true).ToList();
        void Before(ToolKind tool, ToolKind anchor) { if (!tools.Contains(tool)) tools.Insert(tools.IndexOf(anchor), tool); }
        if (kind == FileKind.Pdf) Before(ToolKind.PdfMerge, ToolKind.Zip);
        if (FileTypes.IsAudio(kind)) Before(ToolKind.AudioMerge, ToolKind.Zip);
        return tools;
    }

    public static string Label(ToolKind tool) => tool switch
    {
        ToolKind.Png or ToolKind.Jpeg or ToolKind.Webp or ToolKind.Bmp or ToolKind.Tiff or ToolKind.Gif or ToolKind.Ico
            or ToolKind.ImagePdf or ToolKind.OfficePdf or ToolKind.VideoMp4 or ToolKind.AudioMp3 or ToolKind.AudioM4a or ToolKind.AudioWav => "Chuyển sang " + ToolCatalog.Label(tool),
        ToolKind.PdfPng or ToolKind.PdfJpeg => "Xuất các trang ra " + ToolCatalog.Label(tool),
        ToolKind.PdfWord => "Chuyển sang Word (DOCX)",
        ToolKind.VideoFrame => "Lưu khung hình (PNG)",
        _ => ToolCatalog.Label(tool)
    };
}
