using SourceSharp.Abi;
using SourceSharp.Client.Flashlight;

namespace SourceSharp.Client.Render.LeafSystem;

// clientleafsystem.cpp's ConVars, declared here since the file left client.so
// (C7 cluster 12 L2): names, defaults, flags and help as stock declared them
// (LeafSystemConVarOracleTests holds each to the pristine file). Not here:
// r_portalsopenall, which the engine declares too (stock's
// client copy was a child of it), read by name through the convar watch.

/// <summary>cl_drawleaf: a debug build's filter, the one world-list leaf whose renderables are drawn (-1 all).</summary>
[ConVar("cl_drawleaf", "-1", Flags = ConVarFlags.Cheat)]
public sealed class ClDrawLeafConVar : IntConVar { }

/// <summary>r_PortalTestEnts: a renderable inside one area is culled against that area's portal frustum.</summary>
[ConVar("r_PortalTestEnts", "1", Flags = ConVarFlags.Cheat, Help = "Clip entities against portal frustums.")]
public sealed class PortalTestEntsConVar : BoolConVar { }

/// <summary>Declared by stock and read by nothing (its threaded arms are compiled out).</summary>
[ConVar("cl_threaded_client_leaf_system", "0")]
public sealed class ThreadedClientLeafSystemConVar : BoolConVar { }

/// <summary>
/// The leaf system's ConVars for its decisions: its own declarations, and
/// r_portalsopenall through the convar watch (the module routes the watch's
/// changes to <see cref="OnChanged"/>).
/// </summary>
public sealed class LeafListConVars : ILeafListSettings
{
    /// <summary>The ConVars the leaf system reads but does not declare.</summary>
    public static string[] EngineNames() => ["r_portalsopenall"];

    private readonly ClDrawLeafConVar _drawLeaf;
    private readonly PortalTestEntsConVar _portalTestEnts;
    private readonly WatchedConVarValues _engine;

    public LeafListConVars(ConVarRegistry convars, WatchedConVarValues engine)
    {
        _drawLeaf = convars.Get<ClDrawLeafConVar>() ?? throw new InvalidOperationException("cl_drawleaf is not registered");
        _portalTestEnts = convars.Get<PortalTestEntsConVar>() ?? throw new InvalidOperationException("r_PortalTestEnts is not registered");
        _engine = engine;
    }

    /// <summary>Watches the one it reads by name.</summary>
    public void Start() => _engine.Watch(EngineNames());

    public bool OnChanged(int id, string newValue) => _engine.OnChanged(id, newValue);

    public bool PortalTestEnts => _portalTestEnts.Value;

    public bool PortalsOpenAll => _engine.Bool("r_portalsopenall");

    public int DrawLeaf => _drawLeaf.Value;
}
