//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTESmoke</c>, played. A smoke sprite puffing at a point.
///
/// <para>
/// The whole of <c>c_te_smoke.cpp</c>'s <c>C_TESmoke::PostDataUpdate</c>:
/// </para>
/// <code>
/// // The number passed down is 10 times smaller...
/// g_pEffects-&gt;Smoke( m_vecOrigin, m_nModelIndex, m_fScale * 10.0f, m_nFrameRate );
/// RecordSmoke( m_vecOrigin, m_fScale * 10.0f, m_nFrameRate );
/// </code>
///
/// <para>
/// THE TIMES TEN IS PART OF THE DECODE AND IS DONE HERE. Stock's comment above
/// the line is Valve's own, and the multiply appears TWICE in that file — once
/// in <c>PostDataUpdate</c> and once in the client-side <c>TE_Smoke</c> — for
/// the same reason: <c>m_fScale</c> is eight bits over [0, 25.6], so the wire
/// can only carry a tenth of the scale the effect wants. Doing it in the
/// backend instead would have made the ABI slot's <c>flScale</c> mean something
/// different from every other <c>flScale</c> on the boundary, and a caller
/// reading the header could not have told. <c>SourceSharp_ClientSmoke</c>'s own
/// comment says the multiply has already happened by the time it is called.
/// </para>
///
/// <para>
/// <c>m_nFrameRate</c> IS AN INT ON THE WIRE AND A FLOAT AT THE CALL, which is
/// stock's own widening: <c>IEffects::Smoke</c> takes
/// <c>( const Vector &amp;, int, float, float )</c> and the table declares eight
/// unsigned bits. The widening is here rather than in the slot so that the one
/// place a reader has to look to know what a smoke puff is stays this file.
/// </para>
///
/// <para>
/// LIVE FROM THE MOMENT IT REGISTERS. <c>CEffectsServer::Smoke</c> reaches
/// <c>te-&gt;Smoke</c> through <c>g_pEffects</c> and <c>UTIL_Smoke</c> reaches
/// the same place, so unlike most of this port's client halves this one has
/// server traffic on the day it lands rather than only when something managed
/// asks. See <see cref="Shared.TeSmoke"/>.
/// </para>
///
/// <para>
/// NO LEDGER ROW. <c>g_pEffects-&gt;Smoke</c> is <c>FX_Smoke</c>, ordinary
/// client drawing code that was never guarded, and stock's <c>Precache()</c>
/// was empty. The <c>RecordSmoke</c> tool message is not carried; that is the
/// one divergence and it is docs/divergences.md's, shared by all eighteen
/// recorders.
/// </para>
/// </summary>
[ClientTempEnt("CTESmoke")]
public sealed class ClientTeSmoke : ClientTempEnt<TeSmoke>
{
    /// <summary>
    /// Valve's own factor, with Valve's own comment on it — see the class note.
    /// A constant rather than a literal because it appears in the test that
    /// pins it, and the two must be the same number by construction.
    /// </summary>
    public const float ScaleFromWire = 10.0f;

    private readonly IEngineEffects _effects;

    public ClientTeSmoke(IEngineEffects effects)
    {
        _effects = effects;
    }

    public override void Play(TeSmoke te)
    {
        // UNSIGNED both: a model index and a frame rate are counts. The model
        // index is thirteen bits and the frame rate eight, and neither is ever
        // negative on this wire.

        float scale = te.Scale * ScaleFromWire;

        _effects.ClientSmoke(te.Origin, te.ModelIndex, scale, te.FrameRate);
    }
}
