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
/// <c>CTEMetalSparks</c>, played — a round off metal, with no sound.
///
/// <para>
/// The whole of <c>c_te_armorricochet.cpp</c>'s
/// <c>C_TEMetalSparks::PostDataUpdate</c>, minus the decode:
/// </para>
/// <code>
/// g_pEffects->MetalSparks( m_vecPos, m_vecDir );
/// RecordMetalSparks( m_vecPos, m_vecDir );
/// </code>
///
/// <para>
/// <b>THE FILE IS NAMED FOR ITS OTHER CLASS.</b> Both
/// <c>C_TEMetalSparks</c> and <c>C_TEArmorRicochet</c> live in
/// <c>c_te_armorricochet.cpp</c>, so evicting one evicts both and a lane
/// scanning by filename misses this half entirely. Lane E lost time to exactly
/// this shape on <c>te_glassshatter.cpp</c>. <see cref="ClientTeArmorRicochet"/>
/// is the other half and the two landed together, which they had to: the
/// managed <c>CTEArmorRicochet</c> nests <c>DT_TEMetalSparks</c> BY NAME, and
/// the client resolves a nested data table by name and cannot hold two of one
/// (plan 2E-bis). Neither could move without the other.
/// </para>
///
/// <para>
/// <b>WHAT MOVED AND WHAT DID NOT.</b> The decode moved: stock's two
/// <c>RECVINFO</c> members are two named reads out of the blob the server wrote
/// through. The decision moved with it — for a metal spark that is "always,
/// unconditionally", and saying so is the point rather than a shrug: this class
/// has no violence convar, no distance cull and no state, so a spark that does
/// not appear is a table failure and nothing else. The drawing did not move:
/// <see cref="IEngineEffects.ClientMetalSparks"/> is
/// <c>g_pEffects->MetalSparks</c>, which is <c>FX_MetalSpark</c>. Stock's
/// <c>Precache()</c> is a commented-out <c>LoadModel</c> and carried nothing.
/// </para>
///
/// <para>
/// <b>WHAT DID NOT COME WITH IT, SAID OUT LOUD: <c>RecordMetalSparks</c>.</b>
/// Stock's <c>PostDataUpdate</c> posts a <c>KeyValues</c> tool message beside
/// the draw, for SFM-style recording. It is guarded by <c>ToolsEnabled()</c>,
/// which is <c>m_pTools &amp;&amp; m_pTools->InToolMode()</c>
/// (<c>toolframework_client.cpp:49</c>) and therefore false in every non-tool
/// launch of this mod — so nothing observable stops happening today. It is
/// still a capability that lapses with the eviction, and this port's rule is
/// that such a thing is written down rather than left to be noticed: it is
/// reported to the integrator as a ledger candidate, in the shape lane S5's
/// <c>DoSpark</c> finding took. <b>It is NOT closed by pretending the draw
/// covers it</b>, and it is not fixed here: the tool message would go in
/// <c>SourceSharp_ClientMetalSparks</c>, beside the <c>g_pEffects</c> call it
/// already makes, which is C-Phase 2's file and not this lane's.
/// </para>
/// </summary>
[ClientTempEnt("CTEMetalSparks")]
public sealed class ClientTeMetalSparks : ClientTempEnt<TeMetalSparks>
{
    private readonly IEngineEffects _effects;

    public ClientTeMetalSparks(IEngineEffects effects)
    {
        _effects = effects;
    }

    public override void Play(TeMetalSparks te)
    {
        // THROUGH THE DECLARATION, which is what makes the hazard this comment
        // used to describe unreachable. The wire spells these m_vecPos and
        // m_vecDir, NOT m_vecOrigin and m_vecDirection -- its lane-mate
        // DT_TEBloodSprite uses the other pair of names for the same two
        // quantities -- and getting that wrong here was a warning and a zero
        // vector rather than a compile error. The names now come from
        // TeMetalSparks' own members.

        _effects.ClientMetalSparks(te.Position, te.Direction);
    }
}
