using System;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A mover pushes what is in its way — <c>CPhysicsPushedEntities</c>
/// (physics_main.cpp), driven by <see cref="EntityMotion"/> one frame at a time.
///
/// <para>
/// Each fact is a stock behaviour with stock's numbers, run through the real
/// integrator over a world of boxes (<see cref="PushBoxWorld"/>) whose sweep,
/// like the ABI's, does not hide the pusher. The expected positions are
/// computed here from the geometry, not read back from the code under test.
/// </para>
/// </summary>
public class PhysicsPushedEntitiesTests
{
    private static readonly EntityHandle Mover = new(42);
    private static readonly EntityHandle Player = new(7);
    private static readonly EntityHandle Wall = new(9);
    private static readonly EntityHandle Prop = new(11);

    private sealed class AMover : ScriptedEntity
    {
        public readonly List<EntityHandle> BlockedBy = new();
        public bool Unblockable;
        public override void OnBlocked(EntityHandle other) => BlockedBy.Add(other);
        public override bool UnblockableByPlayer => Unblockable;
    }

    private sealed class Rig
    {
        public required EntityMotion Motion;
        public required PushBoxWorld World;
        public required AMover Class;
    }

    private static Rig ARig(PushBoxWorld world)
    {
        var engine = new RecordingEngine { Strict = false };
        var directory = new EntityDirectory(new RecordingLog());
        var mover = new AMover();

        directory.Bind(Mover, mover);
        world.Install(engine);

        var motion = new EntityMotion(new RecordingLog(), engine, engine, engine, engine, directory,
                                      new PhysicsPushedEntities(world, new RecordingLog()))
        {
            Parenting = new Hierarchy(world),
        };

        return new Rig { Motion = motion, World = world, Class = mover };
    }

    /// <summary>A door 8 thick and 64 wide, its front face at x = 4.</summary>
    private static PushBoxWorld ADoorAndAPlayerInFrontOfIt(float playerX)
    {
        var world = new PushBoxWorld();
        world.Add(Mover, Vector.Zero, new Vector(-4f, -32f, 0f), new Vector(4f, 32f, 96f));
        world.AddPlayer(Player, new Vector(playerX, 0f, 0f), ground: 0);
        return world;
    }

    //=========================================================================
    // Linear.
    //=========================================================================

    /// <summary>
    /// A sliding door moving into a standing player pushes them by the door's
    /// own step and is not blocked. Stock: the player is in the push list
    /// because the door's final box intersects them, the sweep goes the whole
    /// way, and a linear push that went the whole way is done.
    /// </summary>
    [Fact]
    public void ALinearDoorPushesAStandingPlayerByItsStepAndIsNotBlocked()
    {
        // The player's front face is one unit from the door's; a step of 1.5
        // (100 units a second over 15 ms) overlaps them by half a unit.
        Rig rig = ARig(ADoorAndAPlayerInFrontOfIt(21f));

        rig.Motion.MoveToPosition(Mover, new Vector(64f, 0f, 0f), 100f);
        rig.Motion.RunFrame(0.015f);

        float step = rig.World[Mover].Origin.X;

        Assert.Equal(1.5f, step, 3);
        Assert.Empty(rig.Class.BlockedBy);
        Assert.Equal(21f + step, rig.World[Player].Origin.X, 4);
        Assert.Equal(0f, rig.World[Player].Origin.Y);
    }

    /// <summary>
    /// The same player with a wall at their back: the sweep cannot move them,
    /// the place they are left is inside the door, so the push fails — the door
    /// is put back, nobody moves, and the door's Blocked hears who.
    /// </summary>
    [Fact]
    public void APlayerPushedIntoAWallBlocksTheDoorAndNothingMoves()
    {
        PushBoxWorld world = ADoorAndAPlayerInFrontOfIt(21f);
        world.Add(Wall, new Vector(37f, 0f, 0f), new Vector(0f, -64f, 0f), new Vector(16f, 64f, 128f));
        Rig rig = ARig(world);

        rig.Motion.MoveToPosition(Mover, new Vector(64f, 0f, 0f), 100f);
        rig.Motion.RunFrame(0.015f);

        Assert.Equal(new[] { Player }, rig.Class.BlockedBy);
        Assert.Equal(Player, rig.Motion.LastBlocker);
        Assert.Equal(0f, rig.World[Mover].Origin.X);
        Assert.Equal(21f, rig.World[Player].Origin.X);
        Assert.Equal(0, rig.Motion.PushedLastFrame);

        // And it is STILL TRAVELLING: a door held by a player who then steps
        // aside must go on by itself.
        Assert.True(rig.Motion.IsPushing(Mover), "a blocked door stopped travelling");
    }

