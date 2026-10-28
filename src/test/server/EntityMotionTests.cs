//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The movement integrators — <c>plan_entity.md</c> step 2f.
///
/// <para>
/// <b>THE FIXTURE IS THE POINT OF THIS SUITE.</b>
/// <c>Fixtures/flight-trajectory-stock.tsv</c> is thirty frames of
/// <c>CStandaloneEntity::UpdateFlight</c> recorded from a live
/// <c>dm_lockdown</c> BEFORE this step changed anything — the inputs each frame
/// of flight saw, the trace answer the engine handed it, and the state it
/// produced. Replaying those inputs through <see cref="MoveMath.AdvanceFlight"/>
/// and <see cref="MoveMath.ResolveFlight"/> compares managed arithmetic with the
/// C++ arithmetic it replaces. A test that compared managed output with managed
/// output would agree forever and prove nothing about parity; that is the whole
/// reason the recorded rows carry the TRACE ANSWERS as inputs rather than being
/// a list of positions to reproduce.
/// </para>
///
/// <para>
/// The comparison is <b>bit-exact</b>, not within a tolerance. The fixture is
/// written at <c>%.9g</c>, which round-trips an IEEE-754 single, and both sides
/// are scalar SSE floats in the same order — so an epsilon here would be an
/// epsilon-sized hole for a reordered expression to hide in. See
/// <c>Fixtures/README-flight-trajectory-stock.md</c> for how it was recorded.
/// </para>
/// </summary>
public class EntityMotionTests
{
    //=========================================================================
    // The recorded stock trajectory.
    //=========================================================================

    /// <summary>One recorded frame of the native integrator.</summary>
    private sealed record StockFrame(
        int Line, string ClassName,
        FlightInput Input,
        float Fraction, Vector EndPosition, Vector Normal, bool NamedAnEntity,
        Vector Destination,
        Vector OutOrigin, Vector OutVelocity, bool OutFlying, int OutBounces);

    private static float F(string text)
        => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static IReadOnlyList<StockFrame> StockTrajectory()
    {
        string path = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed",
                                   "SourceSharp.Tests", "Fixtures",
                                   "flight-trajectory-stock.tsv");

        Assert.True(File.Exists(path), "the recorded stock trajectory is missing: " + path);

        var rows = new List<StockFrame>();
        string[] lines = File.ReadAllLines(path);

        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
                continue;

            string[] c = lines[i].Split('\t');

            Assert.True(c.Length == 30,
                $"line {i + 1} of the fixture has {c.Length} columns, not 30");

