namespace Debug.Api.Server.Interfaces;

/// <summary>One named number in a counter group.</summary>
public sealed record DebugCounter(string Name, long Value);

/// <summary>
/// A group of counters, read from the same sources a console readout prints,
/// so a caller asserts on numbers rather than regex-parsing the readout's text.
/// </summary>
/// <param name="Name">The group, as /counters?group= names it.</param>
/// <param name="Readout">The console command that prints the same numbers, for a reader comparing the two.</param>
/// <param name="Available">False when the source is absent (no level, a build without it); the counters are then empty.</param>
public sealed record DebugCounterGroup(string Name, string Readout, bool Available, IReadOnlyList<DebugCounter> Counters);

/// <summary>
/// The counter groups the server serves its dev API: what
/// <c>cs_sv_physics_events</c> and <c>cs_sv_playershadow</c> print, as data.
/// Read on the game thread.
/// </summary>
public interface IDebugCounterGroups
{
    /// <summary>The group names, in a stable order.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>One group, or null for a name no group has.</summary>
    DebugCounterGroup? Read(string name);
}
