using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A mover blocked by vphysics (CBaseEntity::VPhysicsUpdatePusher and
/// CheckPushedEntity): when its shadow could not get where it was sent, it
/// gives back the share of the step the shadow lost, or the whole frame and
/// its pushes when it pushed somebody, and is told what blocked it. Found
/// beside stock on ss_sandbox (2026-10-27): a door closed on a crate and kept
/// going where stock's reverses.
/// </summary>
public class PusherReconcileTests
{
    private static readonly EntityHandle Door = new(0x0004_0371u);
    private static readonly EntityHandle Crate = new(0x0004_0372u);
    private static readonly EntityHandle Rider = new(0x0004_0373u);
    private const nint Shadow = 0x7700;
    private const nint CrateObject = 0x7800;
    private const float Dt = 0.015f;

    private sealed class Mover : ScriptedEntity
    {
        public readonly List<EntityHandle> BlockedBy = new();
        public override void OnBlocked(EntityHandle other) => BlockedBy.Add(other);
    }

    private sealed class World : IHierarchyWorld
    {
        public readonly Dictionary<EntityHandle, (Vector Origin, QAngle Angles)> At = new();

        public void WriteComposed(EntityHandle child, Vector origin, QAngle angles, Transform childToWorld)
            => At[child] = (origin, angles);

        public bool IsStandalone(EntityHandle handle) => true;
        public bool IsBase(EntityHandle handle) => false;
        public bool CanPose(EntityHandle handle) => false;
        public bool TryGetEntityFrame(EntityHandle handle, out Transform frame) { frame = Transform.Identity; return false; }
        public bool TryGetAttachmentFrame(EntityHandle handle, int attachment, out Transform frame) { frame = Transform.Identity; return false; }
        public int LookupAttachment(EntityHandle handle, string attachmentName) => -1;
        public bool TryGetAbsolute(EntityHandle handle, out Vector origin, out QAngle angles) { origin = Vector.Zero; angles = QAngle.Zero; return false; }
        public EntityHandle FindStandaloneByName(string name) => EntityHandle.None;
        public EntityHandle FindBaseByName(string name) => EntityHandle.None;
        public string NameOf(EntityHandle handle) => "test";
        public void Remove(EntityHandle handle) { }
        public void DetachPhysics(EntityHandle child) { }
        public void Warn(string message) { }
    }

    private sealed class Pusher : IVPhysicsPusherCalls
    {
        public ShadowState? State;
        public nint Touching;

        public bool TryShadow(nint physicsObject, out ShadowState state)
        {
            state = State ?? default;
            return State is not null && physicsObject == Shadow;
        }

        public nint ContactObject(nint physicsObject) => Touching;
    }

    private sealed class Rig
    {
        public required EntityMotion Motion;
        public required RecordingEngine Engine;
        public required World World;
        public required Mover Mover;
        public required Pusher Pusher;
        public required FakeSolverObjects Objects;
        public required PusherReconcile Reconcile;
        public readonly Dictionary<EntityHandle, Vector> Origins = new();

        public Vector DoorAt => World.At.TryGetValue(Door, out var at) ? at.Origin : Origins[Door];
    }

    private static Rig Make(SolidType solid = SolidType.VPhysics)
    {
        var engine = new RecordingEngine { Strict = false };
        var directory = new EntityDirectory(new RecordingLog());
        var mover = new Mover();
        var world = new World();
        directory.Bind(Door, mover);

        var origins = new Dictionary<EntityHandle, Vector> { [Door] = new(0f, 0f, 100f), [Rider] = new(0f, 0f, 40f) };
        engine.ReturnsEachCall("Entities.GetOrigin", () =>
        {
            var h = engine.Calls.Last().ArgAs<EntityHandle>(0);
            if (world.At.TryGetValue(h, out var at))
                return at.Origin;
            return origins.TryGetValue(h, out var o) ? o : Vector.Zero;
        })
              .WhenCalled("Entities.SetOrigin", c => origins[c.ArgAs<EntityHandle>(0)] = c.ArgAs<Vector>(1))
              .Returns("Entities.GetAngles", Vector.Zero)
              .Returns("Entities.GetBounds", (new Vector(-32f, -32f, 0f), new Vector(32f, 32f, 8f)))
              .Returns("WorldQuery.EntitiesInSphere", 0)
              .Returns("Console.FindConVarValue", null)
              .Returns("Entities.GetSolidType", solid)
              .Returns("Entities.GetParent", EntityHandle.None)
              .Returns("EntityPhysics.PhysicsObject", Shadow);

        var motion = new EntityMotion(new RecordingLog(), engine, engine, engine, engine, engine, directory,
            new PhysicsPushedEntities(new EnginePushWorld(engine, engine, engine, directory), new RecordingLog()))
        {
            Parenting = new Hierarchy(world),
        };
        var pusher = new Pusher();
        var objects = new FakeSolverObjects();
        objects.Owned(CrateObject, Crate);
        var rig = new Rig
        {
            Motion = motion, Engine = engine, World = world, Mover = mover, Pusher = pusher, Objects = objects,
            Reconcile = new PusherReconcile(motion, engine, engine, pusher, objects),
        };
        foreach (var (k, v) in origins)
            rig.Origins[k] = v;
        return rig;
    }

    /// <summary>The door one frame down at 80 units a second, its shadow sent there and stuck where it started.</summary>
    private static Rig Closing(float shadowLag, SolidType solid = SolidType.VPhysics)
    {
        var rig = Make(solid);
        rig.Motion.MoveToPosition(Door, new Vector(0f, 0f, 0f), 80f);
        rig.Motion.RunFrame(Dt);

        Vector at = rig.DoorAt;
        rig.Pusher.State = new ShadowState(at + new Vector(0f, 0f, shadowLag), QAngle.Zero, at, QAngle.Zero);
        rig.Pusher.Touching = CrateObject;
        return rig;
    }

    [Fact]
    public void A_door_whose_shadow_was_held_back_gives_back_its_step_and_hears_what_blocked_it()
    {
        // 80 * 0.015 = 1.2 units a frame; the shadow lost all 1.2 of them.
        var rig = Closing(shadowLag: 1.2f);
        Vector stepped = rig.DoorAt;

        rig.Reconcile.Updated(Door);

        Assert.Equal(100f, rig.DoorAt.Z, 2);
        Assert.True(stepped.Z < 100f, "the step was taken first");
        Assert.Equal([Crate], rig.Mover.BlockedBy);
        Assert.Equal(1, rig.Reconcile.Blocked);
    }

    [Fact]
    public void A_shadow_that_kept_up_changes_nothing()
    {
        var rig = Closing(shadowLag: 0.5f);
        Vector stepped = rig.DoorAt;

        rig.Reconcile.Updated(Door);

        Assert.Equal(stepped.Z, rig.DoorAt.Z);
        Assert.Empty(rig.Mover.BlockedBy);
    }

    [Fact]
    public void A_bsp_mover_is_never_blocked_by_vphysics()
    {
        var rig = Closing(shadowLag: 1.2f, SolidType.Bsp);
        Vector stepped = rig.DoorAt;

        rig.Reconcile.Updated(Door);

        Assert.Equal(stepped.Z, rig.DoorAt.Z);
        Assert.Empty(rig.Mover.BlockedBy);
    }

    [Fact]
    public void It_reconciles_once_a_frame()
    {
        var rig = Closing(shadowLag: 1.2f);

        rig.Reconcile.Updated(Door);
        // A second update the same frame finds the same shadow lag again.
        Vector at = rig.DoorAt;
        rig.Pusher.State = new ShadowState(at + new Vector(0f, 0f, 1.2f), QAngle.Zero, at, QAngle.Zero);
        rig.Reconcile.Updated(Door);

        Assert.Single(rig.Mover.BlockedBy);
    }

    [Fact]
    public void A_parented_mover_is_left_to_its_root()
    {
        var rig = Closing(shadowLag: 1.2f);
        rig.Engine.Returns("Entities.GetParent", Rider);

        rig.Reconcile.Updated(Door);

        Assert.Empty(rig.Mover.BlockedBy);
    }

    [Fact]
    public void Without_a_contact_it_still_gives_back_its_step_but_blames_nobody()
    {
        var rig = Closing(shadowLag: 1.2f);
        rig.Pusher.Touching = 0;

        rig.Reconcile.Updated(Door);

        Assert.Equal(100f, rig.DoorAt.Z, 2);
        Assert.Empty(rig.Mover.BlockedBy);
    }

    //-------------------------------------------------------------------------
    // The push list (CPhysicsPushedEntities::StoreMovedEntities): a step that
    // pushed somebody is undone whole, the pushed put back, and the blocker the
    // contact pressing hardest that the push did not move.
    //-------------------------------------------------------------------------

    private static readonly EntityHandle Player = new(7);
    private static readonly EntityHandle Slab = new(9);

    [Fact]
    public void A_door_that_pushed_a_player_and_was_blocked_undoes_the_frame_and_the_push()
    {
        var world = new PushBoxWorld();
        world.Add(Door, Vector.Zero, new Vector(-4f, -32f, 0f), new Vector(4f, 32f, 96f));
        world.AddPlayer(Player, new Vector(21f, 0f, 0f), ground: 0);
        var engine = new RecordingEngine { Strict = false };
        var directory = new EntityDirectory(new RecordingLog());
        var mover = new Mover();
        directory.Bind(Door, mover);
        world.Install(engine);
        engine.Returns("Entities.GetSolidType", SolidType.VPhysics)
              .Returns("Entities.GetParent", EntityHandle.None)
              .Returns("EntityPhysics.PhysicsObject", Shadow);
        var motion = new EntityMotion(new RecordingLog(), engine, engine, engine, engine, engine, directory,
                                      new PhysicsPushedEntities(world, new RecordingLog()))
        {
            Parenting = new Hierarchy(world),
        };
        var objects = new FakeSolverObjects();
        objects.Owned(0x7901, Player);
        objects.Owned(0x7902, Slab);
        // The slab first; the pushed player presses harder along the push, and
        // is passed over because the push moved them.
        objects.At(Shadow).Contacts =
        [
            new SnapshotContact(0x7902, 200f, Vector.Zero, new Vector(1f, 0f, 0f)),
            new SnapshotContact(0x7901, 500f, Vector.Zero, new Vector(1f, 0f, 0f)),
        ];
        var pusher = new Pusher();
        var reconcile = new PusherReconcile(motion, engine, engine, pusher, objects);

        motion.MoveToPosition(Door, new Vector(64f, 0f, 0f), 100f);
        motion.RunFrame(Dt);
        Assert.Equal(1.5f, world[Door].Origin.X, 3);
        Assert.Equal(22.5f, world[Player].Origin.X, 3);

        pusher.State = new ShadowState(Vector.Zero, QAngle.Zero, world[Door].Origin, QAngle.Zero);
        reconcile.Updated(Door);

        Assert.Equal(0f, world[Door].Origin.X, 3);
        Assert.Equal(21f, world[Player].Origin.X, 3);
        // The player was pushed, so the slab pressing against the push is the blocker.
        Assert.Equal([Slab], mover.BlockedBy);
    }

    [Fact]
    public void A_turn_whose_shadow_was_held_back_gives_back_its_turn()
    {
        var world = new PushBoxWorld();
        world.Add(Door, Vector.Zero, new Vector(-4f, -32f, 0f), new Vector(4f, 32f, 96f));
        var engine = new RecordingEngine { Strict = false };
        var directory = new EntityDirectory(new RecordingLog());
        var mover = new Mover();
        directory.Bind(Door, mover);
        world.Install(engine);
        engine.Returns("Entities.GetSolidType", SolidType.VPhysics)
              .Returns("Entities.GetParent", EntityHandle.None)
              .Returns("EntityPhysics.PhysicsObject", Shadow);
        var motion = new EntityMotion(new RecordingLog(), engine, engine, engine, engine, engine, directory,
                                      new PhysicsPushedEntities(world, new RecordingLog()))
        {
            Parenting = new Hierarchy(world),
        };
        var objects = new FakeSolverObjects();
        objects.Owned(CrateObject, Crate);
        var pusher = new Pusher { Touching = CrateObject };
        var reconcile = new PusherReconcile(motion, engine, engine, pusher, objects);

        motion.MoveToAngles(Door, new QAngle(0f, 90f, 0f), 90f);
        motion.RunFrame(Dt);
        float turned = world[Door].Angles.Yaw;
        Assert.True(turned > 1f, $"the door turned {turned}");

        pusher.State = new ShadowState(Vector.Zero, QAngle.Zero, Vector.Zero, world[Door].Angles);
        reconcile.Updated(Door);

        Assert.Equal(0f, world[Door].Angles.Yaw, 2);
        Assert.Equal([Crate], mover.BlockedBy);
    }
}

