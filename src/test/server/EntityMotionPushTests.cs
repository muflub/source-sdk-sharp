using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>MOVETYPE_PUSH</c> in managed code — <c>plan_entity.md</c> step 2g.
///
/// <para>
/// The bit-exact agreement with the native stepper is <see cref="MoverFixtureTests"/>'s
/// job; what is here is everything a recording of one map cannot reach. Three
/// kinds of thing:
/// </para>
///
/// <list type="bullet">
/// <item><description>
/// <b>Boundaries the recording does not contain.</b> Mutating the clamp from
/// <c>&gt;=</c> to <c>&gt;</c>, and the rotation's arrival from <c>&lt;=</c> to
/// <c>&lt;</c>, left every fixture fact green: 1690 recorded frames contain not
/// one where the step is EXACTLY what is left. Those are constructed here.
/// </description></item>
/// <item><description>
/// <b>What the integrator does around the arithmetic</b> — the blocked frame
/// that commits nothing and asks the class, the arrival that is reported even
/// when there was no movement, and the carry, which is the thing the native
/// stepper never did at all.
/// </description></item>
/// <item><description>
/// <b>That the native stepper is gone</b>, read out of the C++ with its
/// comments stripped — this file's own prose names every symbol it is asserting
/// the absence of, and a needle that survives in a comment is how a count stays
/// green across the deletion it existed to notice.
/// </description></item>
/// </list>
/// </summary>
public class EntityMotionPushTests
{
    //=========================================================================
    // The boundaries 1690 recorded frames do not contain.
    //=========================================================================

    /// <summary>
    /// A step exactly as long as what is left ARRIVES.
    ///
    /// <para>
    /// Stock's clamp is <c>flStep &gt;= flRemaining</c>. Changed to <c>&gt;</c>
    /// the whole fixture replay stays green, because no frame of a live
    /// <c>dm_lockdown</c> happens to land on the boundary — so the case is
    /// constructed, from the mover's own idea of the distance.
    /// </para>
    ///
    /// <para>
    /// <b>Measured with <see cref="MoveVec.Normalize"/> and not with
    /// <c>Length</c>.</b> The distance the clamp compares against is the one the
    /// rsqrt approximation reconstructs, which is not the length; a test that
    /// used the accurate distance would be one ulp off the boundary it means to
    /// sit exactly on.
    /// </para>
    /// </summary>
    [Fact]
    public void AStepExactlyAsLongAsWhatIsLeftArrives()
    {
        var current = new Vector(0f, 0f, 0f);
        var target = new Vector(0f, 0f, 96f);

        Vector direction = target - current;
        float remaining = MoveVec.Normalize(ref direction);

        // Speed times a frametime of one is the step, exactly.
        LinearStep step = MoverMath.AdvanceLinear(current, target, remaining, 1f);

        Assert.True(step.Arrived, "a step exactly as long as what is left did not arrive");
        Assert.Equal(target.Z, step.Origin.Z);
    }

    /// <summary>And one ulp short of it does not, which is the other side of the same clamp.</summary>
    [Fact]
    public void AStepOneUlpShortOfWhatIsLeftDoesNotArrive()
    {
        var current = new Vector(0f, 0f, 0f);
        var target = new Vector(0f, 0f, 96f);

        Vector direction = target - current;
        float remaining = MoveVec.Normalize(ref direction);

        LinearStep step = MoverMath.AdvanceLinear(current, target,
                                                  MathF.BitDecrement(remaining), 1f);

        Assert.False(step.Arrived, "a step short of what is left reported arriving");
        Assert.True(step.Origin.Z < target.Z);
    }

    /// <summary>
    /// A rotation whose remaining angle is EXACTLY the step arrives.
    ///
    /// <para>
    /// Stock is <c>fabsf(flRemaining) &lt;= flStep</c>. With <c>&lt;</c> the
    /// axis lands on the same number by the other branch — <c>current + step</c>
    /// IS the target when they are equal — so only the ARRIVAL differs, and
    /// only on the boundary. A door that never reports arriving never fires
    /// OnFullyOpen and never opens again.
    /// </para>
    /// </summary>
    [Fact]
    public void ARotationWhoseRemainderIsExactlyTheStepArrives()
    {
        AngularStep step = MoverMath.AdvanceAngular(QAngle.Zero, new QAngle(0f, 90f, 0f),
                                                    90f, 1f);

        Assert.True(step.Arrived, "a rotation exactly one step from its target did not arrive");
        Assert.Equal(90f, step.Angles.Yaw);
    }

