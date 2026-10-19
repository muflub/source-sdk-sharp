using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed B2.2 and plan_physics_bones_native_zero 2.1: managed's
/// IPhysicsCollisionEvent table answers all ten slots. Since 2.3 the binding
/// has no outbound call at all: the event ring's drain went with the ring.
/// </summary>
public unsafe class PhysicsCollisionEventsTests
{
    // Physics objects whose game data names a managed owner, and one that names none.
    private const nint TriggerObject = 0x50, CrateObject = 0x51, SdkObject = 0x52;
    // The world's own object and a static prop's: static, NULL game data.
    private const nint WorldObject = 0x53, StaticPropObject = 0x54;
    private static readonly EntityHandle Trigger = new(0x0001_0010u);
    private static readonly EntityHandle Crate = new(0x0001_0011u);
    // worldspawn: entry 0, with a serial, as the engine's list hands it.
    private static readonly EntityHandle World = new(0x0123_0000u);

    private static nint GameData(nint physicsObject) => physicsObject switch
    {
        TriggerObject => unchecked((nint)(PhysicsGameData.Tag | (Trigger.Value + PhysicsGameData.Bias))),
        CrateObject => unchecked((nint)(PhysicsGameData.Tag | (Crate.Value + PhysicsGameData.Bias))),
        _ => 0,
    };

    private readonly SourceSharp.Server.Interop.SolverHeldOverlaps _held = new();

    // The objects' game data, and how many contacts each still has with an entity.
    private sealed class FakeObjects : IVPhysicsObjects
    {
        public int Contacts = 1;
        // When set, the friction snapshot's other objects, counted through the caller's owner rule.
        public nint[]? Snapshot;
        public void RecheckContactPoints(nint physicsObject) { }
        public nint GameData(nint physicsObject) => PhysicsCollisionEventsTests.GameData(physicsObject);
        public int ContactsWith(nint physicsObject, Func<nint, EntityHandle> ownerOf, EntityHandle other)
            => Snapshot is null ? Contacts : Snapshot.Count(o => ownerOf(o) == other);
        public Vector ContactPoint(nint collisionData) => new(1f, 2f, 3f);
        public Vector VelocityAtPoint(nint physicsObject, Vector worldPoint) => new(30f, 40f, 0f);
        public (Vector Linear, Vector Angular) Velocity(nint physicsObject) => default;
        public void AddVelocity(nint physicsObject, Vector linear, Vector angular) { }
        public float LinearDrag(nint physicsObject, Vector direction) => 0f;
        public float AngularDrag(nint physicsObject, Vector axis) => 0f;
        public float Mass(nint physicsObject) => 0f;
        public ushort GameFlags(nint physicsObject) => 0;
        public int MaterialIndex(nint physicsObject) => 0;
        public (Vector X, Vector Y, Vector Z) Axes(nint physicsObject) => default;
        public void SetVelocity(nint physicsObject, Vector linear, Vector angular) { }
        public bool IsStatic(nint physicsObject) => physicsObject is WorldObject or StaticPropObject;
        public bool IsMoveable(nint physicsObject) => false;
        public ushort CallbackFlags(nint physicsObject) => 0;
        public Vector SurfaceNormal(nint collisionData) => Vector.Zero;
    }

    private sealed class RecordingFluids : IPhysicsFluidSink
    {
        public readonly List<string> Seen = new();
        public void FluidStart(nint physicsObject, nint fluid) => Seen.Add($"start {physicsObject:x} {fluid:x}");
        public void FluidEnd(nint physicsObject, nint fluid) => Seen.Add($"end {physicsObject:x} {fluid:x}");
    }

    private readonly RecordingFluids _fluids = new();

    private sealed class RecordingImpacts : IPhysicsCollisionSink
    {
        public readonly List<string> Seen = new();
        public void PreCollision(in VCollisionEvent ev) => Seen.Add($"pre {ev.Object0:x} {ev.Object1:x}");
        public void PostCollision(in VCollisionEvent ev) => Seen.Add($"post {ev.Object0:x} {ev.CollisionSpeed}");
    }

    private readonly RecordingImpacts _impacts = new();

    private readonly List<PhysicsEvent> _posted = new();

    private readonly FakeObjects _objects = new();

    private EntityHandle _world = World;

