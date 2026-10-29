using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// CPhysicsHook's mass-centre overrides (physics.cpp's
/// PhysSetMassCenterOverride and PhysGetMassCenterOverride) and their two
/// producers, info_mass_center (physobj.cpp) and a world phys_hinge's spawn.
/// Found beside stock (2026-10-27): info_mass_center was an info_target, and
/// a world hinge left its object's mass centre off its axis.
/// </summary>
public class MassCenterOverridesTests
{
    private static readonly EntityHandle Point = new(0x0001_00C0);

    private static bool Near(Vector v, float x, float y, float z)
        => MathF.Abs(v.X - x) < 1e-4f && MathF.Abs(v.Y - y) < 1e-4f && MathF.Abs(v.Z - z) < 1e-4f;

    //-------------------------------------------------------------------------
    // The list
    //-------------------------------------------------------------------------

    [Fact]
    public void A_point_is_the_mass_centre_in_the_bodys_space()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToPoint("door", new Vector(10f, 5f, 0f)));

        // Turned a quarter: the point five units along world y is five along the body's x.
        Assert.True(list.TryTake("door", new Vector(10f, 0f, 0f), new QAngle(0f, 90f, 0f), Vector.Zero, out Vector local));
        Assert.True(Near(local, 5f, 0f, 0f), $"{local}");
    }

    [Fact]
    public void An_axis_moves_the_solids_own_centre_onto_it_along_it()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToAxis("door", new Vector(0f, 0f, 0f), new Vector(0f, 0f, 1f)));

        Assert.True(list.TryTake("door", Vector.Zero, default, new Vector(3f, 4f, 5f), out Vector local));
        Assert.True(Near(local, 0f, 0f, 5f), $"{local}");
    }

    [Fact]
    public void An_override_is_taken_once_by_name()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToPoint("door", new Vector(1f, 0f, 0f)));

        Assert.False(list.TryTake("lid", Vector.Zero, default, Vector.Zero, out _));
        Assert.True(list.TryTake("door", Vector.Zero, default, Vector.Zero, out _));
        Assert.False(list.TryTake("door", Vector.Zero, default, Vector.Zero, out _));
    }

    [Fact]
    public void Two_for_one_name_are_taken_in_turn()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToPoint("door", new Vector(1f, 0f, 0f)));
        list.Add(MassCenterOverride.SnapToPoint("door", new Vector(2f, 0f, 0f)));

        list.TryTake("door", Vector.Zero, default, Vector.Zero, out Vector first);
        list.TryTake("door", Vector.Zero, default, Vector.Zero, out Vector second);

        Assert.True(first.X == 1f && second.X == 2f);
    }

    [Fact]
    public void One_at_the_bodys_origin_is_no_override_and_is_still_used_up()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToPoint("door", new Vector(0f, 0f, 0.03f)));

        Assert.False(list.TryTake("door", Vector.Zero, default, Vector.Zero, out _));
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void One_with_no_name_is_dropped()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToPoint("", new Vector(1f, 0f, 0f)));
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void The_level_takes_what_nobody_took()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToPoint("door", new Vector(1f, 0f, 0f)));

        ((IGameLifecycle)list).OnLevelShutdown();

        Assert.Equal(0, list.Count);
    }

    //-------------------------------------------------------------------------
    // info_mass_center
    //-------------------------------------------------------------------------

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine, MassCenterOverrides List) Spawned(
        string className, EntityHandle handle, params (string Key, string Value)[] keys)
        => Spawned(className, handle, 8, keys);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine, MassCenterOverrides List) Spawned(
        string className, EntityHandle handle, int spawnFlags, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(InfoMassCenter).Assembly);
        var list = new MassCenterOverrides();
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance(list);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetOrigin", new Vector(4f, 5f, 6f))
              .Returns("Entities.GetSpawnFlags", spawnFlags);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);
        registry.Spawn(handle, className);
        return (registry, engine, list);
    }

    [Fact]
    public void An_info_mass_center_with_a_target_leaves_its_point_and_goes()
    {
        var (registry, _, list) = Spawned(EntityNames.InfoMassCenter, Point, ("target", "crate"));

        Assert.True(list.TryTake("crate", Vector.Zero, default, Vector.Zero, out Vector local));
        Assert.True(Near(local, 4f, 5f, 6f));
        registry.Directory.FreeRemoved();
        Assert.True(registry.Get(Point) is null, "the point is removed");
    }

    [Fact]
    public void An_info_mass_center_with_no_target_stays_and_leaves_nothing()
    {
        var (registry, _, list) = Spawned(EntityNames.InfoMassCenter, Point);

        registry.Directory.FreeRemoved();
        Assert.Equal(0, list.Count);
        Assert.IsType<InfoMassCenter>(registry.Get(Point));
    }

    //-------------------------------------------------------------------------
    // phys_hinge
    //-------------------------------------------------------------------------

    [Fact]
    public void A_world_hinge_snaps_its_one_named_ends_mass_centre_to_its_axis()
    {
        // At (4,5,6), the axis point straight up from it, a fraction off true.
        var (_, _, list) = Spawned(EntityNames.PhysHinge, Point,
            ("attach1", "door"), ("hingeaxis", "4.01 5 70"));

        // The door's own centre (10, 5, 20), body at the origin: moved onto x=4, y=5.
        Assert.True(list.TryTake("door", Vector.Zero, default, new Vector(10f, 5f, 20f), out Vector local));
        Assert.True(Near(local, 4f, 5f, 20f), $"{local}");
    }

    [Fact]
    public void A_hinge_that_does_not_assume_world_geometry_leaves_no_override()
    {
        var (_, _, list) = Spawned(EntityNames.PhysHinge, Point, spawnFlags: 0,
            ("attach1", "door"), ("hingeaxis", "4 5 70"));

        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void A_hinge_between_two_named_ends_leaves_no_override()
    {
        var (_, _, list) = Spawned(EntityNames.PhysHinge, Point,
            ("attach1", "door"), ("attach2", "frame"), ("hingeaxis", "4 5 70"));

        Assert.Equal(0, list.Count);
    }
}
