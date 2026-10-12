using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBeamEntPoint</c> — a beam from an entity to a point, or between two
/// of either.
///
/// <para>
/// From <c>te_beamentpoint.cpp:96</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEBeamEntPoint, DT_TEBeamEntPoint)
///     SendPropInt(    SENDINFO(m_nStartEntity),  24, SPROP_UNSIGNED ),
///     SendPropInt(    SENDINFO(m_nEndEntity),    24, SPROP_UNSIGNED ),
///     SendPropVector( SENDINFO(m_vecStartPoint), -1, SPROP_COORD ),
///     SendPropVector( SENDINFO(m_vecEndPoint),   -1, SPROP_COORD ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// THE TABLE'S ORDER IS NOT THE CLASS'S. <c>te_beamentpoint.cpp:39-42</c>
/// declares <c>m_nStartEntity</c>, <c>m_vecStartPoint</c>, <c>m_nEndEntity</c>,
/// <c>m_vecEndPoint</c> — interleaved — and the table sends both ints and then
/// both vectors. Declared table order is the wire, so that is the order here.
/// </para>
///
/// <para>
/// The two entity props are a LITERAL 24 in stock's table and stay a literal 24
/// here. Five of this lane's seven do; <c>DT_TEBeamRing</c> writes
/// <c>MAX_EDICT_BITS</c> for the same pair and travels as
/// <see cref="TeBits.MaxEdict"/>. Unifying them would be a prop of a different
/// width against a client that was not consulted, which is a bitstream that
/// stops making sense partway through and produces no log line anywhere.
/// </para>
/// </summary>
[TempEntity]
public class TeBeamEntPoint : TeBaseBeam
{
    /// <summary>
    /// The entity the beam starts at, or 0 for "use the start point instead".
    /// <c>TE_BeamEntPoint</c> packs it as <c>(index &amp; 0xFFF) | (1 &lt;&lt; 12)</c>
    /// — an index and an attachment — which is why twelve bits would not do.
    /// </summary>
    [TeProp(Bits = 24, Flags = TeFlags.Unsigned)]
    public int StartEntity;

    /// <summary>The entity it ends at, packed the same way, or 0.</summary>
    [TeProp(Bits = 24, Flags = TeFlags.Unsigned)]
    public int EndEntity;

    /// <summary>Where it starts when <see cref="StartEntity"/> is 0.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector StartPoint;

    /// <summary>Where it ends when <see cref="EndEntity"/> is 0.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector EndPoint;
}
