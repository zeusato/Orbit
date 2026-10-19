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
