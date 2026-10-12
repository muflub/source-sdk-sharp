using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBloodStream</c> — the arterial spurt, and the lane's one vector that
/// is neither COORD nor NOSCALE.
///
/// <para>
/// From <c>te_bloodstream.cpp:93</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEBloodStream, DT_TEBloodStream)
///     SendPropVector( SENDINFO(m_vecDirection), 11, 0, -10.0, 10.0 ),
///     SendPropInt( SENDINFO(r), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(g), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(b), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(a), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nAmount), 8, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// BASED, where its lane-mate <see cref="TeBloodSprite"/> is NOBASE. The macro
/// without <c>_NOBASE</c> nests <c>CTEParticleSystem</c>'s table first, which is
/// where <c>m_vecOrigin</c> comes from: this table declares a DIRECTION and no
/// position at all, and the position is the base's three COORD floats. A reader
/// who took the NOBASE spelling from the file above would produce a table with
/// no origin and an effect that always plays at the world origin.
/// </para>
///
/// <para>
/// ELEVEN BITS OVER [-10, 10], WITH NO ENCODING FLAG. Not <c>SPROP_COORD</c>,
/// not <c>SPROP_NORMAL</c>, not <c>SPROP_NOSCALE</c> — an explicit range on a
/// narrow vector, which is what <c>UTIL_RandomBloodVector</c> produces: a
/// direction of roughly unit length that stock allows ten units of slack around.
/// The flags field really is a zero and the range really is the encoding; a
/// <c>SPROP_NORMAL</c> "tidy" here would quantise to [-1,1] and send every
/// spurt straight up.
/// </para>
/// </summary>
[TempEntity]
public class TeBloodStream : TeParticleSystem
{
    /// <summary>
    /// Which way the blood goes, at eleven bits over [-10, 10]. See the class
    /// note: the absence of an encoding flag is the encoding.
    /// </summary>
    [TeProp(Bits = 11, Flags = TeFlags.None, Low = -10.0f, High = 10.0f)]
    public Vector Direction;

    /// <summary>
    /// The blood's colour. ONE MEMBER, FOUR PROPS — <c>ColorR</c>,
    /// <c>ColorG</c>, <c>ColorB</c> and <c>ColorA</c>, eight bits unsigned each,
    /// which is what stock's four separate <c>r g b a</c> props are and what the
    /// parity test still checks position by position.
    ///
    /// <para>
    /// It costs FOUR BYTES of the blob where the four ints cost sixteen; see
    /// <c>EntityData.SizeOf</c> for why the wire footprint and the CLR one are
    /// allowed to differ here.
    /// </para>
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public Color32 Color;

    /// <summary>
    /// How much of it. Eight bits unsigned, which is why
    /// <c>UTIL_BloodStream</c> clamps with <c>MIN( amount, 255 )</c> before it
    /// ever reaches the wire (<c>util.cpp:1844</c>).
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Amount;
}
