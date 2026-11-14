using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// A TF2 class as a player who knows the game knows it (plan_bots_teams_tf2 P6 step 4): how fast
/// it runs and how much health it spawns with (its class script's), its body (one hull for every
/// class, stand and duck) and where its eye is.
/// </summary>
public readonly record struct TFClassFacts(TFClass Class, float MaxSpeed, int MaxHealth, AiVector HullMins, AiVector HullMaxs, float DuckHeight, float EyeHeight);

/// <summary>A building as an Engineer and anyone fighting one knows it: its metal, its build time, its levels and their health.</summary>
public readonly record struct TFBuildingFacts(TFObjectType Type, int Cost, float BuildSeconds, int UpgradeCost, int MaxLevel, ImmutableArray<int> MaxHealthByLevel);

/// <summary>
/// What a TF2 bot knows of the game before it plays (plan_bots_teams_tf2 C.2, P6 step 4), every
/// number read from the mod's own tables: the classes, the weapons (with a projectile's fall and a
/// charge's time), the packs, the buildings, the abilities and where each aim point sits on a body.
/// </summary>
public static class TFKnowledge
{
    /// <summary>The playable classes, Scout to Engineer, in the mod's order.</summary>
    public static IEnumerable<TFClass> Playable()
    {
        for (var c = TFClass.Scout; c <= TFClass.Engineer; c++)
            yield return c;
    }

    /// <summary>Each playable class's facts, from <paramref name="speedAndHealth"/> (the class table's script numbers) and the mod's one hull.</summary>
    public static ImmutableArray<TFClassFacts> Classes(Func<TFClass, (float MaxSpeed, int MaxHealth)> speedAndHealth)
    {
        MovementParams body = TFViewVectors.Params;
        return [.. Playable().Select(c =>
        {
            (float speed, int health) = speedAndHealth(c);
            return new TFClassFacts(c, speed, health, new AiVector(body.HullMin.X, body.HullMin.Y, body.HullMin.Z),
                                    new AiVector(body.HullMax.X, body.HullMax.Y, body.HullMax.Z), body.DuckHullMax.Z,
                                    TFViewVectors.ClassEyeHeight(c, body.View.Z));
        })];
    }

    /// <summary>
    /// The classes as the bodies a bot believes of (plan_bots_teams_tf2_part_g G.2.1): each class's
    /// run from its script, by the class a client draws (<see cref="ActorFacts.Class"/> is the mod's
    /// class number). An enemy whose class the bot has not seen runs at the classes' mean. A class
    /// whose script gave no speed has no row, and with none at all (the table not loaded) the
    /// bodies are the core's default.
    /// </summary>
    public static BodyKnowledge Bodies(ImmutableArray<TFClassFacts> classes)
    {
        (int Class, BodyFacts Facts)[] rows = [.. classes.Where(c => c.MaxSpeed > 0f).Select(c => ((int)c.Class, new BodyFacts(c.MaxSpeed)))];
        return rows.Length == 0 ? BodyKnowledge.Default() : new BodyKnowledge(new BodyFacts(rows.Average(r => r.Facts.RunSpeed)), rows);
    }

    /// <summary>The engine's pull on a projectile whose gravity scale is 1: the mod's movement gravity.</summary>
    public static float Gravity => TFViewVectors.Params.Gravity;

    /// <summary>
    /// How hard a projectile falls, units/s²: its gravity scale (the mod's projectile classes') times
    /// <see cref="Gravity"/>; an arrow at a full draw. 0 for a projectile that flies flat (a rocket) or none.
    /// </summary>
    public static float ProjectileGravity(TFProjectile projectile) => Gravity * projectile switch
    {
        TFProjectile.Pipebomb or TFProjectile.PipebombRemote or TFProjectile.PipebombPractice => TFProjectileDefs.PipebombGravity,
        TFProjectile.Arrow or TFProjectile.FestiveArrow => TFProjectileDefs.ArrowGravity(TFProjectileDefs.ArrowChargeTime),
        TFProjectile.HealingBolt or TFProjectile.FestiveHealingBolt or TFProjectile.BuildingRepairBolt => TFProjectileDefs.CrossbowGravity,
        TFProjectile.Syringe => TFProjectileDefs.SyringeGravity,
        TFProjectile.Flare => TFProjectileDefs.FlareGravity,
        _ => 0f,
    };

