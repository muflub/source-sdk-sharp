using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The AR2's ball, as stock's: only its clock or the sky ends it, and neither end hurts anybody.</summary>
public class EnergyBallTests
{
    private const int SurfSky = 0x0004;

    private sealed class Effects : TempEntsDouble
    {
        public readonly List<string> Dispatched = [];
        public int Rings;

        public override void DispatchEffect(TeFilter filter, float delay, string effectName, in TeEffectData data)
            => Dispatched.Add(effectName);

        public override void BeamRingPoint(TeFilter filter, float delay, Vector center, float startRadius, float endRadius,
                                           in TeBeamLook look, int flags = 0)
            => Rings++;
    }

    private sealed class Roster(params EntityHandle[] players) : IPlayerDirectory
    {
        public PlayerRecord this[EntityHandle player] => new();
        public bool Knows(EntityHandle player) => false;
        public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All => players.Select(p => (p, new PlayerRecord()));
        public float WaitRemaining(EntityHandle player, float now) => 0f;
        public float RespawnDelay { get; set; }
        public ObserverMode DeathView { get; set; }
        public bool DropWeaponOnDeath { get; set; }
        public float DropSpeed { get; set; }
        public void OnSpawned(EntityHandle player, float now) { }
        public void OnKilled(EntityHandle player, EntityHandle attacker, float now) { }
        public void Forget(EntityHandle player) { }
    }

    private static readonly EntityHandle Other = new(0x0003_0021u);

