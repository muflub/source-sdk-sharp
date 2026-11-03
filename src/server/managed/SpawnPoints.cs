//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The placement's mod arm, as a hook of <see cref="SpawnPoints.Place"/>:
/// stock's <c>CGameRules::GetPlayerSpawnSpot</c> is virtual and a mod overrides
/// it to move the position before the one-unit lift
/// (<c>CTFGameRules::GetPlayerSpawnSpot</c> drops the spawning player to the
/// ground under the spot, shared/tf/tf_gamerules.cpp). A CHAIN: each hook is
/// given the previous one's answer, in <see cref="IOrderedHook.HookOrder"/>'s
/// order, and a hook that throws is named and passed through — a broken mod
/// must not strand a player on the origin.
/// </summary>
public interface ISpawnPlacement
{
    /// <summary>
    /// Where to put <paramref name="player"/> on <paramref name="spot"/>:
    /// <paramref name="placement"/> is the previous hook's answer, or the
    /// spot's own origin for the first hook of the chain.
    /// </summary>
    Vector PlaceAt(EntityHandle player, EntityHandle spot, Vector placement);
}

/// <summary>
/// Whether the core's walk kills for the spot it settles on, as a hook of
/// <see cref="SpawnPoints"/>. Killing is <c>CHL2MP_Player::EntSelectSpawnPoint</c>'s
/// rule (and CBasePlayer's before it), and a mod whose players have a walk of their
/// own has no such rule: <c>CTFPlayer::EntSelectSpawnPoint</c> never kills, so a
/// player its selector places nowhere (a spectator) must not kill by reaching the
/// core's walk. Every hook has to agree for the kill to happen.
/// </summary>
public interface ISpawnFragRule
{
    /// <summary>Whether <paramref name="spawning"/>, with every spot taken, kills those at the one it settles on.</summary>
    bool Frags(EntityHandle spawning);
}

/// <summary>
/// <c>hl2mp_spawn_frag_fallback_radius</c> (hl2mp_player.cpp:38): how far from the
/// spot a player settles on, with every spawn taken, the others are killed.
/// </summary>
[ConVar("hl2mp_spawn_frag_fallback_radius", "48",
        Help = "If no spawns are available, kill players with this radius to allow new players to spawn.")]
public sealed class Hl2mpSpawnFragFallbackRadiusConVar : FloatConVar { }

