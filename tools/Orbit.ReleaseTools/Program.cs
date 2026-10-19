using System.Security.Cryptography;
using System.Text.Json;

if (args.Length == 3 && args[0] == "init")
{
    if (File.Exists(args[1]) || File.Exists(args[2])) throw new IOException("Signing key already exists; never rotate implicitly.");
    using var rsa = RSA.Create(3072);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllText(args[1], rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(args[2], rsa.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine("Created private signing key and public verification key. Back up the private key securely.");
}
else if (args.Length == 5 && args[0] == "sign")
{
    using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(args[1]));
    var zip = new FileInfo(args[2]);
    if (!Version.TryParse(args[3], out _)) throw new ArgumentException("Invalid version");
    using var file = zip.OpenRead();
    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, version = args[3], asset = zip.Name, size = zip.Length, sha256 = Convert.ToHexString(SHA256.HashData(file)) });
    File.WriteAllBytes(args[4], bytes);
    File.WriteAllText(args[4] + ".sig", Convert.ToBase64String(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
    Console.WriteLine("Signed manifest: " + args[4]);
}
else if (args.Length == 4 && args[0] == "verify")
{
    using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(args[3]));
    if (!rsa.VerifyData(File.ReadAllBytes(args[1]), Convert.FromBase64String(File.ReadAllText(args[2])), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new CryptographicException("Signing key does not match the app public key.");
    Console.WriteLine("Manifest signature matches the app public key.");
}
else if (args.Length == 3 && args[0] == "runner")
{
    // Same copy the app performs before applying an update.
    Orbit.Core.UpdatePackage.CopyRunner(args[1], args[2]);
    Console.WriteLine("Copied updater runner: " + Orbit.Core.UpdatePackage.RunnerFiles(args[1]).Length + " files");
}
else throw new ArgumentException("init <private.pem> <public.pem> | sign <private.pem> <zip> <version> <manifest.json> | verify <manifest> <sig> <public.pem> | runner <install> <runner>");
