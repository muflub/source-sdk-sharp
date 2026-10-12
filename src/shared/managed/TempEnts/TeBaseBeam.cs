//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_BaseBeam</c> — fifteen props every beam carries, and the base of the
/// seven concrete ones in this lane.
///
/// <para>
/// From <c>te_basebeam.cpp:49</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST_NOBASE( CTEBaseBeam, DT_BaseBeam )
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropModelIndex( SENDINFO(m_nHaloIndex) ),
///     SendPropInt(   SENDINFO(m_nStartFrame),  8, SPROP_UNSIGNED ),
///     SendPropInt(   SENDINFO(m_nFrameRate),   8, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO(m_fLife),        8, 0, 0.0,  25.6 ),
///     SendPropFloat( SENDINFO(m_fWidth),      10, 0, 0.0, 128.0 ),
///     SendPropFloat( SENDINFO(m_fEndWidth),   10, 0, 0.0, 128.0 ),
///     SendPropInt(   SENDINFO(m_nFadeLength),  8, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO(m_fAmplitude),   8, 0, 0.0,  64.0 ),
///     SendPropInt(   SENDINFO(m_nSpeed),       8, SPROP_UNSIGNED ),
///     SendPropInt(   SENDINFO(r),              8, SPROP_UNSIGNED ),
///     SendPropInt(   SENDINFO(g),              8, SPROP_UNSIGNED ),
///     SendPropInt(   SENDINFO(b),              8, SPROP_UNSIGNED ),
///     SendPropInt(   SENDINFO(a),              8, SPROP_UNSIGNED ),
///     SendPropInt(   SENDINFO(m_nFlags),      32, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <c>m_nSpeed</c> COMES BEFORE <c>r g b a</c> HERE AND AFTER THEM IN THE
/// STRUCT. <c>te_basebeam.h:44-49</c> declares <c>r, g, b, a</c> and then
/// <c>m_nSpeed</c>; the table above sends <c>m_nSpeed</c> fifth from last.
/// Declared table order is the wire, so this file copies the TABLE and not the
/// class — and the client's own <c>DT_BaseBeam</c> in
/// <c>c_te_basebeam.cpp:63</c> lists them in the table's order too, which is
/// what makes the swap invisible until someone transcribes the header instead.
/// </para>
///
/// <para>
/// NOBASE, exactly as stock has it. <c>CTEBaseBeam</c> derives from
/// <c>CBaseTempEntity</c> in C++, but its send table nests nothing — so this one
/// declares no <c>BaseTable</c> and a derived beam's blob starts at
/// <c>BaseBytes = 60</c>, which is these fifteen fields and nothing else.
/// </para>
///
/// <para>
/// <c>RegistersClass = true</c>, AND IT MEANS SOMETHING DIFFERENT ON EACH SIDE
/// — which is the whole of what C-Phase 3 changed about this file, so it is
/// worth being exact about.
/// </para>
///
/// <para>
/// The flag says "this class name is a real registered class in stock's build",
/// which <c>CTEBaseBeam</c> is: <c>te_basebeam.cpp</c> declares a ServerClass
/// and <c>c_te_basebeam.cpp</c> declared the matching ClientClass. Each side's
/// shell then decides for itself whether the MANAGED declaration is the one that
/// supplies it, by asking whether the native build still does.
/// </para>
///
/// <para>
/// ON THE SERVER, NOTHING CHANGES AND THE SHELL SAYS SO. <c>te_basebeam.cpp</c>
/// is still compiled — the managed table has to carry stock's name (2E-bis) and
/// nothing else defines <c>CTEBaseBeam</c>'s constructor — so
/// <c>shell_tempents.cpp</c>'s <c>ServerClassExists</c> finds stock's and
/// refuses to add a second, out loud, exactly as it was written to. A second
/// ServerClass of one name makes the client begin instantiating and stop with
/// nothing in any log (plan 2E). One console line at registration is that
/// refusal working, not a regression.
/// </para>
///
/// <para>
/// ON THE CLIENT IT IS LOAD-BEARING, and this is why the flag had to flip.
/// C-Phase 3 evicts <c>c_te_basebeam.cpp</c> — it must, because
/// <c>DT_BaseBeam</c> cannot be two tables and the seven derived managed tables
/// have to nest the one whose offsets address the blob. That eviction takes
/// <c>IMPLEMENT_CLIENTCLASS( C_TEBaseBeam, DT_BaseBeam, CTEBaseBeam )</c> with
/// it, and the server's <c>CTEBaseBeam</c> ServerClass is still there — so
/// without a managed ClientClass of that name the join hangs. The client shell
/// builds it, because <c>ClientClassExists</c> now answers false.
/// </para>
///
/// <para>
/// THE ALTERNATIVE WAS NOT AVAILABLE, and it is the obvious one: leave the flag
/// false and keep <c>c_te_basebeam.cpp</c>. Then <c>FindStockTableNamed</c>
/// refuses the managed <c>DT_BaseBeam</c>, <c>FindRegisteredTable</c> falls
/// through to STOCK's, and the seven derived managed tables nest a base whose
/// props read at <c>offsetof( C_TEBaseBeam, ... )</c> instead of into the blob.
/// That is 2E-bis' second failure — the one that joins, plays, and decodes an
/// origin of <c>(0,0,0)</c> while the counters say everything arrived.
/// </para>
///
/// <para>
/// There is deliberately no <c>IClientTempEnt</c> for <c>CTEBaseBeam</c>. Stock's
/// <c>C_TEBaseBeam::PostDataUpdate</c> is <c>Assert( 0 )</c>: a base is never
/// sent, nothing calls <c>CBaseTempEntity::Create</c> for it, and there is no
/// <c>TE_BaseBeam()</c>. One arriving would be counted as dropped and named once
/// on the console, which is that assert in a build where asserts are compiled
/// out.
/// </para>
/// </summary>
[TempEntity("CTEBaseBeam", "DT_BaseBeam")]
public class TeBaseBeam : TempEntState
{
    /// <summary>
    /// The beam's sprite. <c>SendPropModelIndex</c> is
    /// <c>SendPropInt(x, SP_MODEL_INDEX_BITS, 0)</c> — flags ZERO, so it is a
    /// SIGNED thirteen-bit int and not an unsigned one (plan 2B, and
    /// <c>dt_send.h:750</c>). The width is <see cref="TeBits.ModelIndex"/>
    /// rather than a 13 typed here, because a width with two copies drifts.
    /// </summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>The glow sprite at each end, or 0 for none. Same encoding.</summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int HaloIndex;

