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
/// <c>CTEBeamEntPoint</c>, played — a beam with an entity at each end, a point
/// at each end, or one of each.
///
/// <para>
/// The whole of <c>c_te_beamentpoint.cpp</c>'s <c>PostDataUpdate</c>:
/// </para>
/// <code>
/// beams->CreateBeamEntPoint( m_nStartEntity, &amp;m_vecStartPoint, m_nEndEntity, &amp;m_vecEndPoint,
///     m_nModelIndex, m_nHaloIndex, 0.0f,
///     m_fLife,  m_fWidth, m_fEndWidth, m_nFadeLength, m_fAmplitude, a, 0.1 * m_nSpeed,
///     m_nStartFrame, 0.1 * m_nFrameRate, r, g, b );
/// </code>
///
/// <para>
/// BOTH POINTS ARE ALWAYS SENT, WHICH IS WHY THE ABI TAKES VECTORS AND NOT
/// POINTERS. Stock passes the addresses of two members, so the pointers are
/// never null and the primitive's "a NULL point means the origin" arm is
/// unreachable from this class; what decides "entity or point" is the ENTITY
/// index being zero or less, and <c>CreateBeamEntPoint</c> is where that
/// decision lives. The port does not take it over — a second copy of the
/// renderer's own rule is a copy that drifts.
/// </para>
///
/// <para>
/// BEHAVIOUR LEDGER ROW C8 IS CLOSED. An end described by an ENTITY reached the
/// same unfiltered <c>CBaseHandle::Get()</c> that <see cref="ClientTeBeamEnts"/>
/// describes, and <c>IClientBeams.CreateBeamEntPoint</c> refused a managed one;
/// the resolve answers for both hierarchies now. This was the ONE class in the
/// lane the refusal did not necessarily kill: an ent-point beam whose two
/// indices are both zero is two points, was never refused, and drew — which is
/// exactly the form <c>cs_te_fire beamentpoint</c> fires, the form the
/// superseded <c>cs_sv_te_beam</c> fired too, and the reason TeDemos keeps
/// it.
/// </para>
/// </summary>
[ClientTempEnt("CTEBeamEntPoint")]
public sealed class ClientTeBeamEntPoint : ClientTempEnt<TeBeamEntPoint>
{
    private readonly IClientBeams _beams;

    public ClientTeBeamEntPoint(IClientBeams beams)
    {
        _beams = beams;
    }

    public override void Play(TeBeamEntPoint te)
    {
        // ONE READ FOR BOTH HALVES. TeBeamEntPoint IS a TeBaseBeam, so the base's
        // fifteen fields and this table's own come out of the same walk.

        _beams.CreateBeamEntPoint(te.StartEntity, te.StartPoint, te.EndEntity, te.EndPoint,
            te.ModelIndex, te.HaloIndex, haloScale: 0f,
            te.Life, te.Width, te.EndWidth, te.FadeLength, te.Amplitude,
            brightness: te.Color.A, te.ScrollSpeed,
            te.StartFrame, te.AnimationRate,
            te.Color.R, te.Color.G, te.Color.B);
    }
}
