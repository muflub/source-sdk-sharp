using SourceSharp.Abi;

using SourceSharp.Shared;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The jars (<c>CTFJar</c>, <c>tf_weapon_jar.cpp</c>, the milk its second
/// face): the lob the launch asks for, the touch rules the pipebomb wears,
/// and the splash that wets the enemy and puts the burning teammate out.
/// </summary>
public sealed class TFJarFacts
{
    private static TFWeaponScript JarScript(string name)
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get(name)
            ?? throw new InvalidOperationException($"no script {name}");
    }

    private static WeaponAttackContext Attack(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    [Fact]
    public void The_armed_jar_wears_the_throwers_team_and_the_shots_crit()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Players.GetTeam", 3);
        var jar = rig.Jar();

        jar.ArmedBy(critical: true, ownerIndex: 3);

        Assert.True(jar.State.Critical);
        Assert.Equal(3, jar.State.TeamNumber);
    }

    [Fact]
    public void The_spawn_precaches_the_shatter()
    {
        var rig = new ProjectileRig();
        var jar = rig.Jar();

        jar.OnSpawn();

        Assert.Equal(TFProjectileDefs.JarImpactEffect,
                     rig.Engine.Last("Precache.PrecacheParticleSystem")!.Value.ArgAs<string>(0));
        Assert.Equal(TFProjectileDefs.JarExplodeSound,
                     rig.Engine.Last("Precache.PrecacheSound")!.Value.ArgAs<string>(0));
    }

    [Fact]
    public void The_jar_is_thrown_under_its_own_name()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null; // the mapping is what the fact holds

        rig.Effects.Projectile(Attack(3), JarScript("tf_weapon_jar"), default, 0f);

        Assert.Equal(TFProjectileEntityNames.Jar, rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
    }

    [Fact]
    public void The_jar_leaves_the_hand_at_the_pipebomb_lobs_shape()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = rig.Jar();

        rig.Effects.Projectile(Attack(3), JarScript("tf_weapon_jar"), default, 0f);

        // Born at the pipebomb's muzzle off the eye, the lob forward at the
        // jar's speed with the pipebomb's up and no jitter (the dial at its
        // midpoint).
        var muzzle = rig.Engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.PipeMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.PipeMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.PipeMuzzleUp, muzzle.Z, 0.5f);
        var velocity = rig.Engine.Last("Entities.SetVelocity")!.Value.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.JarSpeed, velocity.X, 0.5f);
        Assert.Equal(TFProjectileDefs.PipeLaunchUp, velocity.Z, 0.5f);
    }

    [Fact]
    public void The_milk_leaves_under_its_own_name()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null;

        rig.Effects.Projectile(Attack(3), JarScript("tf_weapon_jar_milk"), default, 0f);

        Assert.Equal(TFProjectileEntityNames.JarMilk, rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
    }

    /// <summary>A launched jar whose touch is about to be asked.</summary>
    private static (ProjectileRig Rig, TFJar Jar) FiredJar(string name = "tf_weapon_jar")
    {
        var rig = new ProjectileRig();
        // The victim is a player; anything else the touch names is not.
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
        var jar = rig.Jar();
        if (name == "tf_weapon_jar_milk")
            jar.ClassName = TFProjectileEntityNames.JarMilk; // the milk's projectile name, what Milk reads
        jar.OnSpawn();
        jar.ArmedBy(critical: false, ownerIndex: 3);
        jar.FiredBy(ProjectileRig.Player, new Vector(TFProjectileDefs.JarSpeed, 0f, 0f));
        return (rig, jar);
    }

    /// <summary>The touch questions answered, the victim standing wet-able in the open.</summary>
    private static (ProjectileRig Rig, TFJar Jar) LandedJar(string name = "tf_weapon_jar")
    {
        var (rig, jar) = FiredJar(name);
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Players.IsAlive", true);
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 64f));
        return (rig, jar);
    }

    [Fact]
    public void The_jar_flies_through_what_is_neither_player_nor_world()
    {
        var (rig, jar) = FiredJar();

        jar.OnTouch(new EntityHandle(0x0002_0000u | 7));

        // Flown on, not spent: no removal, the splash never asked.
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
        Assert.Empty(rig.Wet.Added);
        Assert.Empty(rig.Wet.Removed);
    }

    [Fact]
    public void The_dry_teammate_is_flown_through()
    {
        var (rig, jar) = FiredJar();
        rig.Engine.Returns("Players.GetTeam", 2); // the same side, thrower and touched

        jar.OnTouch(ProjectileRig.Victim);

        // The grace's fly-on, and the splash's stay-home with it.
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
        Assert.Empty(rig.Wet.Added);
        Assert.Empty(rig.Wet.Removed);
    }

    [Fact]
    public void The_skybox_takes_the_jar_whole()
    {
        var (rig, jar) = FiredJar();
        EnemyInTheOpen(rig);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(1200f, 0f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLineSurface",
                           new SurfaceTraceResult(true, 0.5f, new Vector(0f, 0f, 320f),
                                                  new Vector(0f, 0f, 1f), EntityHandle.None, 0,
                                                  0x0004 /* SURF_SKY */));

        jar.OnTouch(ProjectileRig.Victim);

        // Spent, and no splash: the skybox shatters nothing.
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        Assert.Empty(rig.Wet.Added);
    }

    /// <summary>The enemy team behind the splash: the thrower's side is 3, everyone else 2.</summary>
    private static void EnemyInTheOpen(ProjectileRig rig)
    {
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Player ? 3 : 2);
    }

    [Fact]
    public void The_splash_wets_the_enemy_in_the_open()
    {
        var (rig, jar) = LandedJar();
        EnemyInTheOpen(rig);

        jar.OnTouch(ProjectileRig.Victim);

        var added = Assert.Single(rig.Wet.Added);
        Assert.Equal(ProjectileRig.Victim, added.Player);
        Assert.Equal(TFCond.Urine, added.Cond);
        Assert.Equal(TFProjectileDefs.JarDuration, added.Duration, 2);
        Assert.Equal(ProjectileRig.Player, added.Provider);

        // The shatter, heard where the jar burst.
        Assert.Equal(TFProjectileDefs.JarExplodeSound,
                     rig.Engine.Last("Sound.EmitSoundAtPosition")!.Value.ArgAs<string>(0));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_invulnerable_stay_dry()
    {
        var (rig, jar) = LandedJar();
        EnemyInTheOpen(rig);
        var conditions = new TFConditions(authoritative: true);
        conditions.ReceiveBits(TFCondMask.Of([TFCond.Invulnerable]));
        rig.Wet.States[ProjectileRig.Victim.EntryIndex] = conditions;

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Empty(rig.Wet.Added);
    }

    [Fact]
    public void The_phased_stay_dry()
    {
        var (rig, jar) = LandedJar();
        EnemyInTheOpen(rig);
        var conditions = new TFConditions(authoritative: true);
        conditions.ReceiveBits(TFCondMask.Of([TFCond.Phase]));
        rig.Wet.States[ProjectileRig.Victim.EntryIndex] = conditions;

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Empty(rig.Wet.Added);
    }

    [Fact]
    public void A_wall_between_the_splatter_and_the_victim_keeps_them_dry()
    {
        var (rig, jar) = LandedJar();
        EnemyInTheOpen(rig);
        rig.Engine.Returns("Entities.GetIndex", 0); // the trace's stop is the world
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 0.5f, default, default, new EntityHandle(0)));

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Empty(rig.Wet.Added);
    }

    [Fact]
    public void The_burning_teammate_is_put_out()
    {
        var (rig, jar) = LandedJar();
        rig.Wet.Burning.Add(ProjectileRig.Victim);
        rig.Engine.Returns("Players.GetTeam", 2); // the same side, both

        jar.OnTouch(ProjectileRig.Victim);

        var doused = Assert.Single(rig.Wet.Removed);
        Assert.Equal(ProjectileRig.Victim, doused.Item1);
        Assert.Equal(TFCond.Burning, doused.Item2);
        Assert.Empty(rig.Wet.Added);
    }

    [Fact]
    public void The_milk_wets_with_the_milks_condition()
    {
        var (rig, jar) = LandedJar("tf_weapon_jar_milk");
        EnemyInTheOpen(rig);

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Equal(TFCond.MadMilk, Assert.Single(rig.Wet.Added).Cond);
    }

    [Fact]
    public void The_milk_precaches_the_milks_shatter()
    {
        var rig = new ProjectileRig();
        var jar = rig.Jar();
        jar.ClassName = TFProjectileEntityNames.JarMilk;

        jar.OnSpawn();

        Assert.Equal(TFProjectileDefs.JarMilkImpactEffect,
                     rig.Engine.Last("Precache.PrecacheParticleSystem")!.Value.ArgAs<string>(0));
    }
}
