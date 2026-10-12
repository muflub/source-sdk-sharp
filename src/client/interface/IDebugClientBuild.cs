namespace SourceSharp.Client.Interface;

/// <summary>
/// One half of a build (native or managed) as the dev API reports it
/// (group G8). Server.Interface carries the same shape; DebugApiShapeTests holds the
/// two equal, field by field (plan_devapi_interfaces §5.8, Q2).
/// </summary>
/// <param name="Known">Whether a stamp was found at all.</param>
/// <param name="Hash">The build hash; empty when unknown.</param>
/// <param name="BuiltAtUnixMs">When it was built, in Unix milliseconds; 0 when unknown.</param>
/// <param name="Parts">How many files the stamp covers.</param>
/// <param name="Branch">The branch it was built from; "(detached)" for a detached HEAD, empty when unknown.</param>
/// <param name="Commit">The commit it was built from; empty when unknown.</param>
/// <param name="Dirty">Whether the tree had uncommitted work; null when git could not say.</param>
/// <param name="Source">Branch, commit and dirt in one string, e.g. <c>main@45e801f2+dirty</c>.</param>
/// <param name="Freshness">"unknown", "current" or "stale": whether something newer was built since it loaded.</param>
/// <param name="Path">The file or directory it was read from; empty when unknown.</param>
public sealed record DebugBuildHalf(
    bool Known, string Hash, long BuiltAtUnixMs, int Parts, string Branch, string Commit,
    bool? Dirty, string Source, string Freshness, string Path);

/// <summary>This side's build: both halves.</summary>
public sealed record DebugBuildInfo(DebugBuildHalf Native, DebugBuildHalf Managed);

/// <summary>A named count.</summary>
public readonly record struct DebugCount(string Name, long Count);

/// <summary>
/// The inbound crossing counters (group G8). Process-wide today; the same shape is
/// in Server.Interface (Q2).
/// </summary>
public sealed record DebugCrossings(
    long Total, long Resets, int OpCount, long ServerFrames, long ClientFrames,
    IReadOnlyList<DebugCount> Ops, IReadOnlyList<DebugCount> EntityOps);

/// <summary>Which build the client runs, and what has crossed into it (P3).</summary>
public interface IDebugClientBuild
{
    /// <summary>The client's native and managed build.</summary>
    DebugBuildInfo Build();

    /// <summary>The crossing counters, with only the operations that have crossed.</summary>
    DebugCrossings Crossings();

    /// <summary>The client's managed build in one line (time and hash), for the channel's hello.</summary>
    string ManagedStamp();

    /// <summary>The user messages the client has received, by name, with how many; only those received.</summary>
    IReadOnlyList<DebugCount> UserMessagesReceived();

    /// <summary>The game events the client has received (each claimed one dispatched), by name, with how many.</summary>
    IReadOnlyList<DebugCount> GameEventsReceived();
}
