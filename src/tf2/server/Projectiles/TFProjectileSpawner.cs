//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Projectiles;

/// <summary>
/// One projectile to put in the world (<see cref="ITFProjectileSpawner.Spawn"/>):
/// its class, whose it is, where it starts, the velocity it leaves with and
/// what its hit is worth.
///
/// <para>
/// The owner is any entity, as stock's creates take a <c>CBaseEntity</c>
/// (<c>CTFBaseRocket::Create</c>, <c>tf_weaponbase_rocket.cpp:254</c> — the
/// bosses fire rockets they own): the damage is dealt in its name, the flight
/// never touches it, and a blast with an own pass reaches it as stock's does.
/// </para>
/// </summary>
/// <param name="ClassName">The projectile's classname (<see cref="TFProjectileEntityNames"/>).</param>
/// <param name="Owner">The entity it is fired by: the attacker of its damage.</param>
/// <param name="Origin">Where it is born. Nothing is added: the caller chose the muzzle.</param>
/// <param name="Velocity">The velocity it leaves with; it faces along it.</param>
/// <param name="Damage">What the hit is worth before the damage layer's ramps and falloff.</param>
/// <param name="Critical">Whether the shot is a crit.</param>
public readonly record struct TFProjectileSpawn(string ClassName, EntityHandle Owner, Vector Origin,
                                                Vector Velocity, float Damage, bool Critical = false)
{
    /// <summary>
    /// The projectile's team, stock's <c>ChangeTeam( pOwner->GetTeamNumber() )</c>
    /// (<c>tf_weaponbase_rocket.cpp:321</c>). Null is the owner's own: a
    /// player's team, asked as every weapon's launch asks it, and
    /// <see cref="TFTeam.Unassigned"/> for an owner that is not a player —
    /// the player seam holds no team for one, so its caller names it here.
    /// The rocket, the pipe and the flare carry it (their trail's colour and
    /// their friend-or-foe arms); the energy projectiles and the arrow ask
    /// the player seam about their owner whatever this says.
    /// </summary>
    public int? Team { get; init; }

    /// <summary>
    /// The weapon the damage is dealt as: its damage bits
    /// (<c>g_aWeaponDamageTypes</c>) and the damage layer's weapon arms.
    /// <see cref="TFWeaponId.None"/> is the class's stock launcher.
    /// </summary>
    public TFWeaponId Weapon { get; init; }

    /// <summary>
    /// The weapon row the hit names: the kill's icon, and — for a player
    /// owner carrying that row — the item whose attributes the projectile's
    /// own hooks read. Null is <see cref="Weapon"/>'s script name.
    /// </summary>
    public string? WeaponName { get; init; }
}

/// <summary>
/// Puts TF projectiles in the world: the one place a projectile entity is
/// created, for the weapons' launches (<see cref="TFProjectileEffects"/>) and
/// for any other code that fires one with numbers of its own.
/// </summary>
public interface ITFProjectileSpawner
{
    /// <summary>Whether <see cref="Spawn"/> launches <paramref name="className"/>.</summary>
    bool Launches(string className);

    /// <summary>
    /// Creates, arms and launches one projectile. An unknown class is a
    /// caller's mistake and throws; null is the engine refusing the entity or
    /// an owner that is no longer there, and nothing was fired.
    /// </summary>
    /// <exception cref="ArgumentException">The class is not one <see cref="Launches"/> answers for.</exception>
    ScriptedEntity? Spawn(in TFProjectileSpawn spawn);

    /// <summary>
    /// The birth alone: the entity created, placed at <paramref name="origin"/>
    /// and spawned, unarmed and at rest — for a launch that arms it with more
    /// than <see cref="TFProjectileSpawn"/> says. Null when the engine refuses.
    /// </summary>
    ScriptedEntity? Born(string className, Vector origin);
}

