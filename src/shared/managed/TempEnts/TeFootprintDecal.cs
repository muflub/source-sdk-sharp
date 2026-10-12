using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEFootprintDecal</c> — a footprint, with a facing and a material to
/// decide which print is stamped.
///
/// <para>
/// From <c>te_footprintdecal.cpp:43</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEFootprintDecal, DT_TEFootprintDecal)
///     SendPropVector( SENDINFO(m_vecOrigin),      -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecDirection),   -1, SPROP_COORD),
///     SendPropInt(  SENDINFO(m_nEntity),          11, SPROP_UNSIGNED ),
///     SendPropInt(  SENDINFO(m_nIndex),            8, SPROP_UNSIGNED ),
///     SendPropInt(  SENDINFO(m_chMaterialType),    8, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <c>m_nEntity</c> IS A LITERAL ELEVEN AND STAYS ONE. Every other entity index
/// in this lane is <c>MAX_EDICT_BITS</c>, which is also eleven — and the two are
/// deliberately NOT unified. plan 3.1's rule cuts both ways: a width that is a
/// symbol in stock's table travels as a symbol so it cannot drift, and a width
/// that is a literal in stock's table stays a literal so this port does not
/// quietly decide that Valve meant the symbol. If <c>MAX_EDICT_BITS</c> ever
/// moves, stock's client half of THIS table still reads eleven, and a send side
/// that had followed the symbol would be one bit wide of a hand-written
/// <c>RecvTable</c> — which is a bitstream that stops making sense partway
/// through and produces no log line anywhere.
/// </para>
///
/// <para>
/// <c>m_nIndex</c> is EIGHT bits here where the other five decal tables send
/// nine, and <c>m_chMaterialType</c> is an <c>unsigned char</c> — one byte of
/// blob, so <c>SendPropInt</c> picks its one-byte read proxy
/// (<c>dt_send.cpp:492</c>). Describing it as an <c>int</c> would read three
/// bytes of whatever came after it.
/// </para>
/// </summary>
[TempEntity]
public class TeFootprintDecal : TeBaseTempEntity
{
    /// <summary>Where the print goes.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// Which way it points — stock's "right" vector, normalised by
    /// <c>TE_FootprintDecal</c> before it is sent.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Direction;

    /// <summary>
    /// The edict stepped on. ELEVEN, a literal, because stock's table says 11
    /// and not <c>MAX_EDICT_BITS</c> — see the class note.
    /// </summary>
    [TeProp(Bits = 11, Flags = TeFlags.Unsigned)]
    public int Entity;

    /// <summary>Which print. EIGHT bits here, not the nine the other decals send.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Index;

    /// <summary>
    /// The surface's material character — <c>'C'</c> for concrete, and so on.
    /// One BYTE of blob, because stock's member is an <c>unsigned char</c> and
    /// the prop's read proxy is chosen from that storage width.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public byte MaterialType;
}