    /// <summary>And one degree short of it does not.</summary>
    [Fact]
    public void ARotationShortOfItsTargetDoesNotArrive()
    {
        AngularStep step = MoverMath.AdvanceAngular(QAngle.Zero, new QAngle(0f, 90f, 0f),
                                                    89f, 1f);

        Assert.False(step.Arrived);
        Assert.Equal(89f, step.Angles.Yaw);
    }

    /// <summary>
    /// Every axis must arrive before the rotation has. A platform told to turn
    /// about two axes at once is not there when one of them is.
    /// </summary>
    [Fact]
    public void ARotationIsNotArrivedUntilEveryAxisIs()
    {
        AngularStep step = MoverMath.AdvanceAngular(QAngle.Zero, new QAngle(45f, 90f, 0f),
                                                    45f, 1f);

        Assert.False(step.Arrived, "pitch arrived and the rotation reported itself finished");
        Assert.Equal(45f, step.Angles.Pitch);
        Assert.Equal(45f, step.Angles.Yaw);
    }

    /// <summary>
    /// <c>AngleDiff</c> takes the short way round, and the sign says which way.
    /// </summary>
    [Fact]
    public void AngleDiffTakesTheShortWayRound()
    {
        Assert.Equal(20f, MoverMath.AngleDiff(10f, 350f));
        Assert.Equal(-20f, MoverMath.AngleDiff(350f, 10f));
        Assert.Equal(0f, MoverMath.AngleDiff(90f, 90f));
    }

    /// <summary>
    /// <c>AngleDiff</c>'s asymmetric fix-up is arithmetically INERT, and this
    /// says so rather than leaving the next reader to "simplify" it.
    ///
    /// <para>
    /// Stock branches on <c>destAngle &gt; srcAngle</c> and then tests ONE side
    /// of the remainder; the obvious spelling tests both sides of the remainder
    /// and branches on neither angle. Replacing one with the other leaves every
    /// fixture fact green — which looks like a hole in the recording and is not.
    /// <c>fmodf</c>'s sign follows the DIVIDEND, so <c>dest &gt; src</c> gives a
    /// remainder in <c>[0, 360)</c> and <c>dest &lt;= src</c> one in
    /// <c>(-360, 0]</c>; neither can ever reach the clause the other arm owns.
    /// The two functions agree everywhere, and stock's spelling is kept because
    /// it is stock's.
    /// </para>
    /// </summary>
    [Fact]
    public void TheAsymmetricFixUpAgreesWithTheObviousSpellingEverywhere()
    {
        static float Obvious(float dest, float src)
        {
            float delta = (dest - src) % 360f;

            if (delta >= 180f)
                delta -= 360f;
            else if (delta <= -180f)
                delta += 360f;

            return delta;
        }

        int compared = 0;

        for (float dest = -720f; dest <= 720f; dest += 7.5f)
        {
            for (float src = -720f; src <= 720f; src += 7.5f)
            {
                Assert.Equal(Obvious(dest, src), MoverMath.AngleDiff(dest, src));
                compared++;
            }
        }

        Assert.True(compared > 30000, $"only {compared} pairs were compared");
    }

    /// <summary>
    /// A travel with nowhere to go arrives instead of dividing by nothing.
    ///
    /// <para>
    /// <see cref="MoveVec.Normalize"/>'s <c>1e-10f</c> keeps the reciprocal
    /// finite, so a zero-length travel answers a remaining distance of about
    /// <c>1e-5</c> rather than zero — and any speed at all steps past it.
    /// </para>
    /// </summary>
    [Fact]
    public void ATravelWithNowhereToGoArrives()
    {
        LinearStep step = MoverMath.AdvanceLinear(new Vector(1f, 2f, 3f),
                                                  new Vector(1f, 2f, 3f), 100f, 0.015f);

        Assert.True(step.Arrived);
        Assert.Equal(3f, step.Origin.Z);
    }

