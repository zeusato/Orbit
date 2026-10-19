using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orbit.Core;

public sealed record UpdateManifest(int Schema, string Version, string Asset, long Size, string Sha256);

public static class UpdatePackage
{
    public const long MaxZipBytes = 256L * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static string PublicKey
    {
        get { using var stream = typeof(UpdatePackage).Assembly.GetManifestResourceStream("Orbit.UpdatePublicKey")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    }
    public static Version ParseVersion(string text)
    {
        if (!Regex.IsMatch(text, @"^\d+\.\d+\.\d+$") || !System.Version.TryParse(text, out var version)) throw new InvalidDataException("Phiên bản cập nhật không hợp lệ.");
        return version;
    }
    public static UpdateManifest VerifyManifest(byte[] bytes, string signature, string? publicKey = null)
    {
        if (bytes.Length > 16384 || signature.Length > 4096) throw new InvalidDataException("Manifest quá lớn.");
        using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey ?? PublicKey);
        if (!rsa.VerifyData(bytes, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new CryptographicException("Chữ ký bản cập nhật không hợp lệ.");
        var m = JsonSerializer.Deserialize<UpdateManifest>(bytes, Json) ?? throw new InvalidDataException("Thiếu manifest.");
        _ = ParseVersion(m.Version);
        if (m.Schema != 1 || m.Asset != $"Orbit-{m.Version}-win-x64.zip" || m.Size <= 0 || m.Size > MaxZipBytes || !Regex.IsMatch(m.Sha256 ?? "", "^[A-Fa-f0-9]{64}$")) throw new InvalidDataException("Manifest không được hỗ trợ.");
        return m;
    }
    public static void VerifyArchive(string zip, UpdateManifest manifest)
    {
        using var file = File.OpenRead(zip);
        if (file.Length != manifest.Size || !Convert.ToHexString(SHA256.HashData(file)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("Gói cập nhật bị thiếu hoặc sai SHA-256.");
    }
    public static string SafePath(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length == 0 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 || p.Any(char.IsControl) || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Đường dẫn trong gói không hợp lệ.");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, Path.Combine(parts)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Đường dẫn vượt thư mục cập nhật.");
        // Refuse reparse points in the destination chain, including the root.
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Không cập nhật qua liên kết thư mục.");
        return path;
    }
    public static string[] Extract(string zip, string target)
    {
        if (Directory.Exists(target)) throw new IOException("Thư mục staging phải là mới.");
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 2000) throw new InvalidDataException("Gói có quá nhiều file.");
        var files = new List<(ZipArchiveEntry Entry, string Relative)>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith("Orbit/", StringComparison.Ordinal) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("Cấu trúc ZIP không hợp lệ.");
            if (name.EndsWith('/')) { if (name != "Orbit/") _ = SafePath(target, name[6..].TrimEnd('/')); continue; }
            var relative = name[6..]; _ = SafePath(target, relative);
            if (relative.StartsWith(".orbit-", StringComparison.OrdinalIgnoreCase) || relative.Equals("settings.json", StringComparison.OrdinalIgnoreCase) || !seen.Add(relative)) throw new InvalidDataException("Gói chứa tên file trùng hoặc dành riêng.");
            total = checked(total + entry.Length); if (total > 768L * 1024 * 1024) throw new InvalidDataException("Gói giải nén quá lớn.");
            files.Add((entry, relative));
        }
        foreach (var required in new[] { "Orbit.exe", "Orbit.dll", "Orbit.deps.json", "Orbit.runtimeconfig.json", "Orbit.Core.dll", "Orbit.Updater.exe", "Orbit.Updater.dll", "Orbit.Updater.deps.json", "Orbit.Updater.runtimeconfig.json" })
            if (!seen.Contains(required)) throw new InvalidDataException("Gói thiếu " + required);
        Directory.CreateDirectory(target);
        foreach (var (entry, relative) in files)
        {
            var path = SafePath(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew); input.CopyTo(output);
            if (output.Length != entry.Length) throw new InvalidDataException("File giải nén không đầy đủ.");
        }
        return files.Select(f => f.Relative).ToArray();
    }
}
