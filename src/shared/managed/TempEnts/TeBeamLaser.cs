using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBeamLaser</c> — a beam between two entities that fades out when it is
/// edge-on to the viewer.
///
/// <para>
/// From <c>te_beamlaser.cpp:78</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEBeamLaser, DT_TEBeamLaser)
///     SendPropInt( SENDINFO(m_nStartEntity), 24, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nEndEntity),   24, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// Prop for prop identical to <c>DT_TEBeamEnts</c>, and deliberately still its
/// own table: the difference between the two is entirely in what the CLIENT
/// does with it (<c>c_te_beamlaser.cpp</c> asks for a <c>BEAM_ENTS</c> with
/// <c>FBEAM_FADEOUT</c>), and the set of registered class names must be exactly
/// stock's (plan 2E). Two effects sharing one table would be one effect.
/// </para>
/// </summary>
[TempEntity]
public class TeBeamLaser : TeBaseBeam
{
    /// <summary>Index and attachment of the entity it starts at.</summary>
    [TeProp(Bits = 24, Flags = TeFlags.Unsigned)]
    public int StartEntity;

    /// <summary>Index and attachment of the entity it ends at.</summary>
    [TeProp(Bits = 24, Flags = TeFlags.Unsigned)]
    public int EndEntity;
}
