using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
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

    /// <summary>
    /// <c>FireProjectile</c>'s jar arm (<c>tf_weaponbase_gun.cpp:303-311</c>): the
    /// festive and bread-monster items override the projectile type, and every
    /// one of them still throws a jar.
    /// </summary>
    [Theory]
    [InlineData(22f, TFProjectileEntityNames.Jar)]
    [InlineData(24f, TFProjectileEntityNames.Jar)]
    [InlineData(25f, TFProjectileEntityNames.JarMilk)]
    public void An_overridden_jar_is_still_thrown(float face, string thrown)
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null;
        rig.Owners.Item = rig.AttributeItem("override_projectile_type",
                                            AttributeDescriptionFormat.Additive, face);

        rig.Effects.Projectile(Attack(3), JarScript("tf_weapon_jar"), default, 0f);

        Assert.Equal(thrown, rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
    }

    /// <summary>
    /// The arrow arm of the same switch (<c>tf_weaponbase_gun.cpp:314-320</c>):
    /// the festive bow and crossbow override the type, and still fire their
    /// arrow and bolt.
    /// </summary>
    [Theory]
    [InlineData(TFProjectile.FestiveArrow, "tf_weapon_compound_bow", TFProjectileEntityNames.Arrow)]
    [InlineData(TFProjectile.FestiveHealingBolt, "tf_weapon_crossbow", TFProjectileEntityNames.HealingBolt)]
    public void An_overridden_arrow_is_still_fired(TFProjectile face, string weapon, string fired)
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null;
        rig.Owners.Item = rig.AttributeItem("override_projectile_type",
                                            AttributeDescriptionFormat.Additive, (float)face);

        var bow = new WeaponNetState();
        bow.BindLocal();
        var drawn = new WeaponAttackContext(default!, bow, null!, 1f, true, null!,
                                            new WeaponCommand(1, 5, 3));
        rig.Effects.Projectile(drawn, JarScript(weapon), default, 1f);

        Assert.Equal(fired, rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
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
        rig.Engine.Returns("Clock.Now", 10f); // the throw's clock, the teammate grace's start
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

    /// <summary>
    /// Stock's grace ends (tf_weapon_jar.cpp:517-533, <c>CanCollideWithTeammates</c>
    /// after <see cref="TFProjectileDefs.CollideWithTeammatesDelay"/>): a jar that has
    /// flown past it shatters on a dry teammate. Review of F242: the jar's own touch
    /// passed a dry teammate for its whole flight.
    /// </summary>
    [Fact]
    public void Past_the_grace_a_dry_teammate_stops_the_jar()
    {
        var (rig, jar) = LandedJar();
        jar.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = jar;
        rig.Engine.Returns("Players.GetTeam", 2) // the same side, thrower and touched
                  .Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.GetAngles", Vector.Zero)
                  .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity", "Entities.SetAngles");
        // The clock past the grace since the throw (at 10).
        rig.Engine.Returns("Clock.Now", 10f + TFProjectileDefs.CollideWithTeammatesDelay + 0.05f);

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
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
        var conditions = new TFConditions(authoritative: true,
                         new TFCondHolder());
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
        var conditions = new TFConditions(authoritative: true,
                         new TFCondHolder());
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
        // The refund's reads on the thrower's held jar, answered the plain way.
        rig.Engine.Returns("Entities.GetIndex", 3);
        rig.Engine.Returns("Players.GetActiveWeaponEntity", new EntityHandle(0x0002_0000u | 7));

        jar.OnTouch(ProjectileRig.Victim);

        var doused = Assert.Single(rig.Wet.Removed);
        Assert.Equal(ProjectileRig.Victim, doused.Item1);
        Assert.Equal(TFCond.Burning, doused.Item2);
        Assert.Empty(rig.Wet.Added);
    }

    /// <summary>The refund behind the extinguish (JarExplode :452-468): a held
    /// item's extinguish_reduces_cooldown is the fraction of the wait the
    /// extinguisher keeps; the rest lands on the jar's refill at once.</summary>
    [Fact]
    public void Putting_out_an_ally_hands_the_refund_to_the_launchers_refill()
    {
        var (rig, jar) = LandedJar();
        rig.Wet.Burning.Add(ProjectileRig.Victim);
        rig.Engine.Returns("Players.GetTeam", 2); // the same side, both
        var (gun, gunHandle) = HeldJar(rig);
        rig.Engine.Returns("Players.GetActiveWeaponEntity", gunHandle);
        // Percentage over the held one: the hook's answer is the kept fraction.
        rig.Owners.Active = rig.AttributeItem("extinguish_reduces_cooldown",
                                              AttributeDescriptionFormat.Percentage, 0.5f);

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Equal(0.5f, gun.State.ItemMeter);   // half the wait kept, half handed back
    }

    /// <summary>No attribute, no refund: the held default of one is the whole
    /// wait kept, and the bar stands where the launch left it.</summary>
    [Fact]
    public void A_plain_jar_hands_nothing_back_for_the_extinguish()
    {
        var (rig, jar) = LandedJar();
        rig.Wet.Burning.Add(ProjectileRig.Victim);
        rig.Engine.Returns("Players.GetTeam", 2);
        var (gun, gunHandle) = HeldJar(rig);
        rig.Engine.Returns("Players.GetActiveWeaponEntity", gunHandle);

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Equal(0f, gun.State.ItemMeter);
    }

    /// <summary>The held launcher: a weapon entity behind the active-weapon
    /// ask, its bar the refund lands on.</summary>
    private static (SsWeapon Gun, EntityHandle Handle) HeldJar(ProjectileRig rig)
    {
        var handle = new EntityHandle(0x0004_0000u | 11);
        var gun = new SsWeapon();
        gun.Handle = handle;
        gun.State.BindLocal();
        rig.Directory.Holds(handle, gun);
        return (gun, handle);
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

    /// <summary>
    /// Stock's refund behind the douse (tf_weapon_jar.cpp:452-468): half the
    /// recharge comes back to the thrower's jarate bar while the bar is still
    /// behind the refund, and the bar stands where it was with no hook.
    /// </summary>
    [Fact]
    public void The_burning_teammate_refunds_half_the_launchers_bar()
    {
        var (rig, jar) = RefundRig(out var launcher, startingMeter: 0.25f);
        rig.Owners.Active = rig.AttributeItem("extinguish_reduces_cooldown",
                                              AttributeDescriptionFormat.Percentage, 0.5f);

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Equal(TFCond.Burning, Assert.Single(rig.Wet.Removed).Item2);
        Assert.Equal(0.75f, launcher.State.ItemMeter, 3);
    }

    [Fact]
    public void With_no_hook_the_launchers_bar_keeps_where_it_was()
    {
        var (rig, jar) = RefundRig(out var launcher, startingMeter: 0.25f);

        jar.OnTouch(ProjectileRig.Victim);

        Assert.Equal(TFCond.Burning, Assert.Single(rig.Wet.Removed).Item2);
        Assert.Equal(0.25f, launcher.State.ItemMeter, 3);
    }

    /// <summary>
    /// The douse's refund, ready to ask: a jar on the ground by a burning
    /// teammate of the thrower's own side, the thrower's held jar on record
    /// in the directory at the starting bar. Both facts press through the
    /// touch, so the refund rides the same path the splash does.
    /// </summary>
    private static (ProjectileRig Rig, TFJar Jar) RefundRig(out SourceSharp.Server.SsWeapon launcher,
                                                             float startingMeter)
    {
        var (rig, jar) = LandedJar();
        rig.Wet.Burning.Add(ProjectileRig.Victim);
        rig.Engine.Returns("Players.GetTeam", 2); // the same side, both

        var weaponHandle = new EntityHandle(0x0002_0000u | 7);
        rig.Engine.Returns("Players.GetActiveWeaponEntity", weaponHandle);
        rig.Engine.Returns("Entities.GetIndex", 3); // the thrower's own entry
        launcher = new SourceSharp.Server.SsWeapon { Handle = weaponHandle };
        launcher.State.BindLocal();
        launcher.State.ItemMeter = startingMeter;
        rig.Directory.Holds(weaponHandle, launcher);
        return (rig, jar);
    }

    /// <summary>
    /// <c>SetCustomPipebombModel</c> (:261-284): the festive held item
    /// re-skins the born jar; a plain held item keeps the jar's own model.
    /// </summary>
    [Fact]
    public void The_festive_held_item_wears_the_festive_jar()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Players.GetTeam", 3);
        var jar = rig.Jar();
        rig.Owners.Active = rig.AttributeItem("override_projectile_type",
                                              AttributeDescriptionFormat.Additive, 22f);

        jar.OnSpawn();
        jar.ArmedBy(critical: false, ownerIndex: 3);

        Assert.Equal(TFProjectileDefs.JarFestiveModel,
                     rig.Engine.Last("EntityRender.SetModel")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void The_plain_held_item_keeps_the_born_jar()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Players.GetTeam", 3);
        var jar = rig.Jar();

        jar.OnSpawn();
        jar.ArmedBy(critical: false, ownerIndex: 3);

        Assert.Equal(TFProjectileDefs.JarModel,
                     rig.Engine.Last("EntityRender.SetModel")!.Value.ArgAs<string>(1));
    }

    /// <summary>
    /// The milk's own arm (:836-855): the bread monster's face answers on the
    /// milk too, the madmilk model beside it.
    /// </summary>
    [Fact]
    public void The_milk_bread_monster_wears_the_bread_monster()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Players.GetTeam", 3);
        var jar = rig.Jar();
        jar.ClassName = TFProjectileEntityNames.JarMilk;
        rig.Owners.Active = rig.AttributeItem("override_projectile_type",
                                              AttributeDescriptionFormat.Additive, 24f);

        jar.OnSpawn();
        jar.ArmedBy(critical: false, ownerIndex: 3);

        Assert.Equal(TFProjectileDefs.JarMilkBreadmonsterModel,
                     rig.Engine.Last("EntityRender.SetModel")!.Value.ArgAs<string>(1));
    }

    // ---- the flight: stock's jar is a physics body (lane r2s4's stock A/B, 2026-11-24)

    /// <summary>
    /// The jar, the milk and the gas register as physics props, as stock's <c>CTFProjectile_Jar</c> is a
    /// <c>CTFGrenadePipebombProjectile</c> made in <c>VPhysicsInitNormal</c> (tf_weaponbase_grenadeproj.cpp:280): the solver
    /// flies them under the environment's gravity and drag. Was a projectile the integrator flew at 0.4 of gravity, no drag.
    /// </summary>
    [Fact]
    public void Every_jar_face_is_a_physics_body()
    {
        var kinds = typeof(TFJar).GetCustomAttributes(typeof(ScriptedEntityAttribute), false).Cast<ScriptedEntityAttribute>().ToList();
        Assert.Equal(3, kinds.Count);
        Assert.All(kinds, k => Assert.Equal(ScriptedEntityKind.Prop, k.Kind));
    }

    /// <summary>The jar is born facing the eye and spun as stock's: <c>AngularImpulse(300, 0, 0)</c> twice about its own forward.</summary>
    [Fact]
    public void The_jar_is_born_facing_the_eye_and_spun_about_its_forward()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f));
        rig.Directory.Instance = rig.Jar();

        rig.Effects.Projectile(Attack(3), JarScript("tf_weapon_jar"), default, 0f);

        Vector born = rig.Engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1);
        Assert.Equal(90f, born.Y, 0.01f);
        Vector spin = rig.Engine.Last("Entities.SetAngularVelocity")!.Value.ArgAs<Vector>(1);
        Assert.Equal(0f, spin.X, 0.5f);
        Assert.Equal(2f * TFProjectileDefs.JarSpinRoll, spin.Y, 0.5f);
        Assert.Equal(0f, spin.Z, 0.5f);
    }

    /// <summary>The solved impact on the world shatters the jar (<c>VPhysicsCollision</c>, tf_weapon_jar.cpp:592-617).</summary>
    [Fact]
    public void A_world_collision_shatters_the_jar()
    {
        var (rig, jar) = LandedJar();
        EnemyInTheOpen(rig);
        var ev = new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.PostCollision,
            Flags = PhysicsEvent.FlagCollision,
            Entity0 = jar.Handle.Value,
            Entity1 = PhysicsEvent.NoEntity,
            Static1 = 1,
        };

        jar.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        Assert.Single(rig.Wet.Added);
    }

    private static PhysicsEvent BodyCollision(TFJar jar) => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Flags = PhysicsEvent.FlagCollision,
        Entity0 = jar.Handle.Value,
        Entity1 = 0x0002_0000u | 7,
    };

    /// <summary>A dynamic prop breaks it too (the collision's <c>CDynamicProp</c> arm, tf_weapon_jar.cpp:607-614).</summary>
    [Fact]
    public void A_dynamic_prop_shatters_the_jar()
    {
        var (rig, jar) = LandedJar();
        rig.Engine.Returns("Entities.GetClassName", "prop_dynamic");
        PhysicsEvent ev = BodyCollision(jar);
        jar.OnVPhysicsCollision(in ev, 0);
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    /// <summary>Another moving body (a physics prop) bounces it: no shatter.</summary>
    [Fact]
    public void A_physics_prop_does_not_shatter_the_jar()
    {
        var (rig, jar) = LandedJar();
        rig.Engine.Returns("Entities.GetClassName", "prop_physics");
        PhysicsEvent ev = BodyCollision(jar);
        jar.OnVPhysicsCollision(in ev, 0);
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
    }
}
