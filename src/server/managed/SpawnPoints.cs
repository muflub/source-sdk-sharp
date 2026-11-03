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
/// One difference from stock, on purpose: when every point of the core's walk
/// is occupied, stock kills whoever is standing on the first one. This picks a
/// random occupied point instead. A mod that kills a player for standing still
/// is a decision, and this one does not make it.
/// </para>
/// </summary>
public sealed class SpawnPoints(IEngineEntities entities, IEnginePlayers players, IEngineWorldQuery world,
                                Random random, IHooks<ISpawnPlacement>? placement = null, ILog? log = null)
{
    private readonly IHooks<ISpawnPlacement>? _placement = placement;
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

        foreach (string className in Pickups.SpawnPointClasses)
        {
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

        // Preferring one nobody is standing in, and settling for a random one
        // when every point is taken.
        int first = random.Next(candidates.Count);
        EntityHandle chosen = candidates[first];

        for (int i = 0; i < candidates.Count; i++)
        {
            var spot = candidates[(first + i) % candidates.Count];

            if (!IsOccupied(spot, player))
            {
                chosen = spot;
                break;
            }
        }

        return chosen;
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
    }

    /// <summary>
    /// Whether a player is already standing on a spawn point. The radius is
    /// stock's, because this replaces the engine's SEARCH and not its idea of
    /// what makes a point usable.
    /// </summary>
    private bool IsOccupied(EntityHandle spot, EntityHandle spawning)
    {
        Span<EntityHandle> nearby = stackalloc EntityHandle[16];
        int found = world.EntitiesInSphere(entities.GetOrigin(spot), 128f, nearby);

        for (int i = 0; i < found; i++)
        {
            if (nearby[i] != spawning && entities.IsPlayer(nearby[i]))
                return true;
        }

        return false;
    }
}
