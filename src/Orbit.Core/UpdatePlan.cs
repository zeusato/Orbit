namespace Orbit.Core;
// Foreground: reopen the main window after a recovery the user started by opening Orbit.
public sealed record UpdatePlan(string Root, int ParentPid, long ParentStartTicks, string Id, bool Foreground = false);