    /// <summary>
    /// FL_UNBLOCKABLE_BY_PLAYER: the same player against the same wall does
    /// NOT block an unblockable mover. Stock moves them where the push says,
    /// tries half a unit each way along the mover's forward and left, and when
    /// none of that fits leaves them at the push's destination ("Ignoring
    /// player blocking train").
    /// </summary>
    [Fact]
    public void AnUnblockableMoverPushesAPlayerWhereThePushSaysEvenIntoAWall()
    {
        PushBoxWorld world = ADoorAndAPlayerInFrontOfIt(21f);
        world.Add(Wall, new Vector(37f, 0f, 0f), new Vector(0f, -64f, 0f), new Vector(16f, 64f, 128f));
        Rig rig = ARig(world);
        rig.Class.Unblockable = true;

        rig.Motion.MoveToPosition(Mover, new Vector(64f, 0f, 0f), 100f);
        rig.Motion.RunFrame(0.015f);

        float step = rig.World[Mover].Origin.X;

        Assert.Equal(1.5f, step, 3);
        Assert.Empty(rig.Class.BlockedBy);
        Assert.Equal(21f + step, rig.World[Player].Origin.X, 4);
        Assert.Equal(0f, rig.World[Player].Origin.Y);
    }

    /// <summary>
    /// A physics prop in the door's way is NOT pushed by the door: stock's
    /// enumerator refuses MOVETYPE_VPHYSICS, because the door's shadow moves it
    /// through the solver. The door goes on, and does not call it a blocker.
    /// </summary>
    [Fact]
    public void APhysicsPropInTheWayIsNotPushedByTheDoor()
    {
        var world = new PushBoxWorld();
        world.Add(Mover, Vector.Zero, new Vector(-4f, -32f, 0f), new Vector(4f, 32f, 96f));
        PushBoxWorld.Box prop = world.Add(Prop, new Vector(13f, 0f, 0f), new Vector(-8f, -8f, 0f),
                                          new Vector(8f, 8f, 16f));
        prop.MoveType = MoveType.VPhysics;
        prop.State = EntityState.Solid | EntityState.Physics;
        Rig rig = ARig(world);

        rig.Motion.MoveToPosition(Mover, new Vector(64f, 0f, 0f), 100f);
        rig.Motion.RunFrame(0.015f);

        Assert.Equal(1.5f, rig.World[Mover].Origin.X, 3);
        Assert.Equal(13f, rig.World[Prop].Origin.X);
        Assert.DoesNotContain(rig.World.OriginWrites, w => w.Entity == Prop);
        Assert.Empty(rig.Class.BlockedBy);
    }

    /// <summary>
    /// A PARENTED plate coming down on a player standing on the floor is
    /// blocked by them, and does not go through them. The numbers are the
    /// live probe's (crush2 on dm_lockdown): a 66x114x4 plate at world
    /// (-2880, 2894, 77.1) moving down at 40, whose parent frame puts its
    /// LOCAL origin 8192 units away in x. The push is a world question, so it
    /// must be asked where the plate is in the world, not in its parent's frame.
    /// </summary>
    [Fact]
    public void AParentedPlateComingDownOnAPlayerIsBlockedByThem()
    {
        var parent = new EntityHandle(13);
        var world = new PushBoxWorld();
        world.Add(parent, new Vector(-8192f, 0f, 4f), Vector.Zero, Vector.Zero).State = EntityState.None;
        world.Add(Mover, new Vector(-2880f, 2894f, 77.1f), new Vector(-33f, -57f, -2f), new Vector(33f, 57f, 2f));
        world.Add(Wall, new Vector(-2880f, 2894f, -0.13f), new Vector(-256f, -256f, -16f), new Vector(256f, 256f, 0f));
        world.AddPlayer(Player, new Vector(-2880f, 2894f, -0.13f), ground: 0);
        Rig rig = ARig(world);

        Assert.True(rig.Motion.Parenting!.SetParent(Mover, parent).Granted, "the plate was not parented");

        rig.Motion.MoveToPosition(Mover, new Vector(-2880f, 2894f, 17.1f), 40f);

        for (int i = 0; i < 20; i++)
            rig.Motion.RunFrame(0.015f);

        Assert.Contains(Player, rig.Class.BlockedBy);
        Assert.Equal(-0.13f, rig.World[Player].Origin.Z);
        Assert.Equal(-2880f, rig.World[Mover].Origin.X, 3);
        Assert.True(rig.World[Mover].Origin.Z - 2f >= -0.13f + 72f - 0.01f,
                    $"the plate went into the player: its bottom is at {rig.World[Mover].Origin.Z - 2f}");
    }

