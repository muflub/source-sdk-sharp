//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// A TF player's spawn (<see cref="IPlayerSpawnHook"/> around the core's kit):
/// the class flow of <c>CTFPlayer::ForceRespawn</c> (tf_player.cpp:14576-14700)
/// and <c>Spawn</c> (:3561-3712).
///
/// <list type="bullet">
/// <item>Before the kit (<see cref="OnSpawning"/>): the desired class becomes
/// the class (<c>GetPlayerClass()->Init( iDesiredClass )</c>, counted in
/// <c>m_iClassChanges</c>), the player's items go (<c>RemoveAllItems( true )</c>,
/// so the kit is fresh, full clips and all), and every condition goes
/// (<c>m_Shared.RemoveAllCond()</c>, which ForceRespawn and Spawn both
/// run). The loadout provider then hands out the class's kit.</item>
/// <item>After the kit (<see cref="OnSpawned"/>): the class's health
/// (<c>InitClass</c>' <c>SetHealth( GetMaxHealth() )</c>, after the items),
/// the class's player model (<c>UpdateModel</c>), and a fresh crit bucket for
/// the weapon the kit filled (a stock weapon entity is new each spawn and its
/// constructor fills the bucket, basecombatweapon_shared.cpp:104-106).</item>
/// <item>A player with no class is an observer, as stock's is: the welcome
/// state's <c>StartObserverMode( OBS_MODE_FIXED )</c> before a team
/// (:13590), the observer state's chase cam on one (:13835), with no
/// weapons (<c>GiveDefaultItems</c>' <c>RemoveAllWeapons</c>).</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerSpawns(TFPlayerCarriers carriers, TFClassTable classes, IEnginePlayers players,
                                   IEngineEntities entities, IEngineEntityRender render,
                                   TFPlayerConditions conditions, TFCritBuckets buckets,
                                   IEntityDirectory directory, Observers observers, ITFCarriedWeapons weapons,
                                   TFSpawnGlowsDurationConVar spawnGlows, ILog log)
    : IPlayerSpawnHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.Spawn;

    public void OnSpawning(EntityHandle player, float now)
    {
        int index = player.EntryIndex;
        TFPlayerClass? cls = carriers.Part<TFPlayerClass>(index);
        if (cls is not null
            && cls.Desired is > TFClass.Undefined and < TFClass.CountAll
            && cls.Desired != carriers.ClassOf(player)
            && carriers.SetClass(index, cls.Desired))
        {
            cls.ClassChanges++;
        }

        // CTFPlayer::Spawn's m_bAllowInstantSpawn = false (tf_player.cpp:3710):
        // the spawn consumes a granted instant spawn (game_forcerespawn's).
        if (cls is { } granted)
            granted.AllowInstantSpawn = false;

        // ForceRespawn's RemoveAllItems( true ) before Spawn's GiveDefaultItems:
        // a respawn without a death (a round restart, a class change) gets a
        // fresh kit, full clips included, not the one it was holding.
        players.RemoveAllItems(player, stripSuit: true);
        conditions.RemoveAll(player);

        // CTFPlayerShared::Spawn's stun reset (tf_player_shared.cpp:1016-1025),
        // which CTFPlayer::Spawn runs beside the condition wipe.
        carriers.Part<TFStunStack>(index)?.ClearOnSpawn();

        // CTFPlayer::Spawn's SetRespawnOverride( -1.f, NULL_STRING )
        // (tf_player.cpp:1089): the old life's timed hold and named spot go
        // too, after the spawn walk has read them (the core selects the spot
        // before this hook runs).
        carriers.Part<TFRespawnOverride>(index)?.ClearOnSpawn();
    }

    public void OnSpawned(EntityHandle player, float now)
    {
        TFClass cls = carriers.ClassOf(player);
        if (cls is <= TFClass.Undefined or >= TFClass.CountAll)
        {
            Observe(player, now);
            return;
        }

        var data = classes[cls];
        entities.SetHealth(player, data.MaxHealth);

        // Spawn's team glows for a while after the respawn
        // (tf_player.cpp:3635-3636, inside the TF_STATE_ACTIVE arm this classed
        // branch is): m_Shared.AddCond( TF_COND_TEAM_GLOWS,
        // tf_spawn_glows_duration.GetInt() ), the countdown the player's
        // condition think runs.
        conditions.Add(player, TFCond.TeamGlows, spawnGlows.Value);

        // Spawn's SetSpyCloakMeter( 100.0f ) (tf_player.cpp:3712): a full watch.
        if (carriers.Get(player.EntryIndex) is { } state)
            state.CloakMeter = 100f;
        if (data.Model.Length > 0)
            render.SetModel(player, data.Model);

        var weapon = weapons.WeaponOf(player);
        if (weapon != EntityHandle.None)
            buckets.Reset(entities.GetIndex(weapon));

        log.Msg($"[TF2/Player] player {player.EntryIndex} spawned as {TFClassNames.NonLocalized(cls)}: " +
                $"{data.MaxHealth} health, {data.Model}\n");
    }

    /// <summary>The classless player watches: fixed before a team, the chase cam on one.</summary>
    private void Observe(EntityHandle player, float now)
    {
        players.RemoveAllItems(player, stripSuit: true);
        if (directory.Get(player) is not SsPlayer watcher)
            return;

        var mode = players.GetTeam(player) > (int)TFTeam.Spectator ? ObserverMode.Chase : ObserverMode.Fixed;
        watcher.Observer.EnterObserver = true;
        observers.Start(watcher, mode, now);
        log.Msg($"[TF2/Player] player {player.EntryIndex} has no class: observing ({mode})\n");
    }
}
