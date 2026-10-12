using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBeamRing</c> — a ring of beam expanding between two entities.
///
/// <para>
/// From <c>te_beamring.cpp:84</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEBeamRing, DT_TEBeamRing)
///     SendPropInt( SENDINFO(m_nStartEntity), MAX_EDICT_BITS, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nEndEntity),   MAX_EDICT_BITS, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>MAX_EDICT_BITS, WHERE FIVE SIBLINGS WRITE A LITERAL 24, AND THE TWO ARE
/// NOT UNIFIED.</b> <c>DT_TEBeamEnts</c>, <c>DT_TEBeamLaser</c>,
/// <c>DT_TEBeamEntPoint</c> and <c>DT_TEBeamFollow</c> all send the same
/// index-and-attachment value in twenty-four bits; this one sends it in
/// <c>MAX_EDICT_BITS</c>, which is eleven — so <c>TE_BeamRing</c>'s
/// <c>(start &amp; 0xFFF) | (1 &lt;&lt; 12)</c> loses its attachment nibble and
/// its top index bit on the way out. That is stock's behaviour, it is what
/// <c>c_te_beamring.cpp</c>'s <c>RecvTable</c> was compiled against, and the
/// port reproduces it rather than improving it: a prop one bit wider or
/// narrower than the other side expects is a bitstream that stops making sense
/// partway through, and it produces no log line anywhere.
/// </para>
///
/// <para>
/// The width travels as <see cref="TeBits.MaxEdict"/> and native resolves it
/// from <c>public/const.h</c> — never as an 11 typed here, because a width with
/// two copies drifts. The literal 24s stay literals for the mirror-image reason:
/// they are literals in stock's own table.
/// </para>
/// </summary>
[TempEntity]
public class TeBeamRing : TeBaseBeam
{
    /// <summary>The entity the ring starts at, in <c>MAX_EDICT_BITS</c> bits.</summary>
    [TeProp(BitsFrom = TeBits.MaxEdict, Flags = TeFlags.Unsigned)]
    public int StartEntity;

    /// <summary>The entity it ends at, same width.</summary>
    [TeProp(BitsFrom = TeBits.MaxEdict, Flags = TeFlags.Unsigned)]
    public int EndEntity;
}
