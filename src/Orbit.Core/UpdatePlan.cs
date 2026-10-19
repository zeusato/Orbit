namespace Orbit.Core;
public sealed record UpdatePlan(string Root, int ParentPid, long ParentStartTicks, string Id);
