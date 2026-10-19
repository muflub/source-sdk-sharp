using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PhysicsCollisionImpacts against stock's CCollisionEvent::PreCollision /
/// PostCollision (physics.cpp) and the POST_COLLISION record native built from
/// them: a crate (side 0, owned) striking an object on side 1.
/// </summary>
public class PhysicsCollisionImpactsTests
{
    private const nint CrateObject = 0x51, OtherObject = 0x52, Unowned = 0x53, StaticProp = 0x54, Data = 0x90;
    private static readonly EntityHandle Crate = new(0x0001_0011u);
    private static readonly EntityHandle Other = new(0x0002_0022u);

    private sealed class Objects : IVPhysicsObjects
    {
        public readonly Dictionary<nint, (Vector Linear, Vector Angular)> Velocities = new();
        public readonly Dictionary<nint, ushort> Flags = new();
        public readonly HashSet<nint> Static = new();
        public readonly HashSet<nint> Moveable = new();
        public readonly HashSet<nint> Shadow = new();
        public readonly List<(nint Object, Vector Linear, Vector Angular)> Set = new();

        public void RecheckContactPoints(nint physicsObject) { }
        public nint GameData(nint physicsObject) => physicsObject switch
        {
            CrateObject => unchecked((nint)(PhysicsGameData.Tag | (Crate.Value + PhysicsGameData.Bias))),
            OtherObject => unchecked((nint)(PhysicsGameData.Tag | (Other.Value + PhysicsGameData.Bias))),
            _ => 0,
        };
        public int ContactsWith(nint physicsObject, EntityHandle other) => 0;
        public Vector ContactPoint(nint collisionData) => collisionData == Data ? new Vector(1f, 2f, 3f) : Vector.Zero;
        public Vector SurfaceNormal(nint collisionData) => collisionData == Data ? new Vector(0f, 0f, 1f) : Vector.Zero;
        public Vector VelocityAtPoint(nint physicsObject, Vector worldPoint) => Vector.Zero;
        public (Vector Linear, Vector Angular) Velocity(nint physicsObject)
            => Velocities.TryGetValue(physicsObject, out var v) ? v : (Vector.Zero, Vector.Zero);
        public void SetVelocity(nint physicsObject, Vector linear, Vector angular)
        {
            Set.Add((physicsObject, linear, angular));
            Velocities[physicsObject] = (linear, angular);
        }
        public void AddVelocity(nint physicsObject, Vector linear, Vector angular) { }
        public float LinearDrag(nint physicsObject, Vector direction) => 0f;
        public float AngularDrag(nint physicsObject, Vector axis) => 0f;
        public float Mass(nint physicsObject) => physicsObject == CrateObject ? 40f : 900f;
        public ushort GameFlags(nint physicsObject) => Flags.GetValueOrDefault(physicsObject);
        public int MaterialIndex(nint physicsObject) => 0;
        public (Vector X, Vector Y, Vector Z) Axes(nint physicsObject) => default;
        public bool IsStatic(nint physicsObject) => Static.Contains(physicsObject);
        public bool IsMoveable(nint physicsObject) => Moveable.Contains(physicsObject);
        public ushort CallbackFlags(nint physicsObject) => Shadow.Contains(physicsObject) ? (ushort)0x0010 : (ushort)0;
    }

    private sealed class Surfaces : IPhysicsCollisionWorld
    {
        public MoveSurface Surface(int surfaceProps) => default;
        public void ScreenShake(in CollisionShake shake) { }
        public void DustEffect(Vector origin, Vector direction, float size, float speed) { }
        public void ImpactSound(EntityHandle entity, CollisionSoundChannel channel, int surfaceProps,
                                int surfacePropsHit, float volume, float speed) { }
    }

    private sealed class World
    {
        public readonly Objects Objects = new();
        public readonly List<PhysicsEvent> Posted = new();
        public readonly RecordingEngine Engine = Fake();
        public readonly Dictionary<EntityHandle, (EntityState State, MoveType MoveType)> States = new();
        public readonly PhysicsCollisionImpacts Impacts;
        public readonly PhysicsCollisionResponse Response = new(new Surfaces());

        public World()
        {
            States[Crate] = (EntityState.Physics, MoveType.VPhysics);
            States[Other] = (EntityState.Physics, MoveType.VPhysics);
            Engine.ReturnsEachCall("Networking.EntityGetState",
                () => States[Engine.Calls[^1].ArgAs<EntityHandle>(0)].State);
            Engine.ReturnsEachCall("Networking.EntityGetState.moveType",
                () => States[Engine.Calls[^1].ArgAs<EntityHandle>(0)].MoveType);
            Engine.ReturnsEachCall("EntityPhysics.Inertia",
                () => Engine.Calls[^1].ArgAs<nint>(0) == CrateObject ? new Vector(4f, 5f, 6f) : new Vector(7f, 8f, 9f));
            Impacts = new PhysicsCollisionImpacts(Objects, Engine, Engine, Response, Posted.Add);
        }

