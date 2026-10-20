using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Orbit.Core;

public sealed record PdfOptions(string? Pages = null, bool SeparateFiles = true);

public static class PdfOutput
{
    public static string ValidateStem(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name != name.Trim() ||
            name.EndsWith('.') || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
            name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException("Tên không được trống, kết thúc bằng dấu chấm hoặc chứa ký tự tên file không hợp lệ.");
        var device = name.Split('.')[0].ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            System.Text.RegularExpressions.Regex.IsMatch(device, @"^(COM|LPT)[0-9¹²³]$"))
            throw new ArgumentException("Tên này được Windows dành riêng. Hãy chọn tên khác.");
        return name;
    }
}

public static class PdfPages
{
    public const int Maximum = 200;

    // One-based user input, zero-based output. Preserve entered order; overlapping ranges
    // include each page once. Empty input explicitly means all pages.
    public static int[] Parse(string? text, int count)
    {
        if (count is < 1 or > Maximum) throw new ArgumentException("Mỗi tác vụ hỗ trợ từ 1 đến 200 trang PDF.");
        if (string.IsNullOrWhiteSpace(text)) return Enumerable.Range(0, count).ToArray();
        var result = new List<int>();
        var seen = new HashSet<int>();
        foreach (var part in text.Replace('–', '-').Replace('—', '-').Split(','))
        {
            var bounds = part.Trim().Split('-');
            int Number(string value)
            {
                if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number > count)
                    throw new ArgumentException($"Trang phải từ 1 đến {count}. Ví dụ: 1-3, 5.");
                return number;
            }
            if (bounds.Length is < 1 or > 2) throw new ArgumentException("Nhập trang dạng 1-3, 5.");
            var first = Number(bounds[0]); var last = bounds.Length == 2 ? Number(bounds[1]) : first;
            if (last < first) throw new ArgumentException("Điểm cuối khoảng trang phải lớn hơn hoặc bằng điểm đầu.");
            for (var page = first; page <= last; page++) if (seen.Add(page - 1)) result.Add(page - 1);
        }
        return result.ToArray();
    }
}
