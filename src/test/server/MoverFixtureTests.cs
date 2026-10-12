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

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed push integrator against a RECORDING of the native one —
/// <c>plan_entity.md</c> step 2g.
///
/// <para>
/// <c>Fixtures/mover-updatemovement-stock.tsv</c> is 1690 frames of
/// <c>CStandaloneEntity::UpdateMovement</c> taken off a live <c>dm_lockdown</c>
/// BEFORE this step changed anything: the frame each step was measured in, the
/// movement the mover had been asked for, <b>the answer BlockerAt gave it</b>,
/// and the transform and velocity it produced. Replaying those inputs through
/// <see cref="MoverMath"/> compares managed arithmetic with the C++ arithmetic
/// it replaces.
/// </para>
///
/// <para>
/// <b>The blocker answer is an INPUT and that is the whole design.</b> A test
/// that recomputed the blocker question and then compared its own step against
/// its own step would agree forever and say nothing about parity. The rule that
/// produced those answers is checked separately, against its own recording of
/// every candidate the native walk looked at.
/// </para>
///
/// <para>
/// <b>No tolerance.</b> <c>%.9g</c> round-trips a single exactly, so every
/// comparison here is bit equality. A tolerance would hide precisely the two
/// things this exists to catch: <c>VectorNormalize</c>'s rsqrt approximation
/// (which decides which frame a door arrives on) and <c>AngleDiff</c>'s
/// asymmetric fix-up.
/// </para>
///
/// <para>
/// See <c>Fixtures/README-mover-push-stock.md</c> for how it was recorded and
/// what it does and does not exercise.
/// </para>
/// </summary>
public class MoverFixtureTests
{
    //=========================================================================
    // Reading it.
    //=========================================================================

    /// <summary>One recorded frame of the native stepper.</summary>
    private sealed record MoveRow(
        string Name, float Dt, bool Parented,
        Vector WorldBefore,
        Vector Current, QAngle AngCurrent,
        bool Linear, Vector Target, float Speed,
        bool Angular, QAngle AngleTarget, float AngleSpeed,
        QAngle Spin,
        bool Blocked,
        Vector Next, QAngle AngNext,
        bool Moved, bool Arrived,
        Vector WorldAfter, Vector Velocity);

    /// <summary>One candidate the native blocker walk looked at, and its verdict.</summary>
    private sealed record RiderRow(string Mover, string Candidate, bool IsPlayer,
                                   float FeetZ, float TopZ, bool Blocks);

    private static string[][] Read(string fixture)
    {
        string path = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed",
                                   "SourceSharp.Tests", "Fixtures", fixture);

        Assert.True(File.Exists(path), "the recorded stock fixture is missing: " + path);