/// <summary>
/// The server's <see cref="ITFProjectileSpawner"/>.
///
/// <para>
/// A launch here is the entity's own stock arm with the request's numbers in
/// place of a weapon script's: the rocket's and the energy projectiles'
/// <c>ArmedBy</c>, the pipe's <c>ThrownBy</c> at the regular mode
/// (<c>TF_GL_MODE_REGULAR</c>) and the stock launcher's script radius, the
/// flare at its stock gravity and plain mode, the arrow at the uncharged
/// draw's gravity. Nothing reads the owner's eyes: the origin and the velocity
/// are the caller's.
/// </para>
///
/// <para>
/// Not launched here, and refused by name: the projectiles whose hit is not
/// a number the caller can hand in (the syringe's, the cleaver's and the jars'
/// fixed worth), the sticky (a launcher's tray owns its life), and the ones
/// whose impact is its owner's weapon's affair (the healing bolt's charge,
/// the ball of fire's bonus, the ornament's bat, the mechanical arm's orb).
/// <see cref="Born"/> still creates them for their own launch code.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFProjectileSpawner)])]
public sealed class TFProjectileSpawner(IEngineEntities entities, IEntityDirectory directory, ILog log)
    : ITFProjectileSpawner
{
    /// <summary>
    /// The stock grenade launcher's blast radius, its script's
    /// <c>DamageRadius</c> (<c>tf_weapon_grenadelauncher.txt</c>): what a
    /// spawned pipe explodes over, there being no script behind it.
    /// </summary>
    public const float PipeRadius = 146f;

    public bool Launches(string className) => StockLauncher(className) != TFWeaponId.None;

    public ScriptedEntity? Spawn(in TFProjectileSpawn spawn)
    {
        TFWeaponId stock = StockLauncher(spawn.ClassName);
        if (stock == TFWeaponId.None)
            throw new ArgumentException(
                $"'{spawn.ClassName}' is not a projectile the spawner launches (it launches "
                + $"{TFProjectileEntityNames.Rocket}, {TFProjectileEntityNames.Pipebomb}, "
                + $"{TFProjectileEntityNames.Flare}, {TFProjectileEntityNames.EnergyBall}, "
                + $"{TFProjectileEntityNames.EnergyRing} and {TFProjectileEntityNames.Arrow})",
                nameof(spawn));

        // No owner, no shot: the damage would have nobody to answer for it
        // (stock's "No attacker, deal no damage", tf_weaponbase_rocket.cpp:496).
        EntityHandle owner = spawn.Owner;
        if (!entities.IsValid(owner))
        {
            log.Warning($"[TF2/Projectiles] {spawn.ClassName} not spawned: its owner {owner} is gone\n");
            return null;
        }

        ScriptedEntity? born = Born(spawn.ClassName, spawn.Origin);
        if (born is null)
            return null;

        TFWeaponId weapon = spawn.Weapon != TFWeaponId.None ? spawn.Weapon : stock;
        string weaponName = spawn.WeaponName ?? TFWeaponNames.Name(weapon).ToLowerInvariant();
        int ownerIndex = entities.GetIndex(owner);

        // A player's projectile asks the player seam for its team, as the
        // weapons' launches do; any other owner's is the request's.
        int? team = spawn.Team
                    ?? (entities.IsPlayer(owner) ? null : (int)TFTeam.Unassigned);

        switch (born)
        {
            case TFRocket rocket:
                rocket.ArmedBy(spawn.Damage, weapon, weaponName, spawn.Critical, ownerIndex, team);
                rocket.FiredBy(owner, spawn.Velocity);
                break;

            case TFPipebomb pipe:
                pipe.ThrownBy(owner, spawn.Velocity, spawn.Damage, PipeRadius, weapon, weaponName,
                              spawn.Critical, TFProjectileDefs.GrenadeModeRegular, team);
                break;

            case TFFlare flare:
                flare.ArmedBy(spawn.Damage, weapon, weaponName, spawn.Critical, TFProjectileDefs.FlareGravity,
                              ownerIndex, TFProjectileBehaviour.FlareNormal, team);
                flare.FiredBy(owner, spawn.Velocity);
                break;

            case TFProjectileEnergyBall ball:
                ball.ArmedBy(spawn.Damage, weapon, weaponName, spawn.Critical, charged: false, ownerIndex);
                ball.FiredBy(owner, spawn.Velocity);
                break;

            case TFProjectileEnergyRing ring:
                ring.ArmedBy(spawn.Damage, weapon, weaponName, spawn.Critical, ownerIndex);
                ring.FiredBy(owner, spawn.Velocity);
                break;

            case TFArrow arrow:
                arrow.ArmedBy(weapon, weaponName, spawn.Critical, lit: false, spawn.Damage,
                              TFProjectileDefs.ArrowGravity(0f));
                arrow.FiredBy(owner, spawn.Velocity);
                break;

            default:
                // The class is one this launches and the entity behind it is
                // not: a registration replaced it. Nothing armed it, so it goes.
                entities.Remove(born.Handle);
                throw new InvalidOperationException(
                    $"'{spawn.ClassName}' spawned as {born.GetType().Name}, which the spawner cannot arm");
        }

        return born;
    }

    /// <summary>
    /// The birth: the entity is brought to life before its managed half is
    /// asked for — the registry binds the instance when the engine spawns the
    /// shell (the <c>env_fire</c> flame's order), so an entity that was
    /// created but never spawned has no managed half to arm.
    /// </summary>
    public ScriptedEntity? Born(string className, Vector origin)
    {
        EntityHandle handle = entities.Create(className);
        if (!entities.IsValid(handle))
            return null;

        entities.SetOrigin(handle, origin);
        entities.Spawn(handle);
        return directory.Get(handle);
    }

    /// <summary>
    /// The weapon a class's hit is dealt as when the request names none: the
    /// stock launcher of each projectile <see cref="Spawn"/> arms, and
    /// <see cref="TFWeaponId.None"/> for every other name.
    /// </summary>
    private static TFWeaponId StockLauncher(string className) => className switch
    {
        TFProjectileEntityNames.Rocket => TFWeaponId.Rocketlauncher,
        TFProjectileEntityNames.Pipebomb => TFWeaponId.Grenadelauncher,
        TFProjectileEntityNames.Flare => TFWeaponId.Flaregun,
        TFProjectileEntityNames.EnergyBall => TFWeaponId.ParticleCannon,
        TFProjectileEntityNames.EnergyRing => TFWeaponId.Raygun,
        TFProjectileEntityNames.Arrow => TFWeaponId.CompoundBow,
        _ => TFWeaponId.None,
    };
}
