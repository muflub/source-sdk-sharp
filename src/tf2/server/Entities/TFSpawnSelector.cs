//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Movement;

namespace TF2.Server.Entities;

/// <summary>
/// The TF team spawn walk: CTFPlayer::EntSelectSpawnPoint (tf_player.cpp
/// L5675) and SelectSpawnSpotByType (L5803) over CTFGameRules::IsSpawnPointValid
/// (tf_gamerules.cpp L9990), as the core's spawn selector (stage 0.5). A
/// red or blue player gets a spawn of their team that is enabled, not at the
/// origin, admits their class and is clear; spectators and the unassigned get
/// None, which is the core's walk (stock answers the world entity).
/// </summary>
[SideService(Side.Server)]
public sealed class TFSpawnSelector(ITeams teams, IObjectiveEntities objectives, IEngineEntities entities,
                                    IEngineWorldQuery world, ITFPlayerClasses? classes = null,
                                    Player.TFPlayerCarriers? carriers = null, IEntityDirectory? directory = null)
    : ISpawnSelector, IGameLifecycle
{
    /// <summary>VEC_HULL_MIN / VEC_HULL_MAX: the mod's one copy of g_TFViewVectors (<see cref="TFViewVectors"/>).</summary>
    public static Vector HullMin => TFViewVectors.Params.HullMin;

    /// <inheritdoc cref="HullMin"/>
    public static Vector HullMax => TFViewVectors.Params.HullMax;

    /// <summary>A stage of its own in TF2's thousand (see <see cref="ObjectiveResourceSpawner.Order"/>).</summary>
    public const int Order = 1_000_000 - 1_000 + 30;

    public int LifecycleOrder => Order;

    /// <summary>g_pLastSpawnPoints: each team's last spawn, where the next walk starts.</summary>
    private readonly InfoPlayerTeamSpawn?[] _last = new InfoPlayerTeamSpawn?[(int)TFTeam.Count];

    public void OnLevelInit(string mapName) => Array.Clear(_last);

    public EntityHandle SelectSpawn(EntityHandle player)
    {
        // GetRespawnLocationOverride's named spot (tf_player.cpp:5745-5757),
        // read before the team walk: a TRIGGERED spot of that name the player
        // may use (SelectSpawnSpotByName's IsSpawnPointValid walk, :5903-5919,
        // ignoring the other players) wins whole. Stock's match-summary arm
        // (ShowMatchSummary) is the unported flag lane's, taken false; its
        // loop over same-named entities is this build's one find-by-name
        // answer, and a named spot that is not a team spawn is invalid here
        // (docs/divergences.md).
        if (carriers is not null && directory is not null
            && carriers.Part<Player.TFRespawnOverride>(player.EntryIndex) is { Location: { Length: > 0 } location })
        {
            if (directory.FindByName<InfoPlayerTeamSpawn>(location) is { } named
                && IsSpawnPointValid(named, player, teams.Team(player), ignorePlayers: true, TeamSpawnMode.Triggered))
                return named.Handle;
        }

        int team = teams.Team(player);
        if (team != (int)TFTeam.Red && team != (int)TFTeam.Blue)
            return EntityHandle.None;

        var spot = SelectSpawnSpotByType(player, team, _last[team]);
        if (spot is null)
            return EntityHandle.None;

        _last[team] = spot;
        return spot.Handle;
    }

    /// <summary>
    /// SelectSpawnSpotByType, as stock walks gEntList: from the spawn after
    /// the team's last, around the list (with the list's end as one empty
    /// step), first honouring the class flags and the other players, then
    /// only the players, then neither. Stock's own quirk is kept: the second
    /// and third passes start one past the first spot and stop on reaching
    /// it, so the first spot is only ever checked in the first pass.
    /// </summary>
    private InfoPlayerTeamSpawn? SelectSpawnSpotByType(EntityHandle player, int team, InfoPlayerTeamSpawn? last)
    {
        var list = objectives.Spawns.ToList();
        InfoPlayerTeamSpawn? Find(InfoPlayerTeamSpawn? after)
        {
            if (after is null)
                return list.Count > 0 ? list[0] : null;
            int at = list.IndexOf(after);
            return at >= 0 && at + 1 < list.Count ? list[at + 1] : null;
        }

        // Get an initial spawn point.
        var spot = Find(last);
        if (spot is null)
        {
            // Sometimes the first spot can be NULL????
            spot = Find(spot);
        }

        bool ignorePlayers = false;
        bool restrictByClass = true;
        // The spawnflags name the classes a spawn admits (post-2016 maps); with no
        // class owner registered yet every player is Undefined and the class pass is skipped.
        TFClass playerClass = classes?.ClassOf(player) ?? TFClass.Undefined;

        var first = spot;
        do
        {
            if (spot is not null && IsSpawnPointValid(spot, player, team, ignorePlayers, TeamSpawnMode.Normal))
            {
                var origin = entities.GetOrigin(spot.Handle);
                bool bad = origin.X == 0f && origin.Y == 0f && origin.Z == 0f;

                // Only a map compiled with the class spawnflags restricts (they default to on).
                bool classRefused = !bad && restrictByClass && spot.SpawnFlags != 0
                                    && playerClass > TFClass.Undefined
                                    && (spot.SpawnFlags & (1 << ((int)playerClass - 1))) == 0;

                if (!bad && !classRefused)
                    return spot;
            }

            // Get the next spawning point to check.
            spot = Find(spot);

            // Exhausted the list.
            if (ReferenceEquals(spot, first))
            {
                if (restrictByClass)
                {
                    // Loop through again, ignoring class restrictions (but check against players).
                    restrictByClass = false;
                    spot = Find(spot);
                }
                else if (!ignorePlayers)
                {
                    // Loop through again, ignoring players and classes.
                    ignorePlayers = true;
                    spot = Find(spot);
                }
            }
        }
        while (!ReferenceEquals(spot, first));

        return null;
    }

    /// <summary>
    /// IsSpawnPointValid, without the item-testing and match-summary arms:
    /// the team, the switch, the spawn mode, and a clear hull — against the
    /// world and the players, or (ignoring players) against the world alone.
    /// </summary>
    internal bool IsSpawnPointValid(InfoPlayerTeamSpawn spot, EntityHandle player, int team, bool ignorePlayers,
                                    TeamSpawnMode mode)
    {
        if (spot.Team != team)
            return false;

        if (spot.Disabled)
            return false;

        if (spot.SpawnMode != TeamSpawnMode.Normal && spot.SpawnMode != mode)
            return false;

        if (spot.MatchSummary != TeamSpawnMatchSummary.None)
            return false;

        var origin = entities.GetOrigin(spot.Handle);

        if (!ignorePlayers)
        {
            // UTIL_IsSpaceEmpty( player, origin + mins, origin + maxs ): a box trace at the box's centre.
            var mins = origin + HullMin;
            var maxs = origin + HullMax;
            var half = (maxs - mins) * 0.5f;
            var centre = mins + half;
            var empty = world.TraceHull(centre, centre, Vector.Zero - half, half, TraceMask.Solid, player);
            return !empty.Blocked && !empty.StartSolid;
        }

        // MASK_PLAYERSOLID in stock; the ABI's nearest is MASK_SOLID (no playerclip: docs/divergences.md).
        var trace = world.TraceHull(origin, origin, HullMin, HullMax, TraceMask.Solid, player);
        return !trace.Blocked && !trace.StartSolid;
    }
}
