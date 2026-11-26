using SourceSharp.Abi;

using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Tests.Combat;

using TF2.Tests.Buildings;
using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The Flying Guillotine's cleaver (<c>CTFProjectile_Cleaver</c>,
/// <c>tf_weapon_jar.cpp:980-1149</c>): the throw's ten-to-one lob, the hit's
/// bleed and 50 slash, the miss's thunk, and the flight's pay back to the
/// launcher's wait.
/// </summary>
public sealed class TFCleaverFacts
{
    /// <summary>The now every clock read answers, so the flight gate reads against it.</summary>
    private const float Now = 10f;

    /// <summary>Where the arrived cleaver's clock stands: a second's flight.</summary>
    private const float LandedAt = Now + 1f;

    private static TFWeaponScript CleaverScript() => Weapons.WeaponFixtures.ParseExtra("tf_weapon_cleaver");

    private static WeaponAttackContext Attack(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    /// <summary>The giver behind the cleaver, recorded: every meter advance.</summary>
    public sealed class GiverSpies : IWeaponGiver
    {
        public readonly List<(EntityHandle Player, string Weapon, float Seconds)> Advanced = [];

        public bool GiveWeapon(EntityHandle player, string className) => true;

        public int GiveAmmo(EntityHandle player, int count, string ammoName) => count;

        public void AdvanceCarriedMeter(EntityHandle player, string className, float seconds)
            => Advanced.Add((player, className, seconds));
    }

    /// <summary>
    /// The cleaver over the projectile rig, plus the player data, conditions
    /// and bleeds its hit's arms read, and the giver its recharge pays.
    /// </summary>
    private sealed class Rig
    {
        public readonly ProjectileRig Base = new();
        public readonly TFMovementFixtures.Players PlayerData = new();
        public readonly RigConditions Conditions;
        public readonly TFBleeds Bleeds;
        public readonly TFBleed VictimBleed;
        public readonly GiverSpies Giver = new();
        public readonly ThinkScheduler Thinks = new();
        public readonly TFCleaver Cleaver;

        public Rig()
        {
            Base.Engine.Returns("Clock.Now", Now);
            Base.Engine.Returns("Players.IsAlive", true);
            Conditions = new RigConditions(PlayerData);
            Bleeds = new TFBleeds(PlayerData, Conditions, Base.Engine, Base.Engine, Base.Engine, Base.Scopes);
            PlayerData.Add(ProjectileRig.Victim.EntryIndex, TFClass.Scout);
            VictimBleed = (TFBleed)Bleeds.Create(ProjectileRig.Victim).Single();
            VictimBleed.Attach(ProjectileRig.Victim);

            Cleaver = new TFCleaver(Base.Engine, Base.Engine, Base.Engine, Base.HitGroups,
                                    Base.Scopes, Bleeds, Base.Engine, Conditions, Base.Engine, Base.Engine,
                                    Base.Engine, Giver, Base.Engine, Base.Directory);
            Cleaver.RegisteredKind = ScriptedEntityKind.Prop; // a physics body, as the registration says
            Cleaver.BindAreas(TestServices.Areas(Base.Engine));
            Cleaver.NetStates = NetStateBinding.AttachLocal(Cleaver, Cleaver.Handle, new NetStateFields(new RecordingLog()));
            Cleaver.BindThinks(Thinks);
        }

        /// <summary>The touched player's team; the enemy by default.</summary>
        public int VictimTeam = 2;

        /// <summary>The victim a player the cleaver can see, in the enemy team.</summary>
        public void PlayersVisible()
        {
            Base.Engine.ReturnsEachCall("Entities.IsPlayer",
                () => Base.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
            Base.Engine.ReturnsEachCall("Players.GetTeam",
                () => Base.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                          ? VictimTeam : 3);
        }

        /// <summary>The real throw, the shot armed and flown by the effects pass.</summary>
        public TFCleaver Thrown(bool critical = false)
        {
            Base.Directory.Instance = Cleaver;
            PlayersVisible();
            Base.Effects.Projectile(Attack(ProjectileRig.Player.EntryIndex), CleaverScript(),
                                    new ShotDamage(TFProjectileDefs.CleaverDamage, Critical: critical), 0f);
            return Cleaver;
        }

        /// <summary>The thrown cleaver, arrived where it strikes.</summary>
        public TFCleaver Arrived(float landedAt, bool critical = false)
        {
            Thrown(critical);
            Base.Engine.Returns("Clock.Now", landedAt);
            Base.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 40f));
            // A clear surface, the world arm's trace's no-sky answer.
            Base.Engine.Returns("WorldQuery.TraceLineSurface",
                                new SurfaceTraceResult(true, 1f, default, default, EntityHandle.None, 0, 0));
            return Cleaver;
        }
    }

