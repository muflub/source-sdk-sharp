namespace Debug.Api.Server.Interfaces;

/// <summary>
/// Everything the server serves its dev API, one property per group
/// (plan_devapi_interfaces §5.3). SourceSharp.Server builds one per server
/// composition and hands it to the dev API's constructor; two compositions
/// hand over two, which share nothing.
/// </summary>
public interface IDebugServerHooks
{
    /// <summary>The server's console, and a tap on every managed line (G9).</summary>
    IDebugLog Log { get; }

    /// <summary>The server's build and the crossing counters (G8).</summary>
    IDebugBuild Build { get; }

    /// <summary>Outputs as they fire (G4).</summary>
    IDebugEntityEvents EntityEvents { get; }

    /// <summary>Scripted entity classes and live instances (G3).</summary>
    IDebugEntityCatalog Catalog { get; }

    /// <summary>/status's counters and game state, the container, the current map (G7).</summary>
    IDebugStatus Status { get; }

    /// <summary>What the networked payload costs (G6).</summary>
    IDebugNetTraffic NetTraffic { get; }

    /// <summary>The server's console: commands, convars and the engine's table (G1).</summary>
    IDebugConsole Console { get; }

    /// <summary>The server's entities, players, world, clock and mod directory (G2).</summary>
    IDebugWorld World { get; }

    /// <summary>The networked schema, its tiers and refusals, and entity payloads (G5).</summary>
    IDebugNetSchema NetSchema { get; }

    /// <summary>Counter groups, as data: what the physics and player-shadow readouts print.</summary>
    IDebugCounterGroups CounterGroups { get; }

    /// <summary>The process's exit recorders: who asked it to end.</summary>
    IDebugProcess Process { get; }

    /// <summary>
    /// The managed command capture: a fresh instance of a declared command
    /// class, run with a sink for its print channels (user ruling
    /// 2026-10-07: command capture is all managed logic, no native code).
    /// </summary>
    IDebugCommandRun CommandRun { get; }
}