    /// <summary>
    /// A frame of no time is a paused server, not a mover going infinitely
    /// fast. The one arm of the velocity rule no live recording can reach —
    /// <c>gpGlobals-&gt;frametime</c> is never zero on a running server, which
    /// is exactly why it is a fact here and named as absent in the fixture's
    /// README.
    /// </summary>
    [Fact]
    public void AFrameOfNoTimeReportsNoVelocityRatherThanDividing()
    {
        Vector velocity = MoverMath.Velocity(Vector.Zero, new Vector(0f, 0f, 10f), 0f);

        Assert.Equal(0f, velocity.Z);
        Assert.Equal(0f, MoverMath.Velocity(Vector.Zero, new Vector(0f, 0f, 10f), -1f).Z);
    }

    //=========================================================================
    // The rider rule, and the parameter the native one did not have.
    //=========================================================================

    /// <summary>
    /// Somebody standing on the top face is a rider and not a blocker — the
    /// exemption the whole rule exists for. Without it a lift with a passenger
    /// reports itself blocked by its own passenger and never rises.
    /// </summary>
    [Fact]
    public void SomebodyStandingOnTheTopFaceIsNotABlocker()
    {
        Assert.True(MoverMath.IsRider(feetZ: 25f, currentTopZ: 25f));
        Assert.False(MoverMath.Blocks(isPlayer: true, overlapsDestination: true,
                                      feetZ: 25f, currentTopZ: 25f));
    }

    /// <summary>
    /// And one whole step inside it is still a rider. On a RISING mover the
    /// brush moves first and the carry puts the feet back on the face
    /// afterwards, so a legitimate passenger is inside the brush every tick.
    /// </summary>
    [Fact]
    public void SomebodyOneStepInsideTheTopFaceIsStillARider()
    {
        Assert.True(MoverMath.IsRider(feetZ: 25f - MoverMath.RiderSink, currentTopZ: 25f));
        Assert.False(MoverMath.IsRider(feetZ: MathF.BitDecrement(25f - MoverMath.RiderSink),
                                       currentTopZ: 25f));
    }

    /// <summary>
    /// Somebody standing in front of a door blocks it. Their feet are below the
    /// brush's top and no tolerance reaches them.
    /// </summary>
    [Fact]
    public void SomebodyStandingInTheWayBlocks()
    {
        Assert.True(MoverMath.Blocks(isPlayer: true, overlapsDestination: true,
                                     feetZ: 8f, currentTopZ: 25f));
    }

    /// <summary>
    /// A door does not stop for a dropped weapon, and stock does not either —
    /// <c>FL_CLIENT|FL_NPC</c> is the mask <c>CBaseToggle</c> checks, and the
    /// NPC half is absent because nothing in this mod is one.
    /// </summary>
    [Fact]
    public void SomethingThatIsNotAPersonNeverBlocks()
    {
        Assert.False(MoverMath.Blocks(isPlayer: false, overlapsDestination: true,
                                      feetZ: 8f, currentTopZ: 25f));
    }

    /// <summary>
    /// <b>The parameter the native rule did not have.</b> Native was fed by a
    /// partition walk over the destination BOX, so overlap was decided before
    /// the rule was consulted. The managed walk is a SPHERE — the box's own
    /// circumscribed sphere, because there is no box query in the ABI — and a
    /// sphere around a 66×114×4 plate reaches four times as far as the plate
    /// does. Without this a train would stop for somebody standing beside the
    /// track.
    /// </summary>
    [Fact]
    public void SomebodyInsideTheSphereButOutsideTheBoxDoesNotBlock()
    {
        Assert.False(MoverMath.Blocks(isPlayer: true, overlapsDestination: false,
                                      feetZ: 8f, currentTopZ: 25f));
    }

    /// <summary>Touching faces overlap: a rider resting exactly on one is the commonest candidate there is.</summary>
    [Fact]
    public void TwoBoxesThatShareAFaceOverlap()
    {
        Assert.True(MoverMath.BoxesOverlap(new Vector(0f, 0f, 0f), new Vector(10f, 10f, 10f),
                                           new Vector(10f, 0f, 0f), new Vector(20f, 10f, 10f)));
    }

    /// <summary>And a box a hair clear of another does not.</summary>
    [Fact]
    public void TwoBoxesThatDoNotTouchDoNotOverlap()
    {
        Assert.False(MoverMath.BoxesOverlap(new Vector(0f, 0f, 0f), new Vector(10f, 10f, 10f),
                                            new Vector(MathF.BitIncrement(10f), 0f, 0f),
                                            new Vector(20f, 10f, 10f)));
    }

