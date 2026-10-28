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
/// <c>CTEBeamFollow</c>, played — a trail of decaying beam segments behind one
/// entity, until it stops moving.
///
/// <para>
/// The whole of <c>c_te_beamfollow.cpp</c>'s <c>PostDataUpdate</c>:
/// </para>
/// <code>
/// beams->CreateBeamFollow( m_iEntIndex, m_nModelIndex, m_nHaloIndex, 0, m_fLife,
///     m_fWidth, m_fEndWidth, m_nFadeLength, r, g, b, a );
/// </code>
///
/// <para>
/// THE ARGUMENT ORDER IS NOT ITS SIX SIBLINGS' AND IS NOT TIDIED.
/// <c>CreateBeamFollow</c> takes <c>r, g, b</c> BEFORE brightness, where every
/// other <c>CreateBeam*</c> takes brightness in the middle — and it has no
/// amplitude, no speed, no start frame and no frame rate at all. Six of the
/// fifteen base fields therefore go unread here, which is not a decode that
/// forgot them: they are not on this primitive. A reader folding the seven
/// players into one shape produces a beam whose colour is its alpha, which is
/// why <c>te_backend_args_test.cpp</c> pins the order on the native side and why
/// this is spelled with named arguments below.
/// </para>
///
/// <para>
/// THE FIELD IS <c>EntIndex</c>, WHICH STOCK CALLED <c>m_iEntIndex</c> — its
/// one odd-man-out name in the lane, where the six siblings say
/// <c>m_nStartEntity</c>. Both sides read it out of <see cref="TeBeamFollow"/>
/// now, so the spelling agrees by construction rather than by care; reading a
/// name the declaration does not have is still a warning and a zero — a trail
/// behind the world — rather than an error, which is why this is one string and
/// not seven.
/// </para>
///
/// <para>
/// BEHAVIOUR LEDGER ROW C8 IS CLOSED, AND THIS IS THE CLASS THAT HAD MOST TO
/// GAIN. Following an entity IS the whole of this effect and there is no
/// point-taking form to fall back to, so while the row was open this drew
/// nothing at all — lane CA measured that entity 1 in this build is
/// <c>player</c>, which is managed, leaving the effect with no drawable
/// subject anywhere. <c>IClientBeams.CreateBeamFollow</c> no longer refuses one,
/// and the endpoint is re-resolved every frame through <c>cl_entitylist</c> so
/// the trail tracks the entity as it moves.
/// </para>
/// </summary>
[ClientTempEnt("CTEBeamFollow")]
public sealed class ClientTeBeamFollow : ClientTempEnt<TeBeamFollow>
{
    private readonly IClientBeams _beams;

    public ClientTeBeamFollow(IClientBeams beams)
    {
        _beams = beams;
    }

    public override void Play(TeBeamFollow te)
    {
        // ONE READ FOR BOTH HALVES. TeBeamFollow IS a TeBaseBeam, so the
        // base's fifteen fields and this table's own EntIndex come out of the
        // same walk.

        _beams.CreateBeamFollow(te.EntIndex,
            te.ModelIndex, te.HaloIndex, haloScale: 0f,
            te.Life, te.Width, te.EndWidth, te.FadeLength,
            te.Color.R, te.Color.G, te.Color.B, brightness: te.Color.A);
    }
}