    /// <summary>First frame of the sprite's animation.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int StartFrame;

    /// <summary>Frames per second it animates at.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int FrameRate;

    /// <summary>Seconds it lives, in eight bits over [0, 25.6].</summary>
    [TeProp(Bits = 8, Low = 0.0f, High = 25.6f)]
    public float Life;

    /// <summary>Width at the start, ten bits over [0, 128].</summary>
    [TeProp(Bits = 10, Low = 0.0f, High = 128.0f)]
    public float Width;

    /// <summary>Width at the end. Same encoding, and often the same value.</summary>
    [TeProp(Bits = 10, Low = 0.0f, High = 128.0f)]
    public float EndWidth;

    /// <summary>How far back from the end it fades out.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int FadeLength;

    /// <summary>Noise amplitude, eight bits over [0, 64].</summary>
    [TeProp(Bits = 8, Low = 0.0f, High = 64.0f)]
    public float Amplitude;

    /// <summary>
    /// Scroll speed. FIFTH FROM LAST, which is where the table puts it and not
    /// where the struct does — see the class remarks.
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Speed;

    /// <summary>
    /// Colour. Stock spelt these <c>r</c>, <c>g</c>, <c>b</c> and <c>a</c> —
    /// bare single letters, because <c>c_te_basebeam.cpp</c>'s hand-written
    /// <c>RecvTable</c> had to pair with them BY NAME and plan 2E forbade
    /// tidying either side.
    ///
    /// <summary>
    /// The beam's colour. ONE MEMBER, FOUR PROPS — <c>ColorR</c>, <c>ColorG</c>,
    /// <c>ColorB</c> and <c>ColorA</c>, eight bits unsigned each, which is what
    /// stock's four separate <c>r g b a</c> props are and what the parity test
    /// still checks position by position against <c>te_basebeam.cpp</c>.
    ///
    /// <para>
    /// THE ALPHA IS THE BRIGHTNESS. It is <c>a</c> on the wire and
    /// <c>brightness</c> at every one of stock's seven beam call sites; the name
    /// changes at the boundary and the value does not, so <c>Color.A</c> is what
    /// the primitives are handed.
    /// </para>
    ///
    /// <para>
    /// Four bytes of the blob where four ints were sixteen — see
    /// <c>EntityData.SizeOf</c> for why the wire footprint and the CLR one are
    /// allowed to differ.
    /// </para>
    /// </summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public Color32 Color;

    /// <summary>
    /// <c>FBEAM_*</c> bits. A whole unsigned word, and the only 32-bit prop in
    /// the lane; two of the seven forwards set it and five leave it zero,
    /// exactly as stock's singletons do.
    /// </summary>
    [TeProp(Bits = 32, Flags = TeFlags.Unsigned)]
    public int Flags;

    /// <summary>
    /// The scroll speed the beam primitive takes: <c>0.1 * m_nSpeed</c>, exactly
    /// as every <c>c_te_beam*.cpp</c> call site spells it.
    ///
    /// <para>
    /// DECODING, NOT PRESENTATION, which is why it lives on the declaration. The
    /// wire carries tenths because a scroll speed fits eight bits that way and
    /// does not otherwise; the real value is what every consumer wants. It was
    /// previously on a client-side mirror struct that restated all fifteen
    /// fields to add these two lines.
    /// </para>
    /// </summary>
    public float ScrollSpeed => Tenth(Speed);

    /// <summary>The animation rate the primitive takes: <c>0.1 * m_nFrameRate</c>.</summary>
    public float AnimationRate => Tenth(FrameRate);

    /// <summary>
    /// Stock's <c>0.1 * n</c>: a double multiply narrowed once at the call, so
    /// there is one answer to what the tenth is rather than seven.
    /// </summary>
    private static float Tenth(int value) => (float)(0.1 * value);

}
