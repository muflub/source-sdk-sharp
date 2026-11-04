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

/// <summary>The leaf system's ConVars for its decisions.</summary>
public sealed class LeafListConVars(ClDrawLeafConVar drawLeaf, PortalTestEntsConVar portalTestEnts, RPortalsOpenAllConVar portalsOpenAll)
    : ILeafListSettings
{
    public bool PortalTestEnts => portalTestEnts.Value;

    public bool PortalsOpenAll => portalsOpenAll.Value;

    public int DrawLeaf => drawLeaf.Value;
}
