using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>Bonus points the stat arm was asked to pay, with the player.</summary>
internal sealed class FakeStats : ITFPlayerStats
{
    public readonly List<(EntityHandle Player, TFStat Stat, int Amount)> Paid = new();

    public void Increment(EntityHandle player, TFStat stat, int amount = 1) => Paid.Add((player, stat, amount));
    public int Round(int playerIndex, TFStat stat) => 0;
    public int Accumulated(int playerIndex, TFStat stat) => 0;
    public int RoundScore(int playerIndex) => 0;
    public int Score(int playerIndex) => 0;
    public void ResetRound() { }
    public void ResetPlayer(EntityHandle player) { }
}

/// <summary>
/// The minigun arm's stand: the projectile rig's engine, one enemy rocket
/// (<see cref="ProjectileRig.Player"/>'s opposite team) on the sweep's aim
/// line, and the answers a fact names over it.
/// </summary>
internal sealed class MinigunRig
{
    public readonly ProjectileRig Base = new();
    public readonly FakeConditions Conditions = new();
    public readonly FakeStats Stats = new();
    public readonly Dictionary<EntityHandle, Vector> Origins = new();
    public readonly TFRocket Rocket;

    /// <summary>The ring's standing victim: an enemy player by the gun.</summary>
    public static readonly EntityHandle Victim = new(0x0001_0000u | 7);

    public TFMinigunEffects Effects { get; }

    public MinigunRig()
    {
        var engine = Base.Engine;

        // Where each entity stands; the gun is the rig eye (0, 0, 64) facing +X.
        engine.ReturnsEachCall("Entities.GetOrigin",
            () => engine.Calls[^1].Args[0] is EntityHandle h && Origins.TryGetValue(h, out var o) ? o : default(Vector));
        engine.ReturnsEachCall("Entities.GetWorldSpaceCenter",
            () => engine.Calls[^1].Args[0] is EntityHandle h && Origins.TryGetValue(h, out var o) ? o : default(Vector));
        // Airborne unless a fact grounds the rocket.
        engine.Returns("Networking.EntityGetState", default(EntityState));
        engine.Returns("Networking.EntityGetState.moveType", default(MoveType));

        // The ring's answers: only the victim is a player, and it stands on
        // the other team under a 32 x 72 hull. The gun's own origin is the
        // stand's zero; the particle index a constant.
        engine.ReturnsEachCall("Entities.IsPlayer",
            () => engine.Calls[^1].Args[0] is EntityHandle h && h == Victim);
        engine.ReturnsEachCall("Players.GetTeam",
            () => engine.Calls[^1].Args[0] is EntityHandle h && h == Victim ? 3 : 2);
        engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        engine.Returns("Precache.ParticleSystemIndex", 17);
        Origins[Victim] = new Vector(40f, 0f, 0f);

        Rocket = Base.Rocket();
        Rocket.Handle = ProjectileRig.ProjectileHandle;
        Rocket.State.TeamNumber = 3;
        Base.Directory.Instance = Rocket;
        Origins[Rocket.Handle] = new Vector(100f, 0f, 64f);
        engine.FillsSphere(Rocket.Handle, Victim);

        Effects = new TFMinigunEffects(engine, engine, engine, engine, engine, engine, engine,
                                       Base.Temps, Conditions, Base.Directory, Stats, Base.Log);
    }

    public void Sweep(float now, int projectiles = 2, bool critical = false) =>
        Effects.AttackEnemyProjectiles(Attack(now), projectiles, critical);

    /// <summary>Rings the gun at <paramref name="now"/> for a reared clock, after <see cref="Rearmed"/>.</summary>
    public void Rearmed(float now) => Effects.Rearmed(Attack(now));

    public void Ring(float now, int damage = 10) => Effects.RingOfFire(Attack(now), damage);

    /// <summary>Every plasma hit, as the engine saw it.</summary>
    public IReadOnlyList<EngineCall> Hits => Base.Engine.AllOf("Damage.TakeDamage").ToList();

    public int Particles => Base.Temps.Effects.Count(e => e.Name == "ParticleEffect");

