using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// What a point_spotlight leaves in the world once it has set itself up:
/// stock's CPointSpotlight::Activate and SpotlightCreate (point_spotlight.cpp).
///
/// <para>
/// Found side by side with stock (tools/stock-ab, 2026-10-25): every station
/// beam on ss_sandbox was a green shaft in stock and nothing here, because the
/// ss_beam a spotlight builds was never told to draw (its Visible flag
/// defaults to off, and only env_laser and env_beam set it).
/// </para>
/// </summary>
public class SpotlightBeamTests
{
    private static readonly EntityHandle Spot = new(0x0004_0201u);
    private static readonly EntityHandle End = new(0x0004_0202u);
    private static readonly EntityHandle Beam = new(0x0004_0203u);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Lit(string name)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PointSpotlight).Assembly);
        registry.Services = Services(out var engine);

        // Nothing is valid until the spotlight has made something: it asks
        // whether its end exists before making one.
        var made = new Queue<EntityHandle>([End, Beam]);
        bool any = false;
        engine.ReturnsEachCall("Entities.IsValid", () => any)
              .Returns("Entities.GetAngles", new Vector(90f, 0f, 0f))
              .Returns("Precache.PrecacheModel", 7)
              .Returns("Entities.GetOrigin", new Vector(1600f, 1004f, 188f))
              // The spawnflags and the name are the engine's: 1 is "start on".
              .Returns("Entities.GetSpawnFlags", 1)
              .Returns("Entities.GetName", name)
              .ReturnsEachCall("Entities.Create", () =>
              {
                  any = true;
                  return made.Count > 0 ? made.Dequeue() : EntityHandle.None;
              });

        // The end and the beam exist before the spotlight asks for them, so
        // that Entities.Create can hand their handles out in stock's order.
        registry.Spawn(End, EntityNames.SpotlightEnd);
        registry.Spawn(Beam, EntityNames.SsBeam);

        registry.KeyValue(Spot, EntityNames.PointSpotlight, "IgnoreSolid", "1");
        registry.KeyValue(Spot, EntityNames.PointSpotlight, "SpotlightLength", "120");
        registry.KeyValue(Spot, EntityNames.PointSpotlight, "SpotlightWidth", "14");
        registry.Spawn(Spot, EntityNames.PointSpotlight);

        registry.Think(Spot);
        return (registry, engine);
    }

    [Fact]
    public void ASpotlightThatStartsOnDrawsItsBeam()
    {
        var (registry, engine) = Lit("station_beam");

        Assert.True(engine.CountOf("Entities.Create") == 2, "the spotlight never built its end and beam");
        var beam = Assert.IsType<SsBeam>(registry.Get(Beam));
        Assert.True(beam.Visible, "a spotlight's beam is drawn whenever it exists, as stock's CBeam is");
    }

    [Fact]
    public void AnUnnamedSpotlightLeavesItsBeamAndRemovesItselfAndItsEnd()
    {
        // Stock: "No targetname and no parent implies this is a static beam.
        // Hence, we can kill off ourselves and the end point."
        var (_, engine) = Lit("");

        var removed = engine.AllOf("Entities.Remove").Select(c => (EntityHandle)c.Args[0]!).ToList();
        Assert.Contains(Spot, removed);
        Assert.Contains(End, removed);
        Assert.DoesNotContain(Beam, removed);
    }

    [Fact]
    public void ANamedSpotlightKeepsItsEnd()
    {
        // The control: a spotlight something can switch keeps the pool of light.
        var (_, engine) = Lit("station_beam");

        Assert.False(engine.Called("Entities.Remove"), "a named spotlight removes nothing");
    }
}