    /// <summary>
    /// cs_sv_movers_pushed says why a candidate was not pushed — the question a
    /// door going through somebody leaves with no other answer.
    /// </summary>
    [Fact]
    public void TheTraceSaysWhyACandidateWasNotPushed()
    {
        var world = new PushBoxWorld();
        world.Add(Mover, Vector.Zero, new Vector(-4f, -32f, 0f), new Vector(4f, 32f, 96f));
        PushBoxWorld.Box prop = world.Add(Prop, new Vector(13f, 0f, 0f), new Vector(-8f, -8f, 0f),
                                          new Vector(8f, 8f, 16f));
        prop.MoveType = MoveType.VPhysics;
        var log = new RecordingLog();
        var pushed = new PhysicsPushedEntities(world, log) { Trace = true };

        pushed.BeginLinear(Mover, null, Vector.Zero, new Vector(1.5f, 0f, 0f), QAngle.Zero, false);

        Assert.Contains($"#{Prop.Value} not pushed: movetype VPhysics", log.Text);
    }

    //=========================================================================
    // Riders.
    //=========================================================================

    /// <summary>A lift 128 square, its top face at z = 0, with a player standing on it.</summary>
    private static PushBoxWorld ALiftWithARider()
    {
        var world = new PushBoxWorld();
        world.Add(Mover, Vector.Zero, new Vector(-64f, -64f, -8f), new Vector(64f, 64f, 0f));
        world.AddPlayer(Player, Vector.Zero, ground: (int)(Mover.Value & 0x7FF));
        return world;
    }

    /// <summary>A rising lift lifts the player standing on it by its step.</summary>
    [Fact]
    public void ARisingLiftLiftsItsRiderByItsStep()
    {
        Rig rig = ARig(ALiftWithARider());

        rig.Motion.MoveToPosition(Mover, new Vector(0f, 0f, 96f), 100f);
        rig.Motion.RunFrame(0.015f);

        float step = rig.World[Mover].Origin.Z;

        Assert.Equal(1.5f, step, 3);
        Assert.Equal(step, rig.World[Player].Origin.Z, 4);
        Assert.Empty(rig.Class.BlockedBy);
    }

    /// <summary>
    /// A DESCENDING lift takes its rider down with it: stock's list holds
    /// whoever stands on the pusher, and pushes them by its step whichever way
    /// it goes. They are not left a step above the floor for gravity.
    /// </summary>
    [Fact]
    public void ADescendingLiftCarriesItsRiderDown()
    {
        Rig rig = ARig(ALiftWithARider());

        rig.Motion.MoveToPosition(Mover, new Vector(0f, 0f, -96f), 100f);
        rig.Motion.RunFrame(0.015f);

        float step = rig.World[Mover].Origin.Z;

        Assert.Equal(-1.5f, step, 3);
        Assert.Equal(step, rig.World[Player].Origin.Z, 3);
        Assert.Empty(rig.Class.BlockedBy);
    }

    /// <summary>
    /// A rider with a ceiling over their head blocks a rising lift: the sweep
    /// cannot lift them, they would be inside the lift, so the lift stays and
    /// its Blocked hears who — which is what lets a crusher crush. It does not
    /// rise past them.
    /// </summary>
    [Fact]
    public void ARiderPinnedUnderACeilingBlocksTheRisingLift()
    {
        PushBoxWorld world = ALiftWithARider();
        world.Add(Wall, new Vector(0f, 0f, 72f), new Vector(-64f, -64f, 0f), new Vector(64f, 64f, 8f));
        Rig rig = ARig(world);

        rig.Motion.MoveToPosition(Mover, new Vector(0f, 0f, 96f), 100f);
        rig.Motion.RunFrame(0.015f);

        Assert.Equal(new[] { Player }, rig.Class.BlockedBy);
        Assert.Equal(0f, rig.World[Mover].Origin.Z);
        Assert.Equal(0f, rig.World[Player].Origin.Z);
    }

