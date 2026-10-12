using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEShowLine</c> — a line of particles from the base's origin to an end
/// point. The smallest concrete table in the inventory: one prop.
///
/// <para>
/// From <c>te_showline.cpp:84</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEShowLine, DT_TEShowLine)
///     SendPropVector( SENDINFO(m_vecEnd), -1, SPROP_COORD),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// IT NESTS <c>DT_TEParticleSystem</c>, WHICH IS WHERE THE START POINT IS. The
/// line's near end is the base's <c>m_vecOrigin</c>, and
/// <c>TE_ShowLine( filter, delay, start, end )</c> writes it there — so a
/// caller that sets only <c>m_vecEnd</c> draws a line from the world origin,
/// which is a plausible-looking line rather than a missing one. The base's
/// three floats occupy the front of this class's blob (offset 0..11) and
/// <c>m_vecEnd</c> begins at 12; the writer reaches both because
/// <c>TempEntType.Fields</c> carries the whole base chain.
/// </para>
///
/// <para>
/// The only class in this lane on a base other than <c>DT_BaseTempEntity</c>,
/// which is what makes it the one that would catch a base-chain offset mistake.
/// </para>
/// </summary>
[TempEntity]
public class TeShowLine : TeParticleSystem
{
    /// <summary>The far end. The near end is the base's <c>m_vecOrigin</c>.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector End;
}