        public static VCollisionEvent Event(nint first, nint second) => new()
        {
            Object0 = first,
            Object1 = second,
            SurfaceProps0 = 11,
            SurfaceProps1 = 12,
            IsCollision = 1,
            DeltaCollisionTime = 0.75f,
            CollisionSpeed = 320f,
            InternalData = Data,
        };

        /// <summary>One whole impact, as vphysics calls it: Pre, the solve, Post.</summary>
        public PhysicsEvent Strike(VCollisionEvent ev, (Vector, Vector)? after = null)
        {
            Impacts.PreCollision(in ev);
            if (after is { } a)
                Objects.Velocities[ev.Object0] = a;
            Impacts.PostCollision(in ev);
            return Assert.Single(Posted);
        }
    }

    [Fact]
    public void An_impact_between_two_unowned_objects_records_nothing()
    {
        var world = new World();
        var ev = World.Event(Unowned, Unowned);

        world.Impacts.PreCollision(in ev);
        world.Impacts.PostCollision(in ev);

        Assert.Empty(world.Posted);
    }

    [Fact]
    public void An_impact_is_a_post_collision_naming_both_owners()
    {
        var world = new World();

        var posted = world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal(PhysicsEventKind.PostCollision, (PhysicsEventKind)posted.KindRaw);
        Assert.Equal(Crate.Value, posted.Entity0);
        Assert.Equal(Other.Value, posted.Entity1);
    }

    // Regression (live gate 9884cec7, push + impact-damage 0/3): a posted
    // impact reached the router (damage) but never the response, so landings
    // made no sound, dust or shake. Stock's PostCollision called
    // VPhysicsCollision in the callback.
    [Fact]
    public void An_impact_is_answered_by_the_collision_response()
    {
        var world = new World();

        world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal(1, world.Response.Responded);
        // One per managed side: both crates own an object here.
        Assert.Equal(2, world.Response.SoundsQueued);
    }

    [Fact]
    public void An_unrecorded_impact_is_not_answered()
    {
        var world = new World();
        var ev = World.Event(Unowned, Unowned);

        world.Impacts.PreCollision(in ev);
        world.Impacts.PostCollision(in ev);

        Assert.Equal(0, world.Response.Responded);
    }

    // Stock's PostCollision calls VPhysicsCollision -- whose body is the
    // sound, shake and dust -- only on the solver's verdict (physics.cpp:
    // 1893-1914). The router gated the entity hooks on it; the response, fed
    // by this same callback, did not, so an impact the solver called no
    // collision still sounded.
    [Fact]
    public void An_impact_the_solver_called_no_collision_is_not_answered()
    {
        var world = new World();
        var ev = World.Event(CrateObject, OtherObject);
        ev.IsCollision = 0;

        world.Strike(ev);

        Assert.Equal(0, world.Response.Responded);
        Assert.Equal(0, world.Response.SoundsQueued);
    }

    [Fact]
    public void Two_shadows_meeting_are_not_answered()
    {
        // isShadow[0] && isShadow[1] -> pEvent->isCollision = false.
        var world = new World();
        world.Objects.Shadow.Add(CrateObject);
        world.Objects.Shadow.Add(OtherObject);

        world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal(0, world.Response.Responded);
        Assert.Equal(0, world.Response.SoundsQueued);
    }

    [Fact]
    public void One_shadow_is_still_answered()
    {
        // The control for the fact above: the gate is BOTH shadows, not either.
        var world = new World();
        world.Objects.Shadow.Add(OtherObject);

        world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal(2, world.Response.SoundsQueued);
    }

    [Fact]
    public void An_entity_hitting_itself_is_answered_once()
    {
        // "special case for hitting self, only make one non-shadow call".
        var world = new World();

        world.Strike(World.Event(CrateObject, CrateObject));

        Assert.Equal(1, world.Response.Responded);
        Assert.Equal(1, world.Response.SoundsQueued);
    }

    [Fact]
    public void An_unowned_side_is_no_entity()
    {
        var world = new World();

        var posted = world.Strike(World.Event(CrateObject, Unowned));

        Assert.Equal(PhysicsEvent.NoEntity, posted.Entity1);
    }

