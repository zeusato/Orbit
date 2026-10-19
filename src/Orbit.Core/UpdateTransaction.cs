using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

namespace Orbit.Core;

public sealed record UpdateBackupFile(string Path, bool Existed);
public sealed record UpdateJournal(string Root, UpdateBackupFile[] Files);

public static class UpdateTransaction
{
    // Full snapshot before the first replacement; a retained journal supports manual recovery after power loss.
    public static void Install(string root, string staged, string[] files, string backup, Func<bool> startAndCheck)
    {
        root = Path.GetFullPath(root);
        if (!File.Exists(UpdatePackage.SafePath(root, "Orbit.exe"))) throw new IOException("Không tìm thấy Orbit trong thư mục đích.");
        if (Directory.Exists(backup)) throw new IOException("Thư mục backup phải là mới.");
        var entries = files.Select(f => new UpdateBackupFile(f, File.Exists(UpdatePackage.SafePath(root, f)))).ToArray();
        foreach (var item in entries)
        {
            var original = UpdatePackage.SafePath(root, item.Path); var saved = UpdatePackage.SafePath(backup, "files/" + item.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            if (item.Existed) File.Copy(original, saved, false);
        }
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "journal.json"), JsonSerializer.Serialize(new UpdateJournal(root, entries)));
        var touched = new List<UpdateBackupFile>();
        try
        {
            foreach (var item in entries)
            {
                var destination = UpdatePackage.SafePath(root, item.Path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temp = destination + ".orbit-new-" + Guid.NewGuid().ToString("N");
                try { File.Copy(UpdatePackage.SafePath(staged, item.Path), temp, false); File.Move(temp, destination, true); touched.Add(item); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            if (!startAndCheck()) throw new IOException("Bản mới không xác nhận khởi động; khôi phục bản cũ.");
            File.WriteAllText(Path.Combine(backup, "completed.txt"), DateTime.UtcNow.ToString("O"));
        }
        catch
        {
            Restore(root, backup, touched.AsEnumerable().Reverse());
            File.WriteAllText(Path.Combine(backup, "rolled-back.txt"), DateTime.UtcNow.ToString("O"));
            throw;
        }
    }
    // A journal without completed/rolled-back markers means the helper stopped mid-replacement
    // (power loss, kill). The install folder may hold a mix of versions.
    public static string[] FindIncomplete(string root)
    {
        var folder = Path.Combine(Path.GetFullPath(root), ".orbit-backup");
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateDirectories(folder)
            .Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _) && File.Exists(Path.Combine(d, "journal.json"))
                && !File.Exists(Path.Combine(d, "completed.txt")) && !File.Exists(Path.Combine(d, "rolled-back.txt")))
            .OrderBy(Directory.GetCreationTimeUtc).ToArray();
    }
    public static void Recover(string root, string backup)
    {
        root = Path.GetFullPath(root);
        var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(Path.Combine(backup, "journal.json"))) ?? throw new InvalidDataException("Journal cập nhật rỗng.");
        if (!Path.GetFullPath(journal.Root).TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Journal thuộc thư mục Orbit khác.");
        // The snapshot is complete before the first replacement, so restoring every entry is always safe.
        Restore(root, backup, journal.Files);
        File.WriteAllText(Path.Combine(backup, "rolled-back.txt"), DateTime.UtcNow.ToString("O") + " recovered");
    }
    // Keeps the newest completed backup and any download still pending; never touches
    // incomplete journals or names that are not Orbit's GUID folders.
    public static int Cleanup(string root, string downloads, string? keepDownload)
    {
        int removed = 0;
        var backups = Path.Combine(Path.GetFullPath(root), ".orbit-backup");
        if (Directory.Exists(backups))
        {
            var finished = Directory.EnumerateDirectories(backups).Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _))
                .Where(d => File.Exists(Path.Combine(d, "completed.txt")) || File.Exists(Path.Combine(d, "rolled-back.txt")))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "journal.json"))).ToList();
            var newestCompleted = finished.FirstOrDefault(d => File.Exists(Path.Combine(d, "completed.txt")));
            foreach (var dir in finished.Where(d => d != newestCompleted)) removed += TryDelete(dir);
        }
        if (Directory.Exists(downloads))
            foreach (var dir in Directory.EnumerateDirectories(downloads).Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _)))
                if (!string.Equals(Path.GetFullPath(dir), keepDownload == null ? null : Path.GetFullPath(keepDownload), StringComparison.OrdinalIgnoreCase) && Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddMinutes(-10))
                    removed += TryDelete(dir);
        return removed;
    }
    private static int TryDelete(string dir)
    {
        try
        {
            // Do not follow junctions out of Orbit's folders.
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return 0;
            Directory.Delete(dir, true); return 1;
        }
        catch (IOException) { return 0; } catch (UnauthorizedAccessException) { return 0; }
    }
    public static void Restore(string root, string backup, IEnumerable<UpdateBackupFile> files)
    {
        foreach (var item in files)
        {
            var destination = UpdatePackage.SafePath(root, item.Path);
            if (item.Existed) File.Copy(UpdatePackage.SafePath(backup, "files/" + item.Path), destination, true);
            else if (File.Exists(destination)) File.Delete(destination);
        }
    }
}
