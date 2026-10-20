using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orbit.Core;

public enum TextAction { Base64Encode, Base64Decode, UrlEncode, UrlDecode, JsonFormat, JsonMinify, TrimLines, RemoveEmptyLines, UniqueLines, Uppercase, Lowercase }

public static class TextUtilities
{
    public const int MaximumCharacters = 1_000_000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static string Transform(string text, TextAction action)
    {
        if (text.Length > MaximumCharacters) throw new ArgumentException("Văn bản tối đa 1 triệu ký tự.");
        if (action is TextAction.JsonFormat or TextAction.JsonMinify)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = action == TextAction.JsonFormat,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch (JsonException error)
            { throw new ArgumentException($"JSON không hợp lệ ở dòng {error.LineNumber + 1}, vị trí {error.BytePositionInLine + 1}.", error); }
        }
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return action switch
        {
            TextAction.Base64Encode => Convert.ToBase64String(Utf8.GetBytes(text)),
            TextAction.Base64Decode => DecodeBase64(text),
            TextAction.UrlEncode => Uri.EscapeDataString(text),
            TextAction.UrlDecode => DecodeUrl(text),
            TextAction.TrimLines => string.Join(Environment.NewLine, lines.Select(l => l.Trim())),
            TextAction.RemoveEmptyLines => string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrWhiteSpace(l))),
            TextAction.UniqueLines => string.Join(Environment.NewLine, lines.Distinct(StringComparer.Ordinal)),
            TextAction.Uppercase => text.ToUpperInvariant(),
            TextAction.Lowercase => text.ToLowerInvariant(),
            _ => throw new ArgumentException("Công cụ không hợp lệ.")
        };
    }

    private static string DecodeBase64(string text)
    {
        try { return Utf8.GetString(Convert.FromBase64String(text)); }
        catch (Exception error) when (error is FormatException or DecoderFallbackException)
        { throw new ArgumentException("Base64 không hợp lệ hoặc dữ liệu giải mã không phải văn bản UTF-8.", error); }
    }

    private static string DecodeUrl(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '%' && (i + 2 >= text.Length || !Uri.IsHexDigit(text[i + 1]) || !Uri.IsHexDigit(text[i + 2])))
                throw new ArgumentException("URL có chuỗi % không hợp lệ.");
        // Component encoding, not form-urlencoded: '+' remains '+'.
        return Uri.UnescapeDataString(text);
    }

    public static string Statistics(string text) =>
        $"{new System.Globalization.StringInfo(text).LengthInTextElements:N0} ký tự · {Regex.Matches(text, @"\S+").Count:N0} từ · " +
        $"{(text.Length == 0 ? 0 : text.Replace("\r\n", "\n").Replace('\r', '\n').Count(c => c == '\n') + 1):N0} dòng";
}
