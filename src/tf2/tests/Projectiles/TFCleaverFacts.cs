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

            Cleaver = new TFCleaver(Base.Engine, Base.Motion, Base.Engine, Base.Engine, Base.HitGroups,
                                    Base.Scopes, Bleeds, Base.Engine, Conditions, Base.Engine, Base.Engine,
                                    Base.Engine, Giver);
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

    [Fact]
    public void The_cleaver_flies_through_what_is_neither_player_nor_world()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.Base.Engine.Returns("Entities.GetVelocity", new Vector(1200f, 0f, 0f));

        cleaver.OnTouch(new EntityHandle(0x0002_0000u | 7));

        // Flown on at the flight it had: no wound, no pay, no thunk.
        Assert.Equal(new Vector(1200f, 0f, 0f),
                     rig.Base.Engine.Last("Entities.SetVelocity")!.Value.ArgAs<Vector>(1));
        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Empty(rig.Giver.Advanced);
    }

    [Fact]
    public void A_dry_teammates_flesh_flies_the_cleaver_through()
    {
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.VictimTeam = 3; // the same side, thrower and touched
        rig.Base.Engine.Returns("Entities.GetVelocity", new Vector(900f, 0f, 0f));

        cleaver.OnTouch(ProjectileRig.Victim);

        Assert.Equal(new Vector(900f, 0f, 0f),
                     rig.Base.Engine.Last("Entities.SetVelocity")!.Value.ArgAs<Vector>(1));
        Assert.Empty(rig.VictimBleed.State.Bleeds);
    }

    [Fact]
    public void A_burning_teammate_takes_the_hit_the_grace_spares_the_dry()
    {
        // PipebombTouch's burning exception, the cleaver wearing the jar's
        // projectile type: the aflame ally is hit like any enemy.
        var rig = new Rig();
        var cleaver = rig.Arrived(Now + 1f);
        rig.VictimTeam = 3; // the same side the grace would spare
        rig.Conditions.Add(ProjectileRig.Victim, TFCond.Burning);

        cleaver.OnTouch(ProjectileRig.Victim);

        Assert.Single(rig.VictimBleed.State.Bleeds);
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
}
