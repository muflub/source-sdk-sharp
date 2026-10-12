using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEDynamicLight</c> — a coloured light that appears, lives and decays.
/// The table in this lane with the most ways to be nearly right.
///
/// <para>
/// From <c>te_dynamiclight.cpp:104</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEDynamicLight, DT_TEDynamicLight)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropInt( SENDINFO(r), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(g), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(b), 8, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(exponent), 8, 0 ),
///     SendPropFloat( SENDINFO(m_fRadius), 8, SPROP_ROUNDUP, 0, 2560.0 ),
///     SendPropFloat( SENDINFO(m_fTime), 8, SPROP_ROUNDDOWN, 0, 25.6 ),
///     SendPropFloat( SENDINFO(m_fDecay), 8, SPROP_ROUNDDOWN, 0, 2560.0 ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// TWO THINGS HERE ARE ONE TOKEN AWAY FROM WRONG AND NEITHER REPORTS ITSELF.
/// </para>
///
/// <para>
/// <b><c>exponent</c> IS THE INVENTORY'S ONE SIGNED EIGHT-BIT INT.</b> Its
/// flags are literally <c>0</c> where <c>r</c>, <c>g</c> and <c>b</c> — the
/// three props immediately before it — are <c>SPROP_UNSIGNED</c>. Stock's own
/// callers pass small positive falloff exponents, so an unsigned declaration
/// would look correct in every screenshot and still be a bit narrower than the
/// client reads: <c>SPROP_UNSIGNED</c> spends all eight bits on magnitude and
/// the signed form spends one on a sign, so the two disagree on every value
/// from 128 up and on the position of every prop after it. A sign bit in the
/// wrong place is a value that is right half the time.
/// </para>
///
/// <para>
/// <b>ROUNDUP AND ROUNDDOWN BOTH APPEAR, IN THAT ORDER.</b> <c>m_fRadius</c> is
/// <c>SPROP_ROUNDUP</c>; <c>m_fTime</c> and <c>m_fDecay</c> are
/// <c>SPROP_ROUNDDOWN</c>. They decide which endpoint of the range the encoder
/// gives away (<c>dt_send.cpp</c>'s <c>SendProp::SetFlags</c> feeds
/// <c>CBitWrite</c>'s float quantiser), so copying a neighbour's flag rather
/// than the field's own is an off-by-one-quantum error at the ends of the
/// range and nothing at all in the middle. Each of the three below is
/// transcribed from its own line.
/// </para>
///
/// <para>
/// The ranges differ too, and not in step with the flags: <c>m_fRadius</c> and
/// <c>m_fDecay</c> are [0, 2560] while <c>m_fTime</c> is [0, 25.6].
/// </para>
///
/// <para>
/// The wire names are stock's own single letters. <c>r</c>, <c>g</c> and
/// <c>b</c> are not <c>m_</c>-prefixed and the client matches props by name, so
/// this is not a place to tidy up naming (plan 2E).
/// </para>
/// </summary>
[TempEntity]
public class TeDynamicLight : TeBaseTempEntity
{
    /// <summary>Where the light is.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>Red, 0..255. Unsigned, unlike <see cref="Exponent"/> below.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int R;

    /// <summary>Green, 0..255.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int G;

    /// <summary>Blue, 0..255.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int B;

    /// <summary>
    /// The light's falloff exponent. EIGHT BITS AND NO FLAGS — signed, which is
    /// the whole of the note above and the only such int in the inventory.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.None)]
    public int Exponent;

    /// <summary>How far it reaches, 0..2560. <b>ROUNDUP</b>, alone in this table.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.RoundUp, Low = 0.0f, High = 2560.0f)]
    public float Radius;

    /// <summary>How long it lives, 0..25.6 seconds. ROUNDDOWN, and its own range.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.RoundDown, Low = 0.0f, High = 25.6f)]
    public float Time;

    /// <summary>How fast it fades, 0..2560 units a second. ROUNDDOWN.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.RoundDown, Low = 0.0f, High = 2560.0f)]
    public float Decay;
}