    [Fact]
    public void The_cleaver_is_thrown_under_its_own_name()
    {
        var rig = new Rig();
        rig.Base.Directory.Instance = null; // the mapping is what the fact holds

        rig.Base.Effects.Projectile(Attack(3), CleaverScript(), default, 0f);

        Assert.Equal(TFProjectileEntityNames.Cleaver,
                     rig.Base.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
    }

    [Fact]
    public void The_throw_arms_the_shot_with_its_crit_and_launcher()
    {
        var rig = new Rig();

        rig.Thrown(critical: true);

        Assert.True(rig.Cleaver.Critical);
        Assert.Equal("tf_weapon_cleaver", rig.Cleaver.WeaponName);
        Assert.Equal(3, rig.Cleaver.State.TeamNumber); // the thrower's side, the glow the client reads
    }

    [Fact]
    public void The_cleaver_leaves_the_hand_at_the_pipebomb_muzzle()
    {
        var rig = new Rig();

        rig.Thrown();

        var muzzle = rig.Base.Engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.PipeMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.PipeMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.PipeMuzzleUp, muzzle.Z, 0.5f);
    }

    [Fact]
    public void The_cleaver_flies_at_the_guillotines_ten_to_one_tilt()
    {
        // GetVelocityVector (tf_weapon_jar.cpp:906-916): normalize(10 forward
        // + 1 up) at the override's 3000, no sideways draw and no jitter.
        var rig = new Rig();

        rig.Thrown();

        Vector velocity = rig.Base.Engine.Last("Entities.SetVelocity")!.Value.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.CleaverSpeed, velocity.Length, 1);
        Assert.Equal(TFProjectileDefs.CleaverAimForward / TFProjectileDefs.CleaverAimUp, velocity.X / velocity.Z, 3);
    }

    [Fact]
    public void The_spawn_precaches_both_impact_sounds()
    {
        var rig = new Rig();

        rig.Cleaver.OnSpawn();

        string[] precached = rig.Base.Engine.Calls.Where(c => c.Member == "Precache.PrecacheSound")
            .Select(c => c.ArgAs<string>(0)).ToArray();
        Assert.Equal([TFProjectileDefs.CleaverImpactFleshSound, TFProjectileDefs.CleaverImpactWorldSound],
                     precached);
    }

    [Fact]
    public void The_hit_bleeds_the_enemy_for_five_seconds_from_the_thrower()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);

        cleaver.OnTouch(ProjectileRig.Victim);

        var bleed = Assert.Single(rig.VictimBleed.State.Bleeds);
        Assert.Equal(TFWeaponId.Cleaver, bleed.Weapon);
        Assert.Equal("tf_weapon_cleaver", bleed.WeaponName);
        Assert.Equal(TFProjectileDefs.CleaverBleedSeconds, bleed.RemoveTime - LandedAt, 3);
    }

    [Fact]
    public void The_hit_lands_the_50_as_the_cleavers_slash()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);

        cleaver.OnTouch(ProjectileRig.Victim);

        var hit = rig.Base.Engine.Last("Damage.TakeDamage")!.Value;
        Assert.Equal(ProjectileRig.Victim, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(ProjectileRig.Player, hit.ArgAs<EntityHandle>(1));
        Assert.Equal(TFProjectileDefs.CleaverDamage, hit.ArgAs<float>(2), 3);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.GrenadeCleaver), hit.ArgAs<DamageType>(3));
    }

    [Fact]
    public void A_thrown_crits_damage_carries_the_critical_bit()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f, critical: true);

        cleaver.OnTouch(ProjectileRig.Victim);

        var hit = rig.Base.Engine.Last("Damage.TakeDamage")!.Value;
        Assert.True(hit.ArgAs<DamageType>(3).HasFlag(TFDamageBits.Critical));
    }

    [Fact]
    public void The_hit_plays_the_flesh_sound_where_it_landed()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);

        cleaver.OnTouch(ProjectileRig.Victim);

        Assert.Equal(TFProjectileDefs.CleaverImpactFleshSound,
                     rig.Base.Engine.Last("Sound.EmitSoundAtPosition")!.Value.ArgAs<string>(0));
    }

    [Fact]
    public void The_hit_leaves_the_cleaver_unseen_for_two_seconds()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now);
        var thinks = rig.Thinks;

        cleaver.OnTouch(ProjectileRig.Victim);

        var hidden = rig.Base.Engine.Last("EntityRender.SetVisible")!; // RemoveCleaver's NODRAW
        Assert.False(hidden.Value.ArgAs<bool>(1));
        // The rig's entity has no registry owner, so its clock stands at 0:
        // the booking is the bare two seconds.
        Assert.Equal(TFProjectileDefs.CleaverLingerSeconds, Assert.Single(thinks.Bookings()).Time, 3);
    }

    [Fact]
    public void A_carried_hit_lands_the_recharge_the_flight_earned()
    {
        // OnHit (tf_weapon_jar.cpp:1024-1032): past the half-second aloft the
        // launcher's wait is shorter by the constant.
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);

        cleaver.OnTouch(ProjectileRig.Victim);

        var paid = Assert.Single(rig.Giver.Advanced);
        Assert.Equal(ProjectileRig.Player, paid.Player);
        Assert.Equal("tf_weapon_cleaver", paid.Weapon);
        Assert.Equal(TFProjectileDefs.CleaverRechargeSeconds, paid.Seconds, 3);
    }

    [Fact]
    public void A_landing_inside_half_a_second_earns_nothing()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 0.4f);

        cleaver.OnTouch(ProjectileRig.Victim);

        // Bled and hurt all the same; only the pay waits for a real flight.
        Assert.Single(rig.VictimBleed.State.Bleeds);
        Assert.Empty(rig.Giver.Advanced);
    }

    /// <summary>Anything that is neither world nor player is no touch (PipebombTouch :513-514): the body flies on as the solver has it.</summary>
    [Fact]
    public void The_cleaver_flies_through_what_is_neither_player_nor_world()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        int sent = rig.Base.Engine.CountOf("Entities.SetVelocity");

        cleaver.OnTouch(new EntityHandle(0x0002_0000u | 7));

        Assert.Equal(sent, rig.Base.Engine.CountOf("Entities.SetVelocity"));
        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.DoesNotContain(rig.Thinks.Bookings(), b => MathF.Abs(b.Time - TFProjectileDefs.CleaverLingerSeconds) < 0.01f); // only the fuse's look, no linger
    }

    /// <summary>Inside the grace (<see cref="TFProjectileDefs.CollideWithTeammatesDelay"/>) a dry teammate is passed (:517-533).</summary>
    [Fact]
    public void A_dry_teammate_inside_the_grace_is_passed()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 0.1f);
        rig.VictimTeam = 3; // the same side, thrower and touched

        cleaver.OnTouch(ProjectileRig.Victim);

        Assert.DoesNotContain(rig.Thinks.Bookings(), b => MathF.Abs(b.Time - TFProjectileDefs.CleaverLingerSeconds) < 0.01f); // only the fuse's look, no linger
        Assert.Empty(rig.VictimBleed.State.Bleeds);
    }

    /// <summary>
    /// Past the grace a dry teammate stops the cleaver unwounded: <c>OnHit</c> hides it, refuses its own team (:1013-1014),
    /// and <c>Explode</c>'s miss thunks. Was flown through for the whole flight.
    /// </summary>
    [Fact]
    public void Past_the_grace_a_dry_teammate_stops_the_cleaver_unwounded()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.VictimTeam = 3;

        cleaver.OnTouch(ProjectileRig.Victim);

        Assert.Empty(rig.VictimBleed.State.Bleeds);
        Assert.Equal(TFProjectileDefs.CleaverImpactWorldSound,
                     rig.Base.Engine.Last("Sound.EmitSoundAtPosition")!.Value.ArgAs<string>(0));
        Assert.False(rig.Base.Engine.Last("EntityRender.SetVisible")!.Value.ArgAs<bool>(1));
    }

    /// <summary>
    /// A burning teammate inside the grace is struck (the jar face's exception, the cleaver wearing the jar's type, :242) but
    /// never wounded: <c>OnHit</c> refuses its own team (:1013-1014). Was bled like an enemy.
    /// </summary>
    [Fact]
    public void A_burning_teammate_stops_the_cleaver_unwounded()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 0.1f);
        rig.VictimTeam = 3;
        rig.Conditions.Add(ProjectileRig.Victim, TFCond.Burning);

        cleaver.OnTouch(ProjectileRig.Victim);

        Assert.Empty(rig.VictimBleed.State.Bleeds);
        Assert.Single(rig.Thinks.Bookings());
    }

    [Fact]
    public void The_invulnerable_take_the_miss_instead_of_the_wound()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.Conditions.Add(ProjectileRig.Victim, TFCond.Invulnerable);

        cleaver.OnTouch(ProjectileRig.Victim);

        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Empty(rig.VictimBleed.State.Bleeds);
    }

    [Fact]
    public void The_world_thunks_once_and_the_body_stays_seeing()
    {
        // Explode (tf_weapon_jar.cpp:1083-1096): the miss's sound, once, and
        // the two-second lie with no NODRAW -- the one linger whose model the
        // player still sees.
        var rig = new Rig();
        var cleaver = rig.Arrived(Now);

        cleaver.OnTouch(EntityHandle.None);

        Assert.Equal(TFProjectileDefs.CleaverImpactWorldSound,
                     rig.Base.Engine.Last("Sound.EmitSoundAtPosition")!.Value.ArgAs<string>(0));
        var shown = Assert.Single(rig.Base.Engine.AllOf("EntityRender.SetVisible"));
        Assert.True(shown.ArgAs<bool>(1));
        Assert.Equal(TFProjectileDefs.CleaverLingerSeconds, Assert.Single(rig.Thinks.Bookings()).Time, 3);
    }

    [Fact]
    public void The_skybox_takes_the_cleaver_whole()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.Base.Engine.Returns("Entities.GetVelocity", new Vector(1200f, 0f, 0f));
        rig.Base.Engine.Returns("WorldQuery.TraceLineSurface",
                                new SurfaceTraceResult(true, 0.5f, new Vector(0f, 0f, 320f),
                                                       new Vector(0f, 0f, 1f), EntityHandle.None, 0,
                                                       0x0004 /* SURF_SKY */));

        cleaver.OnTouch(ProjectileRig.Victim);

        // Spent, and silent: the sky thunks nothing.
        Assert.Equal(1, rig.Base.Engine.CountOf("Entities.Remove"));
        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Sound.EmitSoundAtPosition");
        Assert.Empty(rig.Giver.Advanced);
    }

    // ---- the flight: stock's cleaver is a physics body (lane r2s4, 2026-11-24)

    /// <summary>The cleaver registers as a physics prop, as stock's <c>CTFProjectile_Cleaver</c> is a <c>CTFProjectile_Jar</c>.</summary>
    [Fact]
    public void The_cleaver_is_a_physics_body()
    {
        var kind = (ScriptedEntityAttribute)Assert.Single(typeof(TFCleaver).GetCustomAttributes(typeof(ScriptedEntityAttribute), false));
        Assert.Equal(ScriptedEntityKind.Prop, kind.Kind);
    }

    /// <summary>It is born facing the eye (<c>CreateJarProjectile( src, EyeAngles() )</c>, tf_weapon_jar.cpp:195).</summary>
    [Fact]
    public void The_cleaver_is_born_facing_the_eye()
    {
        var rig = new Rig();
        rig.Base.Engine.Returns("Entities.GetEyeAngles", new Vector(-5f, 90f, 0f));
        rig.Thrown();
        Vector born = rig.Base.Engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1);
        Assert.Equal((-5f, 90f), (born.X, born.Y));
    }

    /// <summary>
    /// It is spun as stock's: <c>AngularImpulse(0, 500, 0)</c> twice about its own left (<c>GrenadeSpin</c>); thrown along
    /// +y its left is the world's -x. Was 500 once in the pitch slot of a world-axis spin.
    /// </summary>
    [Fact]
    public void The_cleaver_is_spun_twice_about_its_own_left()
    {
        var rig = new Rig();
        rig.Base.Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 90f, 0f));
        rig.Thrown();
        Vector spin = rig.Base.Engine.Last("Entities.SetAngularVelocity")!.Value.ArgAs<Vector>(1);
        Assert.Equal(-2f * TFProjectileDefs.CleaverSpinPitch, spin.X, 0.5f);
        Assert.Equal(0f, spin.Y, 0.5f);
        Assert.Equal(0f, spin.Z, 0.5f);
    }

    /// <summary>The solved impact on the world is the miss with the body showing (the jar's <c>VPhysicsCollision</c>, :592-617).</summary>
    [Fact]
    public void A_world_collision_is_the_showing_miss()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        var ev = new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.PostCollision,
            Flags = PhysicsEvent.FlagCollision,
            Entity0 = cleaver.Handle.Value,
            Entity1 = PhysicsEvent.NoEntity,
            Static1 = 1,
        };

        cleaver.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(TFProjectileDefs.CleaverImpactWorldSound,
                     rig.Base.Engine.Last("Sound.EmitSoundAtPosition")!.Value.ArgAs<string>(0));
        Assert.True(rig.Base.Engine.Last("EntityRender.SetVisible")!.Value.ArgAs<bool>(1));
        Assert.Single(rig.Thinks.Bookings());
    }

    /// <summary>A dynamic prop's solved impact stops it as the world's does (:607-614).</summary>
    [Fact]
    public void A_dynamic_prop_collision_is_the_miss()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.Base.Engine.Returns("Entities.GetClassName", "prop_dynamic");
        var ev = new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.PostCollision,
            Flags = PhysicsEvent.FlagCollision,
            Entity0 = cleaver.Handle.Value,
            Entity1 = 0x0002_0000u | 7,
        };

        cleaver.OnVPhysicsCollision(in ev, 0);

        Assert.Single(rig.Thinks.Bookings());
    }

    // ---- the review's holes (r2s4-2)

    private static PhysicsEvent Collision(TFCleaver cleaver, int mySide, uint other, int otherStatic) => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Flags = PhysicsEvent.FlagCollision,
        Entity0 = mySide == 0 ? cleaver.Handle.Value : other,
        Entity1 = mySide == 0 ? other : cleaver.Handle.Value,
        Static0 = mySide == 0 ? 0 : otherStatic,
        Static1 = mySide == 0 ? otherStatic : 0,
    };

    /// <summary>The thrower's own touch never stops the cleaver (PipebombTouch :507).</summary>
    [Fact]
    public void The_throwers_own_touch_never_stops_the_cleaver()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.Base.Engine.Returns("Entities.IsPlayer", true); // the thrower is a player like any other
        cleaver.OnTouch(ProjectileRig.Player);
        Assert.DoesNotContain(rig.Thinks.Bookings(), b => MathF.Abs(b.Time - TFProjectileDefs.CleaverLingerSeconds) < 0.01f); // only the fuse's look, no linger
    }

    /// <summary>The cleaver on the event's side 1 reads side 0 for the world.</summary>
    [Fact]
    public void A_world_collision_with_the_cleaver_on_side_one_is_the_miss()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        PhysicsEvent ev = Collision(cleaver, mySide: 1, PhysicsEvent.NoEntity, otherStatic: 1);
        cleaver.OnVPhysicsCollision(in ev, 1);
        Assert.Single(rig.Thinks.Bookings());
    }

    /// <summary>A side that names no entity and is not static is no one's: no miss.</summary>
    [Fact]
    public void A_sideless_moving_body_does_not_stop_the_cleaver()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        PhysicsEvent ev = Collision(cleaver, mySide: 0, PhysicsEvent.NoEntity, otherStatic: 0);
        cleaver.OnVPhysicsCollision(in ev, 0);
        Assert.DoesNotContain(rig.Thinks.Bookings(), b => MathF.Abs(b.Time - TFProjectileDefs.CleaverLingerSeconds) < 0.01f); // only the fuse's look, no linger
    }

    /// <summary>A physics prop bounces it: not the miss (the jar's collision arm stops it on the world and dynamic props only).</summary>
    [Fact]
    public void A_physics_prop_does_not_stop_the_cleaver()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.Base.Engine.Returns("Entities.GetClassName", "prop_physics");
        PhysicsEvent ev = Collision(cleaver, mySide: 0, 0x0002_0000u | 7, otherStatic: 0);
        cleaver.OnVPhysicsCollision(in ev, 0);
        Assert.DoesNotContain(rig.Thinks.Bookings(), b => MathF.Abs(b.Time - TFProjectileDefs.CleaverLingerSeconds) < 0.01f); // only the fuse's look, no linger
    }

    /// <summary>A cleaver that has struck is spent: an enemy touching it after the world's impact is not wounded.</summary>
    [Fact]
    public void A_spent_cleaver_wounds_nobody()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        PhysicsEvent ev = Collision(cleaver, mySide: 0, PhysicsEvent.NoEntity, otherStatic: 1);
        cleaver.OnVPhysicsCollision(in ev, 0);
        cleaver.OnTouch(ProjectileRig.Victim);
        Assert.Empty(rig.VictimBleed.State.Bleeds);
        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    // ---- stock's fuse and touched latch (review r2s4-2 item 3)

    /// <summary>Past the 2 s fuse the cleaver's <c>Detonate</c> is <c>Explode</c>'s miss: the thunk and the two-second lie.</summary>
    [Fact]
    public void Past_the_fuse_the_cleaver_takes_its_miss()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + TFProjectileDefs.PipeFuse + 0.05f);
        cleaver.OnThink();
        Assert.Equal(TFProjectileDefs.CleaverImpactWorldSound,
                     rig.Base.Engine.Last("Sound.EmitSoundAtPosition")!.Value.ArgAs<string>(0));
        Assert.Contains(rig.Thinks.Bookings(), b => MathF.Abs(b.Time - TFProjectileDefs.CleaverLingerSeconds) < 0.01f);
    }

    /// <summary>Before the fuse the look books the next one 0.2 s on and takes no miss.</summary>
    [Fact]
    public void Before_the_fuse_the_cleavers_look_waits()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        cleaver.OnThink();
        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Sound.EmitSoundAtPosition");
        Assert.Equal(TFProjectileDefs.FuseCheckSeconds, Assert.Single(rig.Thinks.Bookings()).Time, 3);
    }

    /// <summary>After a solved impact on any body (here a physics prop) an enemy's touch no longer wounds (the touched latch, :555-556).</summary>
    [Fact]
    public void After_a_bounce_the_cleaver_wounds_no_touch()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.Base.Engine.Returns("Entities.GetClassName", "prop_physics");
        PhysicsEvent ev = Collision(cleaver, mySide: 0, 0x0002_0000u | 7, otherStatic: 0);
        cleaver.OnVPhysicsCollision(in ev, 0);
        cleaver.OnTouch(ProjectileRig.Victim);
        Assert.Empty(rig.VictimBleed.State.Bleeds);
    }

    // ---- the enemy's contact (lane r2s4)

    /// <summary>The cleaver's ray one tick ahead (the grenade base's VPhysicsUpdate, :774) touches an enemy player first: OnHit wounds him.</summary>
    [Fact]
    public void The_cleavers_ray_touches_an_enemy_player_and_wounds_him()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.Base.Engine.Returns("Frame.GetFrameTime", 1f / 66f);
        rig.Base.Engine.Returns("Entities.GetVelocity", new Vector(1000f, 0f, 0f));
        rig.Base.Engine.Returns("Entities.GetClassName", "player");
        rig.Base.Engine.Returns("Entities.GetAngularVelocity", default(Vector));
        rig.Base.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, new Vector(10f, 0f, 0f), new Vector(-1f, 0f, 0f), ProjectileRig.Victim));

        cleaver.OnPhysicsUpdate();

        Assert.Single(rig.VictimBleed.State.Bleeds);
    }

    /// <summary>A player's physics collision is no impact (stock's grenades never collide with players): no latch, so his touch still wounds him.</summary>
    [Fact]
    public void A_players_collision_leaves_the_cleaver_for_his_touch()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        PhysicsEvent ev = Collision(cleaver, mySide: 0, ProjectileRig.Victim.Value, otherStatic: 0);

        cleaver.OnVPhysicsCollision(in ev, 0);
        cleaver.OnTouch(ProjectileRig.Victim);

        Assert.Single(rig.VictimBleed.State.Bleeds);
    }

    private sealed class DamageableBody : ScriptedEntity
    {
        public override bool TakesDamage => true;
    }

    /// <summary>
    /// An enemy building it can damage is the cleaver's miss: the pipebomb base's arm (:877-881) detonates it, and the
    /// cleaver's Detonate is its Explode, the miss with no player hit (tf_weapon_jar.cpp:1083-1111).
    /// </summary>
    [Fact]
    public void An_enemy_building_is_the_cleavers_miss()
    {
        var (rig, cleaver, building) = CleaverAgainst(TFTeam.Red);
        PhysicsEvent ev = Collision(cleaver, mySide: 0, building.Handle.Value, otherStatic: 0);

        cleaver.OnVPhysicsCollision(in ev, 0);

        Assert.Equal(TFProjectileDefs.CleaverLingerSeconds, Assert.Single(rig.Thinks.Bookings()).Time, 3);
        Assert.Contains(rig.Base.Engine.Calls, c => c.Member == "Sound.EmitSoundAtPosition");
    }

    /// <summary>A building of the thrower's team does not stop the cleaver.</summary>
    [Fact]
    public void A_friendly_building_does_not_stop_the_cleaver()
    {
        var (rig, cleaver, building) = CleaverAgainst(TFTeam.Blue);
        PhysicsEvent ev = Collision(cleaver, mySide: 0, building.Handle.Value, otherStatic: 0);

        cleaver.OnVPhysicsCollision(in ev, 0);

        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Sound.EmitSoundAtPosition");
    }

    /// <summary>A Prop-kind prop_physics in the directory takes damage and has no team: it does not stop the cleaver.</summary>
    [Fact]
    public void A_prop_physics_that_takes_damage_does_not_stop_the_cleaver()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        GrenadeContacts.ServerTeams(rig.Base.Engine, (ProjectileRig.Player, TFTeam.Blue), (ProjectileRig.Victim, TFTeam.Red));
        var prop = new EntityHandle(0x0002_0000u | 7);
        rig.Base.Directory.Holds(prop, new DamageableBody());
        rig.Base.Engine.Returns("Entities.GetClassName", "prop_physics");
        PhysicsEvent ev = Collision(cleaver, mySide: 0, prop.Value, otherStatic: 0);

        cleaver.OnVPhysicsCollision(in ev, 0);

        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Sound.EmitSoundAtPosition");
    }

    /// <summary>A Blue thrower's cleaver against a building of <paramref name="team"/>, the server answering teams.</summary>
    private static (Rig Rig, TFCleaver Cleaver, TestBuilding Building) CleaverAgainst(TFTeam team)
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        GrenadeContacts.ServerTeams(rig.Base.Engine, (ProjectileRig.Player, TFTeam.Blue), (ProjectileRig.Victim, TFTeam.Red));
        TestBuilding building = GrenadeContacts.PlacedBuilding(team);
        rig.Base.Directory.Holds(building.Handle, building);
        rig.Base.Engine.Returns("Entities.GetClassName", "obj_sentrygun");
        return (rig, cleaver, building);
    }

    // ---- the cleaver's ray bounce (lane r2s4, review r2s4-3 item 5)

    /// <summary>A cleaver whose ray strikes the victim one tick ahead, the victim on <paramref name="victimTeam"/>.</summary>
    private static (Rig Rig, TFCleaver Cleaver) CleaverRaying(int victimTeam, float landedAt)
    {
        var rig = new Rig { VictimTeam = victimTeam };
        var cleaver = rig.Arrived(landedAt);
        rig.Base.Engine.Returns("Frame.GetFrameTime", 1f / 66f);
        rig.Base.Engine.Returns("Entities.GetVelocity", new Vector(1000f, 0f, 0f));
        rig.Base.Engine.Returns("Entities.GetAngularVelocity", new Vector(40f, 0f, 0f));
        rig.Base.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, new Vector(10f, 0f, 0f), new Vector(-1f, 0f, 0f), ProjectileRig.Victim));
        return (rig, cleaver);
    }

    private static Vector LastVelocity(Rig rig) => EngineCallFacts.ArgAs<Vector>(rig.Base.Engine.Last("Entities.SetVelocity"), 1);

    /// <summary>The ray's enemy is wounded and the cleaver bounced off him halved, as stock's bounce after the touch (:774-789).</summary>
    [Fact]
    public void The_cleavers_ray_bounces_it_off_the_enemy_it_wounds_halved()
    {
        var (rig, cleaver) = CleaverRaying(victimTeam: 2, Now + 1f);
        cleaver.OnPhysicsUpdate();
        Assert.Single(rig.VictimBleed.State.Bleeds);
        Assert.Equal(new Vector(-225f, 0f, 0f), LastVelocity(rig));
    }

    /// <summary>A teammate past the 0.25 s pass bounces the cleaver at the elasticity alone, unwounded.</summary>
    [Fact]
    public void The_cleavers_ray_bounces_it_off_a_late_teammate()
    {
        var (rig, cleaver) = CleaverRaying(victimTeam: 3, Now + 1f);
        cleaver.OnPhysicsUpdate();
        Assert.Empty(rig.VictimBleed.State.Bleeds);
        Assert.Equal(new Vector(-450f, 0f, 0f), LastVelocity(rig));
    }

    /// <summary>A teammate inside the pass: no wound and no bounce.</summary>
    [Fact]
    public void A_teammate_inside_the_pass_does_not_bounce_the_cleaver()
    {
        var (rig, cleaver) = CleaverRaying(victimTeam: 3, Now + 0.1f);
        int spun = rig.Base.Engine.CountOf("Entities.SetAngularVelocity"); // the throw's own
        cleaver.OnPhysicsUpdate();
        Assert.Empty(rig.VictimBleed.State.Bleeds);
        Assert.Equal(spun, rig.Base.Engine.CountOf("Entities.SetAngularVelocity"));
    }
}