        return File.ReadAllLines(path)
                   .Skip(1)
                   .Where(line => line.Length != 0)
                   .Select(line => line.Split('\t'))
                   .ToArray();
    }

    private static float F(string text) => float.Parse(text, CultureInfo.InvariantCulture);
    private static bool B(string text) => text == "1";

    private static IReadOnlyList<MoveRow> Moves()
    {
        var rows = new List<MoveRow>();

        foreach (string[] c in Read("mover-updatemovement-stock.tsv"))
        {
            Assert.Equal(40, c.Length);

            rows.Add(new MoveRow(
                c[0], F(c[1]), B(c[2]),
                new Vector(F(c[3]), F(c[4]), F(c[5])),
                new Vector(F(c[6]), F(c[7]), F(c[8])),
                new QAngle(F(c[9]), F(c[10]), F(c[11])),
                B(c[12]), new Vector(F(c[13]), F(c[14]), F(c[15])), F(c[16]),
                B(c[17]), new QAngle(F(c[18]), F(c[19]), F(c[20])), F(c[21]),
                new QAngle(F(c[22]), F(c[23]), F(c[24])),
                B(c[25]),
                new Vector(F(c[26]), F(c[27]), F(c[28])),
                new QAngle(F(c[29]), F(c[30]), F(c[31])),
                B(c[32]), B(c[33]),
                new Vector(F(c[34]), F(c[35]), F(c[36])),
                new Vector(F(c[37]), F(c[38]), F(c[39]))));
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    private static IReadOnlyList<RiderRow> Riders()
    {
        var rows = Read("mover-blocker-stock.tsv")
                   .Select(c => new RiderRow(c[0], c[1], B(c[2]), F(c[3]), F(c[4]), B(c[5])))
                   .ToArray();

        Assert.NotEmpty(rows);
        return rows;
    }

    private static void Same(in Vector expected, in Vector actual, string what, int row)
    {
        Assert.True(expected.X == actual.X && expected.Y == actual.Y && expected.Z == actual.Z,
                    $"row {row}: {what} — stock ({expected.X:R} {expected.Y:R} {expected.Z:R}), " +
                    $"managed ({actual.X:R} {actual.Y:R} {actual.Z:R})");
    }

    private static void Same(in QAngle expected, in QAngle actual, string what, int row)
    {
        Assert.True(expected.Pitch == actual.Pitch && expected.Yaw == actual.Yaw
                    && expected.Roll == actual.Roll,
                    $"row {row}: {what} — stock ({expected.Pitch:R} {expected.Yaw:R} {expected.Roll:R}), " +
                    $"managed ({actual.Pitch:R} {actual.Yaw:R} {actual.Roll:R})");
    }

    //=========================================================================
    // The replay.
    //=========================================================================

    /// <summary>
    /// Every unblocked linear frame lands where the C++ landed, to the bit.
    ///
    /// <para>
    /// This is the fact <see cref="MoveVec.Normalize"/> is spelled the way it is
    /// for: the distance the stepper compares its step against is
    /// <c>sqrlen * invlen</c> reconstructed from an rsqrt approximation, not the
    /// length, and an accurate distance answers "arrived" on a different frame.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryUnblockedLinearStepMatchesTheStockStepExactly()
    {
        int checkedRows = 0;

        foreach ((MoveRow r, int i) in Moves().Select((r, i) => (r, i)))
        {
            if (!r.Linear || r.Blocked)
                continue;

            LinearStep step = MoverMath.AdvanceLinear(r.Current, r.Target, r.Speed, r.Dt);

            Same(r.Next, step.Origin, "the committed step", i);
            checkedRows++;
        }

        Assert.True(checkedRows > 900, $"only {checkedRows} unblocked linear frames were replayed");
    }

    /// <summary>
    /// And it arrives on the frame the C++ arrived on — not one before or after.
    ///
    /// <para>
    /// Separate from the position, because the two fail apart: a step that lands
    /// on the target but does not report arriving leaves a door travelling for
    /// ever, and one that reports arriving early stops it short.
    /// </para>
    /// </summary>
    [Fact]
    public void ALinearTravelArrivesOnTheSameFrameTheStockOneDid()
    {
        int arrivals = 0;

        foreach ((MoveRow r, int i) in Moves().Select((r, i) => (r, i)))
        {
            if (!r.Linear || r.Blocked)
                continue;

            LinearStep step = MoverMath.AdvanceLinear(r.Current, r.Target, r.Speed, r.Dt);

            // `arrived` in the row is the OR of the two halves when a mover does
            // both, so the linear claim is only exact where nothing else could
            // have set it.
            if (!r.Angular)
                Assert.True(step.Arrived == r.Arrived,
                            $"row {i}: stock arrived={r.Arrived}, managed arrived={step.Arrived}");

            if (step.Arrived)
                arrivals++;
        }

        Assert.True(arrivals >= 8, $"only {arrivals} arrivals in the recording");
    }

    /// <summary>
    /// A blocked frame commits NOTHING. The recorded next position is the
    /// current one, every time, and that is what "the mover stays where it is
    /// and asks" means.
    /// </summary>
    [Fact]
    public void ABlockedFrameCommittedNothingInTheStockRun()
    {
        int blocked = 0;

        foreach ((MoveRow r, int i) in Moves().Select((r, i) => (r, i)))
        {
            if (!r.Blocked)
                continue;

            Same(r.Current, r.Next, "a blocked frame's committed position", i);
            Assert.False(r.Moved, $"row {i}: a blocked frame reported having moved");
            Same(r.WorldBefore, r.WorldAfter, "a blocked frame's world position", i);
            blocked++;
        }

        Assert.True(blocked > 300, $"only {blocked} blocked frames in the recording");
    }

    /// <summary>Every rotation toward a target turns exactly as far as the C++ turned.</summary>
    [Fact]
    public void EveryAngularStepMatchesTheStockStepExactly()
    {
        int checkedRows = 0;

        foreach ((MoveRow r, int i) in Moves().Select((r, i) => (r, i)))
        {
            if (!r.Angular)
                continue;

            AngularStep step = MoverMath.AdvanceAngular(r.AngCurrent, r.AngleTarget,
                                                        r.AngleSpeed, r.Dt);

            Same(r.AngNext, step.Angles, "the committed facing", i);
            checkedRows++;
        }

        Assert.True(checkedRows > 350, $"only {checkedRows} angular frames were replayed");
    }

    /// <summary>
    /// A continuous spin — the arm with no target, which never arrives.
    ///
    /// <para>
    /// Its own fact because it is a different expression (<c>current + rate *
    /// dt</c>, with no <c>AngleDiff</c> in it) reached by a different branch,
    /// and a port that folded the two would pass the rotation fact and drift a
    /// func_rotating by a frame's worth every frame.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryContinuousSpinFrameMatchesTheStockSpinExactly()
    {
        int checkedRows = 0;

        foreach ((MoveRow r, int i) in Moves().Select((r, i) => (r, i)))
        {
            if (r.Angular || r.Spin == QAngle.Zero)
                continue;

            QAngle next = MoverMath.AdvanceSpin(r.AngCurrent, r.Spin, r.Dt);

            Same(r.AngNext, next, "the committed spin", i);
            checkedRows++;
        }

        Assert.True(checkedRows > 150, $"only {checkedRows} spin frames were replayed");
    }

    /// <summary>
    /// The velocity a mover reported is the step it committed, over the frame it
    /// took — including the frames where that is zero.
    /// </summary>
    [Fact]
    public void TheReportedVelocityMatchesTheStockVelocityExactly()
    {
        var rows = Moves();

        foreach ((MoveRow r, int i) in rows.Select((r, i) => (r, i)))
            Same(r.Velocity, MoverMath.Velocity(r.WorldBefore, r.WorldAfter, r.Dt),
                 "the reported velocity", i);

        Assert.True(rows.Count > 1300, $"only {rows.Count} frames in the recording");
    }

    /// <summary>
    /// A mover that committed nothing reported zero — not the speed it wanted.
    ///
    /// <para>
    /// The half of the velocity rule that is a DECISION rather than a division:
    /// a blocked lift reporting the speed it intended hands its passenger a
    /// floor that stayed where it was.
    /// </para>
    /// </summary>
    [Fact]
    public void ABlockedMoverReportedZeroVelocityInTheStockRun()
    {
        int blocked = 0;

        foreach (MoveRow r in Moves())
        {
            if (!r.Blocked)
                continue;

            Assert.True(r.Velocity.X == 0f && r.Velocity.Y == 0f && r.Velocity.Z == 0f,
                        $"a blocked frame reported velocity ({r.Velocity.X:R} " +
                        $"{r.Velocity.Y:R} {r.Velocity.Z:R})");
            blocked++;
        }

        Assert.True(blocked > 300, $"only {blocked} blocked frames in the recording");
    }

    /// <summary>
    /// And a mover that STOPPED reported zero, which is the other half: the
    /// recording has one row per frame a mover's velocity actually fell to zero,
    /// and the number after it is the number the shell was left holding.
    /// </summary>
    [Fact]
    public void AStoppedMoverWasLeftHoldingZeroInTheStockRun()
    {
        string[][] rows = Read("mover-stopped-stock.tsv");

        Assert.True(rows.Length >= 8, $"only {rows.Length} stop rows in the recording");

        foreach (string[] c in rows)
            Assert.Equal(0f, F(c[4]));
    }

    //=========================================================================
    // The rider rule, against its own recording.
    //=========================================================================

    /// <summary>
    /// Every verdict the native blocker walk reached about every candidate it
    /// looked at, reached again by <see cref="MoverMath.Blocks"/>.
    ///
    /// <para>
    /// <c>overlapsDestination</c> is TRUE for every row, and that is not a
    /// shortcut: the native rule was fed by a partition walk over the
    /// destination box, so a candidate that reached it had already overlapped by
    /// the engine's own test. The managed walk is a sphere and has to make that
    /// decision itself, which is the next fact along.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRecordedBlockerVerdictIsReachedAgain()
    {
        var rows = Riders();

        foreach ((RiderRow r, int i) in rows.Select((r, i) => (r, i)))
        {
            bool blocks = MoverMath.Blocks(r.IsPlayer, true, r.FeetZ, r.TopZ);

            Assert.True(blocks == r.Blocks,
                        $"row {i}: '{r.Candidate}' feet {r.FeetZ:R} against top {r.TopZ:R} — " +
                        $"stock blocks={r.Blocks}, managed blocks={blocks}");
        }

        Assert.True(rows.Count > 400, $"only {rows.Count} candidates in the recording");
    }

    /// <summary>
    /// And the recording actually contains all three answers.
    ///
    /// <para>
    /// The fact above is satisfied by a rule that says "nobody ever blocks" if
    /// the recording happens to hold no blockers, and by "everyone blocks" if it
    /// holds nothing else. It holds all three: somebody in the way, somebody
    /// standing ON the mover (the exemption the rule exists for), and something
    /// that is not a person at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBlockerRecordingHoldsAllThreeAnswers()
    {
        var rows = Riders();

        int obstructions = rows.Count(r => r.IsPlayer && r.Blocks);
        int passengers = rows.Count(r => r.IsPlayer && !r.Blocks);
        int notPeople = rows.Count(r => !r.IsPlayer);

        Assert.True(obstructions > 0, "the recording holds nobody in a mover's way");
        Assert.True(passengers > 0,
                    "the recording holds nobody standing ON a mover, which is the "
                    + "exemption the whole rule exists for");
        Assert.True(notPeople > 0, "the recording holds no candidate that is not a person");
    }

    //=========================================================================
    // Testing the fixture, not just reading it.
    //=========================================================================

    /// <summary>
    /// The recording covers every arm of the stepper that a live run can reach.
    ///
    /// <para>
    /// Counted here rather than trusted, because the first take of this fixture
    /// was silently missing two arms — <c>parented</c> read 0 for all 1512 rows
    /// (a mid-game <c>parentname</c> is never resolved) and "linear and angular
    /// in one frame" read 0 (nothing in <c>dm_lockdown</c> asks for both). Both
    /// replays were green over a recording that could not have caught the bug
    /// they were for. A fixture that loses an arm has to fail, so the arms are
    /// a check and not a paragraph in a README.
    /// </para>
    ///
    /// <para>
    /// The two arms deliberately absent are named in the README: a frametime of
    /// zero or less, which no live frame has, and <c>BlockerAt</c>'s
    /// <c>ent.pBase</c> arm, which has no subject on a build where
    /// <c>GetBaseEntity</c> answers NULL for everything.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRecordingExercisesEveryArmOfTheStepperALiveRunCanReach()
    {
        var rows = Moves();

        Assert.True(rows.Count(r => r.Linear && !r.Blocked && !r.Arrived) > 800,
                    "no ordinary linear steps");
        Assert.True(rows.Count(r => r.Linear && r.Blocked) > 300, "no blocked steps");
        Assert.True(rows.Count(r => r.Linear && !r.Blocked && r.Arrived) >= 8,
                    "no clamped final step");
        Assert.True(rows.Count(r => r.Angular && !r.Arrived) > 350, "no angular steps");
        Assert.True(rows.Count(r => r.Angular && r.Arrived) >= 3, "no angular arrival");
        Assert.True(rows.Count(r => !r.Angular && r.Spin != QAngle.Zero) > 150,
                    "no continuous spin");
        Assert.True(rows.Count(r => r.Linear && r.Angular) > 100,
                    "nothing moved and turned in the same frame, so the single "
                    + "write for both halves is unexercised");
        Assert.True(rows.Count(r => r.Parented) > 100,
                    "nothing was parented, so the whole local-frame arm is unexercised");
        Assert.True(rows.Count(r => !r.Parented) > 100, "nothing was unparented");
    }

    /// <summary>
    /// The recording is of more than one mover, and of more than one KIND of
    /// mover. Every row coming from one entity would make every count above a
    /// statement about that entity rather than about the stepper.
    /// </summary>
    [Fact]
    public void TheRecordingIsOfSeveralMovers()
    {
        string[] names = Moves().Select(r => r.Name).Distinct().ToArray();

        Assert.True(names.Length >= 5,
                    "the recording is of " + names.Length + " movers: " + string.Join(", ", names));
    }
}
