using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Orbit.Core;

if (args.Length != 1 || args[0] is not ("--apply" or "--recover")) return 2;
var work = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
if (args[0] == "--recover") return Recovery.Run(work);
string? root = null;
Process? child = null;
bool parentExited = false, attempted = false;
try
{
    var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(Path.Combine(work, "request.json"))) ?? throw new InvalidDataException("Missing request");
    if (!Guid.TryParseExact(plan.Id, "N", out _)) throw new InvalidDataException("Invalid update ID");
    using var parent = Process.GetProcessById(plan.ParentPid);
    var parentExe = parent.MainModule?.FileName ?? throw new IOException("Missing parent image");
    if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTicks || !Path.GetFileName(parentExe).Equals("Orbit.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Parent identity changed");
    root = Path.GetDirectoryName(parentExe)!;
    if (!Path.GetFullPath(plan.Root).TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid installation root");
    _ = UpdatePackage.SafePath(root, "Orbit.exe");
    using var updateLock = new FileStream(Path.Combine(root, ".orbit-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var manifest = UpdatePackage.VerifyManifest(File.ReadAllBytes(Path.Combine(work, "manifest.json")), File.ReadAllText(Path.Combine(work, "manifest.sig")));
    var current = AssemblyName.GetAssemblyName(Path.Combine(root, "Orbit.dll")).Version!;
    if (UpdatePackage.ParseVersion(manifest.Version) <= new Version(current.Major, current.Minor, current.Build)) throw new InvalidDataException("Refusing downgrade");
    var zip = Path.Combine(work, "package.zip"); UpdatePackage.VerifyArchive(zip, manifest);
    var stage = Path.Combine(work, "stage"); var files = UpdatePackage.Extract(zip, stage);
    var stagedVersion = AssemblyName.GetAssemblyName(Path.Combine(stage, "Orbit.dll")).Version!;
    if (new Version(stagedVersion.Major, stagedVersion.Minor, stagedVersion.Build) != UpdatePackage.ParseVersion(manifest.Version)) throw new InvalidDataException("Payload version mismatch");
    using (var ready = EventWaitHandle.OpenExisting("Local\\Orbit.Update.Ready." + plan.Id)) ready.Set();
    if (!parent.WaitForExit(90000)) throw new IOException("Orbit did not exit; update deferred");
    parentExited = true;
    var backup = Path.Combine(root, ".orbit-backup", plan.Id);
    attempted = true;
    UpdateTransaction.Install(root, stage, files, backup, () =>
    {
        bool confirmed = false;
        try
        {
        using var healthy = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\Orbit.Update.Health." + plan.Id);
        var start = new ProcessStartInfo(Path.Combine(root, "Orbit.exe")) { UseShellExecute = false, WorkingDirectory = root };
        start.ArgumentList.Add("--background"); start.ArgumentList.Add("--update-health"); start.ArgumentList.Add(plan.Id);
        child = Process.Start(start) ?? throw new IOException("Cannot start updated Orbit");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(45))
        {
            if (child.HasExited) return false;
            if (healthy.WaitOne(100)) { confirmed = true; return true; }
        }
        return false;
        }
        finally { if (!confirmed && child != null && !child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
    });
    File.WriteAllText(Path.Combine(root, ".orbit-update-result.json"), JsonSerializer.Serialize(new { success = true, version = manifest.Version, backup, time = DateTime.UtcNow }));
    return 0;
}
catch (Exception error)
{
    try
    {
        File.WriteAllText(Path.Combine(work, "update-error.txt"), error.ToString());
        if (root != null && parentExited)
        {
            File.WriteAllText(Path.Combine(root, ".orbit-update-result.json"), JsonSerializer.Serialize(new { success = false, error = error.Message, time = DateTime.UtcNow }));
            if (child != null && !child.HasExited) { child.Kill(); child.WaitForExit(5000); }
            // Install only returns after success or restoration. If restoration itself failed,
            // retain the complete journal/backup for recovery rather than claiming success.
            if (attempted) Process.Start(new ProcessStartInfo(Path.Combine(root, "Orbit.exe")) { UseShellExecute = false, WorkingDirectory = root, Arguments = "--background" });
        }
    }
    catch { }
    return 1;
}
finally { child?.Dispose(); }

// Restores a full backup after an update was interrupted mid-replacement. Orbit detects the
// incomplete journal at startup, hands over to this copy outside the install folder and exits.
static class Recovery
{
    public static int Run(string work)
    {
        string? root = null; UpdatePlan? plan = null; bool parentExited = false;
        try
        {
            plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(Path.Combine(work, "request.json"))) ?? throw new InvalidDataException("Missing request");
            if (!Guid.TryParseExact(plan.Id, "N", out _)) throw new InvalidDataException("Invalid backup ID");
            using var parent = Process.GetProcessById(plan.ParentPid);
            var parentExe = parent.MainModule?.FileName ?? throw new IOException("Missing parent image");
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTicks || !Path.GetFileName(parentExe).Equals("Orbit.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Parent identity changed");
            root = Path.GetDirectoryName(parentExe)!;
            if (!Path.GetFullPath(plan.Root).TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid installation root");
            var backup = UpdatePackage.SafePath(root, ".orbit-backup/" + plan.Id);
            if (!UpdateTransaction.FindIncomplete(root).Any(d => Path.GetFullPath(d).Equals(backup, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Backup is not an incomplete update");
            using var updateLock = new FileStream(Path.Combine(root, ".orbit-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using (var ready = EventWaitHandle.OpenExisting("Local\\Orbit.Update.Ready." + plan.Id)) ready.Set();
            if (!parent.WaitForExit(90000)) throw new IOException("Orbit did not exit; recovery deferred");
            parentExited = true;
            // One automatic attempt only: Orbit will not hand over this backup again after a failure.
            File.WriteAllText(Path.Combine(backup, "recovery-attempted.txt"), DateTime.UtcNow.ToString("O"));
            UpdateTransaction.Recover(root, backup);
            File.WriteAllText(Path.Combine(root, ".orbit-update-result.json"), JsonSerializer.Serialize(new { success = false, recovered = true, backup, time = DateTime.UtcNow }));
            updateLock.Dispose();
            Start(root, plan.Foreground);
            return 0;
        }
        catch (Exception error)
        {
            try { File.WriteAllText(Path.Combine(work, "update-error.txt"), error.ToString()); } catch { }
            try { if (parentExited && root != null) Start(root, plan?.Foreground == true); } catch { }
            return 1;
        }
    }
    private static void Start(string root, bool foreground)
    {
        var start = new ProcessStartInfo(Path.Combine(root, "Orbit.exe")) { UseShellExecute = false, WorkingDirectory = root };
        if (!foreground) start.ArgumentList.Add("--background");
        Process.Start(start)?.Dispose();
    }
}
