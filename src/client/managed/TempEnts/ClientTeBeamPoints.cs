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
/// <c>CTEBeamPoints</c>, played — a beam between two points in the world.
///
/// <para>
/// The whole of <c>c_te_beampoints.cpp</c>'s <c>PostDataUpdate</c>:
/// </para>
/// <code>
/// beams->CreateBeamPoints( m_vecStartPoint, m_vecEndPoint, m_nModelIndex, m_nHaloIndex, 0.0f,
///     m_fLife, m_fWidth,  m_fEndWidth, m_nFadeLength, m_fAmplitude, a, 0.1 * m_nSpeed,
///     m_nStartFrame, 0.1 * m_nFrameRate, r, g, b );
/// </code>
///
/// <para>
/// ONE OF THE THREE IN THIS LANE THAT DRAWS TODAY. It takes POINTS, so nothing
/// about it reaches the endpoint resolution that ledger row C8 is about, and it
/// is unaffected by whether the entities near it are managed. Its two live
/// server callers — <c>CEffectsServer::Beam</c> and <c>UTIL_Beam</c>, both
/// through <c>te->BeamPoints</c> — reached stock's client half before this
/// commit and reach this one after it.
/// </para>
///
/// <para>
/// <c>0.0f</c> IS THE HALO SCALE AND IS NOT A PLACEHOLDER. All seven of stock's
/// beam temp entities pass zero: <c>DT_BaseBeam</c> has no halo-scale prop, so
/// there is nothing on the wire to pass and the renderer's own default is what
/// the effect has always used.
/// </para>
/// </summary>
[ClientTempEnt("CTEBeamPoints")]
public sealed class ClientTeBeamPoints : ClientTempEnt<TeBeamPoints>
{
    private readonly IClientBeams _beams;

    public ClientTeBeamPoints(IClientBeams beams)
    {
        _beams = beams;
    }

    public override void Play(TeBeamPoints te)
    {
        // ONE READ FOR BOTH HALVES. TeBeamPoints IS a TeBaseBeam, so the base's
        // fifteen fields and this table's own come out of the same walk.

        _beams.CreateBeamPoints(te.StartPoint, te.EndPoint,
            te.ModelIndex, te.HaloIndex, haloScale: 0f,
            te.Life, te.Width, te.EndWidth, te.FadeLength, te.Amplitude,
            brightness: te.Color.A, te.ScrollSpeed,
            te.StartFrame, te.AnimationRate,
            te.Color.R, te.Color.G, te.Color.B);
    }
}