    [Fact]
    public void The_point_and_normal_are_the_events_contact()
    {
        var world = new World();

        var posted = world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal((1f, 2f, 3f), (posted.PointX, posted.PointY, posted.PointZ));
        Assert.Equal((0f, 0f, 1f), (posted.NormalX, posted.NormalY, posted.NormalZ));
    }

    [Fact]
    public void Speed_delta_time_and_surfaces_are_the_events()
    {
        var world = new World();

        var posted = world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal(320f, posted.Speed);
        Assert.Equal(0.75f, posted.DeltaCollisionTime);
        Assert.Equal((11u, 12u), (posted.SurfaceProps0, posted.SurfaceProps1));
    }

    [Fact]
    public void Each_sides_mass_flags_and_staticness_are_its_objects()
    {
        var world = new World();
        world.Objects.Flags[OtherObject] = 0x0004;
        world.Objects.Static.Add(OtherObject);

        var posted = world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal((40f, 900f), (posted.Mass0, posted.Mass1));
        Assert.Equal((0u, 4u), (posted.GameFlags0, posted.GameFlags1));
        Assert.Equal((0, 1), (posted.Static0, posted.Static1));
    }

    [Fact]
    public void A_missing_object_leaves_its_side_zero()
    {
        var world = new World();

        var posted = world.Strike(World.Event(CrateObject, 0));

        Assert.Equal((0f, 0u, 0), (posted.Mass1, posted.GameFlags1, posted.MoveType1));
        Assert.Equal(Vector.Zero, posted.Inertia(1));
    }

    [Fact]
    public void An_owned_sides_move_type_is_its_owners()
    {
        var world = new World();
        world.States[Other] = (EntityState.Player, MoveType.Walk);

        var posted = world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal(((int)MoveType.VPhysics, (int)MoveType.Walk), (posted.MoveType0, posted.MoveType1));
    }

    [Fact]
    public void An_unowned_sides_move_type_is_none()
    {
        var world = new World();

        var posted = world.Strike(World.Event(CrateObject, Unowned));

        Assert.Equal(0, posted.MoveType1);
    }

    [Fact]
    public void The_velocity_before_is_pre_collisions_and_after_is_post_collisions()
    {
        var world = new World();
        world.Objects.Velocities[CrateObject] = (new Vector(100f, 0f, 0f), new Vector(0f, 0f, 30f));

        var posted = world.Strike(World.Event(CrateObject, OtherObject),
                                  after: (new Vector(-50f, 0f, 0f), Vector.Zero));

        Assert.Equal(new Vector(100f, 0f, 0f), posted.PreVelocity(0));
        Assert.Equal(new Vector(0f, 0f, 30f), posted.PreAngularVelocity(0));
        Assert.Equal(new Vector(-50f, 0f, 0f), posted.PostVelocity(0));
    }

    [Fact]
    public void Each_sides_inertia_is_its_objects()
    {
        var world = new World();

        var posted = world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Equal(new Vector(4f, 5f, 6f), posted.Inertia(0));
        Assert.Equal(new Vector(7f, 8f, 9f), posted.Inertia(1));
    }

    [Fact]
    public void Moveable_and_shadow_are_per_side()
    {
        var world = new World();
        world.Objects.Moveable.Add(CrateObject);
        world.Objects.Shadow.Add(OtherObject);

        var posted = world.Strike(World.Event(CrateObject, OtherObject));

        Assert.True(posted.IsMoveable(0));
        Assert.False(posted.IsMoveable(1));
        Assert.False(posted.IsShadow(0));
        Assert.True(posted.IsShadow(1));
    }

    [Fact]
    public void The_solvers_two_verdicts_are_carried()
    {
        var world = new World();
        var ev = World.Event(CrateObject, OtherObject);
        ev.IsCollision = 0;
        ev.IsShadowCollision = 1;

        var posted = world.Strike(ev);

        Assert.False(posted.IsCollision);
        Assert.True(posted.IsShadowCollision);
    }

    // PreCollision's player-held rule: a carried object striking something
    // owned that is not a player moves at no less than 10 in/s and 1 deg/s,
    // so the hit registers rather than resting against it.
    [Fact]
    public void A_held_object_striking_a_prop_is_raised_to_the_minimum_speeds()
    {
        var world = new World();
        world.Objects.Flags[CrateObject] = 0x0004;
        world.Objects.Velocities[CrateObject] = (new Vector(2f, 0f, 0f), new Vector(0f, 0.5f, 0f));

        var posted = world.Strike(World.Event(CrateObject, OtherObject));

        var set = Assert.Single(world.Objects.Set);
        Assert.Equal(CrateObject, set.Object);
        Assert.Equal(10f, set.Linear.X, 4);
        Assert.Equal(1f, set.Angular.Y, 4);
        Assert.Equal(10f, posted.PreVelocity(0).X, 4);
    }

