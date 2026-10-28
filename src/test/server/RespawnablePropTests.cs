using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// prop_physics_respawnable against stock's CPhysicsPropRespawnable
/// (props.cpp). Event_Killed puts out its flame, sends it back where it
/// spawned (origin and angles) and takes it out of the simulation.
/// Materialize waits while something stands where it spawned, then brings it
/// back whole at full health. Found 2026-10-28 in a dm_lockdown barrel A/B:
/// our hidden barrels kept burning, were blown about while hidden, and came
/// back with the health they broke at, so the first hit set them off; and since
/// the first commit a respawned prop came back as an item's pickup trigger.
/// </summary>
public class RespawnablePropTests
{
    private static readonly EntityHandle Prop = new(0x000F_0101u);
    private static readonly EntityHandle FlameHandle = new(0x000F_0102u);

    private const string BarrelText = "mdlkeyvalue { prop_data { health 20 } fire_interactions { flammable yes } }";

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public bool SpotTaken;

        public PropPhysicsRespawnable Entity => (PropPhysicsRespawnable)Registry.Get(Prop)!;

        public T Last<T>(string member, int arg) => Engine.Last(member)!.Value.ArgAs<T>(arg);
    }

    private static Rig Spawned(int spawnFlags = 0)
    {
        var services = Services(out var engine);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        services.AddInstance(new Flames(new RecordingLog(), registry, engine));
        services.AddInstance<IEntityRoster<EnvFire>>(new EntityRoster<EnvFire>());
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.Returns("EntityRender.GetModelKeyValueText", BarrelText)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.Create", FlameHandle)
              .Returns("Clock.Now", 10f)
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.GetOrigin", new Vector(10f, 20f, 30f))
              .Returns("Entities.GetAngles", new Vector(0f, 90f, 0f))
              .ReturnsEachCall("WorldQuery.TraceHull", () => new HullResult(rig.SpotTaken, rig.SpotTaken, EntityHandle.None));

        registry.KeyValue(Prop, EntityNames.PropPhysicsRespawnable, "model", "models/props_c17/oildrum001_explosive.mdl");
        registry.Spawn(Prop, EntityNames.PropPhysicsRespawnable);
        registry.KeyValue(FlameHandle, EntityNames.EntityFlame, "origin", "0 0 0");
        rig.Entity.OnPhysicsCreated();
        rig.Entity.OnThink();
        return rig;
    }

    /// <summary>Event_Killed: hidden where it spawned, its flame put out, and still and colliding with nothing.</summary>
    [Fact]
    public void A_broken_respawnable_goes_home_its_flame_out_and_out_of_the_simulation()
    {
        var rig = Spawned();
        rig.Entity.Ignite(10f);

        rig.Registry.Killed(Prop, EntityHandle.None);

        Assert.Contains(rig.Engine.AllOf("Entities.Remove"), c => c.ArgAs<EntityHandle>(0) == FlameHandle);
        Assert.Equal(new Vector(10f, 20f, 30f), rig.Last<Vector>("Entities.SetOrigin", 1));
        Assert.Equal(new Vector(0f, 90f, 0f), rig.Last<Vector>("Entities.SetAngles", 1));
        Assert.False(rig.Last<bool>("EntityPhysics.EnableCollisions", 1));
        Assert.False(rig.Last<bool>("EntityPhysics.EnableMotion", 1));
        Assert.False(rig.Last<bool>("EntityRender.SetVisible", 1));
    }

    /// <summary>
    /// HL2MP's CBreakableProp::Break removes nothing, and only Event_Killed
    /// hides a respawnable: broken by its Break input it stays where it was,
    /// drawn and not solid, and never comes back.
    /// </summary>
    [Fact]
    public void One_broken_by_its_input_stays_where_it_is_and_does_not_come_back()
    {
        var rig = Spawned();
        float think = rig.Registry.Directory.Think.NextThinkTime(Prop, null);

        rig.Registry.Input(Prop, "Break", "", 0f, EntityHandle.None);

        Assert.Equal(1, rig.Entity.OnBreak.FireCount);
        Assert.False(rig.Engine.Called("EntityRender.SetVisible"));
        Assert.False(rig.Engine.Called("Entities.SetOrigin"));
        Assert.False(rig.Entity.Gone);
        Assert.Contains(rig.Engine.AllOf("EntityPhysics.SetNotSolid"), c => c.ArgAs<bool>(1));
        Assert.Equal(think, rig.Registry.Directory.Think.NextThinkTime(Prop, null));
    }

    [Fact]
    public void It_waits_a_second_while_something_stands_where_it_spawned()
    {
        var rig = Spawned();
        rig.Registry.Killed(Prop, EntityHandle.None);
        rig.SpotTaken = true;

        rig.Entity.OnThink();

        Assert.False(rig.Entity.Respawned);
        Assert.Equal(11f, rig.Registry.Directory.Think.NextThinkTime(Prop, null), 3);
    }

    [Fact]
    public void It_comes_back_whole_at_full_health_solid_and_moving()
    {
        var rig = Spawned();
        rig.Registry.Killed(Prop, EntityHandle.None);
        int healthWrites = rig.Engine.AllOf("Entities.SetHealth").Count();

        rig.Entity.OnThink();

        Assert.True(rig.Entity.Respawned);
        Assert.Equal(healthWrites + 1, rig.Engine.AllOf("Entities.SetHealth").Count());
        Assert.Equal(20, rig.Last<int>("Entities.SetHealth", 1));
        Assert.False(rig.Last<bool>("EntityPhysics.SetNotSolid", 1));
        Assert.True(rig.Last<bool>("EntityPhysics.EnableCollisions", 1));
        Assert.True(rig.Last<bool>("EntityPhysics.EnableMotion", 1));
        Assert.True(rig.Last<bool>("EntityRender.SetVisible", 1));
    }

    /// <summary>
    /// Never the item pickup toggle, which makes a SOLID_BBOX trigger: a
    /// respawned barrel came back a non-solid trigger and fell through the
    /// world (live, dm_lockdown, 2026-10-28).
    /// </summary>
    [Fact]
    public void It_is_no_pickup_trigger_on_the_way_out_or_back()
    {
        var rig = Spawned();
        rig.Registry.Killed(Prop, EntityHandle.None);

        rig.Entity.OnThink();

        Assert.False(rig.Engine.Called("Players.SetPickupEnabled"));
    }

    [Fact]
    public void A_pinned_one_comes_back_pinned()
    {
        var rig = Spawned(spawnFlags: 8);
        rig.Registry.Killed(Prop, EntityHandle.None);

        rig.Entity.OnThink();

        Assert.True(rig.Entity.Respawned);
        Assert.False(rig.Last<bool>("EntityPhysics.EnableMotion", 1));
    }
}