    //=========================================================================
    // Rotation.
    //=========================================================================

    /// <summary>
    /// A door 64 long and 4 thick, hinged at the origin and lying along -X, and
    /// a player standing half a unit off its -Y face. Turning +yaw swings the
    /// free end toward -Y, into them.
    /// </summary>
    private static PushBoxWorld ASwingingDoorAndAPlayer(SolidType doorSolid)
    {
        var world = new PushBoxWorld();
        world.Add(Mover, Vector.Zero, new Vector(-64f, -2f, 0f), new Vector(0f, 2f, 96f)).Solid = doorSolid;
        world.AddPlayer(Player, new Vector(-48f, -18.5f, 0f), ground: 0);
        return world;
    }

    /// <summary>Where a point goes when it turns about Z through the origin.</summary>
    private static Vector Turned(Vector p, float degrees)
    {
        double r = degrees * Math.PI / 180.0;
        return new Vector((float)(p.X * Math.Cos(r) - p.Y * Math.Sin(r)),
                          (float)(p.X * Math.Sin(r) + p.Y * Math.Cos(r)), p.Z);
    }

    /// <summary>
    /// A rotating door sweeping a player moves them along stock's rotational
    /// push direction: where the player's collision origin goes when it turns
    /// rigidly with the door (ComputeRotationalPushDirection). The door is not
    /// blocked, and — its root not being SOLID_VPHYSICS — the player is not
    /// marked for the shadow's stuck check.
    /// </summary>
    [Fact]
    public void ARotatingDoorPushesAPlayerAlongTheRotationalPushDirection()
    {
        Rig rig = ARig(ASwingingDoorAndAPlayer(SolidType.Bsp));

        rig.Motion.MoveToAngles(Mover, new QAngle(0f, 90f, 0f), 90f);
        rig.Motion.RunFrame(0.015f);

        float yaw = rig.World[Mover].Angles.Yaw;
        Vector expected = Turned(new Vector(-48f, -18.5f, 0f), yaw);

        Assert.Equal(1.35f, yaw, 3);
        Assert.Empty(rig.Class.BlockedBy);
        Assert.Equal(expected.X, rig.World[Player].Origin.X, 3);
        Assert.Equal(expected.Y, rig.World[Player].Origin.Y, 3);
        Assert.Empty(rig.World.RotPushed);
    }

    /// <summary>
    /// The SOLID_VPHYSICS form of the same push: stock guesses the contact from
    /// a corner of the player's box (the mins corner, with no previous move to
    /// steer it), pushes by where THAT corner goes, and sets
    /// PFLAG_GAMEPHYSICS_ROTPUSH on the player.
    /// </summary>
    [Fact]
    public void AVPhysicsRotatingDoorPushesByTheCornerAndMarksThePlayer()
    {
        Rig rig = ARig(ASwingingDoorAndAPlayer(SolidType.VPhysics));

        rig.Motion.MoveToAngles(Mover, new QAngle(0f, 90f, 0f), 90f);
        rig.Motion.RunFrame(0.015f);

        float yaw = rig.World[Mover].Angles.Yaw;
        var corner = new Vector(-64f, -34.5f, 0f);
        Vector push = Turned(corner, yaw) - corner;

        Assert.Empty(rig.Class.BlockedBy);
        Assert.Equal(-48f + push.X, rig.World[Player].Origin.X, 3);
        Assert.Equal(-18.5f + push.Y, rig.World[Player].Origin.Y, 3);
        Assert.Equal(new[] { Player }, rig.World.RotPushed);
    }

    /// <summary>
    /// A rotating door that would push a player into a wall is blocked: it
    /// turns back to where it was, the player stays, and Blocked hears who.
    /// </summary>
    [Fact]
    public void ARotatingDoorPushingAPlayerIntoAWallIsBlocked()
    {
        PushBoxWorld world = ASwingingDoorAndAPlayer(SolidType.Bsp);
        world.Add(Wall, new Vector(-48f, -34.5f, 0f), new Vector(-64f, -16f, 0f), new Vector(64f, 0f, 128f));
        Rig rig = ARig(world);

        rig.Motion.MoveToAngles(Mover, new QAngle(0f, 90f, 0f), 90f);
        rig.Motion.RunFrame(0.015f);

        Assert.Equal(new[] { Player }, rig.Class.BlockedBy);
        Assert.Equal(0f, rig.World[Mover].Angles.Yaw);
        Assert.Equal(-48f, rig.World[Player].Origin.X);
        Assert.Equal(-18.5f, rig.World[Player].Origin.Y);
    }