            rows.Add(new StockFrame(
                i + 1, c[0],
                new FlightInput(F(c[1]), F(c[2]), F(c[3]),
                                new Vector(F(c[4]), F(c[5]), F(c[6])),
                                new Vector(F(c[7]), F(c[8]), F(c[9])),
                                int.Parse(c[10], CultureInfo.InvariantCulture)),
                F(c[14]),
                new Vector(F(c[15]), F(c[16]), F(c[17])),
                new Vector(F(c[18]), F(c[19]), F(c[20])),
                c[21] == "1",
                new Vector(F(c[11]), F(c[12]), F(c[13])),
                new Vector(F(c[22]), F(c[23]), F(c[24])),
                new Vector(F(c[25]), F(c[26]), F(c[27])),
                c[28] == "1",
                int.Parse(c[29], CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static void Exactly(Vector expected, Vector actual, string what)
    {
        Assert.True(expected.X == actual.X && expected.Y == actual.Y && expected.Z == actual.Z,
                    $"{what}: expected ({expected.X:R}, {expected.Y:R}, {expected.Z:R}), " +
                    $"got ({actual.X:R}, {actual.Y:R}, {actual.Z:R})");
    }

    /// <summary>
    /// A fixture whose rows were all one branch would let two of the three go
    /// unmeasured, and it read as a full trajectory either way — the bounce arm
    /// is ONE frame out of thirty, and getting it into the recording took
    /// moving the player off the spawn.
    /// </summary>
    [Fact]
    public void TheRecordedTrajectoryCoversAllThreeArmsOfTheStockIntegrator()
    {
        var rows = StockTrajectory();

        Assert.Equal(30, rows.Count);
        Assert.Equal(27, rows.Count(r => r.Fraction >= 1f));
        Assert.Equal(1, rows.Count(r => r.Fraction < 1f && r.Input.BouncesLeft > 0));
        Assert.Equal(2, rows.Count(r => r.Fraction < 1f && r.Input.BouncesLeft == 0));
    }

    /// <summary>And both gravity arms, which is what separates FLY from TOSS.</summary>
    [Fact]
    public void TheRecordedTrajectoryCoversBothGravityArms()
    {
        var rows = StockTrajectory();

        Assert.Contains(rows, r => r.Input.GravityScale == 0f);
        Assert.Contains(rows, r => r.Input.GravityScale != 0f);
    }

    /// <summary>
    /// THE GATE. Every recorded frame, replayed: the destination the managed
    /// step aims the trace at is the destination the native one aimed it at, to
    /// the bit.
    /// </summary>
    [Fact]
    public void EveryRecordedStepAimsWhereTheStockIntegratorAimed()
    {
        foreach (var row in StockTrajectory())
        {
            FlightStep step = MoveMath.AdvanceFlight(row.Input);

            Exactly(row.Destination, step.Destination,
                    $"line {row.Line} ({row.ClassName}) destination");
        }
    }

    /// <summary>
    /// And the origin it leaves the entity at, through all three arms — the
    /// clear step, the terminal impact and the bounce.
    /// </summary>
    [Fact]
    public void EveryRecordedFrameLeavesTheEntityWhereTheStockIntegratorLeftIt()
    {
        foreach (var row in StockTrajectory())
        {
            FlightStep step = MoveMath.AdvanceFlight(row.Input);
            FlightResult result = MoveMath.ResolveFlight(row.Input, step, row.Fraction,
                                                         row.EndPosition, row.Normal);

            Exactly(row.OutOrigin, result.Origin, $"line {row.Line} ({row.ClassName}) origin");
        }
    }

    /// <summary>
    /// And the velocity, which is the half a position check cannot see: a
    /// trajectory reproduced from the wrong velocity is right for one frame.
    /// </summary>
    [Fact]
    public void EveryRecordedFrameProducesTheStockVelocity()
    {
        foreach (var row in StockTrajectory())
        {
            FlightStep step = MoveMath.AdvanceFlight(row.Input);
            FlightResult result = MoveMath.ResolveFlight(row.Input, step, row.Fraction,
                                                         row.EndPosition, row.Normal);

            Exactly(row.OutVelocity, result.Velocity, $"line {row.Line} ({row.ClassName}) velocity");
        }
    }

    /// <summary>Whether it is still flying afterwards, which is what ends a projectile.</summary>
    [Fact]
    public void EveryRecordedFrameAgreesWithStockOnWhetherTheFlightGoesOn()
    {
        foreach (var row in StockTrajectory())
        {
            FlightStep step = MoveMath.AdvanceFlight(row.Input);
            FlightResult result = MoveMath.ResolveFlight(row.Input, step, row.Fraction,
                                                         row.EndPosition, row.Normal);

            Assert.True(row.OutFlying == result.Flying,
                        $"line {row.Line} ({row.ClassName}): stock left flying={row.OutFlying}, " +
                        $"managed says {result.Flying}");
        }
    }

    /// <summary>And how many bounces it has left, which is what ends an energy ball.</summary>
    [Fact]
    public void EveryRecordedFrameAgreesWithStockOnTheBounceCount()
    {
        foreach (var row in StockTrajectory())
        {
            FlightStep step = MoveMath.AdvanceFlight(row.Input);
            FlightResult result = MoveMath.ResolveFlight(row.Input, step, row.Fraction,
                                                         row.EndPosition, row.Normal);

            Assert.True(row.OutBounces == result.BouncesLeft,
                        $"line {row.Line} ({row.ClassName}): stock left {row.OutBounces} bounces, " +
                        $"managed says {result.BouncesLeft}");
        }
    }

    /// <summary>
    /// The whole recorded flight of ONE projectile, chained: each frame's
    /// output fed into the next frame's input rather than each frame being
    /// judged against stock in isolation.
    ///
    /// <para>
    /// This is the fact the six above cannot make. Per-frame agreement is
    /// agreement given stock's own state; a trajectory is what happens when the
    /// error from frame one is what frame two integrates. The bolt is the
    /// subject because it is the one that falls: with gravity scale zero the
    /// chain is exact for arithmetic reasons that say nothing about the
    /// integrator.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBoltsWholeFlightSurvivesBeingChainedFrameToFrame()
    {
        var bolt = StockTrajectory().Where(r => r.ClassName == "ss_bolt").ToList();

        Assert.True(bolt.Count >= 8, $"only {bolt.Count} recorded bolt frames");

        Vector origin = bolt[0].Input.Origin;
        Vector velocity = bolt[0].Input.Velocity;
        int bounces = bolt[0].Input.BouncesLeft;

        foreach (var row in bolt)
        {
            var input = new FlightInput(row.Input.FrameTime, row.Input.Gravity,
                                        row.Input.GravityScale, origin, velocity, bounces);

            FlightStep step = MoveMath.AdvanceFlight(input);
            FlightResult result = MoveMath.ResolveFlight(input, step, row.Fraction,
                                                         row.EndPosition, row.Normal);

            origin = result.Origin;
            velocity = result.Velocity;
            bounces = result.BouncesLeft;

            Exactly(row.OutOrigin, origin, $"line {row.Line} chained origin");
            Exactly(row.OutVelocity, velocity, $"line {row.Line} chained velocity");
        }
    }

    //=========================================================================
    // The arithmetic, stated as behaviours rather than replayed.
    //=========================================================================

    /// <summary>
    /// Gravity lands on the velocity BEFORE the step uses it, which is
    /// semi-implicit Euler and is what <c>UpdateFlight</c> wrote. Reversing the
    /// two gives a different trajectory that still looks like one.
    /// </summary>
    [Fact]
    public void GravityIsAppliedBeforeTheStepAndNotAfterIt()
    {
        var input = new FlightInput(0.5f, 800f, 1f, Vector.Zero,
                                    new Vector(0f, 0f, 0f), 0);

        FlightStep step = MoveMath.AdvanceFlight(input);

        // One half-second of 800 is 400 off the velocity, and the step is that
        // velocity times the half second: -200. Applied after, it would be 0.
        Assert.Equal(-400f, step.Velocity.Z);
        Assert.Equal(-200f, step.Destination.Z);
    }

    /// <summary>
    /// A gravity scale of exactly zero skips the term rather than adding a
    /// zero — stock's <c>MOVETYPE_FLY</c> against <c>MOVETYPE_FLYGRAVITY</c>.
    /// </summary>
    [Fact]
    public void AZeroGravityScaleLeavesTheVelocityAlone()
    {
        var input = new FlightInput(0.015f, 600f, 0f, Vector.Zero,
                                    new Vector(0f, 1500f, 0f), 0);

        Assert.Equal(0f, MoveMath.AdvanceFlight(input).Velocity.Z);
    }

    /// <summary>The scale is a scale: a bolt at 0.15 falls at 15% of a rocket-less fall.</summary>
    [Fact]
    public void TheGravityScaleScalesTheFall()
    {
        var full = new FlightInput(0.1f, 600f, 1f, Vector.Zero, Vector.Zero, 0);
        var bolt = new FlightInput(0.1f, 600f, 0.15f, Vector.Zero, Vector.Zero, 0);

        Assert.Equal(-60f, MoveMath.AdvanceFlight(full).Velocity.Z);
        Assert.Equal(-9f, MoveMath.AdvanceFlight(bolt).Velocity.Z, 4);
    }

    /// <summary>
    /// A clear step ends at the DESTINATION, not at the trace's end position.
    /// The two are the same for a clear trace and the distinction is what a
    /// blocked one is about.
    /// </summary>
    [Fact]
    public void AClearStepArrivesAtTheDestination()
    {
        var input = new FlightInput(0.1f, 0f, 0f, Vector.Zero, new Vector(100f, 0f, 0f), 0);
        FlightStep step = MoveMath.AdvanceFlight(input);

        FlightResult result = MoveMath.ResolveFlight(input, step, 1f, Vector.Zero, Vector.Zero);

        Exactly(new Vector(10f, 0f, 0f), result.Origin, "clear step");
        Assert.False(result.Struck);
        Assert.True(result.Flying);
    }

    /// <summary>
    /// A blocked step stops WHERE IT STRUCK and not at the destination, so an
    /// explosion happens against the surface instead of inside it.
    /// </summary>
    [Fact]
    public void ABlockedStepStopsWhereItStruckRatherThanAtTheDestination()
    {
        var input = new FlightInput(0.1f, 0f, 0f, Vector.Zero, new Vector(100f, 0f, 0f), 0);
        FlightStep step = MoveMath.AdvanceFlight(input);

        FlightResult result = MoveMath.ResolveFlight(input, step, 0.5f,
                                                     new Vector(5f, 0f, 0f),
                                                     new Vector(-1f, 0f, 0f));

        Exactly(new Vector(5f, 0f, 0f), result.Origin, "blocked step");
        Assert.True(result.Struck);
    }

    /// <summary>With no bounces left, a blocked step ends the flight.</summary>
    [Fact]
    public void AProjectileWithNoBouncesLeftStopsFlyingWhenItIsBlocked()
    {
        var input = new FlightInput(0.1f, 0f, 0f, Vector.Zero, new Vector(100f, 0f, 0f), 0);
        FlightStep step = MoveMath.AdvanceFlight(input);

        FlightResult result = MoveMath.ResolveFlight(input, step, 0.5f,
                                                     new Vector(5f, 0f, 0f),
                                                     new Vector(-1f, 0f, 0f));

        Assert.False(result.Flying);
        Assert.Equal(0, result.BouncesLeft);
    }

    /// <summary>And with one, it keeps going, one bounce poorer.</summary>
    [Fact]
    public void ABounceKeepsTheFlightGoingAndSpendsOneBounce()
    {
        var input = new FlightInput(0.1f, 0f, 0f, Vector.Zero, new Vector(0f, 0f, -100f), 3);
        FlightStep step = MoveMath.AdvanceFlight(input);

        FlightResult result = MoveMath.ResolveFlight(input, step, 0.5f,
                                                     new Vector(0f, 0f, -5f),
                                                     new Vector(0f, 0f, 1f));

        Assert.True(result.Flying);
        Assert.Equal(2, result.BouncesLeft);
    }

    /// <summary>
    /// A bounce reflects the velocity ABOUT the surface — <c>v - 2(v·n)n</c> —
    /// so a straight-down shot on a flat floor comes straight back up at the
    /// same speed.
    /// </summary>
    [Fact]
    public void ABounceReflectsTheVelocityAboutTheSurface()
    {
        var input = new FlightInput(0.1f, 0f, 0f, Vector.Zero, new Vector(0f, 0f, -100f), 1);
        FlightStep step = MoveMath.AdvanceFlight(input);

        FlightResult result = MoveMath.ResolveFlight(input, step, 0.5f,
                                                     new Vector(0f, 0f, -5f),
                                                     new Vector(0f, 0f, 1f));

        Exactly(new Vector(0f, 0f, 100f), result.Velocity, "reflected velocity");
    }

    /// <summary>
    /// And LIFTS THE ENTITY CLEAR of what it hit by one unit along the normal.
    /// Resuming exactly on the surface starts the next frame's trace inside it,
    /// which reports a second hit at the same place and eats every bounce in
    /// one tick.
    /// </summary>
    [Fact]
    public void ABounceLeavesTheEntityOneUnitClearOfTheSurface()
    {
        var input = new FlightInput(0.1f, 0f, 0f, Vector.Zero, new Vector(0f, 0f, -100f), 1);
        FlightStep step = MoveMath.AdvanceFlight(input);

        FlightResult result = MoveMath.ResolveFlight(input, step, 0.5f,
                                                     new Vector(0f, 0f, -5f),
                                                     new Vector(0f, 0f, 1f));

        Exactly(new Vector(0f, 0f, -4f), result.Origin, "lifted clear");
    }

    /// <summary>
    /// The branch is on the FRACTION and not on "something was named": a trace
    /// that stopped on the world names no entity at all, and the world is most
    /// of what a projectile hits.
    /// </summary>
    [Fact]
    public void AStepStoppedByTheWorldIsStillAStepThatWasStopped()
    {
        var input = new FlightInput(0.1f, 0f, 0f, Vector.Zero, new Vector(100f, 0f, 0f), 0);
        FlightStep step = MoveMath.AdvanceFlight(input);

        // Fraction under one, and not one entity in sight.
        FlightResult result = MoveMath.ResolveFlight(input, step, 0.25f,
                                                     new Vector(2.5f, 0f, 0f),
                                                     new Vector(-1f, 0f, 0f));

        Assert.True(result.Struck);
        Assert.False(result.Flying);
    }

    /// <summary>
    /// <c>MOVETYPE_NOCLIP</c> is velocity applied and nothing asked — stock's
    /// <c>VectorMA(origin, frametime, velocity, origin)</c>.
    /// </summary>
    [Fact]
    public void ANoClipStepIsTheVelocityAppliedWithNothingAsked()
    {
        Vector moved = MoveMath.NoClipStep(new Vector(1f, 2f, 3f),
                                           new Vector(10f, 20f, 30f), 0.5f);

        Exactly(new Vector(6f, 12f, 18f), moved, "noclip step");
    }

    //=========================================================================
    // The per-frame pass.
    //=========================================================================

    /// <summary>
    /// A pass over a double and a real directory.
    ///
    /// <para>
    /// EVERY TEST BELOW STUBS <c>Entities.GetParent</c> WITH
    /// <see cref="EntityHandle.None"/> AND NOT WITH <c>Ignores</c>, and the
    /// difference is not stylistic: an ignored member answers
    /// <c>default(EntityHandle)</c>, which is entry ZERO — a perfectly real
    /// handle — where the ABI slot answers <c>INVALID_EHANDLE_INDEX</c> for "no
    /// parent". Ignoring it makes every entity read as parented, and the whole
    /// suite then measures the refusal path.
    /// </para>
    /// </summary>
    private static (EntityMotion motion, RecordingEngine engine, EntityDirectory directory) AMotionPass()
    {
        var engine = Fake();
        var directory = new EntityDirectory(new RecordingLog());

        return (new EntityMotion(new RecordingLog(), engine, engine, engine, engine, directory,
            new PhysicsPushedEntities(new EnginePushWorld(engine, engine, engine, directory), new RecordingLog())), engine, directory);
    }

    /// <summary>A shell that records the impacts it was told about.</summary>
    private sealed class ATarget : ScriptedEntity
    {
        public readonly List<EntityHandle> Touched = new();

        public override void OnTouch(EntityHandle toucher) => Touched.Add(toucher);
    }

    private static ATarget Bound(EntityDirectory directory, EntityHandle handle)
    {
        var entity = new ATarget();
        directory.Bind(handle, entity);
        return entity;
    }

    private static readonly EntityHandle TheProjectile = new(11);
    private static readonly EntityHandle TheShooter = new(3);

    /// <summary>
    /// A frametime of zero does nothing at all — <c>UpdateFlight</c>'s own
    /// first guard. A paused server would otherwise integrate gravity into a
    /// velocity over no time and drift.
    /// </summary>
    [Fact]
    public void AFrameOfNoTimeIntegratesNothing()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None);
        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);

        Assert.Equal(0, motion.RunFrame(0f));
    }