    /// <summary>The arm's command: the rig player's, live, at <paramref name="now"/>.</summary>
    public static WeaponAttackContext Attack(float now) => new(
        default, default!, default!, now, EffectsLive: true, new RecordingArms(), new WeaponCommand(1, 5, 3));

    public IReadOnlyList<EngineCall> Sounds(string name) =>
        Base.Engine.Calls.Where(c => c.Member == "Sound.EntityEmitSound"
                                     && c.Args[1] is string s && s == name).ToList();

    public int Removes => Base.Engine.CountOf("Entities.Remove");
}

/// <summary>
/// The sweep (<c>CTFMinigun::AttackEnemyProjectiles</c>,
/// <c>shared/tf/tf_weapon_minigun.cpp:564-654</c>).
/// </summary>
public sealed class TFMinigunSweepFacts
{
    [Fact]
    public void A_rocket_on_the_aim_line_is_destroyed_by_the_strong_count()
    {
        var rig = new MinigunRig();

        rig.Sweep(10f);

        Assert.Equal(1, rig.Removes);
        EngineCall axe = Assert.Single(rig.Sounds(TFMinigunEffects.AxeHitSound));
        Assert.Equal(ProjectileRig.Player, axe.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void A_kill_pays_two_bonus_points_to_the_shooter()
    {
        var rig = new MinigunRig();

        rig.Sweep(10f);

        (EntityHandle Player, TFStat Stat, int Amount) paid = Assert.Single(rig.Stats.Paid);
        Assert.Equal(ProjectileRig.Player, paid.Player);
        Assert.Equal(TFStat.BonusPoints, paid.Stat);
        Assert.Equal(2, paid.Amount);
    }

    [Fact]
    public void The_weak_count_nicks_first_and_kills_on_the_second_sweep()
    {
        var rig = new MinigunRig();

        rig.Sweep(10f, projectiles: 1);
        Assert.Equal(0, rig.Removes);
        Assert.Single(rig.Sounds(TFMinigunEffects.RicochetSound));

        // The recharge spent, the second beat kills.
        rig.Sweep(10.1f, projectiles: 1);
        Assert.Equal(1, rig.Removes);
    }

    [Fact]
    public void A_crit_need_not_nick_first()
    {
        var rig = new MinigunRig();

        rig.Sweep(10f, projectiles: 1, critical: true);

        Assert.Equal(1, rig.Removes);
        Assert.Empty(rig.Sounds(TFMinigunEffects.RicochetSound));
    }

    [Fact]
    public void The_sweep_recharges_a_tenth_after_the_last()
    {
        var rig = new MinigunRig();

        rig.Sweep(10f, projectiles: 1);
        rig.Sweep(10.05f, projectiles: 1);

        // The nicked rocket's second beat waits for the recharge.
        Assert.Single(rig.Sounds(TFMinigunEffects.RicochetSound));
    }

    [Fact]
    public void An_allys_rocket_is_left_alone()
    {
        var rig = new MinigunRig();
        rig.Rocket.State.TeamNumber = 2;

        rig.Sweep(10f);

        Assert.Equal(0, rig.Removes);
        Assert.Empty(rig.Sounds(TFMinigunEffects.RicochetSound));
    }

    [Fact]
    public void A_rocket_off_the_aim_line_is_left_alone()
    {
        var rig = new MinigunRig();
        rig.Origins[rig.Rocket.Handle] = new Vector(100f, 100f, 64f);

        rig.Sweep(10f);

        Assert.Equal(0, rig.Removes);
    }

    [Fact]
    public void A_grounded_rocket_is_left_alone()
    {
        var rig = new MinigunRig();
        rig.Base.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);

        rig.Sweep(10f);

        Assert.Equal(0, rig.Removes);
    }

    [Fact]
    public void A_hidden_rocket_is_left_alone()
    {
        var rig = new MinigunRig();
        rig.Base.Engine.Returns("WorldQuery.TraceLine",
                                new TraceResult(true, 0.5f, default, default, new EntityHandle(0x0002_0009u)));

        rig.Sweep(10f);

        Assert.Equal(0, rig.Removes);
    }
}

/// <summary>
/// The ring of fire (<c>CTFMinigun::RingOfFireAttack</c>,
/// <c>shared/tf/tf_weapon_minigun.cpp:498-558</c>).
/// </summary>
public sealed class TFMinigunRingFacts
{
    [Fact]
    public void An_enemy_under_the_gun_takes_the_plasma()
    {
        var rig = new MinigunRig();
        rig.Rearmed(10f);

        rig.Ring(10.5f, 12);

        EngineCall hit = Assert.Single(rig.Hits);
        Assert.Equal(MinigunRig.Victim, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(ProjectileRig.Player, hit.ArgAs<EntityHandle>(1));
        Assert.Equal(12f, hit.ArgAs<float>(2));
        Assert.Equal(DamageType.Plasma, hit.ArgAs<DamageType>(3));
        // The force origin is the shooter's ground zero, not the victim's.
        Assert.Equal(default(Vector), hit.ArgAs<Vector>(5));
    }

    [Fact]
    public void The_ring_waits_for_the_rearmed_half_and_rings_on_half_a_later()
    {
        var rig = new MinigunRig();
        rig.Rearmed(10f);

        rig.Ring(10.4f);
        Assert.Empty(rig.Hits);
        Assert.Equal(0, rig.Particles);

        rig.Ring(10.5f);
        Assert.Single(rig.Hits);

        rig.Ring(10.6f);
        Assert.Single(rig.Hits);
    }

    [Fact]
    public void The_rings_particle_plays_at_the_shooter_when_the_clock_passes()
    {
        var rig = new MinigunRig();
        rig.Rearmed(10f);

        rig.Ring(10.5f);

        SourceSharp.Tests.DispatchedEffect ring = Assert.Single(rig.Base.Temps.Effects);
        Assert.Equal("ParticleEffect", ring.Name);
        Assert.Equal(17, ring.Data.HitBox);
        Assert.Equal(ring.Data.Origin, ring.Data.Start);
        Assert.Equal(1f, ring.Data.Scale);
    }

    [Fact]
    public void A_teammate_is_left_untouched()
    {
        var rig = new MinigunRig();
        rig.Base.Engine.Returns("Players.GetTeam", 2);
        rig.Rearmed(10f);

        rig.Ring(10.5f);

        Assert.Empty(rig.Hits);
    }

    [Fact]
    public void An_invulnerable_player_is_left_untouched()
    {
        var rig = new MinigunRig();
        rig.Conditions.Grant(MinigunRig.Victim, TFCond.Invulnerable);
        rig.Rearmed(10f);

        rig.Ring(10.5f);

        Assert.Empty(rig.Hits);
    }

    [Fact]
    public void A_player_above_the_ground_plane_is_left_out()
    {
        var rig = new MinigunRig();
        rig.Origins[MinigunRig.Victim] = new Vector(40f, 0f, 200f);
        rig.Rearmed(10f);

        rig.Ring(10.5f);

        Assert.Empty(rig.Hits);
    }

    [Fact]
    public void A_player_past_the_radius_is_left_out()
    {
        var rig = new MinigunRig();
        rig.Origins[MinigunRig.Victim] = new Vector(400f, 0f, 0f);
        rig.Rearmed(10f);

        rig.Ring(10.5f);

        Assert.Empty(rig.Hits);
    }

    [Fact]
    public void A_player_behind_a_wall_is_left_out()
    {
        var rig = new MinigunRig();
        rig.Base.Engine.Returns("WorldQuery.TraceLine",
                                new TraceResult(true, 0.5f, default, default, new EntityHandle(0x0002_0009u)));
        rig.Rearmed(10f);

        rig.Ring(10.5f);

        Assert.Empty(rig.Hits);
    }

    [Fact]
    public void The_rings_particle_plays_even_when_nobody_is_near()
    {
        var rig = new MinigunRig();
        rig.Base.Engine.FillsSphere();
        rig.Rearmed(10f);

        rig.Ring(10.5f);

        Assert.Empty(rig.Hits);
        Assert.Equal(1, rig.Particles);
    }
}
