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
/// beam a spotlight builds was never told to draw (its Visible flag
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
        registry.Spawn(Beam, EntityNames.Beam);

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

    [Fact]
    public void ASpotlightThatLostItsEndRemovesItsOldBeamBeforeBuildingANewOne()
    {
        // Stock's SpotlightUpdate: a target that has gone is SpotlightDestroy
        // and then SpotlightCreate, so the beam is replaced, never doubled.
        EntityHandle end2 = new(0x0004_0204u);
        EntityHandle beam2 = new(0x0004_0205u);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PointSpotlight).Assembly);
        registry.Services = Services(out var engine);

        var made = new Queue<EntityHandle>([End, Beam, end2, beam2]);
        var alive = new HashSet<EntityHandle>();
        engine.ReturnsEachCall("Entities.IsValid",
                  () => alive.Contains((EntityHandle)engine.AllOf("Entities.IsValid").Last().Args[0]!))
              .Returns("Entities.GetAngles", new Vector(90f, 0f, 0f))
              .Returns("Precache.PrecacheModel", 7)
              .Returns("Entities.GetOrigin", new Vector(1600f, 1004f, 188f))
              .Returns("Entities.GetSpawnFlags", 1)
              .Returns("Entities.GetName", "station_beam")
              .ReturnsEachCall("Entities.Create", () =>
              {
                  EntityHandle next = made.Count > 0 ? made.Dequeue() : EntityHandle.None;
                  alive.Add(next);
                  return next;
              });

        registry.Spawn(End, EntityNames.SpotlightEnd);
        registry.Spawn(Beam, EntityNames.Beam);
        registry.Spawn(end2, EntityNames.SpotlightEnd);
        registry.Spawn(beam2, EntityNames.Beam);

        registry.KeyValue(Spot, EntityNames.PointSpotlight, "IgnoreSolid", "1");
        registry.KeyValue(Spot, EntityNames.PointSpotlight, "SpotlightLength", "120");
        registry.KeyValue(Spot, EntityNames.PointSpotlight, "SpotlightWidth", "14");
        registry.Spawn(Spot, EntityNames.PointSpotlight);

        registry.Think(Spot);
        Assert.True(engine.CountOf("Entities.Create") == 2, "the spotlight never built its end and beam");

        // The end goes; the beam it was paired with is still in the world.
        alive.Remove(End);
        registry.Think(Spot);

        Assert.True(engine.CountOf("Entities.Create") == 4, "the spotlight did not rebuild after losing its end");
        var removed = engine.AllOf("Entities.Remove").Select(c => (EntityHandle)c.Args[0]!).ToList();
        Assert.Contains(Beam, removed);
        Assert.DoesNotContain(beam2, removed);
    }

    [Fact]
    public void AResetSpotlightHoldsExactlyOneBeamAndOneEndAfterTheRoundReset()
    {
        // Stock: CleanUpMap removes the spotlight with its beam and its end
        // (UTIL_Remove over the non-preserved entities, then CleanupDeleteList,
        // teamplayroundbased_gamerules.cpp) and the re-parse makes them again,
        // so after a round reset one spotlight holds exactly one beam and one
        // end — not the two additive shafts of a surviving old pair.
        var rig = new MapCleanupTests.Rig("{\n\"classname\" \"worldspawn\"\n}\n"
            + "{\n\"classname\" \"point_spotlight\"\n\"targetname\" \"spot_a\"\n\"spawnflags\" \"3\"\n\"IgnoreSolid\" \"1\"\n\"SpotlightLength\" \"120\"\n}\n");
        rig.Recording.Returns("Entities.GetName", "spot_a");
        rig.Recording.ReturnsEachCall("Entities.IsValid", () =>
        {
            var asked = (EntityHandle)rig.Recording.AllOf("Entities.IsValid").Last().Args[0]!;
            return asked.Value != uint.MaxValue && rig.Engine.InUse(asked.EntryIndex);
        });

        var spot = rig.Named("spot_a")!.Handle;
        rig.Registry.Think(spot);

        Assert.Equal(1, CountAlive(rig, EntityNames.Beam));
        Assert.Equal(1, CountAlive(rig, EntityNames.SpotlightEnd));
        int oldBeamSlot = AliveSlot(rig, EntityNames.Beam);
        int oldEndSlot = AliveSlot(rig, EntityNames.SpotlightEnd);

        rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        var rebuilt = rig.Named("spot_a")!.Handle;
        Assert.NotEqual(spot, rebuilt);   // the cleanup made a fresh one
        rig.Registry.Think(rebuilt);

        Assert.Equal(1, CountAlive(rig, EntityNames.Beam));
        Assert.Equal(1, CountAlive(rig, EntityNames.SpotlightEnd));

        // The rebuild lands in the OLD pair's slots, which only holds when the
        // cleanup really freed them: a surviving old pair would push the fresh
        // one into the next free edict and read as two additive shafts.
        Assert.Equal(oldBeamSlot, AliveSlot(rig, EntityNames.Beam));
        Assert.Equal(oldEndSlot, AliveSlot(rig, EntityNames.SpotlightEnd));
    }

    private static int CountAlive(MapCleanupTests.Rig rig, string className)
        => rig.Registry.Directory.Snapshot().Count(e => e.ClassName == className);

    private static int AliveSlot(MapCleanupTests.Rig rig, string className)
        => rig.Registry.Directory.Snapshot().First(e => e.ClassName == className).Handle.EntryIndex;
}
