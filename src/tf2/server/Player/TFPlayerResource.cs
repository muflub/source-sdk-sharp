//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// <c>CTFPlayerResource::UpdatePlayerData</c>/<c>UpdateConnectedPlayer</c>
/// (tf_player_resource.cpp:119-330): each player's TF scoreboard row from
/// the carrier (the class), the class's health (<see cref="ITFPlayerResupply"/>),
/// the stats (<see cref="TFPlayerStats"/>: the total score is
/// <c>CalcPlayerScore</c> over the accumulated, the damage and healing rows
/// the round's, those sent once every <see cref="StatsSendFrequency"/>
/// seconds as stock's <c>m_flNextDamageAndHealingSend</c> has it) and the
/// respawn waves (the next wave a dead player's team gets). A slot with no
/// player is zeroed (<c>UpdateDisconnectedPlayer</c>'s <c>Init</c>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerResourceRows(TFPlayerCarriers carriers, ITFPlayerResupply resupply, TFPlayerStats stats,
                                         IEnginePlayers players, ITeamRespawnWaves? waves = null,
                                         Combat.TFDominations? dominations = null, ITeamRoundState? rounds = null)
{
    /// <summary><c>STATS_SEND_FREQUENCY</c> (tf_player_resource.cpp:17).</summary>
    public const float StatsSendFrequency = 1f;

    private float _nextDamageAndHealingSend;

    /// <summary>Every row, at <paramref name="now"/>.</summary>
    public void Update(TFPlayerResourceNetState state, float now)
    {
        // The gamerules' words (see the netstate's RoundState): written every
        // think, one value for the whole resource.
        state.RoundState = rounds is { } r ? (byte)r.State : default;
        state.WinningTeam = rounds is { } won ? (byte)won.WinningTeam : default;
        state.WaitingForPlayers = rounds is { } waiting && waiting.InWaitingForPlayers ? (byte)1 : default;
        state.InSetup = rounds is { } setup && setup.InSetup ? (byte)1 : default;
        state.MapResetTime = rounds?.MapResetTime ?? default;

        bool sendStats = now > _nextDamageAndHealingSend;
        if (sendStats)
            _nextDamageAndHealingSend = now + StatsSendFrequency;

        for (int i = 1; i < TFPlayerResourceNetState.Slots; i++)
        {
            if (carriers.Get(i) is null)
            {
                Clear(state, i);
                continue;
            }

            var player = players.GetHandle(i);
            if (sendStats)
            {
                state.Damage[i] = stats.Round(i, TFStat.Damage);
                state.DamageAssist[i] = stats.Round(i, TFStat.DamageAssist);
                state.Healing[i] = stats.Round(i, TFStat.Healing);
                state.HealingAssist[i] = stats.Round(i, TFStat.HealingAssist);
                state.DamageBlocked[i] = stats.Round(i, TFStat.DamageBlocked);
                state.BonusPoints[i] = stats.Round(i, TFStat.BonusPoints);
            }

            short max = (short)resupply.MaxHealth(player);
            state.MaxHealth[i] = max;
            state.MaxBuffedHealth[i] = max;
            state.PlayerClass[i] = (byte)carriers.ClassOf(player);
            state.Team[i] = (byte)players.GetTeam(player);
            state.TotalScore[i] = stats.Score(i);
            state.ActiveDominations[i] = (byte)(dominations?.CountOf(i) ?? 0);

            bool alive = players.IsAlive(player) && players.GetObserverMode(player) == ObserverMode.None;
            state.NextRespawnTime[i] = alive || waves is null ? 0f : waves.NextRespawnWave((TFTeam)players.GetTeam(player), player);

            // UpdatePlayerRespawnData's override arm (tf_player_resource.cpp:274-275):
            // the death time plus the override answers instead of the wave. Stock
            // reads it unguarded (a live player's pair stands -1 from the spawn
            // clear); the pair's fixed run-out moment is the same sum.
            if (carriers.Part<TFRespawnOverride>(i) is { HasOverride: true } held)
                state.NextRespawnTime[i] = held.ReleaseAt;
        }
    }

    private static void Clear(TFPlayerResourceNetState state, int i)
    {
        state.TotalScore[i] = 0;
        state.Team[i] = 0;
        state.MaxHealth[i] = 0;
        state.MaxBuffedHealth[i] = 0;
        state.PlayerClass[i] = 0;
        state.ActiveDominations[i] = 0;
        state.NextRespawnTime[i] = 0f;
        state.ChargeLevel[i] = 0;
        state.Damage[i] = 0;
        state.DamageAssist[i] = 0;
        state.Healing[i] = 0;
        state.HealingAssist[i] = 0;
        state.DamageBlocked[i] = 0;
        state.BonusPoints[i] = 0;
    }
}

/// <summary>
/// <c>tf_player_manager</c>: <see cref="TFPlayerResourceNetState"/> on the
/// hierarchy, always transmitted (stock's <c>FL_EDICT_ALWAYS</c>), its rows
/// refreshed every <see cref="ThinkInterval"/> seconds
/// (<c>PLAYER_RESOURCE_THINK_INTERVAL</c>, as the core's <c>player_manager</c>).
/// Made by <see cref="TFPlayerResourceSpawner"/> at level init, never by a map.
/// </summary>
[ScriptedEntity(TFEntityNames.PlayerResource, ScriptedEntityKind.Effect)]
public sealed class TFPlayerResourceEntity(TFPlayerResourceRows rows, IEngineEntityRender render, IGameClock clock)
    : ScriptedEntity
{
    /// <summary>The core's <c>PlayerResource.ThinkInterval</c>, stock's <c>ResourceThink</c>'s <c>curtime + 0.1f</c>.</summary>
    public const float ThinkInterval = 0.1f;

    /// <summary>What every client is told. Bound to this entity by the registry.</summary>
    [NetState] public TFPlayerResourceNetState State = new();

    public override void OnSpawn()
    {
        render.SetTransmitAlways(Handle, true);
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        rows.Update(State, clock.Now);
        SetNextThink(ThinkInterval);
    }
}

/// <summary>Creates <c>tf_player_manager</c> as each level loads (<c>CTFGameRules::CreateStandardEntities</c>).</summary>
[SideService(Side.Server)]
public sealed class TFPlayerResourceSpawner(IEngineEntities entities, ILog log) : IGameLifecycle
{
    public int LifecycleOrder => TFPlayerOrders.PlayerResource;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        var entity = entities.Create(TFEntityNames.PlayerResource);
        if (entity == EntityHandle.None)
        {
            log.Warning($"[TF2/Player] could not create {TFEntityNames.PlayerResource}; the scoreboard reads no TF columns\n");
            return;
        }

        entities.Spawn(entity);
        log.Msg($"[TF2/Player] {TFEntityNames.PlayerResource} up\n");
    }
}
