// Isolated updater test fixture: no WPF, hooks, registry, settings or user files.
var healthIndex = Array.IndexOf(args, "--update-health");
if (healthIndex >= 0)
{
    if (File.Exists(Path.Combine(AppContext.BaseDirectory, "fail-health.flag"))) return 7;
    using var health = EventWaitHandle.OpenExisting("Local\\Orbit.Update.Health." + args[healthIndex + 1]); health.Set();
    Thread.Sleep(1500); return 0;
}
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe-ready.txt"), Environment.ProcessId.ToString());
var deadline = DateTime.UtcNow.AddSeconds(90);
while (!File.Exists(Path.Combine(AppContext.BaseDirectory, "probe-exit.txt")) && DateTime.UtcNow < deadline) Thread.Sleep(50);
return 0;
