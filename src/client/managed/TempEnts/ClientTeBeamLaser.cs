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
/// <c>CTEBeamLaser</c>, played — the sniper's laser sight: a beam between two
/// entities that fades out when it is edge-on to the viewer.
///
/// <para>
/// The whole of <c>c_te_beamlaser.cpp</c>'s <c>PostDataUpdate</c>:
/// </para>
/// <code>
/// beams->CreateBeamEnts( m_nStartEntity, m_nEndEntity, m_nModelIndex, m_nHaloIndex, 0.0f,
///     m_fLife, m_fWidth,  m_fEndWidth, m_nFadeLength, m_fAmplitude, a, 0.1 * m_nSpeed,
///     m_nStartFrame, 0.1 * m_nFrameRate, r, g, b, TE_BEAMLASER );
/// </code>
///
/// <para>
/// THE WHOLE DIFFERENCE FROM <see cref="ClientTeBeamEnts"/> IS THE LAST
/// ARGUMENT. Same primitive, same fields, same table shape — <c>DT_TEBeamLaser</c>
/// is prop for prop <c>DT_TEBeamEnts</c>. Stock spends a second ServerClass, a
/// second ClientClass and a second table on one integer because the set of
/// registered names has to be exactly stock's (plan 2E), and this port does the
/// same rather than folding the two into one effect with a parameter.
/// </para>
///
/// <para>
/// BEHAVIOUR LEDGER ROW C8 APPLIED HERE AND IS NOW CLOSED. It takes two entity
/// endpoints, and the endpoint resolve answers for the managed hierarchy — see
/// <see cref="ClientTeBeamEnts"/> for the mechanism, and for why removing the
/// call while the row was open would have been the wrong way to make a lane look
/// finished.
/// </para>
/// </summary>
[ClientTempEnt("CTEBeamLaser")]
public sealed class ClientTeBeamLaser : ClientTempEnt<TeBeamLaser>
{
    /// <summary>
    /// <c>TE_BEAMLASER</c>, <c>game/shared/tempentity.h:31</c>. A literal here
    /// and not a shared constant, because it has exactly one user in the whole
    /// port and stock names it at exactly one call site too; the value's home is
    /// the header, and the number is written down beside the name it came from.
    /// </summary>
    private const int TeBeamLaser = 8;

    private readonly IClientBeams _beams;

    public ClientTeBeamLaser(IClientBeams beams)
    {
        _beams = beams;
    }

    public override void Play(TeBeamLaser te)
    {
        // ONE READ FOR BOTH HALVES. TeBeamLaser IS a TeBaseBeam, so the base's
        // fifteen fields and this table's own come out of the same walk.

        _beams.CreateBeamEnts(te.StartEntity, te.EndEntity,
            te.ModelIndex, te.HaloIndex, haloScale: 0f,
            te.Life, te.Width, te.EndWidth, te.FadeLength, te.Amplitude,
            brightness: te.Color.A, te.ScrollSpeed,
            te.StartFrame, te.AnimationRate,
            te.Color.R, te.Color.G, te.Color.B, TeBeamLaser);
    }
}
