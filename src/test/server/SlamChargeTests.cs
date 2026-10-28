/*
 * What weapon_slam leaves in the world: npc_satchel (CSatchelCharge,
 * grenade_satchel.cpp) and npc_tripmine (CTripmineGrenade,
 * grenade_tripmine.cpp), spawned through the registry over a recording
 * engine, their blasts read off RecordingBlastSweep.
 */

using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

public class SlamChargeTests
{
    private static EntityHandle Thrower => new(0x0001_0001);

    private static (T entity, RecordingEngine engine, ServiceContainer services) Spawned<T>(string className, Vector angles)
        where T : ScriptedEntity
    {
        var services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 50f))
              .Returns("Entities.GetAngles", angles)
              .Returns("Entities.IsValid", true)
              .Returns("Precache.PrecacheModel", 1)
              .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              .Returns("WorldQuery.PointContents", Contents.Empty)
              .Returns("Clock.Now", 10f)
              // The beam and the glow are entities of their own.
              .Returns("Entities.Create", new EntityHandle(0x0003_0001));

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(Satchel).Assembly);
        registry.Services = services;

        var handle = new EntityHandle(11);
        registry.Spawn(handle, className);

        return (Assert.IsType<T>(registry.Get(handle)), engine, services);
    }

    private static IReadOnlyList<RecordingBlastSweep.Request> Blasts(ServiceContainer services)
        => ((RecordingBlastSweep)services.Resolve(typeof(IBlastSweep))!).Requests;

    private static string[] Sounds(RecordingEngine engine)
        => engine.AllOf("Sound.EntityEmitSound").Select(c => c.ArgAs<string>(1)).ToArray();

    // =====================================================================
    // npc_satchel.
    // =====================================================================

    [Fact]
    public void ASatchelIsLiveOnlyOnceSomebodyHasThrownIt()
    {
        var (satchel, _, _) = Spawned<Satchel>(EntityNames.NpcSatchel, Vector.Zero);
        Assert.False(satchel.IsLive);

        satchel.ThrownBy(Thrower, attached: false);

        Assert.True(satchel.IsLive);
        Assert.Equal(Thrower, satchel.Thrower);
    }

    [Fact]
    public void TheDetonatorSetsTheSatchelOffAFifthOfASecondLater()
    {
        var (satchel, _, services) = Spawned<Satchel>(EntityNames.NpcSatchel, Vector.Zero);
        satchel.ThrownBy(Thrower, attached: false);

        satchel.Detonate();

        Assert.Empty(Blasts(services));
        Assert.Equal(Satchel.DetonateDelay, satchel.NextThinkDelay("explode"), 3);
    }

    [Fact]
    public void ASatchelGoesOffForOneHundredFiftyOverTwoHundredUnitsScoredToItsThrower()
    {
        var (satchel, _, services) = Spawned<Satchel>(EntityNames.NpcSatchel, Vector.Zero);
        satchel.ThrownBy(Thrower, attached: false);

        satchel.OnThink("explode");

        var bang = Assert.Single(Blasts(services));
        Assert.Equal(150f, bang.Magnitude);
        Assert.Equal(200f, bang.Radius);
        Assert.Equal(Thrower, bang.Attacker);
        Assert.Equal(new Vector(100f, 0f, 66f), bang.Origin);
        Assert.False(satchel.IsLive);
    }

    [Fact]
    public void ItsOwnBlastSetsOffTheGrenadeBlastAsWell()
    {
        var (satchel, engine, _) = Spawned<Satchel>(EntityNames.NpcSatchel, Vector.Zero);
        satchel.ThrownBy(Thrower, attached: false);

        satchel.OnThink("explode");

        // The env_explosion's fireball and CBaseGrenade::Explode's.
        Assert.Equal(2, engine.CountOf("Effects.ExplosionEffect"));
        Assert.Contains(Satchel.GrenadeExplodeSound, Sounds(engine));
    }

    [Fact]
    public void AShotSatchelBangsHarmlesslyAndIsGone()
    {
        var (satchel, engine, services) = Spawned<Satchel>(EntityNames.NpcSatchel, Vector.Zero);
        satchel.ThrownBy(Thrower, attached: false);

        satchel.OnDamaged(new EntityHandle(0x0001_0002), 5f, DamageType.Bullet);

        Assert.Empty(Blasts(services));
        Assert.Equal(1, engine.CountOf("Effects.ExplosionEffect"));
        Assert.Contains(Satchel.GrenadeExplodeSound, Sounds(engine));
        Assert.False(satchel.IsLive, "a detonator no longer finds it");

        // And its queued charge never goes off afterwards.
        satchel.OnThink("explode");
        Assert.Empty(Blasts(services));
    }

    // =====================================================================
    // npc_tripmine.
    // =====================================================================

    /// <summary>On a wall facing -X: VectorAngles of the normal, pitch + 90.</summary>
    private static Vector OnAWall => new(90f, 180f, 0f);

    [Fact]
    public void ATripmineSaysSoWhenPlantedAndItsBeamRunsOutOfTheWall()
    {
        var (mine, engine, _) = Spawned<Tripmine>(EntityNames.NpcTripmine, OnAWall);

        Assert.Contains(Tripmine.PlaceSound, Sounds(engine));
        Assert.True((mine.BeamDirection - new Vector(-1f, 0f, 0f)).Length < 1e-4f,
                    $"the beam leaves along the wall's normal, not {mine.BeamDirection}");
        Assert.False(mine.IsLive);
    }

    [Fact]
    public void ATripmineComesOnTwoSecondsAfterItIsPlanted()
    {
        var (mine, engine, _) = Spawned<Tripmine>(EntityNames.NpcTripmine, OnAWall);

        engine.Returns("Clock.Now", 11.9f);
        mine.OnThink();
        Assert.False(mine.IsLive);

        engine.Returns("Clock.Now", 12.1f);
        mine.OnThink();
        Assert.True(mine.IsLive);
        Assert.Contains(Tripmine.ActivateSound, Sounds(engine));
    }

    [Fact]
    public void AnUnbrokenBeamDoesNotSetItOff()
    {
        var (mine, engine, services) = Spawned<Tripmine>(EntityNames.NpcTripmine, OnAWall);
        engine.Returns("Clock.Now", 12.1f);
        mine.OnThink();

        for (int i = 0; i < 5; i++)
            mine.OnThink();

        Assert.Empty(Blasts(services));
    }

    [Fact]
    public void SomethingCrossingTheBeamSetsItOffAQuarterOfASecondLater()
    {
        var (mine, engine, services) = Spawned<Tripmine>(EntityNames.NpcTripmine, OnAWall);
        mine.PlantedBy(Thrower);
        engine.Returns("Clock.Now", 12.1f);
        mine.OnThink();

        engine.Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0.25f, new Vector(-400f, 0f, 50f), new Vector(1f, 0f, 0f), EntityHandle.None));
        mine.OnThink();

        Assert.Empty(Blasts(services));
        Assert.Equal(0.25f, mine.NextThinkDelay(), 3);

        mine.OnThink();

        var bang = Assert.Single(Blasts(services));
        Assert.Equal(200f, bang.Magnitude);
        Assert.Equal(200f, bang.Radius);
        Assert.Equal(Thrower, bang.Attacker);
        Assert.True((bang.Origin - new Vector(92f, 0f, 50f)).Length < 1e-3f, "eight units out along the beam");
    }

    [Fact]
    public void AShotTripmineGoesOff()
    {
        var (mine, _, services) = Spawned<Tripmine>(EntityNames.NpcTripmine, OnAWall);

        mine.OnDamaged(new EntityHandle(0x0001_0002), 5f, DamageType.Bullet);
        mine.OnThink();

        Assert.Single(Blasts(services));
    }
}
