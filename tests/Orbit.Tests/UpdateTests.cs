using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orbit.Core;
using Orbit.App.Services;

internal static partial class Program
{
    private static void CheckUpdates()
    {
        using var rsa = RSA.Create(2048); var key = rsa.ExportSubjectPublicKeyInfoPem();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new UpdateManifest(1, "0.4.1", "Orbit-0.4.1-win-x64.zip", 123, new string('A', 64)));
        var signature = Convert.ToBase64String(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        Check("Update manifests require trusted signature and valid version/schema", () =>
        {
            Assert(UpdatePackage.VerifyManifest(bytes, signature, key).Version == "0.4.1", "trusted manifest");
            var changed = bytes.ToArray(); changed[10] ^= 1;
            Throws<CryptographicException>(() => UpdatePackage.VerifyManifest(changed, signature, key));
            using var other = RSA.Create(2048);
            Throws<CryptographicException>(() => UpdatePackage.VerifyManifest(bytes, signature, other.ExportSubjectPublicKeyInfoPem()));
            foreach (var bad in new[] { "v0.4.0", "0.4.0-beta", "../0.4.0", "0.4", "0.4.0.1" }) Throws<InvalidDataException>(() => UpdatePackage.ParseVersion(bad));
            Assert(UpdatePackage.ParseVersion("0.10.0") > UpdatePackage.ParseVersion("0.9.9"), "numeric version ordering");
            Assert(UpdatePackage.PublicKey.Contains("BEGIN PUBLIC KEY") && !UpdatePackage.PublicKey.Contains("PRIVATE"), "only public key embedded");
        });
        Check("Updates reject corrupt ZIP hash and size", () =>
        {
            var zip = Path.Combine(_folder, "hash-test.zip"); File.WriteAllText(zip, "good");
            var m = new UpdateManifest(1, "0.4.1", "Orbit-0.4.1-win-x64.zip", 4, Hash(zip)); UpdatePackage.VerifyArchive(zip, m);
            File.WriteAllText(zip, "evil"); Throws<CryptographicException>(() => UpdatePackage.VerifyArchive(zip, m));
            File.WriteAllText(zip, "shorter-or-longer"); Throws<CryptographicException>(() => UpdatePackage.VerifyArchive(zip, m));
        });
        Check("Update paths reject traversal, Windows aliases, devices and ADS", () =>
        {
            foreach (var path in new[] { "../bad", "a/../../bad", "C:/bad", "/bad", "a//b", "a/CON.txt", "a/LPT1", "a/file:ads", "trailing./x", "a /b", "\\\\server\\share", "a/\nb" })
                Throws<InvalidDataException>(() => UpdatePackage.SafePath(_folder, path));
            Assert(UpdatePackage.SafePath(_folder, "x64/library.dll").StartsWith(_folder), "normal nested path");
        });
        Check("Update archives reject unsafe or incomplete bundles before extraction", () =>
        {
            foreach (var name in new[] { "Orbit/../outside.txt", "Other/file.txt", "Orbit/settings.json", "Orbit/.orbit-backup/file", "Orbit/missing-required.txt" })
            {
                var zip = Path.Combine(_folder, Guid.NewGuid() + ".zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write("test"); }
                var stage = Path.Combine(_folder, Guid.NewGuid().ToString()); Throws<InvalidDataException>(() => UpdatePackage.Extract(zip, stage));
                Assert(!Directory.Exists(stage), "validate archive before writing");
            }
        });
        Check("Update transaction preserves user files and backs up replaced files", () =>
        {
            var root = Path.Combine(_folder, "install-good"); var stage = Path.Combine(_folder, "stage-good"); var backup = Path.Combine(_folder, "backup-good"); Directory.CreateDirectory(root); Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(root, "Orbit.exe"), "old"); File.WriteAllText(Path.Combine(root, "notes.txt"), "user data");
            File.WriteAllText(Path.Combine(stage, "Orbit.exe"), "new"); File.WriteAllText(Path.Combine(stage, "new.dll"), "new dependency");
            UpdateTransaction.Install(root, stage, ["Orbit.exe", "new.dll"], backup, () => File.ReadAllText(Path.Combine(root, "Orbit.exe")) == "new");
            Assert(File.ReadAllText(Path.Combine(root, "notes.txt")) == "user data", "unrelated user file survives");
            Assert(File.ReadAllText(Path.Combine(backup, "files", "Orbit.exe")) == "old" && File.Exists(Path.Combine(backup, "completed.txt")), "backup retained");
        });
        Check("Update startup failure rolls back overwritten and newly added files", () =>
        {
            var root = Path.Combine(_folder, "install-failed"); var stage = Path.Combine(_folder, "stage-failed"); var backup = Path.Combine(_folder, "backup-failed"); Directory.CreateDirectory(root); Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(root, "Orbit.exe"), "old"); File.WriteAllText(Path.Combine(stage, "Orbit.exe"), "broken"); File.WriteAllText(Path.Combine(stage, "new.dll"), "temporary");
            Throws<IOException>(() => UpdateTransaction.Install(root, stage, ["Orbit.exe", "new.dll"], backup, () => false));
            Assert(File.ReadAllText(Path.Combine(root, "Orbit.exe")) == "old" && !File.Exists(Path.Combine(root, "new.dll")), "restored exact original state");
            Assert(File.Exists(Path.Combine(backup, "rolled-back.txt")), "rollback marker");
        });
        Check("Partial update copy failure restores earlier files", () =>
        {
            var root = Path.Combine(_folder, "install-partial"); var stage = Path.Combine(_folder, "stage-partial"); var backup = Path.Combine(_folder, "backup-partial"); Directory.CreateDirectory(root); Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(root, "Orbit.exe"), "old"); File.WriteAllText(Path.Combine(stage, "Orbit.exe"), "new");
            Throws<IOException>(() => UpdateTransaction.Install(root, stage, ["Orbit.exe", "missing.dll"], backup, () => true));
            Assert(File.ReadAllText(Path.Combine(root, "Orbit.exe")) == "old", "partial apply restored");
            Assert(!Directory.EnumerateFiles(root, "*.orbit-new-*").Any(), "no staged fragments");
        });
        Check("Updater runner copies helper with every runtime asset from deps.json", () =>
        {
            var root = Path.Combine(_folder, "runner-root"); var runner = Path.Combine(_folder, "runner-copy"); Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Orbit.Updater.deps.json"), "{\"targets\":{\".NETCoreApp,Version=v10.0/win-x64\":{\"Orbit.Updater/1.0.0\":{\"runtime\":{\"Orbit.Updater.dll\":{}}},\"runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.0\":{\"runtime\":{\"System.Runtime.dll\":{}},\"native\":{\"hostfxr.dll\":{},\"coreclr.dll\":{}}}}}}");
            foreach (var name in new[] { "Orbit.Updater.exe", "Orbit.Updater.dll", "Orbit.Updater.runtimeconfig.json", "Orbit.Core.dll", "System.Runtime.dll", "hostfxr.dll", "Orbit.exe", "Magick.Native-Q8-x64.dll" }) File.WriteAllText(Path.Combine(root, name), name);
            Throws<FileNotFoundException>(() => UpdatePackage.RunnerFiles(root));
            File.WriteAllText(Path.Combine(root, "coreclr.dll"), "runtime");
            UpdatePackage.CopyRunner(root, runner);
            foreach (var name in new[] { "Orbit.Updater.exe", "Orbit.Updater.deps.json", "Orbit.Core.dll", "System.Runtime.dll", "hostfxr.dll", "coreclr.dll" }) Assert(File.Exists(Path.Combine(runner, name)), "runner has " + name);
            Assert(!File.Exists(Path.Combine(runner, "Orbit.exe")) && !File.Exists(Path.Combine(runner, "Magick.Native-Q8-x64.dll")), "app-only files stay in install folder");
        });
        Check("Interrupted update journal is found once and restores the full snapshot", () =>
        {
            var root = Path.Combine(_folder, "install-interrupted"); var id = Guid.NewGuid().ToString("N"); var backup = Path.Combine(root, ".orbit-backup", id);
            Directory.CreateDirectory(Path.Combine(backup, "files"));
            File.WriteAllText(Path.Combine(root, "Orbit.exe"), "new-half"); File.WriteAllText(Path.Combine(root, "added.dll"), "new only"); File.WriteAllText(Path.Combine(root, "notes.txt"), "user");
            File.WriteAllText(Path.Combine(backup, "files", "Orbit.exe"), "old");
            File.WriteAllText(Path.Combine(backup, "journal.json"), JsonSerializer.Serialize(new UpdateJournal(root, [new("Orbit.exe", true), new("added.dll", false)])));
            Assert(UpdateTransaction.FindIncomplete(root).Single() == backup, "incomplete journal detected");
            UpdateTransaction.Recover(root, backup);
            Assert(File.ReadAllText(Path.Combine(root, "Orbit.exe")) == "old" && !File.Exists(Path.Combine(root, "added.dll")) && File.ReadAllText(Path.Combine(root, "notes.txt")) == "user", "old state restored, user file kept");
            Assert(UpdateTransaction.FindIncomplete(root).Length == 0, "marked as rolled back");
            var other = Path.Combine(_folder, "install-other"); Directory.CreateDirectory(other);
            Throws<InvalidDataException>(() => UpdateTransaction.Recover(other, backup));
        });
        Check("Update cleanup keeps newest completed backup, pending and recent downloads", () =>
        {
            var root = Path.Combine(_folder, "install-cleanup"); var downloads = Path.Combine(_folder, "downloads-cleanup");
            string Backup(string marker, int ageMinutes)
            {
                var dir = Path.Combine(root, ".orbit-backup", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "journal.json"), "{}"); File.SetLastWriteTimeUtc(Path.Combine(dir, "journal.json"), DateTime.UtcNow.AddMinutes(-ageMinutes));
                if (marker.Length > 0) File.WriteAllText(Path.Combine(dir, marker), "x"); return dir;
            }
            string Download(int ageMinutes) { var dir = Path.Combine(downloads, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "package.zip"), "zip"); Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddMinutes(-ageMinutes)); return dir; }
            var oldDone = Backup("completed.txt", 300); var newDone = Backup("completed.txt", 5); var rolled = Backup("rolled-back.txt", 1); var broken = Backup("", 600);
            var foreign = Path.Combine(root, ".orbit-backup", "keep-me"); Directory.CreateDirectory(foreign);
            var stale = Download(120); var pending = Download(240); var fresh = Download(1); var other = Path.Combine(downloads, "not-a-guid"); Directory.CreateDirectory(other);
            Assert(UpdateTransaction.Cleanup(root, downloads, pending) == 3, "removed old completed, rolled-back and stale download");
            Assert(!Directory.Exists(oldDone) && !Directory.Exists(rolled) && !Directory.Exists(stale), "superseded removed");
            Assert(Directory.Exists(newDone) && Directory.Exists(broken) && Directory.Exists(foreign) && Directory.Exists(pending) && Directory.Exists(fresh) && Directory.Exists(other), "kept latest, incomplete, foreign, pending and in-progress");
        });
        Check("Release assets are restricted to the configured repository and exact version", () =>
        {
            using var good = JsonDocument.Parse("{\"assets\":[{\"name\":\"orbit-update.json\",\"browser_download_url\":\"https://github.com/zeusato/Orbit/releases/download/v0.4.1/orbit-update.json\"}]}");
            Assert(UpdateService.AssetUri(good.RootElement, "orbit-update.json", "0.4.1").Scheme == "https", "official asset");
            Throws<InvalidDataException>(() => UpdateService.AssetUri(good.RootElement, "orbit-update.json", "0.4.2"));
            using var bad = JsonDocument.Parse("{\"assets\":[{\"name\":\"orbit-update.json\",\"browser_download_url\":\"https://evil.example/update\"}]}");
            Throws<InvalidDataException>(() => UpdateService.AssetUri(bad.RootElement, "orbit-update.json", "0.4.1"));
        });
    }
}
