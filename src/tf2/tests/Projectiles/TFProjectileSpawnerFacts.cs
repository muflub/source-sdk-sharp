using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The projectile spawner (<see cref="ITFProjectileSpawner"/>): a projectile
/// fired with the caller's own numbers, by an owner that need not be a
/// player, over the applier's rig.
/// </summary>
public class TFProjectileSpawnerFacts
{
    /// <summary>An owner that is no player: an entity past the player slots.</summary>
    private static readonly EntityHandle Robot = new(0x0002_0000u | 150);

    /// <summary>The world, entry 0: what a rocket that missed runs into.</summary>
    private static readonly EntityHandle World = new(0x0001_0000u);

    private const float Damage = 37f;

    /// <summary>
    /// The rig with an engine that tells its entities apart: an index is the
    /// handle's own entry, only <see cref="ProjectileRig.Player"/> and
    /// <see cref="ProjectileRig.Victim"/> are players (red and blue), and a
    /// player slot's handle is the rig's player or nobody — what the real
    /// seam answers for <see cref="Robot"/>'s index.
    /// </summary>
    private static ProjectileRig Rig()
    {
        var rig = new ProjectileRig();
        var engine = rig.Engine;
        // Everyone stands where the blast goes off: point blank.
        engine.Returns("Entities.GetWorldSpaceCenter", default(Vector));
        engine.ReturnsEachCall("Entities.GetIndex", () => Asked(engine).EntryIndex);
        engine.ReturnsEachCall("Entities.IsPlayer",
            () => Asked(engine) == ProjectileRig.Player || Asked(engine) == ProjectileRig.Victim);
        engine.ReturnsEachCall("Players.GetHandle",
            () => engine.Calls[^1].Args[0] is int index && index == ProjectileRig.Player.EntryIndex
                ? ProjectileRig.Player
                : EntityHandle.None);
        engine.ReturnsEachCall("Players.GetTeam", () => Asked(engine) == ProjectileRig.Player ? (int)TFTeam.Red
                                                      : Asked(engine) == ProjectileRig.Victim ? (int)TFTeam.Blue
                                                      : -1);
        return rig;
    }

    /// <summary>The handle the call being answered asked about.</summary>
    private static EntityHandle Asked(RecordingEngine engine)
        => engine.Calls[^1].Args[0] is EntityHandle handle ? handle : EntityHandle.None;

    private static TFProjectileSpawn RobotShot(string className, int? team = null)
        => new(className, Robot, new Vector(10f, 20f, 30f), new Vector(1100f, 0f, 0f), Damage) { Team = team };

    /// <summary>The robot's rocket, spawned and brought to its touch on <paramref name="hit"/>.</summary>
    private static ProjectileRig RobotRocketHits(EntityHandle hit, params EntityHandle[] near)
        => RobotRocketHits(Rig(), hit, near);

