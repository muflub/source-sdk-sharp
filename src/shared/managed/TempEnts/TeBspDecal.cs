using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBSPDecal</c> — the sprayed map decal, stamped onto brushwork.
///
/// <para>
/// From <c>te_bspdecal.cpp:96</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEBSPDecal, DT_TEBSPDecal)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropInt( SENDINFO(m_nEntity), MAX_EDICT_BITS, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nIndex), 9, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <c>m_nEntity</c> IS <see cref="TeBits.MaxEdict"/> AND NOT AN ELEVEN. The two
/// resolve to the same number today and that is exactly why it matters: the
/// symbol travels and native reads <c>MAX_EDICT_BITS</c> out of
/// <c>public/const.h</c>, so a build that raises the edict limit moves both
/// sides at once. <c>DT_TEFootprintDecal</c> in this same lane writes a literal
/// 11 because stock's own table does, and the two must not be unified — a prop
/// one bit narrower than the other side expects is a bitstream that stops making
/// sense partway through, with no log line anywhere (plan 3.1).
/// </para>
///
/// <para>
/// It has a live caller today: <c>infodecal</c> without an angles key, through
/// <c>IEngineEffects.PaintDecal</c> → <c>SourceSharp_PaintDecal</c> →
/// <c>te-&gt;BSPDecal</c> → <c>TE_BSPDecal</c>, whose last link is now this
/// table (<c>shell_te_fwd_b.cpp</c>).
/// </para>
/// </summary>
[TempEntity("CTEBSPDecal", "DT_TEBSPDecal")]
public class TeBspDecal : TeBaseTempEntity
{
    /// <summary>Where the mark goes, in world space. One COORD vector prop.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// The edict the decal belongs to, or 0 for the world. An INDEX and not a
    /// handle — which is the whole reason this effect survives the parallel
    /// hierarchy where <c>Fizz</c> did not (plan ledger S1).
    /// </summary>
    [TeProp(BitsFrom = TeBits.MaxEdict, Flags = TeFlags.Unsigned)]
    public int Entity;

    /// <summary>Which decal material, as the engine's decal index. Nine bits unsigned.</summary>
    [TeProp(Bits = 9, Flags = TeFlags.Unsigned)]
    public int Index;
}
