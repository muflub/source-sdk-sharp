using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBubbleTrail</c> — the wake a body leaves through water.
///
/// <para>
/// From <c>te_bubbletrail.cpp:101</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEBubbleTrail, DT_TEBubbleTrail)
///     SendPropVector( SENDINFO(m_vecMins), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecMaxs), -1, SPROP_COORD),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropFloat( SENDINFO(m_flWaterZ ), 17, 0, MIN_COORD_INTEGER, MAX_COORD_INTEGER ),
///     SendPropInt( SENDINFO(m_nCount), BUBBLE_TRAIL_COUNT_BITS, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO(m_fSpeed ), 17, 0, MIN_COORD_INTEGER, MAX_COORD_INTEGER ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// PROP FOR PROP <see cref="TeBubbles"/> WITH ONE NAME CHANGED, and the two are
/// still separate tables for the reason plan 2E gives: the client pairs on the
/// exact set of names stock has, and <c>c_te_bubbletrail.cpp</c> spells the
/// fourth prop <c>m_flWaterZ</c> — <c>fl</c>, not <c>f</c> — because it means a
/// world Z rather than a distance. Unifying the two would rename a prop the
/// other side is still looking for.
/// </para>
///
/// <para>
/// <c>m_nCount</c>'s WIDTH IS A FILE-LOCAL SYMBOL, <c>BUBBLE_TRAIL_COUNT_BITS</c>,
/// an enum at <c>te_bubbletrail.cpp:19</c> whose value is 8. It is written here
/// as a literal 8 and NOT as a <see cref="TeBits"/>, and the distinction is the
/// one that enum draws itself: it exists so <c>TE_BubbleTrail</c> can clamp with
/// <c>MIN( count, BUBBLE_TRAIL_MAX_COUNT )</c> in the same file, not because the
/// width is shared with anything. <see cref="TeBits"/> is for widths defined in a
/// header two sides both include — a second copy of one of THOSE drifts. The
/// parity test reads the enum out of the .cpp and resolves it, so the 8 here is
/// still checked against its definition rather than against a memory of it.
/// </para>
///
/// <para>
/// Live on this build's link line through <c>UTIL_BubbleTrail</c>
/// (<c>util.cpp:2148</c>), which is the clamp's only caller.
/// </para>
/// </summary>
[TempEntity]
public class TeBubbleTrail : TeBaseTempEntity
{
    /// <summary>One end of the trail.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Mins;

    /// <summary>The other.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Maxs;

    /// <summary>The bubble sprite, signed and SP_MODEL_INDEX_BITS wide.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>
    /// The world Z the water surface is at, so the client knows where the
    /// bubbles stop. Seventeen bits over [-16384, 16384] — the whole world,
    /// which is what a world Z needs.
    /// </summary>
    [TeProp(Bits = 17, Flags = TeFlags.None, Low = -16384f, High = 16384f)]
    public float WaterZ;

    /// <summary>How many. BUBBLE_TRAIL_COUNT_BITS, which is 8 — see the class note.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Count;

    /// <summary>How fast they rise.</summary>
    [TeProp(Bits = 17, Flags = TeFlags.None, Low = -16384f, High = 16384f)]
    public float Speed;
}