    /// <summary>
    /// The launch speed stock's launchers keep as C++ constants, which the mod's launch uses when the
    /// script names none (TFProjectileEffects: 1100 the rocket, 1200 the pipe, 1000 the syringe, ...):
    /// live, the shipped rocket launcher's script has no speed, so a bot's table said its rockets
    /// flew at 0 and no Soldier ever aimed at a target's feet. 0 for a kind with no stock constant.
    /// </summary>
    public static float StockSpeed(TFProjectile projectile) => projectile switch
    {
        TFProjectile.Rocket or TFProjectile.SentryRocket => TFProjectileDefs.RocketSpeed,
        TFProjectile.Pipebomb or TFProjectile.PipebombPractice => TFProjectileDefs.PipeSpeed,
        TFProjectile.Syringe => TFProjectileDefs.SyringeSpeed,
        TFProjectile.Flare => TFProjectileDefs.FlareSpeed,
        TFProjectile.Arrow or TFProjectile.FestiveArrow => TFProjectileDefs.ArrowSpeed(TFProjectileDefs.ArrowChargeTime),
        TFProjectile.HealingBolt or TFProjectile.FestiveHealingBolt or TFProjectile.BuildingRepairBolt => TFProjectileDefs.CrossbowSpeed,
        TFProjectile.EnergyBall => TFProjectileDefs.EnergyBallSpeed,
        TFProjectile.EnergyRing => TFProjectileDefs.EnergyRingSpeed,
        TFProjectile.Jar or TFProjectile.JarMilk or TFProjectile.FestiveJar => TFProjectileDefs.JarSpeed,
        TFProjectile.Cleaver => TFProjectileDefs.CleaverSpeed,
        _ => 0f,
    };

    /// <summary>
    /// How long a full charge takes, seconds, by the weapon's behaviour: a sniper rifle's to its
    /// top damage, a bow's draw, a sticky launcher's longest throw; 0 for a weapon that does not charge.
    /// </summary>
    public static float ChargeSeconds(string behaviour)
    {
        if (behaviour == TFWeaponKinds.Key(TFWeaponKind.SniperRifle) || behaviour == TFWeaponKinds.Key(TFWeaponKind.SniperRifleClassic))
            return TFSniperRifleBehaviour.DamageMax / TFSniperRifleBehaviour.ChargePerSec;
        if (behaviour == TFWeaponKinds.Key(TFWeaponKind.Bow))
            return TFBowBehaviour.ChargeTime;
        return 0f;
    }

    /// <summary>
    /// A weapon's facts (plan_hl2mp_ai §5.0 in TF2's numbers) from its catalog row and its script's
    /// primary mode: the clip and cadence the row has, a whole reload (a shell-by-shell one fills
    /// the clip from empty), the projectile's speed, fall and charge.
    /// </summary>
    public static WeaponFacts WeaponOf(int kind, in WeaponType w, TFProjectile projectile, in TFWeaponModeData primary, bool singly)
    {
        float reload = singly && w.ClipSize > 0 ? primary.TimeReloadStart + w.ClipSize * primary.TimeReload : primary.TimeReload;
        float speed = projectile is TFProjectile.None or TFProjectile.Bullet ? 0f
            : w.LaunchSpeed > 0f ? w.LaunchSpeed : TFProjectileDefs.Speed(primary.ProjectileSpeed, StockSpeed(projectile));
        float charge = projectile == TFProjectile.PipebombRemote ? TFProjectileBehaviour.MaxChargeTime : ChargeSeconds(w.Behaviour);
        return new WeaponFacts(kind, w.Name, w.ClipSize, w.Interval, reload, ReloadAudible: true, singly, speed,
                               ProjectileGravity: ProjectileGravity(projectile), ChargeSeconds: charge);
    }

