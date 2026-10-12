using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TESpriteSpray</c> — a burst of sprites thrown along a direction.
///
/// <para>
/// From <c>te_spritespray.cpp:106</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTESpriteSpray, DT_TESpriteSpray)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropVector( SENDINFO(m_vecDirection), -1, SPROP_COORD),
///     SendPropModelIndex(SENDINFO(m_nModelIndex)),
///     SendPropFloat( SENDINFO(m_fNoise ), 8, SPROP_ROUNDDOWN, 0.0, 2.56 ),
///     SendPropInt( SENDINFO(m_nSpeed ), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nCount), 8, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// THE TABLE ORDER IS NOT THE MEMBER ORDER. <c>CTESpriteSpray</c> declares
/// <c>m_nSpeed</c> before <c>m_fNoise</c> and the send table sends
/// <c>m_fNoise</c> first; the same trap <c>DT_BaseBeam</c> carries and which
/// plan Part 6 calls out for it. The declared table is the wire, so
/// <see cref="Noise"/> comes first here.
/// </para>
///
/// <para>
/// <c>m_fNoise</c>'s range is <b>[0, 2.56]</b>, not the [0, 25.6] every other
/// eight-bit ROUNDDOWN float in this lane uses. One decimal point is a factor
/// of ten in what arrives.
/// </para>
/// </summary>
[TempEntity]
public class TeSpriteSpray : TeBaseTempEntity
{
    /// <summary>Where the spray starts.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// Which way it goes, and how fast — stock's callers pass a velocity here,
    /// not a unit vector, which is why it is COORD rather than NORMAL.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Direction;

    /// <summary>Which sprite. Thirteen bits, no flags — see <see cref="TeSprite"/>.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex, Flags = TeFlags.None)]
    public int ModelIndex;

    /// <summary>
    /// How much the individual sprites wander off the direction, 0..2.56 in
    /// eight bits, ROUNDDOWN. The range is this table's own — see the note.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.RoundDown, Low = 0.0f, High = 2.56f)]
    public float Noise;

    /// <summary>How fast, 0..255.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Speed;

    /// <summary>How many, 0..255.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Count;
}