    private PhysicsCollisionEventBinding Sink(Crossings? crossings = null)
        => new(crossings ?? new Crossings(), _objects, _held, _posted.Add, _fluids, _impacts,
               new PhysicsWorldEntity(() => _world, h => h == _world));

    private List<OverlapPair> Held()
    {
        var into = new OverlapPair[8];
        int n = _held.Merge(into, 0);
        return into[..n].ToList();
    }

    [Fact]
    public void The_world_is_found_again_once_the_old_one_stops_resolving()
    {
        // A level change makes the world again under a new serial.
        var current = World;
        int finds = 0;
        var world = new PhysicsWorldEntity(() => { finds++; return current; }, h => h == current);

        Assert.Equal(World, world.Handle);
        Assert.Equal(World, world.Handle);
        Assert.Equal(1, finds);

        current = new EntityHandle(0x0456_0000u);
        Assert.Equal(current, world.Handle);
        Assert.Equal(2, finds);
    }

    [Fact]
    public void A_crate_touching_the_world_holds_the_pair_with_the_world_entity_as_stock_CWorld()
    {
        var sink = Sink();

        sink.StartTouch((void*)CrateObject, (void*)WorldObject, (void*)0x99);

        Assert.Equal([new OverlapPair(Crate, World)], Held());
    }

    [Fact]
    public void A_static_prop_is_the_world_entity_in_a_contact_touch()
    {
        // Stock's static props carry the CWorld too (PhysCreateWorld).
        var sink = Sink();

        sink.StartTouch((void*)StaticPropObject, (void*)CrateObject, (void*)0x99);

        Assert.Equal([new OverlapPair(World, Crate)], Held());
    }

    [Fact]
    public void A_moving_object_with_no_game_data_is_not_the_world()
    {
        var sink = Sink();

        sink.StartTouch((void*)CrateObject, (void*)SdkObject, (void*)0x99);

        Assert.Empty(Held());
    }

    [Fact]
    public void With_no_world_to_name_a_world_touch_holds_nothing()
    {
        _world = EntityHandle.None;
        var sink = Sink();

        sink.StartTouch((void*)CrateObject, (void*)WorldObject, (void*)0x99);

        Assert.Empty(Held());
    }

    [Fact]
    public void An_end_against_the_world_counts_every_world_side_contact_as_the_world()
    {
        // The crate rests on the world and a static prop: one contact going
        // leaves another with the world entity, so the pair stays (stock's
        // CountPhysicsObjectEntityContacts with pEntity == the CWorld).
        var sink = Sink();
        sink.StartTouch((void*)CrateObject, (void*)WorldObject, (void*)0x99);
        _objects.Snapshot = [WorldObject, StaticPropObject];

        sink.EndTouch((void*)CrateObject, (void*)WorldObject, (void*)0x99);

        Assert.Single(Held());
    }

    [Fact]
    public void The_last_world_contact_ending_releases_the_pair()
    {
        var sink = Sink();
        sink.StartTouch((void*)CrateObject, (void*)WorldObject, (void*)0x99);
        _objects.Snapshot = [WorldObject, TriggerObject];

        sink.EndTouch((void*)CrateObject, (void*)WorldObject, (void*)0x99);

        Assert.Empty(Held());
    }

