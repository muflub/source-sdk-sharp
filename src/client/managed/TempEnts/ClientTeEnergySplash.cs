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
/// <c>CTEEnergySplash</c>, played. The first temp entity to come off the wire
/// into C#.
///
/// <para>
/// The whole of <c>c_te_energysplash.cpp</c>'s
/// <c>C_TEEnergySplash::PostDataUpdate</c>:
/// </para>
/// <code>
/// g_pEffects->EnergySplash( m_vecPos, m_vecDir, m_bExplosive );
/// </code>
///
/// <para>
/// WHY THIS CLASS AND NOT <c>CTESparks</c>, which is the one S-Phase 1 sends
/// from managed code and the one C-Phase 1 was written against. THIS IS THE
/// CONSTRAINT C-PHASE 3 NEEDS AND IT IS WHY IT IS WRITTEN HERE.
/// </para>
///
/// <para>
/// The client resolves a nested data table BY NAME and cannot hold two of one
/// name (see <c>tempent_schema.h</c> for the segfault and the silently-wrong
/// origin that fact cost). So a table moves to the managed client only when
/// EVERY NATIVE USER OF IT HAS BEEN EVICTED. For a concrete class that is one
/// file. For a BASE table it is every derived class at once —
/// <c>DT_TEParticleSystem</c> has seven client users and six of them
/// (Explosion, LargeFunnel, BloodStream, ShowLine, GaussExplosion, Dust) are
/// still native, so that base moves LAST and <c>CTESparks</c>, which nests it,
/// cannot be received in managed code before then. Renaming the managed copy
/// was tried and is not available: the server is shared by both client
/// implementations at once and its table names must be exactly stock's, which
/// the beams proved immediately.
/// </para>
///
/// <para>
/// <c>DT_TEEnergySplash</c> is <c>NOBASE</c>. Nothing nests anything, evicting
/// one file frees the name outright, and the machinery is proved end to end
/// today rather than at the end of C-Phase 3. It also exercises something
/// Sparks would not have: a field whose STORAGE is one byte and whose WIRE
/// WIDTH is one bit, which is where <c>SendPropInt</c>/<c>RecvPropInt</c> pick
/// their proxies from (<c>dt_send.cpp:492</c>, <c>dt_recv.cpp:202</c>) and the
/// one place a blob can be read three bytes past its own end.
/// </para>
///
/// <para>
/// WHAT A GREEN GATE 3 ON THIS CLASS DOES NOT PROVE, said here rather than left
/// to be assumed. <c>DT_TEEnergySplash</c> is NOBASE and nests nothing, so the
/// nested-datatable path is NOT exercised by it — not the <c>"baseclass"</c>
/// prop, and not an ordinary nested table like <c>DT_EffectData</c>. That path
/// has a crash of its own that lane S4 found the hard way: a datatable prop at
/// offset zero is marked <c>SPROP_COLLAPSIBLE</c> by the sender and applied by
/// the receiver at the OBJECT BASE. Both builders now refuse or warn about it
/// and <c>tempent_plan_test.cpp</c> pins the baseclass exemption, but the first
/// class to prove it on a live wire is a later one.
/// </para>
///
/// <para>
/// WHAT MOVED AND WHAT DID NOT. The decode moved: stock's three members at
/// RECVINFO offsets are three named reads out of a blob whose layout the server
/// also wrote through. The DECISION moved with it — which is nothing much for a
/// splash, and that is the point of taking a small one first: a failure is a
/// table failure and nothing else. The DRAWING did not move:
/// <c>ClientEnergySplash</c> is <c>g_pEffects->EnergySplash</c>. Nothing else
/// was lost with the class — stock's <c>Precache()</c> was empty.
/// </para>
/// </summary>
[ClientTempEnt("CTEEnergySplash")]
public sealed class ClientTeEnergySplash : ClientTempEnt<TeEnergySplash>
{
    private readonly IEngineEffects _effects;

    public ClientTeEnergySplash(IEngineEffects effects)
    {
        _effects = effects;
    }

    public override void Play(TeEnergySplash te)
    {
        // Every read is BY WIRE NAME, which is the same string the server wrote
        // through and the same string the two tables pair on. A misspelling here
        // is a warning and a default rather than a plausible number.

        // Bool, which is one bit on the wire and ONE BYTE in the blob. Read at
        // the declared storage width, because reading four would take three
        // bytes of whatever follows — and for the last field of a blob that is
        // past the end.

        _effects.ClientEnergySplash(te.Position, te.Direction, te.Explosive);
    }
}
