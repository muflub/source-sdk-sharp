//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Buildings;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// Stock's <c>CTFPlayer::RemoveAllOwnedEntitiesFromWorld</c>
/// (tf_player.cpp:13404-13431) as one method over the three arms stock
/// calls it from: a team change (<c>ForceChangeTeam</c> :6533 and
/// <c>ChangeTeam</c> :6634, exploding the buildings), a disconnect
/// (<c>TeamFortress_ClientDisconnected</c> :13346, exploding them), and
/// the class-change respawn (<c>ForceRespawn</c> :14625-14636, quietly,
/// only when the desired class differs from the worn one).
///
/// <para>
/// The method's two halves are stock's two helpers, in stock's order:
/// the owned projectiles first — the flying rockets of the owner
/// (<c>RemoveOwnedProjectiles</c>, the owner read off the rocket's own
/// net state) and the launcher's stickies fizzled with it
/// (<see cref="TFStickyTray.FizzleAll"/>, stock's grenade-owner arm, quiet
/// removal and no blast) — then every standing building of the owner
/// (<c>RemoveAllObjects</c>): the explode arm kills each through
/// <see cref="TFBaseObject.DetonateObject"/> and the quiet arm takes it
/// out through <see cref="TFBaseObject.DestroyObject"/>, which is what
/// stock's <c>SetSolid(SOLID_NONE)</c> plus <c>UTIL_Remove</c> reads as
/// here.
/// </para>
///
/// <para>
/// The disconnect arm works from the leaving player's index, the way the
/// core's lifecycle driver hands it out, and finds the owner's entities
/// by matching that index to the owner index each entity carries — the
/// builder handle, the rocket's launcher, the tray's key.
/// </para>
/// </summary>
/// <remarks>
/// Stock's MvM/raid/bot early-return arms (<c>RETAIN_BUILDINGS</c>,
/// <c>tf_gamerules.cpp</c>'s raid and comp-mode callers) have no mode to
/// reach in this port; the walk itself is the one shared by every stock
/// caller.
/// </remarks>
[SideService(Side.Server)]
public sealed class TFOwnedEntitiesRemoval(IEngineEntities entities, IEntityDirectory directory,
                                           ITFBuildings buildings, TFStickyTray tray,
                                           TFPlayerCarriers carriers)
    : ITeamChangeWatcher, IClientLifecycle, IPlayerSpawnHook, IOrderedHook
{
    /// <summary>The one order, taken in the rules' band for every chain this joins.</summary>
    public const int Order = TeamRoundOrders.OwnedEntitiesRemoval;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public int ClientLifecycleOrder => Order;

    /// <summary>
    /// The one method stock's callers reach: the owner's projectiles go
    /// first, then its buildings — exploding where the caller asks it to.
    /// </summary>
    public void RemoveAllOwnedEntitiesFromWorld(EntityHandle player, bool explodeBuildings = false)
    {
        RemoveOwnedProjectiles(player.EntryIndex);
        foreach (TFBaseObject building in buildings.OwnedBy(player, type: null, mode: null))
            RemoveObject(building, explodeBuildings);
    }

    /// <summary>
    /// Stock's ChangeTeam's call (:6634, and the forced one at :6533):
    /// the active player's move between the two playing teams, buildings
    /// exploding. The join-from-outside and departure moves take stock's
    /// other arms, which do not call through here.
    /// </summary>
    public void TeamChanged(EntityHandle player, int oldTeam, int newTeam)
    {
        // The arm is stock's active-player else (:6687): both ends of the
        // move are playing teams.
        if (!IsPlayingTeam(oldTeam) || !IsPlayingTeam(newTeam))
            return;

        RemoveAllOwnedEntitiesFromWorld(player, explodeBuildings: true);
    }

    /// <summary>
    /// Stock's <c>TeamFortress_ClientDisconnected</c> (:13346): the leaving
    /// player's world goes with them, buildings exploding. The driver
    /// names the departure by index, so the matching is by index too.
    /// </summary>
    public void OnClientDisconnected(int entIndex)
    {
        RemoveOwnedProjectiles(entIndex);
        foreach (TFBaseObject building in buildings.OwnedBy(builder: null, type: null, mode: null))
        {
            if (building.Builder.EntryIndex == entIndex)
                RemoveObject(building, explodeBuildings: true);
        }
    }

    /// <summary>
    /// Stock's <c>ForceRespawn</c> (:14625-14636): the respawn that
    /// changes the class clears the old life's owned world quietly —
    /// guard read before <see cref="TFPlayerSpawns.OnSpawning"/> collapses
    /// the desired class into the worn one. Every other respawn, and the
    /// same-class one, walks past.
    /// </summary>
    public void OnSpawning(EntityHandle player, float now)
    {
        TFPlayerClass? cls = carriers.Part<TFPlayerClass>(player.EntryIndex);
        if (cls is null
            || cls.Desired is <= TFClass.Undefined or >= TFClass.CountAll
            || cls.Desired == carriers.ClassOf(player))
            return;

        RemoveAllOwnedEntitiesFromWorld(player);
    }

    /// <inheritdoc />
    public void OnSpawned(EntityHandle player, float now) { }

    /// <summary>
    /// Stock's <c>RemoveOwnedProjectiles</c> (:13437): every flyer whose
    /// owner is this player, read through the one question the flyers wear
    /// (<see cref="ITFOwnedProjectile"/> — stock's owner/thrower/scorer arms
    /// collapsed into one answer), each going through the engine's removal
    /// route. The stickies ride their launcher's tray instead of the walk
    /// (stock's quiet grenade arm, <see cref="TFStickyTray.FizzleAll"/>),
    /// and the flame managers lose their points to their attacker
    /// (stock's <c>IsEntityAttacker</c> arm through
    /// <see cref="TFFlameManager.ClearPoints"/>).
    /// </summary>
    private void RemoveOwnedProjectiles(int ownerIndex)
    {
        foreach (string className in FlyingClasses)
            foreach (ScriptedEntity flyer in directory.OfClass(className))
                if (flyer is ITFOwnedProjectile owned && owned.OwnerIndex == ownerIndex)
                    entities.Remove(flyer.Handle);

        foreach (ScriptedEntity flame in directory.OfClass(TFFlameManagerDefs.ClassName))
            if (flame is TFFlameManager manager && manager.Attacker.EntryIndex == ownerIndex)
                manager.ClearPoints();

        tray.FizzleAll(ownerIndex);
    }

    /// <summary>The registered flyer names the walk covers; the sticky's
    /// life rides its tray (<see cref="TFStickyTray"/>) and is not walked.</summary>
    private static string[] FlyingClasses =>
    [
        TFProjectileEntityNames.Rocket,
        TFProjectileEntityNames.Pipebomb,
        TFProjectileEntityNames.Syringe,
        TFProjectileEntityNames.Arrow,
        TFProjectileEntityNames.HealingBolt,
        TFProjectileEntityNames.Flare,
        TFProjectileEntityNames.Jar,
        TFProjectileEntityNames.JarMilk,
        TFProjectileEntityNames.JarGas,
        TFProjectileEntityNames.Cleaver,
        TFProjectileEntityNames.BallOfFire,
        TFProjectileEntityNames.BallOrnament,
        TFProjectileEntityNames.EnergyRing,
        TFProjectileEntityNames.EnergyBall,
    ];

    /// <summary>The explode arm kills through the object's own blast; the quiet arm takes it out.</summary>
    private static void RemoveObject(TFBaseObject building, bool explodeBuildings)
    {
        if (explodeBuildings)
            building.DetonateObject();
        else
            building.DestroyObject();
    }

    private static bool IsPlayingTeam(int team)
        => team is (int)TFTeam.Red or (int)TFTeam.Blue;
}
