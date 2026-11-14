//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Sounds;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The Spy's disguise puff and sound, stock's client arms
/// <c>CTFPlayerShared::OnAddDisguising</c>, <c>OnAddDisguised</c> and
/// <c>OnRemoveDisguised</c> (<c>tf_player_shared.cpp:3351-3392</c>,
/// :7230-7263) as the condition driver's arm. On the DISGUISING add: a
/// following puff — <c>spy_start_disguise_red</c> or <c>spy_start_disguise_blue</c>
/// by the team, <c>PATTACH_ABSORIGIN_FOLLOW</c>, which the shell's
/// <see cref="IEngineEffects.ParticleSystemStart"/> is — for every player but
/// the local one (:3362-3367), and for a stealthed enemy no puff (:3362: the
/// puff hides whom the Spy took); then <c>Player.Spy_Disguise</c> from the
/// player, the local one included (:3369). The DISGUISED add only holds the
/// puff's handle: stock's body forgets it (:3384, its StopEmission commented
/// at the same hand), the puff runs its own duration, so nothing stops here.
/// The DISGUISED removal answers the same sound (:7250); its model reset,
/// save-me and taunt-with-me stops and crit refresh are their own rows in
/// <c>docs/plans/tf2/deferred.tsv</c>.
///
/// <para>
/// Stock's predicted re-run skip (<c>GetPredictable() &amp;&amp;
/// (!prediction-&gt;IsFirstTimePredicted() || m_bSyncingConditions)</c>,
/// :3354, :7236) needs no twin: this port's arms run once per decode, from
/// <see cref="ClientTFPlayerData.OnDataChanged"/>, whose word diff fires an arm
/// only on the bit's real transition — a prediction re-run replays the
/// player's own moves, it does not re-decode the words, and the sync pass
/// holds its own words until the shell arrives. As the other arms
/// (<see cref="TFBurningEffect"/>, <see cref="TFConditionDrips"/>) carry no
/// guard either.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFDisguiseEffects(
    IEngineEffects effects,
    IEngineHud hud,
    TFClientCarriers carriers,
    TFClientPlayerResources resources,
    IClientEntitySounds sounds) : ITFClientConditionHook, IOrderedHook
{
    /// <summary>The puff's name for a red-team spy (:3364).</summary>
    public const string RedEffect = "spy_start_disguise_red";

    /// <summary>The puff's name for every other team's spy (:3364).</summary>
    public const string BlueEffect = "spy_start_disguise_blue";

    /// <summary>The disguise's sound script, emitted from the player (:3369, :7250).</summary>
    public const string SoundName = "Player.Spy_Disguise";

    private readonly TFConditionEffects _conds = new();

    public int HookOrder => TFClientConditionOrders.DisguiseEffects;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        switch (cond)
        {
            case TFCond.Disguising:
                // The puff is for the other players only (:3362): the local
                // Spy sees his own world already, as stock's IsLocalPlayer
                // gate says; he hears the sound like anyone (:3369).
                if (player.EntryIndex != hud.LocalPlayerIndex && ShowsPuff(player))
                    effects.ParticleSystemStart(player, TeamName(player));

                sounds.EmitFrom(player, SoundName);
                break;

            case TFCond.Disguised:
                // Stock's OnAddDisguised holds the puff's handle and forgets it
                // (:3380-3385): the puff, started as its own follow-on-shot,
                // runs its duration; there is nothing to stop or remember.
                break;
        }
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.Disguised)
            sounds.EmitFrom(player, SoundName);
    }

    /// <summary>
    /// Stock's gate on the puff (:3362): shown unless a stealthed enemy —
    /// a stealthed teammate still puffs, as stock's second clause answers.
    /// </summary>
    private bool ShowsPuff(EntityHandle player)
    {
        TFCondMask conds = carriers.Of(player.EntryIndex) is { } carrier
            ? carrier.State.Conditions()
            : default;

        return !_conds.IsStealthed(in conds)
               || !ClientTFPlayerData.IsEnemyViewer(TeamAt(hud.LocalPlayerIndex), TeamAt(player.EntryIndex));
    }

    /// <summary>The puff follows the player's own team's colour (:3364).</summary>
    private string TeamName(EntityHandle player)
        => TeamAt(player.EntryIndex) == (int)TFTeam.Red ? RedEffect : BlueEffect;

    /// <summary>The team the scoreboard carries for that slot; not red means blue.</summary>
    private int TeamAt(int entryIndex)
        => resources.Live is { } resource ? resource.State.Team[entryIndex] : 0;
}
