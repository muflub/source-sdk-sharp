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
/// <c>CTEKillPlayerAttachments</c>, played. "Drop everything stuck to that
/// player."
///
/// <para>
/// The whole of <c>c_te_killplayerattachments.cpp</c>'s
/// <c>C_TEKillPlayerAttachments::PostDataUpdate</c>:
/// </para>
/// <code>
/// tempents-&gt;KillAttachedTents( m_nPlayer );
/// </code>
///
/// <para>
/// THE FIRST OF THE ELEVEN POOL-BOUND TEMP ENTITIES, and the one that could
/// land first because it is the only one that needs no drawing primitive at
/// all. Every other class in that group ends in a spawn helper — a sprite, a
/// bubble, a gib — and a spawn helper needs a slot that sets a tempent's twenty
/// fields. This one ends in a WALK OF THE ACTIVE LIST, and the active list is
/// exactly what Phase C4 moved. Lane CN reached this class and stopped here for
/// that reason; <c>shell_te_fwd_cn.cpp</c>'s banner records it.
/// </para>
///
/// <para>
/// SO THERE IS NO BACKEND SLOT BEHIND THIS ONE, and that is not a gap:
/// <see cref="ClientTempEntPool.KillAttachedTents"/> is the effect, in C#, over
/// the same <c>TempEntPoolReadLife</c>/<c>WriteLife</c> pair everything else in
/// the pool reads and writes. The rule it applies is stock's, unchanged —
/// every <c>FTENT_PLYRATTACHMENT</c> tempent whose <c>clientIndex</c> is this
/// player has its <c>die</c> set to now, and dies on the next pool update.
/// </para>
///
/// <para>
/// NOTHING IN THIS BUILD SETS <c>FTENT_PLYRATTACHMENT</c>, and saying so is
/// worth more than pretending otherwise. The only thing that ever set it is
/// <c>CTempEnts::AttachTentToPlayer</c>, whose callers are HL1's, so this
/// temp entity currently walks a list and matches nothing. That is stock's own
/// state in this build, not something the port did: the server half has no
/// caller either (<c>SourceSharp.Shared/TempEnts/TeKillPlayerAttachments.cs</c>
/// records the same fact for the sending side). The class is ported because
/// plan 0.2b requires the two sides' class counts to come out equal, and
/// because when <c>AttachTentToPlayer</c> does move, its consumer is already
/// here.
/// </para>
///
/// <para>
/// NO LEDGER ROW. Nothing was guarded out to make this work: the native class
/// is evicted whole and its two entry points — the received event and
/// <c>c_te.cpp</c>'s <c>TE_KillPlayerAttachments</c> — both end in the same
/// managed walk, the second through <c>shell_te_fwd_c4.cpp</c>. Stock's
/// <c>Precache()</c> was empty, so nothing else left with the file.
/// </para>
/// </summary>
[ClientTempEnt("CTEKillPlayerAttachments")]
public sealed class ClientTeKillPlayerAttachments : ClientTempEnt<TeKillPlayerAttachments>
{
    private readonly ClientTempEntPool _pool;

    public ClientTeKillPlayerAttachments(ClientTempEntPool pool)
    {
        _pool = pool;
    }

    public override void Play(TeKillPlayerAttachments te)
    {
        // Five bits, SPROP_UNSIGNED -- so 0..31, and stock's own upper guard
        // (client > gpGlobals->maxClients, with MAX_PLAYERS 33) cannot be
        // reached from the wire. See KillAttachedTents for the whole of why
        // that guard is not carried. The unsignedness is the declaration's to
        // state, not this call site's to choose.

        _pool.KillAttachedTents(te.Player);
    }
}
