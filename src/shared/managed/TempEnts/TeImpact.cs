using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEImpact</c> — <b>BEHAVIOUR LEDGER ROW S2, RESTORED.</b> A generic
/// impact at a point with a facing, for the client to decide what to draw.
///
/// <para>
/// From <c>te_impact.cpp:76</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEImpact, DT_TEImpact)
///     SendPropVector( SENDINFO( m_vecOrigin ), -1, SPROP_COORD ),
///     SendPropVector( SENDINFO( m_vecNormal ), -1, SPROP_COORD ),
///     SendPropInt( SENDINFO( m_iType ), 32, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>THIS SIDE HAS BEEN SENDING NOTHING AND THE CLIENT HAS BEEN WAITING.</b>
/// <c>te_impact.cpp</c> is excluded from this build
/// (<c>server_csharp.vpc:1169</c>) because the weapon and NPC evictions left
/// <c>TE_Impact</c> with no callers — so <c>CTEImpact</c> has not been in the
/// server's class list, while <c>c_te_impact.cpp</c>'s <c>C_TEImpact</c> has
/// stayed registered in the client's the whole time. plan 2E says the reverse
/// pairing — a <c>ClientClass</c> with no sender — is harmless, and this build
/// ships two of them; this is one. Registering the managed declaration is what
/// closes the row, and the live join is the first time <c>DT_TEImpact</c> has
/// ever paired in this build.
/// </para>
///
/// <para>
/// THERE IS NOTHING TO EVICT AND NOTHING TO FORWARD. The other four classes in
/// this lane replaced a <c>te_*.cpp</c> and put its <c>TE_X()</c> back over the
/// generic writer; this one is a PURE ADDITION. <c>ITempEntsSystem</c> has no
/// <c>Impact</c> member at all — <c>te.cpp</c>'s <c>CTempEntsSystem</c> never
/// had one — so there is no native entry point to preserve and no native symbol
/// anything links against. <see cref="ITempEnts.Impact"/> is the only way to
/// fire it, which is exactly what "restored as a managed declaration" means.
/// </para>
///
/// <para>
/// THE CLIENT'S TABLE HAS A FOURTH PROP AND THIS ONE DELIBERATELY DOES NOT.
/// <c>c_te_impact.cpp:93</c> declares <c>RecvPropInt( RECVINFO( m_ucFlags ) )</c>
/// after the three above; stock's SEND table has never had it, so
/// <c>C_TEImpact::m_ucFlags</c> has always kept its constructor's zero. The
/// engine matches recv props to send props BY NAME and a recv prop with no
/// sender is simply never written, which is why the asymmetry is harmless and
/// why it must be reproduced rather than corrected: adding <c>m_ucFlags</c>
/// here would make this build's wire differ from stock's for a value nothing
/// reads.
/// </para>
/// </summary>
[TempEntity]
public class TeImpact : TeBaseTempEntity
{
    /// <summary>Where it hit.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>
    /// The surface normal there. A COORD vector and NOT <c>SPROP_NORMAL</c>,
    /// though it carries a normal — stock's own comment on the member says
    /// "in a multi-play setup we'll probably want non-oriented effects for
    /// bandwidth", which is a note about what it costs, not a claim about how it
    /// is encoded. Sending it as a normal would be sixty-seven fewer bits and a
    /// different bitstream from the one <c>c_te_impact.cpp</c> decodes.
    /// </summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Normal;

    /// <summary>
    /// What was hit — a surface-property index the client turns into an effect.
    /// Thirty-two bits unsigned, stock's own width.
    /// </summary>
    [TeProp(Bits = 32, Flags = TeFlags.Unsigned)]
    public int Type;
}
