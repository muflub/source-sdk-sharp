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

    private static (ScriptedEntity ball, RecordingEngine engine, ServiceContainer services, ScriptedEntityRegistry registry) Spawned(int surfaceFlags = 0)
    {
        var services = Services(out var engine);
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

    [Fact]
    public void A_wall_does_not_end_it()
    {
        var (ball, engine, _, registry) = Spawned();
        ((EnergyBall)ball).OnStruckSurface(new Vector(0f, 0f, 1f));
        ball.OnTouch(EntityHandle.None);
        Assert.False(Ended(engine));
        Assert.False(Removed(registry));
    }

    [Fact]
    public void The_sky_ends_it()
    {
        var (ball, engine, _, _) = Spawned(SurfSky);
        ((EnergyBall)ball).OnStruckSurface(new Vector(0f, 0f, -1f));
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