    //=========================================================================
    // The integrator around the arithmetic.
    //=========================================================================

    private static readonly EntityHandle TheMover = new(42);

    /// <summary>A shell that records what the integrator told it.</summary>
    private sealed class AMoverClass : ScriptedEntity
    {
        public int MoveDone;
        public readonly List<EntityHandle> BlockedBy = new();

        public override void OnMoveDone() => MoveDone++;
        public override void OnBlocked(EntityHandle other) => BlockedBy.Add(other);
    }

    /// <summary>
    /// The four things <see cref="Hierarchy"/> is asked in this pass, and
    /// nothing else. Everything here is unparented, so the only member the
    /// integrator reaches is the write — which is what makes "the step was
    /// committed" observable at all.
    /// </summary>
    private sealed class ARecordingWorld : IHierarchyWorld
    {
        public readonly List<(EntityHandle Child, Vector Origin, QAngle Angles)> Written = new();

        public void WriteComposed(EntityHandle child, Vector origin, QAngle angles,
                                  Transform childToWorld)
            => Written.Add((child, origin, angles));

        public bool IsStandalone(EntityHandle handle) => true;
        public bool IsBase(EntityHandle handle) => false;
        public bool CanPose(EntityHandle handle) => false;
        public bool TryGetEntityFrame(EntityHandle handle, out Transform frame)
        {
            frame = Transform.Identity;
            return false;
        }
        public bool TryGetAttachmentFrame(EntityHandle handle, int attachment, out Transform frame)
        {
            frame = Transform.Identity;
            return false;
        }
        public int LookupAttachment(EntityHandle handle, string attachmentName) => -1;
        public bool TryGetAbsolute(EntityHandle handle, out Vector origin, out QAngle angles)
        {
            origin = Vector.Zero;
            angles = QAngle.Zero;
            return false;
        }
        public EntityHandle FindStandaloneByName(string name) => EntityHandle.None;
        public EntityHandle FindBaseByName(string name) => EntityHandle.None;
        public string NameOf(EntityHandle handle) => "test";
        public void Remove(EntityHandle handle) { }
        public void DetachPhysics(EntityHandle child) { }
        public void Warn(string message) { }
    }

    /// <summary>
    /// A mover 66 x 114 x 64 — the gate's own plate made TALL, so that the one
    /// bounds the double answers for every entity puts a candidate's feet well
    /// below the mover's top and the rider exemption does not swallow the
    /// blocked case by accident.
    /// </summary>
    private static readonly (Vector Mins, Vector Maxs) TallBox =
        (new Vector(-33f, -57f, 0f), new Vector(33f, 57f, 64f));

    private static (EntityMotion motion, RecordingEngine engine, AMoverClass mover,
                    ARecordingWorld world)
        APushPass(Vector origin, params (string member, object? value)[] extra)
    {
        var engine = new RecordingEngine { Strict = false };
        var directory = new EntityDirectory(new RecordingLog());
        var mover = new AMoverClass();
        var world = new ARecordingWorld();

        directory.Bind(TheMover, mover);

        engine.Returns("Entities.GetOrigin", origin)
              .Returns("Entities.GetAngles", Vector.Zero)
              .Returns("Entities.GetBounds", TallBox)
              .Returns("WorldQuery.EntitiesInSphere", 0)
              .Returns("Console.FindConVarValue", null);

        foreach (var (member, value) in extra)
            engine.Returns(member, value);

        var motion = new EntityMotion(new RecordingLog(), engine, engine, engine, engine, directory,
            new PhysicsPushedEntities(new EnginePushWorld(engine, engine, engine, directory), new RecordingLog()))
        {
            Parenting = new Hierarchy(world),
        };

        return (motion, engine, mover, world);
    }

