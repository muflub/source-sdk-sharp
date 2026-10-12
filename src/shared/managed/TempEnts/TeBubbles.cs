using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBubbles</c> — bubbles rising out of a box of water.
///
/// <para>
/// From <c>te_bubbles.cpp:98</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEBubbles, DT_TEBubbles)
///     SendPropVector( SENDINFO(m_vecMins), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecMaxs), -1, SPROP_COORD),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropFloat( SENDINFO(m_fHeight ), 17, 0, MIN_COORD_INTEGER, MAX_COORD_INTEGER ),
///     SendPropInt( SENDINFO(m_nCount), 8, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO(m_fSpeed ), 17, 0, MIN_COORD_INTEGER, MAX_COORD_INTEGER ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// DECLARATION ORDER IS THE TABLE'S, NOT THE HEADER'S, and this is one of the
/// two tables in the lane where the two disagree: <c>te_bubbles.cpp:38-43</c>
/// declares <c>m_fHeight</c> BEFORE <c>m_nModelIndex</c> and the send table
/// sends the model index first. A reader who transcribed the class would decode
/// a height as a model index — a plausible number that draws the wrong sprite,
/// with no error anywhere. The parity test reads the TABLE off disk for exactly
/// this reason.
/// </para>
///
/// <para>
/// SEVENTEEN BITS OVER THE WHOLE WORLD. <c>[MIN_COORD_INTEGER,
/// MAX_COORD_INTEGER]</c> is [-16384, 16384] (<c>public/worldsize.h:19</c>), so
/// this is a coordinate-sized float with a QUARTER-UNIT step rather than a
/// <c>SPROP_COORD</c>, and the flags argument really is a zero. It applies to a
/// height and to a SPEED, which is the giveaway that stock reached for a range
/// wide enough for anything rather than one fitted to the quantity.
/// </para>
///
/// <para>
/// This is a live effect on this build's link line, through two callers:
/// <c>UTIL_Bubbles</c> (<c>util.cpp:2100</c>) and <c>SourceSharp_BubbleEffect</c>
/// (<c>shell_worldquery.cpp:2055</c>), which is what every managed
/// <c>env_bubbles</c> emits through — deliberately, in place of
/// <c>te->Fizz</c>, for the reason ledger row C6 records.
/// </para>
/// </summary>
[TempEntity]
public class TeBubbles : TeBaseTempEntity
{
    /// <summary>The low corner of the volume they come out of.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Mins;

    /// <summary>The high corner.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Maxs;

    /// <summary>
    /// The bubble sprite. <c>SendPropModelIndex</c> — signed, and its width is
    /// <c>SP_MODEL_INDEX_BITS</c> resolved natively (plan 2B).
    /// </summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>How far up they travel before they stop. See the class note.</summary>
    [TeProp(Bits = 17, Flags = TeFlags.None, Low = -16384f, High = 16384f)]
    public float Height;

    /// <summary>How many. Eight bits unsigned.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Count;

    /// <summary>How fast they rise, on the same world-sized range as the height.</summary>
    [TeProp(Bits = 17, Flags = TeFlags.None, Low = -16384f, High = 16384f)]
    public float Speed;
}
