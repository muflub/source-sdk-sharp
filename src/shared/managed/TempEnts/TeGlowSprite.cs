using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEGlowSprite</c> — <see cref="TeSprite"/> with a life, which is the
/// whole difference between the two tables.
///
/// <para>
/// From <c>te_glowsprite.cpp:105</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEGlowSprite, DT_TEGlowSprite)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropFloat( SENDINFO(m_fScale ), 8, SPROP_ROUNDDOWN, 0.0, 25.6 ),
///     SendPropFloat( SENDINFO(m_fLife ), 8, SPROP_ROUNDDOWN, 0.0, 25.6 ),
///     SendPropInt( SENDINFO(m_nBrightness), 8, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <c>m_fLife</c> SITS BETWEEN <c>m_fScale</c> AND <c>m_nBrightness</c> ON THE
/// WIRE, though <c>TE_GlowSprite</c>'s parameter list puts life before size.
/// The table is the wire and the parameter list is not; copying the argument
/// order here would swap two eight-bit floats over the same [0,25.6] range,
/// which decodes into two plausible numbers and no error anywhere.
/// </para>
///
/// <para>
/// It has the live caller of this lane: <c>env_spark</c>'s glow
/// (<c>EnvSpark.cpp:139</c>, now managed) is a <c>te-&gt;GlowSprite</c>.
/// </para>
/// </summary>
[TempEntity]
public class TeGlowSprite : TeBaseTempEntity
{
    /// <summary>Where the glow is.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>Which sprite. Thirteen bits, no flags — see <see cref="TeSprite"/>.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex, Flags = TeFlags.None)]
    public int ModelIndex;

    /// <summary>How big, 0..25.6, ROUNDDOWN.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.RoundDown, Low = 0.0f, High = 25.6f)]
    public float Scale;

    /// <summary>
    /// How long it lasts, 0..25.6 seconds, ROUNDDOWN — the same encoding as
    /// <see cref="Scale"/> and the reason the two must not be transposed.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.RoundDown, Low = 0.0f, High = 25.6f)]
    public float Life;

    /// <summary>How bright, 0..255.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Brightness;
}