/// <summary>
/// Which spawn point a player enters the map on, and putting them there:
/// CBasePlayer::EntSelectSpawnPoint (the choice) and
/// CGameRules::GetPlayerSpawnSpot (the placement), asked from
/// <see cref="ServerModule.SelectSpawnPoint"/>.
///
/// <para>
/// This is a rule and it is written here, not in C++. The engine's own
/// search walks gEntList for a CBaseEntity called info_player_deathmatch, and
/// there is not one: the class is C#'s. Every branch of that search falls
/// through and leaves the player at the world origin, silently, which reads as
/// a broken map.
/// </para>
///
/// <para>
/// THE CHOICE AND THE PLACEMENT ARE TWO STEPS, and every choice goes through
/// the one placement. Stock's GetPlayerSpawnSpot places whatever
/// EntSelectSpawnPoint answered, whoever overrode the choice (CTFPlayer's team
/// walk is one such override). The placement used to sit at the end of the
/// core's own walk only, so a spot named by an <see cref="ISpawnSelector"/> or
/// the active mode's <see cref="IGameMode.Spawns"/> was returned to native,
/// which ignores the handle on purpose, and the player never moved.
/// </para>
///
/// <para>
/// It runs at the moment the engine asks, inside CBasePlayer::Spawn, before the
/// hull and the physics shadow exist. The first version teleported the player
/// from OnPlayerSpawn, after the shell had made them solid, and a player made
/// solid at the world origin and moved afterwards arrived embedded in whatever
/// was at the destination ("the player is spawning in the wall").
/// </para>
///
/// <para>
/// With every point of the core's walk taken, stock settles on the one the walk
/// began at and kills the other players within
/// <see cref="Hl2mpSpawnFragFallbackRadiusConVar"/> of it
/// (CHL2MP_Player::EntSelectSpawnPoint, hl2mp_player.cpp:1447-1457), and so does
/// this. An info_player_start is never fought over: stock places on it without
/// asking who is there.
/// </para>
/// </summary>
public sealed class SpawnPoints(IEngineEntities entities, IEnginePlayers players, IEngineWorldQuery world,
                                IEngineDamage damage, ConVarRegistry convars, Random random,
                                IHooks<ISpawnPlacement>? placement = null, IHooks<ISpawnFragRule>? fragRules = null,
                                ILog? log = null)
{
    /// <summary>The convar's default, for a composition that did not register it.</summary>
    private const float DefaultFragRadius = 48f;

    /// <summary>CTakeDamageInfo( world, world, 300, DMG_GENERIC ): more than any player has.</summary>
    private const float FragDamage = 300f;

    /// <summary>The world, entry 0, as stock's GetContainingEntity( INDEXENT( 0 ) ): nobody's kill.</summary>
    private static EntityHandle World => new(0u);

    private readonly IHooks<ISpawnPlacement>? _placement = placement;
    private readonly IHooks<ISpawnFragRule>? _fragRules = fragRules;
    private readonly ILog? _log = log;

    /// <summary>
    /// Chooses a spawn point for <paramref name="player"/> and places them on
    /// it; with none to choose from the player is left where they are. Answers
    /// the spot it placed on, or <see cref="EntityHandle.None"/>.
    /// </summary>
    public EntityHandle SelectAndPlace(EntityHandle player, ITeams? teams)
    {
        EntityHandle chosen = Choose(player, teams);

        if (chosen != EntityHandle.None)
            Place(player, chosen);

        return chosen;
    }

    /// <summary>
    /// The selectors first (tf2 stage 0.5/0.7): the active game mode's own
    /// <see cref="IGameMode.Spawns"/>, then the registered
    /// <see cref="ISpawnSelector"/> chain in HookOrder's order
    /// (<see cref="ITeams.SelectSpawn"/>), and the first named spot wins. None
    /// named is the core's walk, which is team-blind: HL2DM stays so until a
    /// mod registers otherwise.
    /// </summary>
    public EntityHandle Choose(EntityHandle player, ITeams? teams)
    {
        if (teams is not null)
        {
            EntityHandle selected = teams.SelectSpawn(player);
            if (selected != EntityHandle.None)
                return selected;
        }

        return Walk(player);
    }

    /// <summary>
    /// The core's walk. Deathmatch points first, and info_player_start only if
    /// there are none. They are not peers: a map with both means the deathmatch
    /// ones, and mixing them would drop a player at the single-player start now
    /// and then for no stated reason.
    /// </summary>
    private EntityHandle Walk(EntityHandle player)
    {
        var candidates = new List<EntityHandle>();
        string? walked = null;

        foreach (string className in Pickups.SpawnPointClasses)
        {
            walked = className;

            foreach (var spot in entities.FindAllByClassname(className))
            {
                // A spawn point left at the world origin is a mapping mistake
                // common enough that the engine skips it by hand too.
                var at = entities.GetOrigin(spot);

                if (at.X != 0f || at.Y != 0f || at.Z != 0f)
                    candidates.Add(spot);
            }

            if (candidates.Count > 0)
                break;
        }

        if (candidates.Count == 0)
            return EntityHandle.None;

        // Preferring one nobody is standing in, and settling for the one the
        // walk began at when every point is taken.
        int first = random.Next(candidates.Count);

        for (int i = 0; i < candidates.Count; i++)
        {
            var spot = candidates[(first + i) % candidates.Count];

            if (!IsOccupied(spot, player))
                return spot;
        }

        EntityHandle settled = candidates[first];

        if (walked == EntityNames.InfoPlayerDeathmatch && Frags(player))
            KillAround(settled, player);

        return settled;
    }

    /// <summary>Whether every <see cref="ISpawnFragRule"/> lets <paramref name="player"/> kill; a mod with none does.</summary>
    private bool Frags(EntityHandle player)
    {
        if (_fragRules is null)
            return true;

        foreach (ISpawnFragRule rule in _fragRules.All)
        {
            if (!rule.Frags(player))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Stock's loop: every player in the convar's sphere but the one spawning takes
    /// 300 generic from the world, through the same damage as any other hit.
    /// </summary>
    private void KillAround(EntityHandle spot, EntityHandle spawning)
    {
        float radius = convars.Get<Hl2mpSpawnFragFallbackRadiusConVar>()?.Value ?? DefaultFragRadius;

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxNearASpot];
        int found = world.EntitiesInSphere(entities.GetOrigin(spot), radius, nearby);

        for (int i = 0; i < found; i++)
        {
            if (nearby[i] != spawning && entities.IsPlayer(nearby[i]))
                damage.TakeDamage(nearby[i], World, FragDamage, DamageType.Generic);
        }
    }

    /// <summary>
    /// CGameRules::GetPlayerSpawnSpot's placement: the spot's origin through
    /// the <see cref="ISpawnPlacement"/> chain (stock's virtual, which a mod
    /// overrides to move the position — TF2 drops the player to the ground
    /// under the spot first), then lifted one unit (a player put exactly on the
    /// point starts intersecting the floor) and given the spot's facing. The
    /// native half (PlayerPlaceAtSpawn) takes yaw only on the entity, zeroes
    /// the velocity and snaps the eye angles.
    /// </summary>
    public void Place(EntityHandle player, EntityHandle spot)
    {
        var origin = entities.GetOrigin(spot);

        if (_placement is not null)
        {
            foreach (ISpawnPlacement hook in _placement.All)
            {
                try
                {
                    origin = hook.PlaceAt(player, spot, origin);
                }
                catch (Exception e)
                {
                    _log?.Warning($"[SourceSharp] spawn placement hook threw for {player}: {e.Message}\n");
                }
            }
        }

        origin.Z += 1f;

        players.PlaceAtSpawn(player, origin, entities.GetAngles(spot));

        // A respawn is a jump across the map: without this every other client draws
        // the body flying from where it died to here (PlayerDeathThink's
        // IncrementInterpolationFrame).
        players.BreakInterpolation(player);

        // pPlayer->SetAbsVelocity( vec3_origin ) (gamerules.cpp:195): what a player
        // moved at when it died does not come back with it.
        entities.SetVelocity(player, Vector.Zero);
    }

    /// <summary>
    /// Whether a player is already standing on a spawn point. The radius is
    /// stock's, because this replaces the engine's SEARCH and not its idea of
    /// what makes a point usable.
    /// </summary>
    private const int MaxNearASpot = 512;

    private bool IsOccupied(EntityHandle spot, EntityHandle spawning)
    {
        // Room for all a spot has round it: stock walks the whole sphere, and a
        // spot among 16 lights and decals never saw the player standing on it, so
        // two came to stand in each other (live, dm_lockdown's (-4116 4600 2)).
        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxNearASpot];
        int found = world.EntitiesInSphere(entities.GetOrigin(spot), 128f, nearby);

        for (int i = 0; i < found; i++)
        {
            if (nearby[i] != spawning && entities.IsPlayer(nearby[i]))
                return true;
        }

        return false;
    }
}
