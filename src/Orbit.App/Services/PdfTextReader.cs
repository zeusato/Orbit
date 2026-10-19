using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Orbit.App.Imaging;

namespace Orbit.App.Services;

public sealed record PdfPageText(int Number, string Text, bool NeedsOcr, bool Blank);
public sealed record PdfTextAnalysis(IReadOnlyList<PdfPageText> Pages)
{
    public bool CanConvertToWord => Pages.Any(p => !p.Blank && !p.NeedsOcr) && Pages.All(p => !p.NeedsOcr);
}

public static class PdfTextReader
{
    public static PdfTextAnalysis Analyze(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (new FileInfo(path).Length > ImageEngine.MaxFileBytes) throw new NotSupportedException("Mỗi PDF tối đa 256 MB.");
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(path);
            if (document.NumberOfPages is < 1 or > 200) throw new NotSupportedException("Mỗi tác vụ hỗ trợ từ 1 đến 200 trang PDF.");
            var pages = new List<PdfPageText>();
            foreach (var page in document.GetPages())
            {
                token.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page).Trim();
                var letters = text.Count(char.IsLetterOrDigit);
                var blank = text.Length == 0 && page.NumberOfImages == 0 && page.Paths.Count == 0;
                var largeImage = page.GetImages().Any(i => Math.Abs(i.BoundingBox.Width * i.BoundingBox.Height) > page.Width * page.Height * .55);
                // A page number/header over a scanned sheet is not a usable text layer.
                var needsOcr = !blank && (letters == 0 || (largeImage && letters < 80) || text.Count(c => c == '\uFFFD') > Math.Max(1, text.Length / 100));
                pages.Add(new PdfPageText(page.Number, text, needsOcr, blank));
            }
            return new PdfTextAnalysis(pages);
        }
        catch (Exception error) when (error is not OperationCanceledException and not NotSupportedException and not IOException and not UnauthorizedAccessException)
        { throw new InvalidDataException("Không đọc được lớp chữ PDF. File có thể bị lỗi hoặc cần mật khẩu.", error); }
    }
}

public static class FileToolResolver
{
    public static IReadOnlyList<ToolKind> Resolve(string[] files, CancellationToken token = default) => ResolveWithNotice(files, token).Tools;
    // Notice: a short reason shown in the menu hub when media tools are withheld (missing codec, limits).
    public static (IReadOnlyList<ToolKind> Tools, string? Notice) ResolveWithNotice(string[] files, CancellationToken token = default)
    {
        var kinds = files.Select(FileTypes.Detect).ToArray();
        var textOnly = kinds.All(k => k == FileKind.Pdf) && files.All(f => PdfTextReader.Analyze(f, token).CanConvertToWord);
        string? notice = null;
        if (kinds.Length > 0 && kinds.All(FileTypes.IsMedia))
        {
            for (int i = 0; i < files.Length && notice == null; i++)
            {
                token.ThrowIfCancellationRequested();
                if (new FileInfo(files[i]).Length > MediaLimits.MaxBytes) { notice = "File quá 2 GB"; break; }
                try
                {
                    var info = MediaEngine.Probe(files[i], token);
                    if (info.Duration > MediaLimits.MaxDuration) { notice = "File dài quá 1 giờ"; break; }
                    // An MP4/MKV without a picture track is audio for every tool.
                    if (FileTypes.IsVideo(kinds[i]) && !info.HasVideo) kinds[i] = FileKind.Audio;
                    // Building the decode chain is the reliable codec check; bound it for large batches.
                    if (i < 8) notice = MediaEngine.MissingCodec(files[i], info, token);
                }
                catch (NotSupportedException) { notice = "Windows chưa đọc được\nđịnh dạng này"; }
            }
        }
        return (ToolCatalog.ForFiles(kinds, OfficeConverter.Available, textOnly, notice == null), notice);
    }
}
