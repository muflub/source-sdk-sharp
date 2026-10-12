using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEDecal</c> — the bullet-hole decal, traced from a start point onto
/// whatever stopped it.
///
/// <para>
/// From <c>te_decal.cpp:98</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEDecal, DT_TEDecal)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecStart), -1, SPROP_COORD),
///     SendPropInt( SENDINFO(m_nEntity), MAX_EDICT_BITS, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nHitbox), 12, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nIndex), 9, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// TWO POINTS, NOT ONE, and they are not interchangeable: the client re-traces
/// from <c>m_vecStart</c> to <c>m_vecOrigin</c> to find the surface and its
/// texture. Swapping them puts the decal on the shooter's side of the wall.
/// </para>
///
/// <para>
/// LEDGER ROW C2 IS THE CLIENT'S AND IS STILL OPEN. It is where a decal aimed
/// at an ENTITY is dropped, because <c>C_StandaloneEntity</c> declares no
/// <c>AddDecal</c> path. Lane CX has since ported the client half —
/// <c>ClientTeDecal</c> — so the row's arm is
/// <c>ClientTeDecal.EntityDecalIsMissing</c> rather than
/// <c>c_te_decal.cpp:157</c>, and it now WARNS once per level instead of
/// returning in silence. The static-prop arm draws through
/// <c>ClientDecalStaticProp</c>. Porting either half changes none of the
/// capability: the row stays open and tracked until a managed client decal API
/// closes it (plan 1.2 C2).
/// </para>
/// </summary>
[TempEntity]
public class TeDecal : TeBaseTempEntity
{
    /// <summary>Where the trace ended, which is where the mark lands.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>Where the trace began. The client re-traces this segment.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Start;

    /// <summary>The edict that was hit, or 0 for the world.</summary>
    [TeProp(BitsFrom = TeBits.MaxEdict, Flags = TeFlags.Unsigned)]
    public int Entity;

    /// <summary>Which hitbox of that entity, for a studio decal. Twelve bits unsigned.</summary>
    [TeProp(Bits = 12, Flags = TeFlags.Unsigned)]
    public int Hitbox;

    /// <summary>The engine's decal index. Nine bits unsigned.</summary>
    [TeProp(Bits = 9, Flags = TeFlags.Unsigned)]
    public int Index;
}
