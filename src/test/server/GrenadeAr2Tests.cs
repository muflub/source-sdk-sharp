using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// grenade_ar2 as stock's CGrenadeAR2 (grenade_ar2.cpp): two-thirds gravity, a
/// dark smoke trail, and at the first solid thing it touches a fireball temp
/// entity (scale 2, 15 fps), a scorch along its flight (SmallScorch on
/// anything but the world), a shake, and the blast (100 / 250, DMG_BLAST)
/// credited to whoever fired it.
/// </summary>
public class GrenadeAr2Tests
{
    private static readonly EntityHandle Grenade = new(0x0004_0b01u);
    private static readonly EntityHandle Shooter = new(0x0001_0001u);
    private static readonly EntityHandle Crate = new(0x0004_0b07u);
    private static readonly Vector At = new(100, 200, 50);

    private sealed class Temps : TempEntsDouble
    {
        public readonly List<(float Scale, int FrameRate, int Radius, int Magnitude)> Explosions = [];
        public readonly List<(int Entity, int Index)> Decals = [];

        public override void Explosion(TeFilter filter, float delay, Vector position, int modelIndex, float scale, int frameRate,
                                       int flags, int radius, int magnitude, Vector? normal = null, byte materialType = (byte)'C')
            => Explosions.Add((scale, frameRate, radius, magnitude));

        public override void Decal(TeFilter filter, float delay, Vector position, Vector start, int entity, int hitbox, int index)
            => Decals.Add((entity, index));
    }

    private sealed class Shakes : IScreenEffects
    {
        public readonly List<(Vector Origin, float Amplitude, float Frequency, float Duration, float Radius)> Made = [];

        public void Shake(Vector origin, float amplitude, float frequency, float duration, float radius,
                          ShakeCommand command = ShakeCommand.Start, bool airShake = false)
            => Made.Add((origin, amplitude, frequency, duration, radius));

        public void Fade(EntityHandle player, int r, int g, int b, int a, float duration, float holdTime, FadeFlags flags)
            => throw new NotSupportedException();
    }

    private const int Scorch = 11, SmallScorch = 12;

    private sealed record Rig(ScriptedEntityRegistry Registry, RecordingEngine Engine, Temps Temps, RecordingBlastSweep Blast,
                              Shakes Shakes);

    private static Rig Fired(EntityHandle struck)
    {
        var temps = new Temps();
        var blast = new RecordingBlastSweep();
        var services = Services(out var engine);
        services.AddInstance<ITempEnts>(temps);
        services.AddInstance<IBlastSweep>(blast);
        var shakes = new Shakes();
        services.AddInstance<IScreenEffects>(shakes);
        services.AddInstance<IServerDecals>(new RecordingDecals().Answers("Scorch", Scorch).Answers("SmallScorch", SmallScorch));
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetOrigin", At)
              .Returns("Entities.Create", new EntityHandle(0x0004_0b02u))
              .Returns("Entities.GetIndex", 7)
              .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, At + new Vector(30, 0, 0), new Vector(-1, 0, 0), struck));

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(GrenadeAr2).Assembly);
        registry.Services = services;
        registry.Spawn(Grenade, EntityNames.GrenadeAr2);
        ((GrenadeAr2)registry.Get(Grenade)!).FiredBy(Shooter, new Vector(1000, 0, 0));
        registry.Get(Grenade)!.OnTouch(EntityHandle.None);
        return new Rig(registry, engine, temps, blast, shakes);
    }

    [Fact]
    public void ItGoesOffAtTheFirstThingItTouches()
    {
        var rig = Fired(EntityHandle.None);

        Assert.Equal(new[] { (2f, 15, 250, 100) }, rig.Temps.Explosions);
        var blast = Assert.Single(rig.Blast.Requests);
        Assert.Equal((100f, 250f, DamageType.Blast, Shooter, Grenade),
                     (blast.Magnitude, blast.Radius, blast.DamageType, blast.Attacker, blast.Inflictor));
        Assert.Equal(1, rig.Registry.Directory.PendingRemovalCount);
    }

    [Fact]
    public void ItShakesTheScreensAroundIt()
    {
        var rig = Fired(EntityHandle.None);

        Assert.Equal(new[] { (At, 25f, 150f, 1f, 750f) }, rig.Shakes.Made);
    }

    [Fact]
    public void TheWorldTakesAScorchAlongItsFlight()
    {
        var rig = Fired(EntityHandle.None);

        Assert.Equal(new[] { (0, Scorch) }, rig.Temps.Decals);
    }

    [Fact]
    public void AnythingElseTakesASmallScorch()
    {
        var rig = Fired(Crate);

        Assert.Equal(new[] { (7, SmallScorch) }, rig.Temps.Decals);
    }

    [Fact]
    public void ItFallsAtTwoThirdsOfTheWorldsGravity()
    {
        var rig = Fired(EntityHandle.None);

        Assert.Equal(400f / 600f, (float)rig.Engine.Calls.First(c => c.Member == "EntityPhysics.SetGravity").Args[1]!);
    }

    [Fact]
    public void ItTrailsDarkSmokeAndTakesItWithIt()
    {
        var rig = Fired(EntityHandle.None);
        var smoke = new EntityHandle(0x0004_0b02u);

        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.Create" && (string)c.Args[0]! == EntityNames.EnvSmoketrail);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.SetKeyValue" && (EntityHandle)c.Args[0]! == smoke
                                               && (string)c.Args[1]! == "spawnrate" && (string)c.Args[2]! == "48");
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.SetParent" && (EntityHandle)c.Args[0]! == smoke
                                               && (EntityHandle)c.Args[1]! == Grenade);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.Remove" && (EntityHandle)c.Args[0]! == smoke);
    }
}
