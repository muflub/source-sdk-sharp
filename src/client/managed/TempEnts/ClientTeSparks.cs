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
/// <c>CTESparks</c>, played. The class S-Phase 1 sent first and the client
/// could not receive until now.
///
/// <para>
/// The whole of <c>c_te_sparks.cpp</c>'s <c>C_TESparks::PostDataUpdate</c>:
/// </para>
/// <code>
/// g_pEffects->Sparks( m_vecOrigin, m_nMagnitude, m_nTrailLength, &amp;m_vecDir );
/// </code>
///
/// <para>
/// WHY THIS ONE COULD NOT BE C-PHASE 1's, which is worth saying here because it
/// is the constraint that shaped the whole client half of the port.
/// <c>DT_TESparks</c> nests <c>DT_TEParticleSystem</c>, the client resolves a
/// nested table BY NAME and cannot hold two, and a BASE table moves to managed
/// only when EVERY native user of its name is evicted (2E-bis). That base had
/// seven users. So Sparks waited for its six siblings, all of them in this
/// lane, and <c>CTEEnergySplash</c> — which is <c>NOBASE</c> and nests nothing
/// — proved the machinery instead.
/// </para>
///
/// <para>
/// IT NEEDED NO BACKEND SLOT. <c>ClientSparks</c> arrived with C-Phase 2's
/// fifteen and is exactly this effect's primitive already, which is why the
/// particle-system subtree added eight slots for seven effects rather than
/// nine.
/// </para>
///
/// <para>
/// THE DIRECTION IS A POINTER ALL THE WAY DOWN in stock —
/// <c>IEffects::Sparks</c> declares it <c>const Vector *pvecDir = NULL</c> and
/// <c>FX_ElectricSpark</c> reads the pointer — so a null is a spark with no
/// preferred direction rather than one aimed at the world origin. The wire
/// always carries one, so this always passes one; the optional shape is on the
/// slot because the local <c>TE_Sparks</c> forward can be called without.
/// </para>
///
/// <para>
/// NOT PORTED: <c>RecordSparks</c>, the tool-recording KeyValues message. SFM
/// is dead (user ruling 2026-10-19). <c>Precache()</c> was empty.
/// </para>
/// </summary>
[ClientTempEnt("CTESparks")]
public sealed class ClientTeSparks : ClientTempEnt<TeSparks>
{
    private readonly IEngineEffects _effects;

    public ClientTeSparks(IEngineEffects effects)
    {
        _effects = effects;
    }

    public override void Play(TeSparks te)
    {
        // Origin belongs to DT_TEParticleSystem, the BASE, and arrives with this
        // table's own three: a base nested at datatable offset zero puts its
        // props at their own offsets in the same blob, and the declaration
        // reader walks the chain root-first.
        //
        // Magnitude and TrailLength are four bits each, UNSIGNED, which the
        // declaration states rather than this method choosing. FX_ElectricSpark
        // multiplies the magnitude into a particle count, so a sign error here
        // is a very large number of particles rather than a wrong-looking spark.

        _effects.ClientSparks(te.Origin, te.Magnitude, te.TrailLength, te.Direction);
    }
}
