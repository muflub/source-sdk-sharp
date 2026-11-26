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

namespace TF2.Server.Projectiles;

/// <summary>
/// <c>CTFWeaponBaseGrenadeProj::VPhysicsUpdate</c>'s server branch (<c>tf_weaponbase_grenadeproj.cpp:716-795</c>), which
/// every grenade inherits (the pipe, the sticky, the jar and the cleaver): a ray one tick ahead of the body touches what it
/// strikes (:774), then bounces off a friend or an enemy (:776-793). Stock's grenades never collide with players
/// (<c>tf_gamerules.cpp:18053-18060</c>), so there the ray is how a grenade meets one, on his hitboxes. Here the solver
/// collides them first, on his bounding box, and most player contacts reach the grenade as the engine's touch after that
/// collision: in a 12-game batch 106 of 108 unlatched player touches followed one, and the ray bounced a pipe off a player
/// twice (docs/divergences.md).
///
/// <para>
/// Stock's filter (<c>CTraceFilterCollisionGrenades</c>, :667-707) is entities only and skips the thrower, other grenades,
/// rockets, debris, respawn rooms and the default group: of what this port has, a player and a building are what it
/// returns. <see cref="Struck"/> returns only what <see cref="TeamOf"/> gives a team: the world, props, doors, ammo packs and
/// projectiles are passed. The trace is <see cref="TraceMask.Shot"/> for stock's hitbox, monster and solid mask.
/// </para>
/// </summary>
public static class TFGrenadeRay
{
    /// <summary>
    /// The team a grenade reads off what it meets: a player's (<see cref="IEnginePlayers.GetTeam"/>), a building's
    /// (<see cref="TFBaseObject.Team"/>); 0 for anything else, for which <c>GetTeam</c> answers -1.
    /// </summary>
    public static int TeamOf(IEngineEntities entities, IEnginePlayers players, IEntityDirectory directory, EntityHandle who)
    {
        if (who == EntityHandle.None)
            return 0;
        if (entities.IsPlayer(who))
            return players.GetTeam(who);
        return directory.Get(who) is TFBaseObject building ? building.Team : 0;
    }

    /// <summary>
    /// Stock's "an enemy we can damage" (<c>GetTeamNumber() &amp;&amp; != GetTeamNumber() &amp;&amp; m_takedamage != DAMAGE_NO</c>,
    /// tf_weapon_grenade_pipebomb.cpp:759, :877) over <see cref="TeamOf"/>: a player or a building of another team, both of
    /// which take damage (a building's <c>m_takedamage</c> is <c>DAMAGE_YES</c> unless map-placed invulnerable, tf_obj.cpp:390-397).
    /// </summary>
    public static bool IsEnemy(int theirTeam, int myTeam) => theirTeam > 0 && myTeam > 0 && theirTeam != myTeam;

    /// <summary>The ray's hit when stock's filter would pass it: something with a team that is not the thrower; else null.</summary>
    public static TraceResult? Struck(IEngineWorldQuery world, IEngineEntities entities, IEnginePlayers players,
                                      IEntityDirectory directory, EntityHandle self, EntityHandle thrower,
                                      Vector origin, Vector velocity, float frameTime)
    {
        TraceResult hit = world.TraceLine(origin, origin + velocity * frameTime, TraceMask.Shot, self);
        EntityHandle struck = hit.Entity;
        if (struck == EntityHandle.None || struck == thrower || TeamOf(entities, players, directory, struck) <= 0)
            return null;
        return hit;
    }

    /// <summary>
    /// The bounce off a friend or an enemy (:776-793): the velocity reflected about the hit's normal (:781), times the
    /// grenade's elasticity (<see cref="TFProjectileDefs.PipebombElasticity"/>, :784), an enemy half again (:786-789), the
    /// angular velocity reversed and halved (:791).
    /// </summary>
    public static void BounceOff(IEngineEntities entities, EntityHandle self, Vector velocity, in TraceResult hit, bool enemy)
    {
        Vector reflected = -2f * hit.Normal * velocity.Dot(hit.Normal) + velocity;
        reflected *= TFProjectileDefs.PipebombElasticity;
        if (enemy)
            reflected *= 0.5f;
        entities.SetVelocity(self, reflected);
        entities.SetAngularVelocity(self, entities.GetAngularVelocity(self) * -0.5f);
    }
}