    /// <summary>
    /// A mover told to go where it already is reports ARRIVING. A door told to
    /// open when it is open must still say it finished, or the class waits for
    /// ever for a MoveDone that will never come.
    /// </summary>
    [Fact]
    public void AMoverToldToGoWhereItAlreadyIsStillReportsArriving()
    {
        var (motion, _, mover, _) = APushPass(new Vector(0f, 0f, 10f));

        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 10f), 60f);

        Assert.Equal(1, mover.MoveDone);
        Assert.False(motion.IsPushing(TheMover));
    }

    /// <summary>And a speed of nothing is not a movement either.</summary>
    [Fact]
    public void AMoverGivenNoSpeedIsNotMoving()
    {
        var (motion, _, mover, _) = APushPass(Vector.Zero);

        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 96f), 0f);

        Assert.Equal(1, mover.MoveDone);
        Assert.False(motion.IsPushing(TheMover));
    }

    /// <summary>
    /// A mover with somewhere to go IS moving, and one frame of it commits a
    /// step and reports a velocity.
    /// </summary>
    [Fact]
    public void OneFrameOfATravelCommitsAStepAndReportsAVelocity()
    {
        var (motion, engine, _, world) = APushPass(Vector.Zero);

        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 96f), 60f);
        Assert.True(motion.IsPushing(TheMover));

        // The origin the engine answers does not move (the double is a
        // recorder, not a world), so the committed step is what SetLocal was
        // told rather than where the entity ended up.
        Assert.Equal(1, motion.RunFrame(0.015f));
        Assert.Equal(1, motion.PushedLastFrame);

        // 60 units a second over a 66Hz frame is 0.9 of a unit, from zero.
        Assert.Single(world.Written);
        Assert.Equal(TheMover, world.Written[0].Child);
        Assert.Equal(0.9f, world.Written[0].Origin.Z);
    }

    /// <summary>
    /// A mover told to stop stops, and clears the velocity it was reporting —
    /// not next frame, now. A rider sampled between the input and the next tick
    /// would otherwise be handed the floor speed of a mover already told to
    /// stop.
    /// </summary>
    [Fact]
    public void AMoverToldToStopClearsItsVelocityAtOnce()
    {
        var (motion, engine, _, world) = APushPass(Vector.Zero);

        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 96f), 60f);
        motion.StopMoving(TheMover);

        Assert.False(motion.IsPushing(TheMover));
        Assert.Equal(Vector.Zero.Z, motion.PushVelocityOf(TheMover).Z);
    }

    /// <summary>
    /// A spin is told to the SHELL as well as integrated. The shell's copy is
    /// what a sensor entity reads back through <c>EntityGetAngularVelocity</c>;
    /// this side holds the number it steps. One store each, and the split is
    /// the one step 2f made for gravity scale.
    /// </summary>
    [Fact]
    public void ASpinIsToldToTheShellAsWellAsIntegrated()
    {
        var (motion, engine, _, world) = APushPass(Vector.Zero);

        motion.Spin(TheMover, new QAngle(0f, 100f, 0f));

        Assert.True(motion.IsPushing(TheMover));
        Assert.Equal(1, engine.CountOf("Entities.SetAngularVelocity"));
    }

    /// <summary>
    /// Turning to a target CANCELS a spin. Spinning and turning to a target are
    /// the same machinery pointed at different things, and doing both at once
    /// means neither arrives.
    /// </summary>
    [Fact]
    public void TurningToATargetCancelsAContinuousSpin()
    {
        var (motion, engine, _, world) = APushPass(Vector.Zero);

        motion.Spin(TheMover, new QAngle(0f, 100f, 0f));
        motion.MoveToAngles(TheMover, new QAngle(0f, 90f, 0f), 45f);

        motion.RunFrame(1f);

        // One frame at 45 degrees a second is 45 degrees, not 45 plus the
        // spin's 100.
        Assert.Single(world.Written);
        Assert.Equal(45f, world.Written[0].Angles.Yaw);
    }

    /// <summary>
    /// An entity that has gone since the pass began is dropped rather than
    /// stepped. A door removed by the class its own arrival woke is the
    /// ordinary case, not a corner.
    /// </summary>
    [Fact]
    public void AMoverWhoseEntityHasGoneIsDroppedFromThePass()
    {
        var engine = new RecordingEngine { Strict = false };
        var directory = new EntityDirectory(new RecordingLog());

        engine.Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetAngles", Vector.Zero);

        var motion = new EntityMotion(new RecordingLog(), engine, engine, engine, engine, directory,
            new PhysicsPushedEntities(new EnginePushWorld(engine, engine, engine, directory), new RecordingLog()))
        {
            Parenting = new Hierarchy(new ARecordingWorld()),
        };

        // Never bound: the directory resolves nothing for it.
        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 96f), 60f);

        Assert.Equal(1, motion.PushCount);
        motion.RunFrame(0.015f);
        Assert.Equal(0, motion.PushCount);
    }

    /// <summary>A level change stops everything, both halves of it.</summary>
    [Fact]
    public void ALevelChangeForgetsEveryMover()
    {
        var (motion, _, _, _) = APushPass(Vector.Zero);

        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 96f), 60f);
        Assert.Equal(1, motion.PushCount);

        motion.Clear();

        Assert.Equal(0, motion.PushCount);
        Assert.Equal(EntityHandle.None, motion.LastBlocker);
    }

    /// <summary>
    /// <c>Forget</c> answers TRUE for something that was only ever pushing.
    ///
    /// <para>
    /// It used to be <c>_movers.Remove(entity)</c> alone, which is the flight
    /// set — so an entity that had never flown reported "there was nothing to
    /// forget" and left its push row behind, on every mover in the map.
    /// </para>
    /// </summary>
    [Fact]
    public void ForgettingAMoverThatNeverFlewStillAnswersTrue()
    {
        var (motion, _, _, _) = APushPass(Vector.Zero);

        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 96f), 60f);

        Assert.True(motion.Forget(TheMover));
        Assert.Equal(0, motion.PushCount);
        Assert.False(motion.Forget(TheMover));
    }

    /// <summary>
    /// Sixty movers cost ONE pass, not sixty of anything the frame does not
    /// already do — step 1g's rule, which this step must not undo.
    /// </summary>
    [Fact]
    public void OnePassStepsEveryMoverThereIs()
    {
        var engine = new RecordingEngine { Strict = false };
        var directory = new EntityDirectory(new RecordingLog());

        engine.Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetAngles", Vector.Zero)
              .Returns("Entities.GetBounds",
                       (new Vector(-8f, -8f, -8f), new Vector(8f, 8f, 8f)))
              .Returns("WorldQuery.EntitiesInSphere", 0);

        var motion = new EntityMotion(new RecordingLog(), engine, engine, engine, engine, directory,
            new PhysicsPushedEntities(new EnginePushWorld(engine, engine, engine, directory), new RecordingLog()))
        {
            Parenting = new Hierarchy(new ARecordingWorld()),
        };

        for (uint i = 100; i < 160; i++)
        {
            var handle = new EntityHandle(i);
            directory.Bind(handle, new AMoverClass());
            motion.MoveToPosition(handle, new Vector(0f, 0f, 96f), 60f);
        }

        Assert.Equal(60, motion.RunFrame(0.015f));
        Assert.Equal(60, motion.PushedLastFrame);
    }

    //=========================================================================
    // The carry — the thing the native stepper never did.
    //=========================================================================

    /// <summary>
    /// A pass whose double answers the position the LAST WRITE left, so the
    /// mover actually moves between <c>worldBefore</c> and <c>worldAfter</c>.
    /// The fixed-origin double every other fact here uses cannot show a carry,
    /// because a mover that never moved has no displacement to hand anybody.
    /// </summary>
    private static (EntityMotion motion, RecordingEngine engine, ARecordingWorld world)
        AMovingPass(Vector start, params (string member, object? value)[] extra)
    {
        var engine = new RecordingEngine { Strict = false };
        var directory = new EntityDirectory(new RecordingLog());
        var world = new ARecordingWorld();

        directory.Bind(TheMover, new AMoverClass());

        engine.ReturnsEachCall("Entities.GetOrigin",
                               () => world.Written.Count == 0 ? start : world.Written[^1].Origin)
              .Returns("Entities.GetAngles", Vector.Zero)
              .Returns("Entities.GetBounds", TallBox)
              .Returns("WorldQuery.EntitiesInSphere", 0)
              .Returns("Console.FindConVarValue", null);

        foreach (var (member, value) in extra)
            engine.Returns(member, value);

        var motion = new EntityMotion(new RecordingLog(), engine, engine, engine, engine, directory,
            new PhysicsPushedEntities(new EnginePushWorld(engine, engine, engine, directory), new RecordingLog()))
        {
            Parenting = new Hierarchy(world),
        };

        return (motion, engine, world);
    }

    /// <summary>
    /// A SOLID mover that moves asks what is in its way — once, the push's own
    /// enumeration (<c>GenerateBlockingEntityListAddBox</c>), which also finds
    /// whoever is riding it. There is no second walk to carry them any more:
    /// stock's push list is the carry.
    /// </summary>
    [Fact]
    public void ASolidMoverThatMovesAsksWhatIsInItsWayOnce()
    {
        var (motion, engine, world) = AMovingPass(Vector.Zero,
                                                  ("Networking.EntityGetState", EntityState.Solid));

        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 96f), 60f);
        motion.RunFrame(0.015f);

        Assert.Single(world.Written);
        Assert.Equal(1, engine.CountOf("WorldQuery.EntitiesInSphere"));
    }

    /// <summary>
    /// And one that is not solid asks nothing: stock's "don't bother if the
    /// pusher isn't solid". A spinning func_rotating made non-solid turns
    /// without a world walk per frame.
    /// </summary>
    [Fact]
    public void ANonSolidMoverAsksNothing()
    {
        var (motion, engine, world) = AMovingPass(Vector.Zero,
                                                  ("Networking.EntityGetState", EntityState.None));

        motion.Spin(TheMover, new QAngle(0f, 100f, 0f));
        motion.RunFrame(0.015f);

        Assert.Single(world.Written);
        Assert.Equal(0, engine.CountOf("WorldQuery.EntitiesInSphere"));
    }

    /// <summary>
    /// A frame of no time steps nothing at all, which is
    /// <c>UpdateMovement</c>'s own guard.
    /// </summary>
    [Fact]
    public void AFrameOfNoTimeStepsNoMover()
    {
        var (motion, _, _, _) = APushPass(Vector.Zero);

        motion.MoveToPosition(TheMover, new Vector(0f, 0f, 96f), 60f);

        Assert.Equal(0, motion.RunFrame(0f));
        Assert.Equal(0, motion.PushedLastFrame);
    }

    //=========================================================================
    // The native stepper is gone. Read with the comments stripped.
    //=========================================================================

    /// <summary>
    /// One C++ file with every comment removed.
    ///
    /// <para>
    /// Every fact below is about a symbol this port's own comments name
    /// repeatedly, in the very block that explains where it went. Matching the
    /// raw text would find those and report the deletion as incomplete — or,
    /// worse, would go on reporting the deletion as complete after somebody put
    /// the code back beside a comment saying it was gone. 67 of this tree's 75
    /// source-reading tests do not strip; this one does.
    /// </para>
    /// </summary>
    private static string CodeOf(params string[] parts)
    {
        string text = File.ReadAllText(Path.Combine(new[] { RepoLayout.SdkTree }.Concat(parts).ToArray()));

        text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", " ");

        return text;
    }

    /// <summary>The comment stripper actually strips, checked on this tree's own text.</summary>
    [Fact]
    public void TheCommentStripperStrips()
    {
        string raw = File.ReadAllText(Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp",
                                                   "game", "server", "shell_standalone_base.cpp"));
        string code = CodeOf("src", "sourcesharp", "game", "server", "shell_standalone_base.cpp");

        Assert.Contains("UpdateMovement", raw);
        Assert.DoesNotContain("UpdateMovement", code);
        Assert.Contains("SetAngularVelocity", code);
    }

    /// <summary>The stepper's five bodies are gone from the C++.</summary>
    [Fact]
    public void TheShellNoLongerSteps()
    {
        string code = CodeOf("src", "sourcesharp", "game", "server", "shell_standalone_base.cpp");

        Assert.DoesNotContain("CStandaloneEntity::UpdateMovement", code);
        Assert.DoesNotContain("CStandaloneEntity::BlockerAt", code);
        Assert.DoesNotContain("CStandaloneEntity::MoveToPosition", code);
        Assert.DoesNotContain("CStandaloneEntity::MoveToAngles", code);
        Assert.DoesNotContain("CStandaloneEntity::StopMoving", code);
    }

    /// <summary>And the shell no longer declares them, so nothing can call one back.</summary>
    [Fact]
    public void TheShellNoLongerDeclaresAStepper()
    {
        string code = CodeOf("src", "sourcesharp", "game", "server", "shell_standalone.h");

        Assert.DoesNotContain("UpdateMovement", code);
        Assert.DoesNotContain("BlockerAt", code);
        Assert.DoesNotContain("MoveToPosition", code);
        Assert.DoesNotContain("StopMoving", code);
    }

    /// <summary>
    /// Nor holds the six members it integrated, nor the bit that told two
    /// owners of one velocity apart.
    /// </summary>
    [Fact]
    public void TheShellNoLongerHoldsAMovementInProgress()
    {
        string code = CodeOf("src", "sourcesharp", "game", "server", "shell_standalone.h");

        foreach (string member in new[] { "m_vecMoveTarget", "m_angMoveTarget", "m_flMoveSpeed",
                                          "m_flAngleSpeed", "m_bMovingLinear", "m_bMovingAngular",
                                          "m_bFlying" })
            Assert.DoesNotContain(member, code);

        // And the three MIRRORS stay, because something else reads each.
        Assert.Contains("m_vecVelocity", code);
        Assert.Contains("m_angVelocity", code);
        Assert.Contains("m_flGravityScale", code);
    }

    /// <summary>
    /// The two headers whose whole subject was the stepper are deleted rather
    /// than left as dead rules nothing consults.
    /// </summary>
    [Fact]
    public void TheStepperSTwoHeadersAreDeleted()
    {
        string root = RepoLayout.SdkTree;

        Assert.False(File.Exists(Path.Combine(root, "src", "sourcesharp", "game", "server",
                                              "standalone_riders.h")));
        Assert.False(File.Exists(Path.Combine(root, "src", "sourcesharp", "game", "server",
                                              "shell_mover_velocity.h")));
    }

    /// <summary>
    /// The native frame step that ran the movers has nothing left to run, and
    /// the managed frame stopped paying a crossing for it.
    /// </summary>
    [Fact]
    public void TheNativeUpdateStepIsNoLongerCalled()
    {
        string cs = File.ReadAllText(Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed",
                                                  "SourceSharp.Server", "StandaloneHierarchy.cs"));

        Assert.DoesNotContain("_frame.StandaloneUpdate();", cs);
        Assert.Contains("_motion.RunFrame(", cs);
    }

    /// <summary>
    /// The three retired ABI slots REFUSE OUT LOUD rather than moving nothing
    /// in silence (LANE_BRIEF §7). A slot that quietly did nothing is
    /// indistinguishable from a mover that ignored its input, which is the
    /// failure this file's whole history is made of.
    /// </summary>
    [Fact]
    public void TheThreeRetiredMoverSlotsSaySoRatherThanDoingNothing()
    {
        string code = CodeOf("src", "sourcesharp", "game", "server", "shell_scripted.cpp");

        Assert.Contains("SourceSharp_MoverSlotRetired", code);
        Assert.Contains("EntityMoveToPosition", code);
        Assert.Contains("EntityMoveToAngles", code);
        Assert.Contains("EntityStopMoving", code);
        Assert.Contains("Warning(", code);

        // And they reach no stepper, because there is none to reach.
        Assert.DoesNotContain("pStandalone->MoveToPosition", code);
        Assert.DoesNotContain("pStandalone->StopMoving", code);
    }

    /// <summary>
    /// The four classes that used to carry their own riders have stopped, so
    /// nobody is carried twice.
    ///
    /// <para>
    /// Doing it twice is not a tidiness problem: <c>Riders.Carry</c> displaces
    /// by the step, so a rider on a sideways-moving train would travel at twice
    /// the train's speed and slide off its nose — which is a measured failure
    /// this tree already has, arrived at from the other direction.
    /// </para>
    /// </summary>
    [Fact]
    public void NoBrushClassCarriesItsOwnRidersAnyMore()
    {
        string root = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed",
                                   "SourceSharp.Server", "Entities", "Brushes");

        foreach (string file in new[] { "FuncPlat.cs", "FuncPlatRot.cs", "TrainBrush.cs" })
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            string code = Regex.Replace(Regex.Replace(text, @"///[^\n]*", " "), @"//[^\n]*", " ");

            Assert.DoesNotContain("Riders.Carry", code);
        }

        // The BELT still rides, and that is a different channel: a stationary
        // conveyor has no step to give and speaks base velocity instead.
        Assert.Contains("Riders.Ride",
                        File.ReadAllText(Path.Combine(root, "FuncConveyor.cs")));
    }
}
