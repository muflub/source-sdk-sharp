using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>One player's death, as <see cref="IPlayerDeathHook"/> is told it.</summary>
/// <param name="Player">The dead player.</param>
/// <param name="EntIndex">Their entity index: what <c>IClientLifecycle</c> calls them.</param>
/// <param name="Attacker">The killer; not a valid entity when the world killed them.</param>
/// <param name="Origin">Where they died, read before anything of dying moved them.</param>
/// <param name="Body">The body they left, or <see cref="EntityHandle.None"/> if none was made.</param>
public readonly record struct PlayerDeath(EntityHandle Player, int EntIndex, EntityHandle Attacker,
                                          Vector Origin, EntityHandle Body);

/// <summary>
/// A player's death, as a hook: implement it on a server <c>[SideService]</c>
/// and <see cref="PlayerDeaths"/> calls it once per death, after the body is
/// made and the player's record is updated. Order among several is
/// <see cref="IOrderedHook"/>'s.
/// </summary>
public interface IPlayerDeathHook
{
    void OnPlayerDied(PlayerDeath death);
}

/// <summary>
/// A veto on respawning, as a hook: <see cref="Players.Think"/> asks every
/// gate before it respawns a dead player, and any <c>false</c> keeps them dead
/// (watching) for that frame. With no gates the stock cycle is unchanged. A
/// gate that throws is logged and does not veto, so a broken mod cannot trap
/// a player dead.
/// </summary>
public interface IRespawnGate
{
    bool MayRespawn(EntityHandle player, int entIndex);
}

/// <summary>
/// A player's death, in order: the body, the record, then the mods. What
/// <c>ServerModule.OnPlayerKilled</c> did inline, here so the order and the
/// dispatch are testable without a module.
/// </summary>
public sealed class PlayerDeaths(ILog log, IEngineEntities entities, Corpses corpses, Players players,
                                 IGameClock clock, IHooks<IPlayerDeathHook> hooks, DeathNotice deathNotice)
{
    public void OnKilled(EntityHandle player, EntityHandle attacker)
    {
        // WHERE THEY DIED, read before anything else: making the body can run
        // map logic, and the respawn cycle will shortly move this very entity.
        Vector origin = entities.GetOrigin(player);

        // THE BODY FIRST, before anything else touches the player. Stock has
        // the same ordering and the same reason: CHL2MP_Player::Event_Killed
        // calls CreateRagdollEntity as its first act, because the corpse is a
        // copy of where the player IS and every later step of dying moves them
        // -- OnKilled below strips their inventory, and the respawn cycle will
        // shortly teleport this very entity to a spawn point.
        EntityHandle body = corpses.Make(player);

        // Stock's PlayerKilled announces the death before it scores it
        // (CMultiplayRules::PlayerKilled -> DeathNotice): the engine's
        // player_death event, which the rules' scoreboard hears.
        deathNotice.Fire(player, attacker);

        players.OnKilled(player, attacker, clock.Now);

        var death = new PlayerDeath(player, entities.GetIndex(player), attacker, origin, body);
        hooks.Each(nameof(IPlayerDeathHook.OnPlayerDied), h => h.OnPlayerDied(death));

        log.Msg($"[SourceSharp/Server] {player} killed by " +
                $"{(entities.IsValid(attacker) ? attacker.ToString() : "world")} " +
                $"({players[player].Deaths} death(s))\n");
    }
}
