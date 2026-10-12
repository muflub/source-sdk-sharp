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
/// <c>CTEBeamRing</c>, played — a ring of beam connecting two entities.
///
/// <para>
/// The whole of <c>c_te_beamring.cpp</c>'s <c>PostDataUpdate</c>:
/// </para>
/// <code>
/// beams->CreateBeamRing( m_nStartEntity, m_nEndEntity, m_nModelIndex, m_nHaloIndex, 0.0f,
///     m_fLife, m_fWidth, m_fEndWidth, m_nFadeLength, m_fAmplitude, a, 0.1 * m_nSpeed,
///     m_nStartFrame, 0.1 * m_nFrameRate, r, g, b, m_nFlags );
/// </code>
///
/// <para>
/// ITS TWO ENTITY PROPS ARE <c>MAX_EDICT_BITS</c> WIDE WHERE FIVE SIBLINGS USE A
/// LITERAL 24, and the two forms are NOT unified — lane A measured them
/// forty-four bits apart on the wire. That difference is entirely upstream of
/// this file: by the time an index reaches here it is an int either way, and
/// this side cannot tell which width carried it. It is written down anyway,
/// because the obvious tidy-up is to make the seven readers identical and the
/// declaration is where that would do damage
/// (<see cref="SourceSharp.Shared.TeBeamRing"/> says what damage).
/// </para>
///
/// <para>
/// BEHAVIOUR LEDGER ROW C8 IS CLOSED AND THIS DRAWS AT AN ENTITY NOW. Both ends
/// are entities, and <c>IClientBeams.CreateBeamRing</c> no longer refuses a
/// managed one — see <see cref="ClientTeBeamEnts"/> for the mechanism. The ring
/// around a POINT is <see cref="ClientTeBeamRingPoint"/> and always drew.
/// </para>
///
/// <para>
/// ONE OF THE TWO IN THE LANE THAT PASSES <c>m_nFlags</c> ON, which is how
/// <c>FBEAM_*</c> reaches the renderer at all: the other five primitives take no
/// flags argument.
/// </para>
/// </summary>
[ClientTempEnt("CTEBeamRing")]
public sealed class ClientTeBeamRing : ClientTempEnt<TeBeamRing>
{
    private readonly IClientBeams _beams;

    public ClientTeBeamRing(IClientBeams beams)
    {
        _beams = beams;
    }

    public override void Play(TeBeamRing te)
    {
        // ONE READ FOR BOTH HALVES. TeBeamRing IS a TeBaseBeam, so the base's
        // fifteen fields and this table's own come out of the same walk.

        _beams.CreateBeamRing(te.StartEntity, te.EndEntity,
            te.ModelIndex, te.HaloIndex, haloScale: 0f,
            te.Life, te.Width, te.EndWidth, te.FadeLength, te.Amplitude,
            brightness: te.Color.A, te.ScrollSpeed,
            te.StartFrame, te.AnimationRate,
            te.Color.R, te.Color.G, te.Color.B, te.Flags);
    }
}
