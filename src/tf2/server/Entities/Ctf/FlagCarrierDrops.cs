//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// The carrier's arms of <c>CTFPlayer</c> that let go of the flag, heard
/// from the core rather than written into the player: a death
/// (<c>Event_Killed</c>: the killer's defend event, then
/// <c>RemoveAllItems</c>' drop where the carrier died) and a disconnect
/// (<c>TeamFortress_ClientDisconnected</c>'s <c>RemoveAllItems</c>).
///
/// <para>
/// The other drops the file's neighbours answer: a forced team change is
/// <see cref="FlagTeamChangeDrops"/> below (stock's ForceChangeTeam,
/// tf_player.cpp:6495-6574), a forced respawn — the class change's instant
/// one and <c>game_forcerespawn</c>' — is <see cref="FlagRespawnDrops"/>
/// (ForceRespawn's <c>DropFlag</c>, tf_player.cpp:14625-14627).
/// </para>
///
/// <para>
/// Not here (docs/plans/tf2/deferred.tsv): the feign-death drop
/// (tf_player.cpp:15704-15707 — Dead Ringer is not ported), the
/// Weapon_Equip reactive drop of a weapon that forbids carrying (:20588-20599
/// — ground weapon pickup is not ported), and the defend's sniper and stats
/// arms.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class FlagCarrierDrops(ICtfRules rules, ICtfPlayers players, ICtfEvents events)
    : IPlayerDeathHook, IClientLifecycle, IOrderedHook
{
    public int HookOrder => CtfOrders.CarrierDrops;

    public int ClientLifecycleOrder => CtfOrders.CarrierDrops;

    public void OnPlayerDied(PlayerDeath death)
    {
        if (rules.CarriedBy(death.Player) is not { } flag)
            return;

        // If the player has a capture flag and was killed by another player, award that player a defense
        if (death.Attacker != EntityHandle.None && death.Attacker != death.Player && players.IsPlayer(death.Attacker))
        {
            events.FlagEvent(TFFlagEvent.Defend, players.Index(death.Attacker), flag.Team, priority: 8,
                             carrier: death.EntIndex);
        }

        // Remove all items...
        flag.RemoveFromCarrier(death.Player);
    }

    public void OnClientDisconnected(int entIndex)
    {
        foreach (var flag in rules.Flags)
        {
            if (flag.IsStolen && flag.Carrier != EntityHandle.None && flag.Carrier.EntryIndex == entIndex)
                flag.RemoveFromCarrier(flag.Carrier);
        }
    }
}

/// <summary>
/// The <c>dropitem</c> command (tf_player.cpp:16068-16083): a carrier lets
/// go of the flag (a thrown drop: they may not take it back for three
/// seconds). The powerup rune's arm is not ported (no powerup mode).
/// </summary>
[SideService(Side.Server)]
public sealed class TFDropItemCommand(ICtfRules rules) : IClientCommandHook, IOrderedHook
{
    /// <summary>The stock word.</summary>
    public const string Command = "dropitem";

    public int HookOrder => CtfOrders.DropItemCommand;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Command, StringComparison.OrdinalIgnoreCase))
            return false;

        rules.CarriedBy(player)?.DropFlag(player);
        return true;
    }
}

/// <summary>
/// The carrier's arms of <c>CTFPlayer::ForceChangeTeam</c>
/// (tf_player.cpp:6495-6574): the team change ends with
/// <c>DropFlag()</c> (:6571), a thrown drop. Ordered after
/// <see cref="TF2.Server.Rules.TFChangeTeamKillRule"/>, so the live
/// Red↔Blue switch that that rule kills has already lost the flag to the
/// death hook by the time this watcher is asked, and only the changes that
/// walk past the kill — a departure to spectator, a join that killed nobody —
/// still find a carrier here.
/// </summary>
[SideService(Side.Server)]
public sealed class FlagTeamChangeDrops(ICtfRules rules) : ITeamChangeWatcher, IOrderedHook
{
    public int HookOrder => CtfOrders.TeamChangeDrop;

    public void TeamChanged(EntityHandle player, int oldTeam, int newTeam)
        => rules.CarriedBy(player)?.DropFlag(player);
}

/// <summary>
/// The carrier's arms of <c>CTFPlayer::ForceRespawn</c>
/// (tf_player.cpp:14576-14700): the respawn drops the flag
/// (<c>DropFlag()</c>, :14625-14627) before its items are stripped — the
/// class change's instant redeploy and <c>game_forcerespawn</c> both walk
/// this hook. A carrier who died dropped at the death hook already.
/// </summary>
[SideService(Side.Server)]
public sealed class FlagRespawnDrops(ICtfRules rules) : IPlayerSpawnHook, IOrderedHook
{
    public int HookOrder => CtfOrders.CarrierRespawnDrop;

    public void OnSpawning(EntityHandle player, float now)
        => rules.CarriedBy(player)?.DropFlag(player);

    public void OnSpawned(EntityHandle player, float now) { }
}
