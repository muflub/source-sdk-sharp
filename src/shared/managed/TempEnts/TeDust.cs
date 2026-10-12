using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEDust</c> — <b>BEHAVIOUR LEDGER ROW S6.</b> The puff of dust a physics
/// object throws up when it hits something, and the ONE server temp entity that
/// was never in a <c>te_*.cpp</c> at all.
///
/// <para>
/// From <c>shell_entity_orphans.cpp:361</c>, which is a verbatim move of
/// <c>func_dust.cpp:265</c> — the two are identical text, and this transcription
/// was taken from the orphans copy because that is the one this build compiles:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEDust, DT_TEDust )
///     SendPropFloat( SENDINFO(m_flSize), -1, SPROP_COORD ),
///     SendPropFloat( SENDINFO(m_flSpeed), -1, SPROP_COORD ),
///     SendPropVector( SENDINFO(m_vecDirection), 4, 0, -1.0f, 1.0f ), // cheap normal
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>IT IS LIVE, AND IT IS WHERE NOBODY WOULD LOOK.</b> <c>func_dust.cpp</c> is
/// excluded (<c>server_csharp.vpc:1000</c>) because its <c>func_dust</c> entity
/// is managed, but <c>IEffects::Dust</c> has four live callers — two in
/// <c>shared/physics_shared.cpp</c>, one in <c>physics.cpp</c>'s collision
/// event, and <c>shell_worldquery.cpp</c>'s <c>SourceSharp_DustEffect</c> — so
/// the class, its table, its static instance and <c>TE_Dust</c> were moved into
/// <c>shell_entity_orphans.cpp</c> whole. (FOUR, not the five that file's own
/// comment claimed: <c>physgun.cpp</c> calls <c>g_pEffects-&gt;Dust</c> and is
/// in no server vpc file list, so it has never been on this link line.)
/// A lane scanning the port by filename ports 43 of 44 and the count still looks
/// right.
/// </para>
///
/// <para>
/// <b><c>m_vecDirection</c> IS THE INVENTORY'S ODDEST VECTOR: FOUR BITS, FLAGS
/// ZERO, RANGE [-1, 1].</b> Stock's own comment calls it a "cheap normal" and it
/// is emphatically NOT <c>SPROP_NORMAL</c> — that flag would spend
/// <c>NORMAL_FRACTIONAL_BITS</c> on x and y and reconstruct z from a sign bit,
/// which is a different encoding, a different bit count and a different
/// bitstream from the one <c>c_func_dust.cpp</c>'s
/// <c>RecvPropVector(RECVINFO(m_vecDirection))</c> decodes. Four bits over
/// [-1, 1] is sixteen steps per component: coarse on purpose, and the whole
/// reason the field exists in this form.
/// </para>
///
/// <para>
/// The two floats are <c>SPROP_COORD</c> with a width of -1, so the flag decides
/// the encoding — a size and a speed sent through the coordinate quantiser,
/// which is stock's choice and not a natural one.
/// </para>
///
/// <para>
/// A NOTE FOR WHOEVER READS <see cref="TeParticleSystem"/>'s
/// <c>RegistersClass = false</c>: its comment names <c>CTEDust</c> as the native
/// subclass keeping stock's <c>CTEParticleSystem</c> alive. Dust has now landed,
/// but the conclusion has not moved — <c>te_explosion.cpp</c>,
/// <c>te_largefunnel.cpp</c> and <c>te_bloodstream.cpp</c> are lanes D and E's
/// and still derive from it, so <c>DT_TEParticleSystem</c> stays a table and not
/// a class until they land. Said here rather than edited there, because that
/// file is S-Phase 1's and three lanes are in flight.
/// </para>
/// </summary>
[TempEntity]
public class TeDust : TeParticleSystem
{
    /// <summary>How big the puff is. A COORD float — the flag picks the width.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public float Size;

    /// <summary>How fast it spreads. Same encoding.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public float Speed;

    /// <summary>
    /// Which way it blows. FOUR BITS PER COMPONENT over an EXPLICIT [-1, 1],
    /// with NO flags at all — the cheap normal. <c>TE_Dust</c> asserts the
    /// vector is unit length before sending, which is what makes four bits
    /// enough.
    /// </summary>
    [TeProp(Bits = 4, Flags = TeFlags.None, Low = -1.0f, High = 1.0f)]
    public Vector Direction;
}
