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
/// <c>CTEBeamEnts</c>, played — a beam between two entities, which it then
/// follows for as long as it lives.
///
/// <para>
/// The whole of <c>c_te_beaments.cpp</c>'s <c>PostDataUpdate</c>:
/// </para>
/// <code>
/// beams->CreateBeamEnts( m_nStartEntity, m_nEndEntity, m_nModelIndex, m_nHaloIndex, 0.0f,
///     m_fLife, m_fWidth,  m_fEndWidth, m_nFadeLength, m_fAmplitude, a, 0.1 * m_nSpeed,
///     m_nStartFrame, 0.1 * m_nFrameRate, r, g, b );
/// </code>
///
/// <para>
/// BEHAVIOUR LEDGER ROW C8 IS CLOSED, AND THE CALL BELOW IS WHAT CLOSED IT. A
/// beam that follows an ENTITY could not resolve its endpoint on this client:
/// <c>view_beams.cpp</c> stored the endpoint as an <c>EHANDLE</c> and read it
/// back through <c>CBaseHandle::Get()</c>, an unfiltered <c>LookupEntity()</c>
/// with no <c>SDKEntityFromUnknown</c> in the way — and this build's handle
/// table holds MANAGED entities, so the first virtual call in
/// <c>ComputeBeamEntPosition</c> dispatched through a mismatched vtable and the
/// client died. The endpoint is resolved through <c>cl_entitylist</c> now, which
/// answers for both hierarchies; an SDK endpoint still goes to stock's own
/// function unchanged. <c>IClientBeams.CreateBeamEnts</c> no longer refuses
/// anything.
/// </para>
///
/// <para>
/// AND THE CALL SURVIVING IS WHAT MADE CLOSING IT POSSIBLE. While the row was
/// open this effect usually did not draw, and the temptation was to remove the
/// call to make a lane look clean — which would have turned a tracked gap into
/// an untracked one, strictly worse than the state it started in. It is still
/// asserted, by <c>TempEntBeamClientTests</c>, for the opposite reason now: a
/// player that stopped calling its backend would be a beam that silently
/// stopped being drawn. An endpoint outside the PVS is unchanged and still
/// <c>view_beams.cpp</c>'s own case — it keeps its previous point, which is
/// what a beam follow whose subject has died is supposed to do.
/// </para>
///
/// <para>
/// NO TYPE ARGUMENT, WHICH IS THE ONE THING SEPARATING THIS FROM
/// <see cref="ClientTeBeamLaser"/>. The two tables are prop for prop identical;
/// stock keeps them apart because this one takes <c>CreateBeamEnts</c>' default
/// type and the laser passes <c>TE_BEAMLASER</c>. <c>-1</c> is that default
/// spelled out — the ABI slot has no default arguments, and the value reaches
/// <c>CreateBeamEnts( ..., int type = -1 )</c> unchanged.
/// </para>
/// </summary>
[ClientTempEnt("CTEBeamEnts")]
public sealed class ClientTeBeamEnts : ClientTempEnt<TeBeamEnts>
{
    /// <summary>
    /// <c>CreateBeamEnts</c>' own default, which stock reaches by omitting the
    /// argument. Not <c>TE_BEAMPOINTS</c> (which is 0): the primitive tests for
    /// -1 and picks the kind itself.
    /// </summary>
    private const int PrimitiveDefaultType = -1;

    private readonly IClientBeams _beams;

    public ClientTeBeamEnts(IClientBeams beams)
    {
        _beams = beams;
    }

    public override void Play(TeBeamEnts te)
    {
        // ONE READ FOR BOTH HALVES. TeBeamEnts IS a TeBaseBeam, so the base's
        // fifteen fields and this table's own come out of the same walk.

        // BEAMENT-ENCODED AND NOT UNPACKED HERE. The low twelve bits are an
        // entity index and the next four an attachment; view_beams.cpp is what
        // splits them, and a second splitter on this side would be a second
        // description of an encoding that already has one.
        _beams.CreateBeamEnts(te.StartEntity, te.EndEntity,
            te.ModelIndex, te.HaloIndex, haloScale: 0f,
            te.Life, te.Width, te.EndWidth, te.FadeLength, te.Amplitude,
            brightness: te.Color.A, te.ScrollSpeed,
            te.StartFrame, te.AnimationRate,
            te.Color.R, te.Color.G, te.Color.B, PrimitiveDefaultType);
    }
}
