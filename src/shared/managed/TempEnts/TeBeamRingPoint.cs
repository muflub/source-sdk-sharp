using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBeamRingPoint</c> — a ring of beam expanding around a point.
///
/// <para>
/// From <c>te_beamringpoint.cpp:90</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEBeamRingPoint, DT_TEBeamRingPoint)
///     SendPropVector( SENDINFO(m_vecCenter),     -1, SPROP_COORD ),
///     SendPropFloat(  SENDINFO(m_flStartRadius), 16, SPROP_ROUNDUP, 0.0f, 4096.0f ),
///     SendPropFloat(  SENDINFO(m_flEndRadius),   16, SPROP_ROUNDUP, 0.0f, 4096.0f ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// The busiest table in the lane by call site count — <c>prop_combine_ball</c>,
/// <c>npc_strider</c>, <c>npc_combinegunship</c>, <c>func_tank</c>,
/// <c>grenade_homer</c> and the vortigaunt all fire one — though every one of
/// those files is excluded from this build, so the caller that survives is
/// <c>te-&gt;BeamRingPoint</c> itself.
/// </para>
///
/// <para>
/// <c>SPROP_ROUNDUP</c> IS NOT DECORATION. It shifts the encoded range so the
/// high end is representable rather than the low end (<c>dt_send.cpp</c>'s
/// range setup), and a radius that decoded a quantum short of 4096 would be a
/// ring that never quite closes. It is carried as a flag on both sides because
/// both sides read the same declaration.
/// </para>
/// </summary>
[TempEntity]
public class TeBeamRingPoint : TeBaseBeam
{
    /// <summary>The point the ring is centred on.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Center;

    /// <summary>The radius it starts at: sixteen bits, rounded up, over [0, 4096].</summary>
    [TeProp(Bits = 16, Flags = TeFlags.RoundUp, Low = 0.0f, High = 4096.0f)]
    public float StartRadius;

    /// <summary>The radius it grows to. Same encoding.</summary>
    [TeProp(Bits = 16, Flags = TeFlags.RoundUp, Low = 0.0f, High = 4096.0f)]
    public float EndRadius;
}