    //=========================================================================
    // The intersection rule.
    //=========================================================================

    /// <summary>Two boxes sharing a face touch; they do not intersect.</summary>
    [Fact]
    public void BoxesThatOnlyTouchDoNotIntersect()
    {
        Assert.False(PhysicsPushedEntities.ObbIntersectsAabb(
            new Vector(-4f, -32f, 0f), new Vector(4f, 32f, 96f), Transform.Identity,
            new Vector(4f, -16f, 0f), new Vector(36f, 16f, 72f)));
    }

    /// <summary>Half a unit of overlap is an intersection.</summary>
    [Fact]
    public void HalfAUnitOfOverlapIntersects()
    {
        Assert.True(PhysicsPushedEntities.ObbIntersectsAabb(
            new Vector(-4f, -32f, 0f), new Vector(4f, 32f, 96f), Transform.Identity,
            new Vector(3.5f, -16f, 0f), new Vector(35.5f, 16f, 72f)));
    }

    /// <summary>
    /// A box turned 45 degrees whose AABB covers a corner it does not: the
    /// oriented test separates them where an AABB test would not.
    /// </summary>
    [Fact]
    public void ATurnedBoxDoesNotIntersectWhatOnlyItsAabbCovers()
    {
        // A 20x20 box turned 45 degrees reaches 14.1 along each axis; a box at
        // (11..13, 11..13) is inside that AABB and outside the diamond.
        Transform turned = Transform.FromOriginAngles(Vector.Zero, new QAngle(0f, 45f, 0f));

        Assert.False(PhysicsPushedEntities.ObbIntersectsAabb(
            new Vector(-10f, -10f, -10f), new Vector(10f, 10f, 10f), turned,
            new Vector(11f, 11f, -1f), new Vector(13f, 13f, 1f)));
    }

    //=========================================================================
    // The engine half.
    //=========================================================================

    /// <summary>
    /// The fraction the ABI's sweep does not carry is how far along the way it
    /// got; all-solid is none of it, and a sweep of no length all of it.
    /// </summary>
    [Fact]
    public void TheSweepsFractionIsHowFarAlongItGot()
    {
        Assert.Equal(0.25f, EnginePushWorld.FractionOf(Vector.Zero, new Vector(4f, 0f, 0f),
                                                       new Vector(1f, 0f, 0f), allSolid: false));
        Assert.Equal(0f, EnginePushWorld.FractionOf(Vector.Zero, new Vector(4f, 0f, 0f),
                                                    new Vector(4f, 0f, 0f), allSolid: true));
        Assert.Equal(1f, EnginePushWorld.FractionOf(new Vector(2f, 2f, 2f), new Vector(2f, 2f, 2f),
                                                    new Vector(2f, 2f, 2f), allSolid: false));
    }

    /// <summary>
    /// The box the ABI cannot ask is asked as its sphere and cut back to the
    /// box: something the sphere reaches past the box's corner is not in it.
    /// </summary>
    [Fact]
    public void TheBoxQueryKeepsOnlyWhatIsInTheBox()
    {
        var inside = new EntityHandle(3);
        var pastTheCorner = new EntityHandle(4);
        var engine = new RecordingEngine();

        engine.Returns("WorldQuery.EntitiesInSphere.into", new[] { inside, pastTheCorner })
              .ReturnsEachCall("Entities.GetOrigin", () =>
                  (EntityHandle)engine.Calls[^1].Args[0]! == inside
                      ? new Vector(5f, 5f, 5f)
                      : new Vector(12f, 12f, 12f))
              .Returns("Entities.GetBounds", (new Vector(-1f, -1f, -1f), new Vector(1f, 1f, 1f)));

        var world = new EnginePushWorld(engine, engine, engine, new EntityDirectory(new RecordingLog()));
        Span<EntityHandle> into = stackalloc EntityHandle[8];

        int count = world.EntitiesInBox(Vector.Zero, new Vector(10f, 10f, 10f), into);

        Assert.Equal(1, count);
        Assert.Equal(inside, into[0]);
    }
}
