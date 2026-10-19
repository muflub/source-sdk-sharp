namespace Debug.Api.Server.Interfaces;

/// <summary>The think scheduler (group G7).</summary>
public sealed record DebugThink(int Pending, int HeapDepth, int RanLastFrame, long RanTotal);

/// <summary>The entity I/O queue (group G7).</summary>
public sealed record DebugIo(
    int Pending, int Connections, int DeliveredLastFrame, long DeliveredTotal, long Unhandled,
    int CyclesRefused, int UnhandledDistinct, long PostedAfterThePass, long SdkAsks, long SdkTook);

/// <summary>Touch links and the overlap pass (group G7).</summary>
public sealed record DebugTouch(int Pairs, int Links, int EdgesLastFrame, long EdgesTotal, int Capacity, int Regrows, int Refused);

/// <summary>The last map spawn (group G7).</summary>
public sealed record DebugMapSpawn(int Blocks, int ManagedBlocks, int NativeBlocks, int ManagedKeys, int CreateCrossings, int SpawnCrossings);

/// <summary>The entity factory dictionary (group G7). Available false means it was never asked.</summary>
public sealed record DebugFactories(bool Available, int Native, int Refused, int RefusalTimes, int Unanswered);

/// <summary>Shell-less (logical) entities (group G7).</summary>
public sealed record DebugLogical(int Classes, int Live, long Allocated, long Released, long Exhausted, int NativeReach);

/// <summary>The debug overlay's last frame (group G7).</summary>
public sealed record DebugOverlayCounts(uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet);

/// <summary>The server's counters /status reports, readable from any thread (group G7).</summary>
public sealed record DebugCounters(
    DebugThink Think, DebugIo Io, DebugTouch Touch, DebugMapSpawn MapSpawn, DebugFactories Factories,
    DebugLogical Logical, DebugOverlayCounts Overlay);

/// <summary>What one managed reload carried (group G7).</summary>
public sealed record DebugReloadTally(
    int Entities, int Classes, int Fields, int Bytes, int Parts, int PartFields, int PartBytes,
    int IoEvents, int IoConnections, int ShellLess);

/// <summary>The server's last managed reload: saved, restored, failed and still waiting (group G7).</summary>
public sealed record DebugReload(
    DebugReloadTally Saved, DebugReloadTally Restored, int FailedEntities, string? FailedFirst,
    int WaitingEntities, int WaitingParts, int WaitingIo, int WaitingShellLess);

/// <summary>What only the game thread may read (group G7).</summary>
public sealed record DebugGameStatus(string Map, int Ticks, int Constraints, int Springs, int MotionControllers);

/// <summary>One registration in the server's container (group G7).</summary>
/// <param name="LookedUpBy">"type" or "name".</param>
/// <param name="Kind">"instance", "lazy", "self", ...</param>
public sealed record DebugService(string Key, string LookedUpBy, string Kind, string Implementation, bool Built);

/// <summary>The server's state for /status, its services, and its current map (group G7).</summary>
public interface IDebugStatus
{
    /// <summary>The counters; any thread.</summary>
    DebugCounters Counters();

    /// <summary>The last managed reload; any thread.</summary>
    DebugReload Reload();

    /// <summary>The map, ticks and physics counts; on the game thread only.</summary>
    DebugGameStatus Game();

    /// <summary>Every registration in the server's container, described without being built.</summary>
    IReadOnlyList<DebugService> Services();

    /// <summary>The map the server has loaded; empty when none.</summary>
    string CurrentMap { get; }
}
