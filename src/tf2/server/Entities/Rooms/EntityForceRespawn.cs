//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Rules;
using TF2.Shared;

namespace TF2.Server.Entities.Rooms;

/// <summary>
/// game_forcerespawn (stock's <c>CTFForceRespawn</c>, entity_forcerespawn.cpp):
/// respawns the playing teams on an input — everyone, everyone after a team
/// swap, or one team's dead — wiping the projectiles, buildings and dropped
/// ammo packs first (the one team's input aside).
/// </summary>
[ScriptedEntity(MapClass)]
public sealed class EntityForceRespawn(ITeamRoundRoster roster, ITFMapPlayers players, ITFBuildings buildings,
                                       TFPlayerCarriers carriers) : ScriptedEntity
{
    public const string MapClass = "game_forcerespawn";

    /// <summary>The dropped weapons' ammo (stock's <c>CTFAmmoPack</c>), removed after the respawn.</summary>
    public const string DroppedAmmoClass = "tf_ammo_pack";

    /// <summary>The classes <c>RemoveAllProjectiles</c> takes: every projectile the build has.</summary>
    public static IReadOnlyList<string> ProjectileClasses =>
        [TFProjectileEntityNames.Rocket, TFProjectileEntityNames.Pipebomb,
         TFProjectileEntityNames.Stickybomb, TFProjectileEntityNames.Syringe];

    public readonly Output OnForceRespawn = new();

    [ScriptedEvent("ForceRespawn")] private void OnForceRespawnInput() => ForceRespawn(false);

    [ScriptedEvent] private void OnForceRespawnSwitchTeams() => ForceRespawn(true);

    [ScriptedEvent] private void OnForceTeamRespawn(float team) => ForceRespawn(false, (int)team, removeEverything: false);

    /// <summary>
    /// <c>CTFForceRespawn::ForceRespawn</c> (entity_forcerespawn.cpp:46-117):
    /// the wipe of projectiles, buildings and (always) dropped ammo packs,
    /// then every player — the off-team and classless ones granted an instant
    /// spawn (<see cref="TFPlayerClass.AllowInstantSpawn"/>) instead of
    /// respawned, the rest respawned past the team and dead filters.
    /// </summary>
    public void ForceRespawn(bool switchTeams, int team = (int)TFTeam.Unassigned, bool removeEverything = true)
    {
        // if ( bRemoveEverything && TFGameRules() ): the round machine is
        // always here; the one-team input passes removeEverything: false.
        if (removeEverything)
            RemoveAllProjectilesAndBuildings();

        foreach (EntityHandle player in EveryPlayer())
        {
            // Stock's ToTFPlayer: a player entity with no TF player behind it is skipped.
            if (carriers.Part<TFPlayerClass>(player.EntryIndex) is not { } cls)
                continue;

            // Ignore players who aren't on an active team: they are let spawn
            // immediately when they do pick a class.
            TFTeam now = roster.TeamOf(player);
            if (now is not (TFTeam.Red or TFTeam.Blue))
            {
                cls.AllowInstantSpawn = true;
                continue;
            }

            if (switchTeams)
            {
                if (now == TFTeam.Red)
                    roster.ChangeTeam(player, TFTeam.Blue);
                else if (now == TFTeam.Blue)
                    roster.ChangeTeam(player, TFTeam.Red);
            }

            // Ignore players who haven't picked a class yet, same grant.
            if (players.ClassOf(player) == TFClass.Undefined)
            {
                cls.AllowInstantSpawn = true;
                continue;
            }

            if (team != (int)TFTeam.Unassigned)
            {
                // Ignore players who aren't on the team we're trying to respawn,
                // and players on the team that aren't dead
                if ((int)roster.TeamOf(player) != team || roster.IsAlive(player))
                    continue;
            }

            roster.ForceRespawn(player);
        }

        // remove any dropped weapons/ammo packs
        foreach (EntityHandle pack in Entities.FindAllByClassname(DroppedAmmoClass).ToList())
            Entities.Remove(pack);

        OnForceRespawn.Fire();
    }

    /// <summary>
    /// The players as stock's <c>UTIL_PlayerByIndex</c> walk has them: every
    /// connected one, team or not, each exactly once — so all four team lists
    /// (the unassigned and spectator ones along with the playing two) are
    /// taken before the walk turns anyone, and a switched player is not
    /// walked again under the team they moved to.
    /// </summary>
    private List<EntityHandle> EveryPlayer()
    {
        var everyone = new List<EntityHandle>();
        foreach (TFTeam team in new[] { TFTeam.Unassigned, TFTeam.Spectator, TFTeam.Red, TFTeam.Blue })
            everyone.AddRange(roster.Players(team));
        return everyone;
    }

    /// <summary>
    /// <c>CTFGameRules::RemoveAllProjectilesAndBuildings</c>
    /// (tf_gamerules.cpp:4098-4103): the projectiles (stock's
    /// <c>RemoveAllProjectiles</c>, :4034-4040, clips the projectile auto
    /// list; this build's four projectile classes are what it can hold, each
    /// removed by classname as the ammo packs are) and the buildings
    /// (stock's <c>RemoveAllBuildings</c>, :4045-4077, unexploded —
    /// <c>bExplodeBuildings</c> defaults false). Stock skips map-placed
    /// buildings and fires the <c>object_removed</c> game event for each
    /// (docs/divergences.md): every building here is built, and the
    /// buildings lane's <c>DestroyObject</c> is the removal, the
    /// <c>OnDestroyed</c> output telling the builder as stock's event told
    /// the round.
    /// </summary>
    public void RemoveAllProjectilesAndBuildings()
    {
        foreach (string className in ProjectileClasses)
            foreach (EntityHandle projectile in Entities.FindAllByClassname(className).ToList())
                Entities.Remove(projectile);

        foreach (TFBaseObject building in buildings.OwnedBy().ToList())
            building.DestroyObject();
    }
}