    [Fact]
    public void A_held_object_already_fast_keeps_its_speed()
    {
        var world = new World();
        world.Objects.Flags[CrateObject] = 0x0004;
        world.Objects.Velocities[CrateObject] = (new Vector(0f, 300f, 0f), new Vector(0f, 0f, 45f));

        world.Strike(World.Event(CrateObject, OtherObject));

        var set = Assert.Single(world.Objects.Set);
        Assert.Equal(300f, set.Linear.Y, 3);
        Assert.Equal(45f, set.Angular.Z, 3);
    }

    [Fact]
    public void A_held_object_striking_a_player_is_left_alone()
    {
        var world = new World();
        world.Objects.Flags[CrateObject] = 0x0004;
        world.States[Other] = (EntityState.Player, MoveType.Walk);

        world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Empty(world.Objects.Set);
    }

    [Fact]
    public void A_held_object_striking_the_unowned_is_left_alone()
    {
        var world = new World();
        world.Objects.Flags[CrateObject] = 0x0004;

        world.Strike(World.Event(CrateObject, Unowned));

        Assert.Empty(world.Objects.Set);
    }

    // STATIC PROPS ARE THE WORLD'S (docs/todo.md "Lane C leftovers", after
    // C8's solver rule): stock's PhysCreateWorld hands the world entity to
    // CreateVPhysicsRepresentations (physics.cpp:422), so PreCollision's
    // pOtherEntity for a static prop is the world -- an entity, not a player
    // -- and a carried object striking one is raised (physics.cpp:1836-1851).
    // Ours are made with NULL game data, so a static object with none is the
    // world's here, as PhysicsSolver.OwnerOf has it.
    [Fact]
    public void A_held_object_striking_a_static_prop_is_raised_as_against_the_world()
    {
        var world = new World();
        world.Objects.Static.Add(StaticProp);
        world.Objects.Flags[CrateObject] = 0x0004;
        world.Objects.Velocities[CrateObject] = (new Vector(2f, 0f, 0f), new Vector(0f, 0.5f, 0f));

        world.Strike(World.Event(CrateObject, StaticProp));

        var set = Assert.Single(world.Objects.Set);
        Assert.Equal(CrateObject, set.Object);
        Assert.Equal(10f, set.Linear.X, 4);
        Assert.Equal(1f, set.Angular.Y, 4);
    }

    // The control for the fact above: an object with NULL game data that is
    // NOT static is stock's NULL entity (pOtherEntity NULL), and nothing is
    // raised -- the existing unowned fact, with the staticness the rule reads
    // shown to be what decides.
    [Fact]
    public void A_held_object_striking_a_moveable_object_with_no_owner_is_left_alone()
    {
        var world = new World();
        world.Objects.Moveable.Add(Unowned);
        world.Objects.Flags[CrateObject] = 0x0004;
        world.Objects.Velocities[CrateObject] = (new Vector(2f, 0f, 0f), new Vector(0f, 0.5f, 0f));

        world.Strike(World.Event(CrateObject, Unowned));

        Assert.Empty(world.Objects.Set);
    }

    // PostCollision's record names the world as no entity (the world's own
    // object has NULL game data too, so its side reads exactly so), and a
    // static prop's side posts the same: no entity, static, MOVETYPE_NONE
    // (CWorld::Spawn, world.cpp:467). The consumers' world arm
    // (ScriptedEntity, PropPhysics, SsPlayer read NoEntity as "the world")
    // is therefore stock's pEntities[1] == world for both.
    [Fact]
    public void A_static_props_side_posts_as_the_worlds()
    {
        var world = new World();
        world.Objects.Static.Add(StaticProp);

        var posted = world.Strike(World.Event(CrateObject, StaticProp));

        Assert.Equal(Crate.Value, posted.Entity0);
        Assert.Equal(PhysicsEvent.NoEntity, posted.Entity1);
        Assert.Equal(1, posted.Static1);
        Assert.Equal(0, posted.MoveType1);
    }

    [Fact]
    public void An_object_not_held_is_left_alone()
    {
        var world = new World();
        world.Objects.Velocities[CrateObject] = (new Vector(2f, 0f, 0f), Vector.Zero);

        world.Strike(World.Event(CrateObject, OtherObject));

        Assert.Empty(world.Objects.Set);
    }
}
