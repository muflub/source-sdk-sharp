//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEBeamRingPoint</c>, played — a ring around a point, growing from one
/// radius to another.
///
/// <para>
/// The whole of <c>c_te_beamringpoint.cpp</c>'s <c>PostDataUpdate</c>:
/// </para>
/// <code>
/// beams->CreateBeamRingPoint( m_vecCenter, m_flStartRadius, m_flEndRadius, m_nModelIndex, m_nHaloIndex, 0.0f,
///     m_fLife, m_fWidth, m_fEndWidth, m_nFadeLength, m_fAmplitude, a, 0.1 * m_nSpeed,
///     m_nStartFrame, 0.1 * m_nFrameRate, r, g, b, m_nFlags );
/// </code>
///
/// <para>
/// THE SECOND OF THE THREE THAT DRAWS TODAY. A centre and two radii, no entity
/// anywhere, so ledger row C8 does not reach it — the ring around two ENTITIES
/// is <see cref="ClientTeBeamRing"/> and that one is refused.
/// </para>
///
/// <para>
/// ONE OF ONLY TWO IN THE LANE THAT PASSES <c>m_nFlags</c> ON. The other is
/// <see cref="ClientTeBeamRing"/>; the remaining five hand the primitive its own
/// default and leave the wire's flags unread, which is what stock does and is
/// not an oversight to tidy up — <c>CreateBeamPoints</c> and friends take no
/// flags parameter at all.
/// </para>
///
/// <para>
/// The two radii are the only <c>ROUNDUP</c> props in the lane
/// (<see cref="SourceSharp.Shared.TeBeamRingPoint"/> says why the rounding flag
/// matters and why no bit count can see it). Nothing here rounds: by the time a
/// float reaches this side the engine has already decoded it, and re-deriving
/// the quantisation would be a second description of the wire.
/// </para>
/// </summary>
[ClientTempEnt("CTEBeamRingPoint")]
public sealed class ClientTeBeamRingPoint : ClientTempEnt<TeBeamRingPoint>
{
    private readonly IClientBeams _beams;

    public ClientTeBeamRingPoint(IClientBeams beams)
    {
        _beams = beams;
    }

    public override void Play(TeBeamRingPoint te)
    {
        // ONE READ FOR BOTH HALVES. TeBeamRingPoint IS a TeBaseBeam, so the base's
        // fifteen fields and this table's own come out of the same walk.

        _beams.CreateBeamRingPoint(te.Center, te.StartRadius, te.EndRadius,
            te.ModelIndex, te.HaloIndex, haloScale: 0f,
            te.Life, te.Width, te.EndWidth, te.FadeLength, te.Amplitude,
            brightness: te.Color.A, te.ScrollSpeed,
            te.StartFrame, te.AnimationRate,
            te.Color.R, te.Color.G, te.Color.B, te.Flags);
    }
}