    /// <summary>
    /// N flying entities cost ONE pass, not N of anything the frame does not
    /// already do — step 1g's rule, which this step must not undo.
    /// </summary>
    [Fact]
    public void OnePassIntegratesEveryFlyerThereIs()
    {
        var (motion, engine, directory) = AMotionPass();

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              .Ignores("Entities.SetOrigin");

        for (uint i = 20; i < 84; i++)
        {
            var handle = new EntityHandle(i);
            Bound(directory, handle);
            motion.Launch(handle, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        }

        Assert.Equal(64, motion.RunFrame(0.015f));
        Assert.Equal(64, motion.MovedLastFrame);
    }

    /// <summary>
    /// A flyer launched with a spin turns by it each frame, as PhysicsToss
    /// turns a MOVETYPE_FLYGRAVITY entity by its angular velocity: the SMG1's
    /// grenade tumbles (RandomAngle( -400, 400 )). One launched without a spin
    /// is never turned.
    /// </summary>
    [Fact]
    public void ASpunFlyerTurnsByItsSpinEachFrame()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);
        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetAngles", new Vector(10f, 20f, 30f))
              .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0.5f, 0, TheShooter, spin: new Vector(100f, -200f, 400f));
        motion.RunFrame(0.1f);

        var turned = (Vector)engine.Last("Entities.SetAngles")!.Value.Args[1]!;
        Assert.Equal((20f, 0f, 70f), (turned.X, turned.Y, turned.Z));
    }

    [Fact]
    public void AnUnspunFlyerIsNeverTurned()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);
        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0.5f, 0, TheShooter);
        motion.RunFrame(0.1f);

        Assert.False(engine.Called("Entities.SetAngles"), "a flyer with no spin was turned");
    }

    /// <summary>
    /// What a touch reads as the velocity it arrived with is the one going into
    /// the step that struck, not what the collision left: a contact grenade's
    /// scorch is traced along its flight.
    /// </summary>
    [Fact]
    public void AStruckFlyerRemembersTheVelocityItArrivedWith()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);
        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f), new Vector(0f, -1f, 0f), EntityHandle.None))
              .Ignores("Entities.SetOrigin", "Entities.SetVelocity");

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        motion.RunFrame(0.015f);

        Assert.Equal(new Vector(0f, 1000f, 0f), motion.IncomingVelocityOf(TheProjectile));
    }

    /// <summary>
    /// Nothing flying is a pass that costs nothing — not even the one native
    /// call for the world's gravity, which is what a server at rest does every
    /// tick of its life.
    /// </summary>
    [Fact]
    public void AFrameWithNothingFlyingAsksTheEngineNothing()
    {
        var (motion, _, _) = AMotionPass();

        // The double is STRICT: any engine read this did not stub would throw
        // naming itself, so "asks nothing" is what not throwing here means.
        Assert.Equal(0, motion.RunFrame(0.015f));
    }

    /// <summary>
    /// The world's gravity is read ONCE per frame and not once per entity: it
    /// is a convar, every falling thing reads the same number, and a native
    /// call per entity for it is the shape this port exists to remove.
    /// </summary>
    [Fact]
    public void TheWorldsGravityIsReadOncePerFrameRatherThanOncePerEntity()
    {
        var (motion, engine, directory) = AMotionPass();

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              .Ignores("Entities.SetOrigin")
              .Ignores("Entities.SetVelocity");

        for (uint i = 30; i < 38; i++)
        {
            var handle = new EntityHandle(i);
            Bound(directory, handle);
            motion.Launch(handle, new Vector(0f, 1000f, 0f), 1f, 0, TheShooter);
        }

        motion.RunFrame(0.015f);

        Assert.Equal(1, engine.CountOf("Effects.CurrentGravity"));
    }

    /// <summary>
    /// A projectile is passed through its OWN step trace. It is SOLID_BBOX and
    /// goes into the spatial partition like anything else, so a trace with
    /// nothing passed through starts inside its own two-unit box and hits IT at
    /// fraction zero on the very first frame — which read as the projectile
    /// never being created and was in fact it destroying itself.
    /// </summary>
    [Fact]
    public void TheStepTracePassesTheProjectileThroughItself()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Ignores("Entities.SetOrigin")
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None));

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        motion.RunFrame(0.015f);

        EngineCall? trace = engine.Last("WorldQuery.TraceLine");

        Assert.True(trace is not null, "no step trace was made at all");
        Assert.Equal(TheProjectile, trace!.Value.ArgAs<EntityHandle>(3));
    }

    /// <summary>
    /// And a hit that names the SHOOTER is discarded rather than acted on: a
    /// rocket fired at the floor spawns inside the shooter's own hull. The ABI
    /// trace passes one entity through, so this is the managed half of what
    /// <c>CTraceFilterSkipTwoEntities</c> did — see EntityMotion's remarks and
    /// abi-requests/2f.md.
    /// </summary>
    [Fact]
    public void AStepThatRanIntoItsOwnShooterCarriesOnFlying()
    {
        var (motion, engine, directory) = AMotionPass();
        var projectile = Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Ignores("Entities.SetOrigin")
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0f, Vector.Zero, new Vector(0f, -1f, 0f), TheShooter));

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        motion.RunFrame(0.015f);

        Assert.Empty(projectile.Touched);
        Assert.Equal(MotionKind.Fly, motion.KindOf(TheProjectile));
    }

    /// <summary>
    /// A hit that names anything ELSE is an impact, and it reaches the entity
    /// as a managed call rather than as a crossing.
    /// </summary>
    [Fact]
    public void AnImpactReachesTheEntityWithWhatItHit()
    {
        var (motion, engine, directory) = AMotionPass();
        var projectile = Bound(directory, TheProjectile);

        var crate = new EntityHandle(42);

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Ignores("Entities.SetOrigin")
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f),
                                       new Vector(0f, -1f, 0f), crate));

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        motion.RunFrame(0.015f);

        Assert.Equal(new[] { crate }, projectile.Touched);
    }

    /// <summary>
    /// An impact against the WORLD is still an impact, and it names nothing.
    /// That is not "no call": a rocket that only reported hitting entities
    /// would never explode on a wall, which is most of what a rocket hits.
    /// </summary>
    [Fact]
    public void AnImpactAgainstTheWorldIsReportedWithNoEntityNamed()
    {
        var (motion, engine, directory) = AMotionPass();
        var projectile = Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Ignores("Entities.SetOrigin")
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f),
                                       new Vector(0f, -1f, 0f), EntityHandle.None));

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        motion.RunFrame(0.015f);

        Assert.Equal(new[] { EntityHandle.None }, projectile.Touched);
    }

    /// <summary>
    /// The velocity is written back to the shell only on the frames it MOVED.
    /// A rocket flies flat and never changes it, so writing every frame would
    /// be a native call per flyer per frame for a value nothing read.
    /// </summary>
    [Fact]
    public void AFlatFlightNeverWritesItsVelocityBackToTheShell()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Ignores("Entities.SetOrigin")
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None));

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        engine.Clear();

        motion.RunFrame(0.015f);
        motion.RunFrame(0.015f);

        Assert.False(engine.Called("Entities.SetVelocity"),
                     "a rocket that never changed its velocity wrote it back anyway");
    }

    /// <summary>And a falling one writes it every frame, because gravity moved it.</summary>
    [Fact]
    public void AFallingFlightWritesTheVelocityGravityChanged()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Ignores("Entities.SetOrigin")
              .Ignores("Entities.SetVelocity")
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None));

        motion.Launch(TheProjectile, new Vector(0f, 2500f, 0f), 0.15f, 0, TheShooter);
        engine.Clear();

        motion.RunFrame(0.015f);

        Assert.True(engine.Called("Entities.SetVelocity"),
                    "gravity moved the velocity and the shell was never told");
        Assert.Equal(-1.35f, motion.VelocityOf(TheProjectile).Z, 4);
    }

    /// <summary>
    /// A LAUNCH ASKS THE ENGINE NOTHING. It is on the path a weapon takes to
    /// fire a shot, everything it needs is in its arguments, and the double is
    /// strict — so any read this grew would throw here naming itself.
    /// </summary>
    [Fact]
    public void ALaunchAsksTheEngineNothing()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);

        Assert.Empty(engine.Calls);
        Assert.Equal(MotionKind.Fly, motion.KindOf(TheProjectile));
    }

    /// <summary>
    /// A parented entity is refused flight, out loud, and put into
    /// <see cref="MotionKind.Follow"/> — flight integrates a world-space
    /// velocity and traces the step, the parent's composition writes the same
    /// origin from its own frame, and the two would take turns.
    /// </summary>
    [Fact]
    public void AParentedEntityIsRefusedFlightAndFollowsInstead()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        // Log.Warning goes to the static Engine facade and not through this
        // double, so there is nothing to stub for the refusal's message.
        engine.Returns("Entities.GetParent", new EntityHandle(9))
              .Returns("Effects.CurrentGravity", 600f);

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        motion.RunFrame(0.015f);

        Assert.Equal(MotionKind.Follow, motion.KindOf(TheProjectile));

        // AND ITS ORIGIN WAS NOT WRITTEN. The refusal is about two writers on
        // one origin, so "it was refused" and "it was not moved" are the same
        // fact and only the second of them is the one that mattered.
        Assert.False(engine.Called("Entities.SetOrigin"));
    }

    /// <summary>And a follower's origin is not written by this pass at all.</summary>
    [Fact]
    public void AFollowersOriginIsNotWrittenByTheMotionPass()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Effects.CurrentGravity", 600f);
        motion.Follow(TheProjectile);

        Assert.Equal(0, motion.RunFrame(0.015f));
    }

    /// <summary>
    /// The parent is re-checked every frame and not only at launch: something
    /// can be parented while it is in the air, and the frame that happens is
    /// the frame two writers start on one origin.
    /// </summary>
    [Fact]
    public void SomethingParentedInFlightStopsBeingFlown()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f);

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);

        engine.Returns("Entities.GetParent", new EntityHandle(9));
        motion.RunFrame(0.015f);

        Assert.Equal(MotionKind.Follow, motion.KindOf(TheProjectile));
    }

    /// <summary>
    /// Gravity scale zero is <see cref="MotionKind.Fly"/> and anything else is
    /// <see cref="MotionKind.Toss"/>: stock's MOVETYPE_FLY / MOVETYPE_FLYGRAVITY
    /// split is one number, so it is carried as one.
    /// </summary>
    [Fact]
    public void AFlatLaunchIsFlyAndAFallingOneIsToss()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);
        var second = new EntityHandle(12);
        Bound(directory, second);

        motion.Launch(TheProjectile, new Vector(0f, 1500f, 0f), 0f, 0, TheShooter);
        motion.Launch(second, new Vector(0f, 2500f, 0f), 0.15f, 0, TheShooter);

        Assert.Equal(MotionKind.Fly, motion.KindOf(TheProjectile));
        Assert.Equal(MotionKind.Toss, motion.KindOf(second));
    }

    /// <summary>A no-clip entity is moved with no trace asked for at all.</summary>
    [Fact]
    public void ANoClipEntityIsMovedWithoutATrace()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f));

        motion.NoClip(TheProjectile, new Vector(10f, 20f, 30f));
        motion.RunFrame(0.5f);

        EngineCall? written = engine.Last("Entities.SetOrigin");

        Assert.True(written is not null, "the no-clip entity was never moved");
        Exactly(new Vector(6f, 12f, 18f), written!.Value.ArgAs<Vector>(1), "noclip origin");
        Assert.False(engine.Called("WorldQuery.TraceLine"),
                     "a no-clip step asked the world what was in the way");
    }

    /// <summary>
    /// An entity that has gone since the pass took its snapshot is dropped, not
    /// flown. This is the common case rather than a corner: a projectile's own
    /// impact removes it.
    /// </summary>
    [Fact]
    public void AnEntityThatHasGoneIsDroppedRatherThanFlown()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None).Returns("Effects.CurrentGravity", 600f);
        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);

        directory.Unbind(TheProjectile);

        Assert.Equal(0, motion.RunFrame(0.015f));
        Assert.Equal(0, motion.Count);
    }

    /// <summary>
    /// An entity removed by an EARLIER entity's impact in the same pass is
    /// skipped too — the pass walks a snapshot, so the collection cannot be
    /// mutated under it, and the row is re-checked rather than trusted.
    /// </summary>
    [Fact]
    public void AProjectileRemovedByAnEarlierImpactInTheSamePassIsNotFlown()
    {
        var (motion, engine, directory) = AMotionPass();

        var second = new EntityHandle(12);
        var secondEntity = Bound(directory, second);

        // The first one's impact takes the second one out of the world, which
        // is what a rocket that killed what the next rocket was aimed at does.
        var first = new ATargetThatRemoves(directory, second);
        directory.Bind(TheProjectile, first);

        engine.Returns("Entities.GetParent", EntityHandle.None)
              .Returns("Effects.CurrentGravity", 600f)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Ignores("Entities.SetOrigin")
              .Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0.5f, new Vector(0f, 7.5f, 0f),
                                       new Vector(0f, -1f, 0f), EntityHandle.None));

        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);
        motion.Launch(second, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);

        Assert.Equal(1, motion.RunFrame(0.015f));
        Assert.Empty(secondEntity.Touched);
    }

    private sealed class ATargetThatRemoves : ScriptedEntity
    {
        public ATargetThatRemoves(EntityDirectory directory, EntityHandle victim)
        {
            _directory = directory;
            _victim = victim;
        }

        private readonly EntityDirectory _directory;
        private readonly EntityHandle _victim;

        public override void OnTouch(EntityHandle toucher) => _directory.Unbind(_victim);
    }

    /// <summary>A level change stops everything, because the handles are about to be reused.</summary>
    [Fact]
    public void ALevelChangeStopsEverythingThatWasMoving()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None);
        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);

        motion.Clear();

        Assert.Equal(0, motion.Count);
        Assert.Equal(MotionKind.None, motion.KindOf(TheProjectile));
    }

    /// <summary>
    /// A stop leaves the row standing with nothing to do, rather than dropping
    /// it: "this entity is not moving" and "nobody ever launched this entity"
    /// are different answers and a class asking deserves the right one.
    /// </summary>
    [Fact]
    public void AStoppedEntityIsStillKnownAboutAndIsNotIntegrated()
    {
        var (motion, engine, directory) = AMotionPass();
        Bound(directory, TheProjectile);

        engine.Returns("Entities.GetParent", EntityHandle.None).Returns("Effects.CurrentGravity", 600f);
        motion.Launch(TheProjectile, new Vector(0f, 1000f, 0f), 0f, 0, TheShooter);

        motion.Stop(TheProjectile);

        Assert.Equal(1, motion.Count);
        Assert.Equal(MotionKind.None, motion.KindOf(TheProjectile));
        Assert.Equal(0, motion.RunFrame(0.015f));
    }

    /// <summary>
    /// Flight is no longer in the native frame step. The pass would otherwise
    /// run twice — once here and once in <c>StandaloneEntities_Update</c> — and
    /// a doubly-integrated projectile flies at twice the speed it was fired at,
    /// which reads as a tuning problem rather than as two integrators.
    /// </summary>
    [Fact]
    public void TheNativeFrameStepNoLongerFliesAnything()
    {
        string text = File.ReadAllText(Path.Combine(
            RepoLayout.SdkTree, "src", "sourcesharp", "game", "server",
            "shell_standalone_base.cpp"));

        Assert.Contains("StandaloneEntities_Update", text);
        Assert.DoesNotContain("pEnt->UpdateFlight();", text);
        Assert.DoesNotContain("void CStandaloneEntity::UpdateFlight()", text);
    }

    /// <summary>
    /// And the shell no longer declares it either, so nothing can call it back
    /// into existence without noticing this suite.
    /// </summary>
    [Fact]
    public void TheShellNoLongerDeclaresAFlightStep()
    {
        string text = File.ReadAllText(Path.Combine(
            RepoLayout.SdkTree, "src", "sourcesharp", "game", "server", "shell_standalone.h"));

        Assert.Contains("void SetBounces(", text);
        Assert.DoesNotContain("void UpdateFlight();", text);
    }
}