    /// <summary>The same, over a <paramref name="rig"/> a fact has already turned.</summary>
    private static ProjectileRig RobotRocketHits(ProjectileRig rig, EntityHandle hit, params EntityHandle[] near)
    {
        var rocket = rig.Rocket();
        rocket.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = rocket;
        rig.Engine.FillsSphere(near);

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Rocket));
        rocket.OnTouch(hit);
        return rig;
    }

    [Fact]
    public void ARocketANonPlayerFiresDeliversItsDamageAtPointBlank()
    {
        // The direct hit is scored at distance zero (ApplyToEntity's
        // GetEnemy arm), so the blast hands the damage layer the request's
        // own number: 37, before any ramp or falloff of the layer's.
        var rig = RobotRocketHits(ProjectileRig.Victim, ProjectileRig.Victim);

        Assert.Equal(Damage, HitOn(rig, ProjectileRig.Victim).ArgAs<float>(2), 0.001f);
    }

    [Fact]
    public void TheDamageIsDealtInTheNonPlayerOwnersName()
    {
        var rig = RobotRocketHits(ProjectileRig.Victim, ProjectileRig.Victim);

        Assert.Equal(Robot, HitOn(rig, ProjectileRig.Victim).ArgAs<EntityHandle>(1));
    }

    /// <summary>The one damage ask <paramref name="victim"/> took.</summary>
    private static EngineCall HitOn(ProjectileRig rig, EntityHandle victim)
        => Assert.Single(rig.Engine.AllOf("Damage.TakeDamageAnswered"),
                         call => call.ArgAs<EntityHandle>(0) == victim);

    [Fact]
    public void ANonPlayerOwnerTakesItsOwnPassFromItsRocket()
    {
        // Stock's second sweep asks only that there is an attacker
        // (CTFGameRules::RadiusDamage, tf_gamerules.cpp:5663): the owner is
        // skipped in the sweep and hit after it at the rocket-jump radius,
        // 121, with the blast's half falloff. A non-player is scored at the
        // trace's end (ApplyToEntity, :5803-5806), 40 out here, and takes
        // none of a player's own arms: no soldier's 0.6 (CTFPlayer's
        // OnTakeDamage), whatever the class seam answers.
        var rig = Rig();
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(0f, 0f, 41f), default, EntityHandle.None));
        rig = RobotRocketHits(rig, World, Robot, ProjectileRig.Victim);

        // 37 - 18.5 * 40/121.
        Assert.Equal(30.88f, HitOn(rig, Robot).ArgAs<float>(2), 0.01f);
        Assert.Equal(Robot, HitOn(rig, Robot).ArgAs<EntityHandle>(1)); // its own blast
    }

    [Fact]
    public void ANonPlayerOwnersRocketCarriesTheTeamTheCallerNamed()
    {
        var rig = Rig();
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Rocket, team: (int)TFTeam.Blue));

        Assert.Equal((int)TFTeam.Blue, rocket.State.TeamNumber);
    }

    [Fact]
    public void ANonPlayerOwnersRocketIsUnassignedWhenNoTeamIsNamed()
    {
        // Not the player seam's -1 for "not a player": the row's unassigned,
        // which the client's trail reads as no team.
        var rig = Rig();
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Rocket));

        Assert.Equal((int)TFTeam.Unassigned, rocket.State.TeamNumber);
    }

    [Fact]
    public void ANonPlayerOwnersPipeCarriesTheTeamTheCallerNamed()
    {
        var rig = Rig();
        rig.Engine.Returns("Clock.Now", 10f);
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Pipebomb, team: (int)TFTeam.Blue));

        Assert.Equal((int)TFTeam.Blue, pipe.State.TeamNumber);
    }

    [Fact]
    public void ANonPlayerOwnersFlareCarriesTheTeamTheCallerNamed()
    {
        var rig = Rig();
        var flare = rig.Flare();
        rig.Directory.Instance = flare;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Flare, team: (int)TFTeam.Blue));

        Assert.Equal((int)TFTeam.Blue, flare.State.TeamNumber);
    }

    [Fact]
    public void TheProjectileIsBornWhereTheCallerSaid()
    {
        var rig = Rig();
        rig.Directory.Instance = rig.Rocket();

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Rocket));

        Assert.Equal(new Vector(10f, 20f, 30f), rig.Engine.Last("Entities.SetOrigin").ArgAs<Vector>(1));
    }

    [Fact]
    public void TheProjectileLeavesAtTheCallersVelocity()
    {
        var rig = Rig();
        rig.Directory.Instance = rig.Rocket();

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Rocket));

        Assert.Equal(new Vector(1100f, 0f, 0f), rig.Engine.Last("Entities.SetVelocity").ArgAs<Vector>(1));
    }

    [Fact]
    public void ASpawnedPipeIsWorthTheCallersDamage()
    {
        var rig = Rig();
        rig.Engine.Returns("Clock.Now", 10f);
        var pipe = rig.Pipebomb();
        rig.Directory.Instance = pipe;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Pipebomb));

        Assert.Equal(Damage, pipe.FullDamage);
    }

    [Fact]
    public void ASpawnedFlareIsWorthTheCallersDamage()
    {
        var rig = Rig();
        var flare = rig.Flare();
        rig.Directory.Instance = flare;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Flare));

        Assert.Equal(Damage, flare.ShotDamage);
    }

    [Fact]
    public void ASpawnedEnergyBallIsWorthTheCallersDamage()
    {
        var rig = Rig();
        var ball = rig.Ball();
        rig.Directory.Instance = ball;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.EnergyBall));

        Assert.Equal(Damage, ball.ShotDamage);
    }

    [Fact]
    public void ASpawnedEnergyRingIsWorthTheCallersDamage()
    {
        var rig = Rig();
        var ring = rig.Ring();
        rig.Directory.Instance = ring;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.EnergyRing));

        Assert.Equal(Damage, ring.ShotDamage);
    }

    [Fact]
    public void ASpawnedArrowIsWorthTheCallersDamage()
    {
        var rig = Rig();
        var arrow = rig.Arrow();
        rig.Directory.Instance = arrow;

        rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Arrow));

        Assert.Equal(Damage, arrow.ShotDamage);
    }

    /// <summary>What a launch left on the rocket and asked of the engine about it.</summary>
    private sealed record RocketLaunch(float Damage, TFWeaponId Weapon, string WeaponName, bool Critical,
                                       int Team, int OwnerRow, string WeaponRow, bool CriticalRow,
                                       Vector Origin, Vector Velocity, Vector Angles, EntityHandle Owner);

    private static RocketLaunch Read(ProjectileRig rig, TFRocket rocket)
        => new(rocket.Damage, rocket.Weapon, rocket.WeaponName, rocket.Critical,
               rocket.State.TeamNumber, rocket.State.OwnerIndex, rocket.State.WeaponName, rocket.State.Critical,
               rig.Engine.Last("Entities.SetOrigin").ArgAs<Vector>(1),
               rig.Engine.Last("Entities.SetVelocity").ArgAs<Vector>(1),
               rig.Engine.Last("Entities.SetAngles").ArgAs<Vector>(1),
               rig.Engine.Last("Entities.SetOwner").ArgAs<EntityHandle>(1));

    [Fact]
    public void APlayersSpawnLeavesTheRocketAsTheLaunchersShotDoes()
    {
        // The weapon's own road: the stock launcher's script, a crit of 90.
        var fired = Rig();
        var firedRocket = fired.Rocket();
        fired.Directory.Instance = firedRocket;
        var files = WeaponFixtures.ShippedWithClasses();
        var script = new TFWeaponScripts(new RecordingLog(), files).Get("tf_weapon_rocketlauncher")!;
        var attack = new WeaponAttackContext(default!, null!, null!, 1f, true, null!,
                                             new WeaponCommand(1, 5, ProjectileRig.Player.EntryIndex));
        fired.Effects.Projectile(attack, script, new ShotDamage(90f, Critical: true), 0f);
        RocketLaunch weapon = Read(fired, firedRocket);

        // The spawner's, from the same muzzle at the same velocity.
        var spawned = Rig();
        var spawnedRocket = spawned.Rocket();
        spawned.Directory.Instance = spawnedRocket;
        spawned.Spawner.Spawn(new TFProjectileSpawn(TFProjectileEntityNames.Rocket, ProjectileRig.Player,
                                                    weapon.Origin, weapon.Velocity, 90f, Critical: true));

        Assert.Equal((int)TFTeam.Red, weapon.Team); // the stimulus reached the row
        Assert.Equal(weapon, Read(spawned, spawnedRocket));
    }

    [Fact]
    public void AnUnknownClassIsRefusedByName()
    {
        var rig = Rig();

        var refusal = Assert.Throws<ArgumentException>(
            () => rig.Spawner.Spawn(RobotShot("tf_projectile_nonesuch")));

        Assert.Contains("tf_projectile_nonesuch", refusal.Message);
    }

    [Fact]
    public void ARefusedClassCreatesNothing()
    {
        var rig = Rig();

        Assert.Throws<ArgumentException>(() => rig.Spawner.Spawn(RobotShot("tf_projectile_nonesuch")));

        Assert.Equal(0, rig.Engine.CountOf("Entities.Create"));
    }

    [Fact]
    public void AProjectileWhoseHitIsNotTheCallersNumberIsRefused()
    {
        // The syringe's worth is its own constant: a damage handed in would
        // be dropped without a word, so the class is not launched here.
        var rig = Rig();

        Assert.Throws<ArgumentException>(() => rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Syringe)));
    }

    [Fact]
    public void AKnownClassIsAccepted()
    {
        // The refusals' other arm: the same request with a class it launches.
        var rig = Rig();
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;

        Assert.Same(rocket, rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Rocket)));
    }

    [Fact]
    public void AnOwnerWhoIsGoneFiresNothing()
    {
        var rig = Rig();
        rig.Directory.Instance = rig.Rocket();
        rig.Engine.ReturnsEachCall("Entities.IsValid", () => Asked(rig.Engine) != Robot);

        Assert.Null(rig.Spawner.Spawn(RobotShot(TFProjectileEntityNames.Rocket)));
        Assert.Equal(0, rig.Engine.CountOf("Entities.Create"));
    }

    /// <summary>A spawner that only writes down the births it was asked for.</summary>
    private sealed class BirthLedger : ITFProjectileSpawner
    {
        public List<string> Births { get; } = new();

        public bool Launches(string className) => false;
        public ScriptedEntity? Spawn(in TFProjectileSpawn spawn) => null;

        public ScriptedEntity? Born(string className, Vector origin)
        {
            Births.Add(className);
            return null;
        }
    }

    [Fact]
    public void TheWeaponsLaunchIsBornThroughTheSpawner()
    {
        // One birth for both roads: the launcher's rocket asks the spawner
        // and creates nothing with its own hands.
        var rig = Rig();
        var ledger = new BirthLedger();
        var effects = new TFProjectileEffects(rig.Engine, ledger, rig.Engine, rig.Tray, rig.FlareTray, rig.Log,
                                              rig.Owners, rig.Random, rig.Hooks, rig.Temps, rig.Engine, rig.Engine,
                                              rig.StickyVars);
        var script = new TFWeaponScripts(new RecordingLog(), WeaponFixtures.ShippedWithClasses())
            .Get("tf_weapon_rocketlauncher")!;
        var attack = new WeaponAttackContext(default!, null!, null!, 1f, true, null!,
                                             new WeaponCommand(1, 5, ProjectileRig.Player.EntryIndex));

        effects.Projectile(attack, script, new ShotDamage(90f), 0f);

        Assert.Equal([TFProjectileEntityNames.Rocket], ledger.Births);
        Assert.Equal(0, rig.Engine.CountOf("Entities.Create"));
    }
}
