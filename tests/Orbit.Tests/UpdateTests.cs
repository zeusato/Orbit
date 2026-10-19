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
