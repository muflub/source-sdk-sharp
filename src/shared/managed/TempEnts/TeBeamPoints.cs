using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBeamPoints</c> — a beam between two points in the world.
///
/// <para>
/// From <c>te_beampoints.cpp:97</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEBeamPoints, DT_TEBeamPoints)
///     SendPropVector( SENDINFO(m_vecStartPoint), -1, SPROP_COORD ),
///     SendPropVector( SENDINFO(m_vecEndPoint),   -1, SPROP_COORD ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// THE ONE IN THIS LANE WITH LIVE NATIVE CALLERS, and the reason the forwarding
/// shim is not optional. <c>CEffectsServer::Beam</c>
/// (<c>EffectsServer.cpp:113</c>) and <c>UTIL_Beam</c> (<c>util.cpp:2172</c>)
/// both reach it through <c>te-&gt;BeamPoints</c>, and both files are on this
/// build's link line. After this port the last link of that chain is the managed
/// table instead of the native one; nothing in between changed and nothing
/// stopped.
/// </para>
/// </summary>
[TempEntity]
public class TeBeamPoints : TeBaseBeam
{
    /// <summary>Where the beam starts, as a world coordinate.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector StartPoint;

    /// <summary>Where it ends.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector EndPoint;
}