    private static (ScriptedEntity ball, RecordingEngine engine, ServiceContainer services, ScriptedEntityRegistry registry) Spawned(
        int surfaceFlags = 0, Effects? effects = null, params EntityHandle[] players)
    {
        var services = Services(out var engine);
        services.AddInstance<ITempEnts>(effects ?? new Effects());
        services.AddInstance<IPlayerDirectory>(new Roster(players));
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f))
              .Returns("WorldQuery.TraceLineSurface",
                       new SurfaceTraceResult(true, 0.1f, Vector.Zero, Vector.Zero, EntityHandle.None, 0, surfaceFlags));
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnergyBall).Assembly);
        registry.Services = services;
        var handle = new EntityHandle(11);
        registry.Spawn(handle, EntityNames.PropCombineBall);
        return (registry.Get(handle)!, engine, services, registry);
    }

    private static bool Ended(RecordingEngine engine)
        => engine.Calls.Any(c => c.Member == "Sound.EntityEmitSound" && c.Args.Contains("NPC_CombineBall.Explosion"));

    private static bool Removed(ScriptedEntityRegistry registry) => registry.Removal.FreeNow() > 0;

    [Fact]
    public void A_ball_has_no_count_of_bounces()
    {
        var (_, engine, _, _) = Spawned();
        Assert.Equal(int.MaxValue, engine.Calls.Single(c => c.Member == "EntityPhysics.SetBounce").ArgAs<int>(1));
    }

    private static readonly Vector Wall = new(0f, 0f, 1f);

    /// <summary>DoImpactEffect, each time; OnHitEntity returned before it for what is hittable.</summary>
    [Fact]
    public void A_wall_shows_the_bounce_each_time_and_a_player_does_not()
    {
        var effects = new Effects();
        var (ball, engine, _, _) = Spawned(effects: effects);
        engine.Returns("Entities.GetIndex", 0);
        var energy = (EnergyBall)ball;

        energy.OnStruckSurface(Wall, EntityHandle.None);
        energy.OnStruckSurface(Wall, EntityHandle.None);
        Assert.Equal([EnergyBall.BounceEffect, EnergyBall.BounceEffect], effects.Dispatched);

        effects.Dispatched.Clear();
        engine.Returns("Entities.GetIndex", 7).Returns("Networking.EntityGetState", EntityState.Player);
        energy.OnStruckSurface(Wall, new EntityHandle(7));
        Assert.Empty(effects.Dispatched);
    }

    /// <summary>What a death names as its killer's weapon: the ball, not the gun in hand.</summary>
    [Fact]
    public void The_ball_is_the_inflictor_of_its_hit()
    {
        var (ball, engine, services, _) = Spawned();
        engine.Returns("Entities.GetIndex", 7);
        var inflictors = (DamageInflictors)services.Resolve(typeof(DamageInflictors))!;
        EntityHandle open = EntityHandle.None;
        engine.WhenCalled("Damage.Apply", _ => inflictors.TryCurrent(out open, out Vector _));

        ball.OnTouch(new EntityHandle(7));

        Assert.Equal(ball.Handle, open);
    }

    /// <summary>m_bEmit: drawn until its end, then kept half a second only to be heard.</summary>
    [Fact]
    public void Its_client_half_draws_it_until_its_end()
    {
        var (ball, _, _, _) = Spawned();
        var energy = (EnergyBall)ball;
        Assert.True(energy.State.Emit);
        Assert.Equal(EnergyBall.BallRadius, energy.State.Radius);

        ball.OnThink();
        Assert.False(energy.State.Emit);
    }

    [Fact]
    public void Its_end_is_the_sparks_and_both_rings()
    {
        var effects = new Effects();
        var (ball, _, _, _) = Spawned(effects: effects);
        ball.OnThink();
        Assert.Equal([EnergyBall.ExplodeEffect], effects.Dispatched);
        Assert.Equal(2, effects.Rings);
    }

    private static readonly Vector AlongX = new(1f, 0f, 0f);

    /// <summary>DeflectTowardEnemy's flMaxDot, for a ball that has hit nobody: about fifteen degrees.</summary>
    [Theory]
    [InlineData(400f, 50f, true)]     // 7 degrees off
    [InlineData(400f, 150f, false)]   // 20 degrees off
    [InlineData(-400f, 0f, false)]    // behind
    [InlineData(3000f, 100f, false)]  // past the ray's 2048 and its 256 either side
    public void Before_a_hit_it_only_helps_a_little(float ahead, float aside, bool wanted)
        => Assert.Equal(wanted, EnergyBall.Wanted(Vector.Zero, AlongX, new Vector(ahead, aside, 0f), struckPlayer: false));

    /// <summary>bSeekKill: having hit someone, anyone ahead inside the search radius.</summary>
    [Theory]
    [InlineData(100f, 300f, true)]    // 70 degrees off, near
    [InlineData(-100f, 300f, false)]  // behind
    [InlineData(100f, 600f, false)]   // past 512
    public void After_a_hit_it_seeks_the_next(float ahead, float aside, bool wanted)
        => Assert.Equal(wanted, EnergyBall.Wanted(Vector.Zero, AlongX, new Vector(ahead, aside, 0f), struckPlayer: true));

    private static (ScriptedEntity ball, RecordingEngine engine) InFlightTowards(Vector target, bool alive = true, int team = 0, float sight = 1f)
    {
        var (ball, engine, _, _) = Spawned(0, null, Other);
        engine.Returns("Entities.GetIndex", 0)
              .Returns("Players.IsAlive", alive)
              .Returns("Players.GetTeam", team)
              .Returns("Players.GetShootOrigin", target)
              .Returns("Entities.GetWorldSpaceCenter", target)
              .Returns("WorldQuery.TraceLine", new TraceResult(sight < 1f, sight, target, Vector.Zero, EntityHandle.None));
        ((EnergyBall)ball).FiredBy(new EntityHandle(0x0003_0022u), new Vector(1000f, 0f, 0f));
        engine.Clear();
        return (ball, engine);
    }

    private static Vector? Steered(ScriptedEntity ball, RecordingEngine engine)
    {
        ((EnergyBall)ball).OnStruckSurface(Wall, EntityHandle.None);
        var set = engine.Calls.Where(c => c.Member == "Entities.SetVelocity").ToArray();
        return set.Length == 0 ? null : set[^1].ArgAs<Vector>(1);
    }

    [Fact]
    public void A_bounce_steers_at_a_player_inside_the_cone_keeping_its_speed()
    {
        var (ball, engine) = InFlightTowards(new Vector(401f, 2f, 53f));
        Vector? v = Steered(ball, engine);
        Assert.NotNull(v);
        Assert.Equal(1000f, v!.Value.Length, 1);
        Assert.True(v.Value.Y > 0f || v.Value.Z > 0f, $"{v}");
    }

    [Theory]
    [InlineData(false, 0, 1f)]   // a dead player
    [InlineData(true, 0, 0.5f)]  // behind a wall
    public void A_bounce_leaves_alone_who_is_not_attractive(bool alive, int team, float sight)
    {
        var (ball, engine) = InFlightTowards(new Vector(401f, 2f, 53f), alive, team, sight);
        Assert.Null(Steered(ball, engine));
    }

    [Fact]
    public void A_wall_does_not_end_it()
    {
        var (ball, engine, _, registry) = Spawned();
        ((EnergyBall)ball).OnStruckSurface(new Vector(0f, 0f, 1f), EntityHandle.None);
        ball.OnTouch(EntityHandle.None);
        Assert.False(Ended(engine));
        Assert.False(Removed(registry));
    }

    [Fact]
    public void The_sky_ends_it()
    {
        var (ball, engine, _, _) = Spawned(SurfSky);
        ((EnergyBall)ball).OnStruckSurface(new Vector(0f, 0f, -1f), EntityHandle.None);
        Assert.True(Ended(engine));
    }

    [Fact]
    public void What_it_hits_takes_the_impact_and_it_goes_on()
    {
        var (ball, engine, _, registry) = Spawned();
        engine.Returns("Entities.GetIndex", 7);
        ball.OnTouch(new EntityHandle(7));

        var hit = engine.Calls.Single(c => c.Member == "Damage.Apply");
        Assert.Equal(EnergyBall.ImpactDamage, hit.ArgAs<float>(2));
        Assert.False(Ended(engine));
        Assert.False(Removed(registry));
    }

    /// <summary>dm_lockdown wraps its spawn points in trigger_multiple, and a volume is no hit.</summary>
    [Fact]
    public void A_volume_it_flies_through_takes_nothing()
    {
        var (ball, engine, _, _) = Spawned();
        engine.Returns("EntityPhysics.GetSolidFlags", SolidFlags.Trigger);
        ball.OnTouch(new EntityHandle(7));
        Assert.False(engine.Called("Damage.Apply"));
    }

    /// <summary>A touch can name the world as worldspawn, entry 0, as well as nothing.</summary>
    [Fact]
    public void The_world_named_takes_nothing()
    {
        var (ball, engine, _, _) = Spawned();
        engine.Returns("Entities.GetIndex", 0);
        ball.OnTouch(new EntityHandle(7));
        Assert.False(engine.Called("Damage.Apply"));
    }

    [Fact]
    public void Its_clock_ends_it_with_no_blast_and_it_goes_half_a_second_later()
    {
        var (ball, engine, services, registry) = Spawned();
        ball.OnThink();

        Assert.True(Ended(engine));
        Assert.False(engine.Called("Damage.Apply"));
        Assert.Empty(((RecordingBlastSweep)services.Resolve(typeof(IBlastSweep))!).Requests);
        Assert.False(Removed(registry));

        ball.OnThink();
        Assert.True(Removed(registry));
    }
}