    /// <summary>
    /// The weapon table: every TF2 row of the catalog (a behaviour of TF2's own, <see cref="TFWeaponKinds"/>)
    /// that has its script, by catalog kind. The core's stock rows share the catalog and are not TF2's.
    /// </summary>
    public static WeaponKnowledge Weapons(WeaponCatalog catalog, Func<string, TFWeaponScript?> script)
    {
        var facts = new List<WeaponFacts>();
        for (int kind = 0; kind < catalog.Count; kind++)
        {
            if (catalog.ByIndex(kind) is not { } w || !IsTF2s(in w) || script(w.Name) is not { } s)
                continue;
            facts.Add(WeaponOf(kind, in w, s.Primary.Projectile, s.Primary, TFWeaponKinds.ReloadsSingly(s)));
        }

        return new WeaponKnowledge(facts);
    }

    /// <summary>Whether a catalog row is one of TF2's: its behaviour is a TF2 kind's key.</summary>
    public static bool IsTF2s(in WeaponType w) => w.Behaviour.StartsWith(BehaviourPrefix, StringComparison.Ordinal);

    /// <summary>What every TF2 kind's behaviour key starts with (<see cref="TFWeaponKinds.Key"/>).</summary>
    public const string BehaviourPrefix = "tf2.";

    /// <summary>The three buildings, from the mod's object table and its health per level (150, 180, 216).</summary>
    public static ImmutableArray<TFBuildingFacts> Buildings()
        => [.. new[] { TFObjectType.Sentrygun, TFObjectType.Dispenser, TFObjectType.Teleporter }.Select(t =>
           {
               TFObjectInfo info = TFObjectInfo.Of(t)!;
               ImmutableArray<int> health = [.. Enumerable.Range(1, TFBaseObject.MaxUpgradeLevel)
                                                          .Select(l => (int)(TFBaseObject.ObjectMaxHealth * MathF.Pow(TFBaseObject.UpgradeLevelHealthMultiplier, l - 1)))];
               return new TFBuildingFacts(t, info.Cost, info.BuildTime, info.UpgradeCost, TFBaseObject.MaxUpgradeLevel, health);
           })];

    /// <summary>Where an aim point sits over a TF2 body's feet, standing and ducked: the feet a blast is aimed at, the middle, the eye.</summary>
    public static StanceHeight AimHeight(AimPoint point, TFClass playerClass)
    {
        MovementParams body = TFViewVectors.Params;
        return point switch
        {
            AimPoint.Feet => new StanceHeight(FeetAim, FeetAim),
            AimPoint.Head => new StanceHeight(TFViewVectors.ClassEyeHeight(playerClass, body.View.Z), body.DuckView.Z),
            _ => new StanceHeight(body.HullMax.Z / 2f, body.DuckHullMax.Z / 2f),
        };
    }

    /// <summary>A splash aimed this far over the feet lands on the floor beside them, not past them.</summary>
    public const float FeetAim = 4f;

    /// <summary>
    /// How far from where it bursts a projectile a client draws hurts a body (plan_bots_teams_tf2_part_g
    /// G.2.3), by the binding's own kind for it: a rocket's blast, and a grenade's (a pipe, a sticky)
    /// as the mod has it before a launcher's own attributes. 0 for a kind that has no blast.
    /// </summary>
    public static float BlastRadius(Senses.TFThingType thing) => thing switch
    {
        Senses.TFThingType.Rocket => TFProjectileDefs.RocketRadius,
        Senses.TFThingType.Pipe or Senses.TFThingType.Sticky => TFProjectileDefs.GrenadeJumpRadius,
        _ => 0f,
    };

    /// <summary>How hard a projectile a client draws falls, units/s²: a pipe and a sticky by the mod's scale, a rocket not at all.</summary>
    public static float FallOf(Senses.TFThingType thing) => thing switch
    {
        Senses.TFThingType.Pipe or Senses.TFThingType.Sticky => ProjectileGravity(TFProjectile.Pipebomb),
        _ => 0f,
    };

    /// <summary>How far off a sentry gun finds a target, whatever its level: inside this of one that sees the bot, it is being shot.</summary>
    public static float SentryRange => TFSentryGun.SentryMaxRange;

    /// <summary>Half a TF2 body's width: a blast that reaches this near its middle reaches it.</summary>
    public static float BodyHalfWidth => TFViewVectors.Params.HullMax.X;
}