    [Fact]
    public void Post_simulation_frame_is_counted_once_and_posts_nothing()
    {
        // Native's PostSimulationFrame was the damage events, the post-step
        // queue, the remove list and the ring's drain: managed, without a
        // producer, or (2.3) gone with the ring.
        var crossings = new Crossings();
        var sink = Sink(crossings);

        sink.PostSimulationFrame();

        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsCollisionPostSimulationFrame));
        Assert.Empty(_posted);
    }

    [Fact]
    public void The_impact_slots_go_to_the_servers_impact_sink_and_are_not_forwarded()
    {
        var sink = Sink();
        var ev = new VCollisionEvent { Object0 = 0x51, Object1 = 0x52, CollisionSpeed = 320f };

        sink.PreCollision(&ev);
        sink.PostCollision(&ev);

        Assert.Equal(new[] { "pre 51 52", "post 51 320" }, _impacts.Seen);
    }

    [Fact]
    public void A_null_impact_event_reaches_nobody()
    {
        var sink = Sink();

        sink.PreCollision(null);
        sink.PostCollision(null);

        Assert.Empty(_impacts.Seen);
    }

    [Fact]
    public void Entering_a_physics_trigger_holds_the_pair_and_is_not_forwarded()
    {
        var sink = Sink();

        sink.ObjectEnterTrigger((void*)TriggerObject, (void*)CrateObject);

        Assert.Equal(1, _held.Count);
    }

    [Fact]
    public void Leaving_releases_the_pair()
    {
        var sink = Sink();
        sink.ObjectEnterTrigger((void*)TriggerObject, (void*)CrateObject);

        sink.ObjectLeaveTrigger((void*)TriggerObject, (void*)CrateObject);

        Assert.Equal(0, _held.Count);
    }

    [Fact]
    public void The_fluid_slots_go_to_the_servers_fluid_sink_and_are_not_forwarded()
    {
        var sink = Sink();

        sink.FluidStartTouch((void*)0x40, (void*)0x41);
        sink.FluidEndTouch((void*)0x42, (void*)0x43);

        Assert.Equal(new[] { "start 40 41", "end 42 43" }, _fluids.Seen);
    }

    [Fact]
    public void A_scrape_posts_the_contact_and_the_velocity_there_and_is_not_forwarded()
    {
        var sink = Sink();

        sink.Friction((void*)CrateObject, 8000f, 7, 9, (void*)0x99);

        var ev = Assert.Single(_posted);
        Assert.Equal(PhysicsEventKind.Friction, (PhysicsEventKind)ev.KindRaw);
        Assert.Equal(Crate.Value, ev.Entity0);
        Assert.Equal((1f, 2f, 3f), (ev.PointX, ev.PointY, ev.PointZ));
        Assert.Equal((30f, 40f, 0f), (ev.NormalX, ev.NormalY, ev.NormalZ));
        Assert.Equal(50f, ev.Speed);
        Assert.Equal(8000f, ev.Energy);
        Assert.Equal((7u, 9u), (ev.SurfaceProps0, ev.SurfaceProps1));
    }

    [Fact]
    public void An_ownerless_scrape_posts_nothing()
    {
        var sink = Sink();

        sink.Friction((void*)SdkObject, 8000f, 7, 9, (void*)0x99);

        Assert.Empty(_posted);
    }

    [Fact]
    public void A_contact_touch_holds_the_pair_and_is_not_forwarded()
    {
        var sink = Sink();

        sink.StartTouch((void*)TriggerObject, (void*)CrateObject, (void*)0x99);

        Assert.Equal(1, _held.Count);
    }

    [Fact]
    public void The_last_contact_ending_releases_the_pair()
    {
        var sink = Sink();
        sink.StartTouch((void*)TriggerObject, (void*)CrateObject, (void*)0x99);
        _objects.Contacts = 1;

        sink.EndTouch((void*)TriggerObject, (void*)CrateObject, (void*)0x99);

        Assert.Equal(0, _held.Count);
    }

    [Fact]
    public void An_end_while_other_contacts_remain_keeps_the_pair_as_stock_does()
    {
        // CountPhysicsObjectEntityContacts( pObject1, owner2 ) > 1: still touching.
        var sink = Sink();
        sink.StartTouch((void*)TriggerObject, (void*)CrateObject, (void*)0x99);
        _objects.Contacts = 2;

        sink.EndTouch((void*)TriggerObject, (void*)CrateObject, (void*)0x99);

        Assert.Equal(1, _held.Count);
    }

    [Fact]
    public void A_pair_with_an_ownerless_side_is_not_held_as_native_skipped_it()
    {
        var sink = Sink();

        sink.ObjectEnterTrigger((void*)TriggerObject, (void*)SdkObject);
        sink.ObjectEnterTrigger(null, (void*)CrateObject);

        Assert.Equal(0, _held.Count);
    }

    [Fact]
    public void Each_slot_counts_its_own_crossing()
    {
        var crossings = new Crossings();
        var sink = Sink(crossings);

        var ev = new VCollisionEvent();
        sink.PostCollision(&ev);
        sink.PostCollision(&ev);
        sink.PostSimulationFrame();

        Assert.Equal(2, crossings.Read(CrossingOp.ServerPhysicsCollisionPostCollision));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsCollisionPostSimulationFrame));
        Assert.Equal(0, crossings.Read(CrossingOp.ServerPhysicsCollisionPreCollision));
    }
}
