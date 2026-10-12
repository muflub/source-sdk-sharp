using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBeamFollow</c> — a beam trailing behind a moving entity.
///
/// <para>
/// From <c>te_beamfollow.cpp:64</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEBeamFollow, DT_TEBeamFollow)
///     SendPropInt( SENDINFO(m_iEntIndex), 24, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// STOCK CALLED IT <c>m_iEntIndex</c>, not <c>m_nEntIndex</c> and not
/// <c>m_nStartEntity</c> — the one table in the lane that spells its entity
/// prop with an <c>i</c>. While <c>c_te_beamfollow.cpp</c>'s hand-written
/// <c>RecvTable</c> existed the two sides paired BY NAME and a tidied name was
/// an unpaired prop; that table is evicted and both halves are built from this
/// declaration now, so the wire name is <c>EntIndex</c> and the oddity is
/// recorded here and in <c>TempEntBeamTests</c>' parity message rather than
/// carried on the wire.
/// </para>
///
/// <para>
/// The only member of the lane whose stock singleton left three base fields
/// unset — <c>TE_BeamFollow</c> writes neither <c>m_fAmplitude</c> nor
/// <c>m_nSpeed</c> nor <c>m_nFlags</c>, so they held whatever
/// <c>CTEBaseBeam</c>'s constructor put there, which is zero. The managed path
/// zeroes its blob in <c>SourceSharpTe_Begin</c>, so the wire is the same
/// value by a shorter argument.
/// </para>
/// </summary>
[TempEntity]
public class TeBeamFollow : TeBaseBeam
{
    /// <summary>Index and attachment of the entity the beam follows.</summary>
    [TeProp(Bits = 24, Flags = TeFlags.Unsigned)]
    public int EntIndex;
}
