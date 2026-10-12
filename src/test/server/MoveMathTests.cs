using SourceSharp.Abi;
// MoveMath lives in SourceSharp.Abi, deliberately -- see the class comment.

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The movement arithmetic that moved to C# for docs/plan-audit.md decision
/// <b>E1</b>.
///
/// <para>
/// Every expectation below was read off <c>game/shared/gamemovement.cpp</c> and
/// not off <see cref="MoveMath"/>. That distinction is the whole reason these
/// exist: a test written from the port agrees with the port by construction and
/// pins whatever the port got wrong. Each fact names the stock line it came
/// from, so the next reader can check the same way.
/// </para>
///
/// <para>
/// One behaviour per fact, because these are the failure modes that do not
/// throw — a wrong number here is felt as the game moving oddly and shows up on
/// the misprediction meter days later.
/// </para>
/// </summary>
public class MoveMathTests
{
    // ---- ClipVelocity, gamemovement.cpp:3268 -------------------------------

    /// <summary>
    /// A plane with a positive Z is a floor, and stock says so with bit 1.
    /// </summary>
    [Fact]
    public void ClipVelocityCallsAnUpwardPlaneAFloor()
    {
        int blocked = MoveMath.ClipVelocity(
            new Vector(0f, 0f, -100f), new Vector(0f, 0f, 1f), out _, 1.0f, 0f);

        Assert.Equal(MoveMath.BlockedFloor, blocked & MoveMath.BlockedFloor);
    }

    /// <summary>
    /// A plane with NO Z is a wall or a step — stock's second bit, and the test
    /// is <c>!angle</c> rather than a tolerance.
    /// </summary>
    [Fact]
    public void ClipVelocityCallsAVerticalPlaneAWall()
    {
        int blocked = MoveMath.ClipVelocity(
            new Vector(100f, 0f, 0f), new Vector(-1f, 0f, 0f), out _, 1.0f, 0f);

        Assert.Equal(MoveMath.BlockedWall, blocked);
    }

    /// <summary>
    /// A ceiling — negative Z — is NEITHER. Stock sets no bit for it, which is
    /// why <c>TryPlayerMove</c> can tell "hit the roof" from "hit the floor".
    /// </summary>
    [Fact]
    public void ClipVelocityCallsADownwardPlaneNeither()
    {
        int blocked = MoveMath.ClipVelocity(
            new Vector(0f, 0f, 100f), new Vector(0f, 0f, -1f), out _, 1.0f, 0f);

        Assert.Equal(0, blocked);
    }

    /// <summary>
    /// The point of the method: at overbounce 1 exactly the component into the
    /// plane is removed and the along-plane component survives untouched.
    /// </summary>
    [Fact]
    public void ClipVelocityRemovesOnlyTheComponentIntoThePlane()
    {
        MoveMath.ClipVelocity(
            new Vector(200f, 0f, -150f), new Vector(0f, 0f, 1f), out Vector output, 1.0f, 0f);

        Assert.Equal(200f, output.X, 4);
        Assert.Equal(0f, output.Z, 4);
    }

    /// <summary>
    /// Overbounce above 1 is a bounce: it removes more than the plane took, so
    /// the result comes back OUT of the plane. This is what
    /// <c>PerformFlyCollisionResolution</c> uses to make a thrown object rebound.
    /// </summary>
    [Fact]
    public void ClipVelocityAboveOneBouncesBackOutOfThePlane()
    {
        MoveMath.ClipVelocity(
            new Vector(0f, 0f, -100f), new Vector(0f, 0f, 1f), out Vector output, 2.0f, 0f);

        Assert.Equal(100f, output.Z, 4);
    }

    /// <summary>
    /// Stock's single corrective iteration. With an overbounce BELOW 1 the
    /// first pass leaves velocity still heading into the plane, and the
    /// <c>adjust &lt; 0</c> block pulls it back to exactly zero — once, not to
    /// convergence.
    /// </summary>
    [Fact]
    public void ClipVelocityIteratesOnceToStopMovingThroughThePlane()
    {
        MoveMath.ClipVelocity(
            new Vector(0f, 0f, -100f), new Vector(0f, 0f, 1f), out Vector output, 0.5f, 0f);

        // Without the second pass this would still be -50.
        Assert.Equal(0f, output.Z, 4);
    }

    // ---- DecayPunchAngle, gamemovement.cpp:1236 ----------------------------

    /// <summary>
    /// Below stock's 0.001 threshold on BOTH vectors, the punch is snapped to
    /// zero rather than left to decay asymptotically. PunchAngle is a predicted
    /// field with a 0.125 tolerance, so a residue here is a thing the meter can
    /// see.
    /// </summary>
    [Fact]
    public void DecayPunchAngleSnapsATinyPunchToZero()
    {
        var angle = new Vector(0.01f, 0f, 0f);      // 1e-4 squared: under 0.001
        var vel = new Vector(0f, 0f, 0f);

        MoveMath.DecayPunchAngle(ref angle, ref vel, 0.015f);

        Assert.Equal(0f, angle.X);
        Assert.Equal(0f, angle.Y);
        Assert.Equal(0f, angle.Z);
    }

    /// <summary>
    /// A punch with no ANGLE but a live VELOCITY still runs the spring: stock's
    /// test is an OR across both vectors, so a kick applied as pure velocity is
    /// not thrown away on the frame it arrives.
    /// </summary>
    [Fact]
    public void DecayPunchAngleRunsOnVelocityAloneBecauseStocksTestIsAnOr()
    {
        var angle = new Vector(0f, 0f, 0f);
        var vel = new Vector(10f, 0f, 0f);

        MoveMath.DecayPunchAngle(ref angle, ref vel, 0.015f);

        // angle += vel * frametime
        Assert.Equal(0.15f, angle.X, 5);
    }

    /// <summary>
    /// The damping factor is clamped at zero, so a frame long enough to make
    /// <c>1 - 9*dt</c> negative stops the punch rather than reversing it.
    /// </summary>
    [Fact]
    public void DecayPunchAngleClampsDampingAtZeroRatherThanReversing()
    {
        var angle = new Vector(0f, 0f, 0f);
        var vel = new Vector(100f, 0f, 0f);

        // 1 - 9*0.5 = -3.5, clamped to 0.
        MoveMath.DecayPunchAngle(ref angle, ref vel, 0.5f);

        // vel *= 0, then vel -= angle * clamp(65*0.5, 0, 2).
        // angle was 0 + 100*0.5 = 50, so vel == -50 * 2 == -100.
        Assert.Equal(-100f, vel.X, 3);
    }

    /// <summary>
    /// The wrap guard, and it is ASYMMETRIC across the axes exactly as stock
    /// writes it: 89 on pitch and roll, 179 on yaw.
    /// </summary>
    [Fact]
    public void DecayPunchAngleClampsPitchAndRollAtEightyNine()
    {
        var angle = new Vector(0f, 0f, 0f);
        var vel = new Vector(100000f, 0f, 100000f);

        MoveMath.DecayPunchAngle(ref angle, ref vel, 0.015f);

        Assert.Equal(89f, angle.X, 4);
        Assert.Equal(89f, angle.Z, 4);
    }

    /// <summary>
    /// And 179 on yaw, which is the axis that can go the whole way round.
    /// </summary>
    [Fact]
    public void DecayPunchAngleClampsYawAtOneSeventyNine()
    {
        var angle = new Vector(0f, 0f, 0f);
        var vel = new Vector(0f, 100000f, 0f);

        MoveMath.DecayPunchAngle(ref angle, ref vel, 0.015f);

        Assert.Equal(179f, angle.Y, 4);
    }

    // ---- CheckVelocity, gamemovement.cpp:3172 ------------------------------

    /// <summary>
    /// A NaN velocity component becomes zero. This is the guard that keeps a
    /// single bad trace from poisoning every subsequent frame.
    /// </summary>
    [Fact]
    public void CheckVelocityZeroesANaNVelocityComponent()
    {
        var velocity = new Vector(float.NaN, 10f, 0f);
        var origin = Vector.Zero;

        MoveMath.CheckVelocity(ref velocity, ref origin, 3500f);

        Assert.Equal(0f, velocity.X);
        Assert.Equal(10f, velocity.Y);
    }

    /// <summary>
    /// And a NaN ORIGIN component, which stock fixes in the same loop.
    /// </summary>
    [Fact]
    public void CheckVelocityZeroesANaNOriginComponent()
    {
        var velocity = Vector.Zero;
        var origin = new Vector(1f, float.NaN, 3f);

        MoveMath.CheckVelocity(ref velocity, ref origin, 3500f);

        Assert.Equal(0f, origin.Y);
        Assert.Equal(1f, origin.X);
        Assert.Equal(3f, origin.Z);
    }

    /// <summary>
    /// The bound is PER AXIS and symmetric, not a cap on the length — so a
    /// player can exceed sv_maxvelocity diagonally, which is stock.
    /// </summary>
    [Fact]
    public void CheckVelocityBoundsEachAxisSeparatelyRatherThanTheLength()
    {
        var velocity = new Vector(5000f, -5000f, 100f);
        var origin = Vector.Zero;

        MoveMath.CheckVelocity(ref velocity, ref origin, 3500f);

        Assert.Equal(3500f, velocity.X);
        Assert.Equal(-3500f, velocity.Y);
        Assert.Equal(100f, velocity.Z);
    }

    /// <summary>
    /// A NaN component is zeroed BEFORE the bound test reads it, so one bad
    /// axis costs exactly one correction rather than two.
    /// </summary>
    [Fact]
    public void CheckVelocityCountsOneCorrectionForOneNaNAxis()
    {
        var velocity = new Vector(float.NaN, 0f, 0f);
        var origin = Vector.Zero;

        Assert.Equal(1, MoveMath.CheckVelocity(ref velocity, ref origin, 3500f));
    }

    /// <summary>
    /// Nothing wrong, nothing counted — the case that has to hold or the count
    /// is noise.
    /// </summary>
    [Fact]
    public void CheckVelocityCountsNothingOnAHealthyMove()
    {
        var velocity = new Vector(200f, -30f, 12f);
        var origin = new Vector(64f, 64f, 8f);

        Assert.Equal(0, MoveMath.CheckVelocity(ref velocity, ref origin, 3500f));
    }

    // ---- Gravity, gamemovement.cpp:1271 / 1728 / 3215 ----------------------

    /// <summary>
    /// StartGravity takes HALF the frame's gravity — stock's "yes, this 0.5
    /// looks wrong, but it's not".
    /// </summary>
    [Fact]
    public void StartGravityAppliesHalfAFrame()
    {
        var velocity = Vector.Zero;
        var baseVel = Vector.Zero;
        var origin = Vector.Zero;

        MoveMath.Gravity(MoveMath.GravityPhase.Start, ref velocity, ref baseVel, ref origin,
                         0f, 600f, 0.015f, 3500f, waterJumping: false);

        Assert.Equal(-4.5f, velocity.Z, 4);
    }

    /// <summary>
    /// AddGravity — the move types that do not split the frame — takes the
    /// whole thing.
    /// </summary>
    [Fact]
    public void AddGravityAppliesAWholeFrame()
    {
        var velocity = Vector.Zero;
        var baseVel = Vector.Zero;
        var origin = Vector.Zero;

        MoveMath.Gravity(MoveMath.GravityPhase.Add, ref velocity, ref baseVel, ref origin,
                         0f, 600f, 0.015f, 3500f, waterJumping: false);

        Assert.Equal(-9f, velocity.Z, 4);
    }

    /// <summary>
    /// STOCK QUIRK. The player's own gravity is tested for being NON-ZERO, not
    /// for being set — so gravity 0 means "use 1", and a player deliberately
    /// given zero gravity falls normally.
    /// </summary>
    [Fact]
    public void GravityTreatsAZeroEntityGravityAsOne()
    {
        var velocity = Vector.Zero;
        var baseVel = Vector.Zero;
        var origin = Vector.Zero;

        MoveMath.Gravity(MoveMath.GravityPhase.Add, ref velocity, ref baseVel, ref origin,
                         0f, 600f, 0.015f, 3500f, waterJumping: false);

        var halfGravity = Vector.Zero;
        var baseVel2 = Vector.Zero;
        var origin2 = Vector.Zero;
        MoveMath.Gravity(MoveMath.GravityPhase.Add, ref halfGravity, ref baseVel2, ref origin2,
                         0.5f, 600f, 0.015f, 3500f, waterJumping: false);

        Assert.Equal(-9f, velocity.Z, 4);
        Assert.Equal(-4.5f, halfGravity.Z, 4);
    }

    /// <summary>
    /// The vertical base velocity is SPENT — added in and then zeroed — so a
    /// lift or a conveyor contributes once per frame rather than every time
    /// gravity is touched.
    /// </summary>
    [Fact]
    public void StartGravitySpendsAndZeroesTheVerticalBaseVelocity()
    {
        var velocity = Vector.Zero;
        var baseVel = new Vector(50f, 0f, 200f);
        var origin = Vector.Zero;

        MoveMath.Gravity(MoveMath.GravityPhase.Start, ref velocity, ref baseVel, ref origin,
                         1f, 600f, 0.1f, 3500f, waterJumping: false);

        Assert.Equal(0f, baseVel.Z);
        Assert.Equal(50f, baseVel.X);        // the horizontal part is not touched
    }

    /// <summary>
    /// FinishGravity does NOT touch the base velocity. Stock's two halves are
    /// not the same call with a flag, and folding them would spend a lift twice.
    /// </summary>
    [Fact]
    public void FinishGravityLeavesTheBaseVelocityAlone()
    {
        var velocity = Vector.Zero;
        var baseVel = new Vector(0f, 0f, 200f);
        var origin = Vector.Zero;

        MoveMath.Gravity(MoveMath.GravityPhase.Finish, ref velocity, ref baseVel, ref origin,
                         1f, 600f, 0.1f, 3500f, waterJumping: false);

        Assert.Equal(200f, baseVel.Z);
    }

    /// <summary>
    /// THE ASYMMETRY THAT MAKES A WATER JUMP WORK. Finish and Add refuse while
    /// the water-jump timer is running; Start does not test it at all.
    /// </summary>
    [Fact]
    public void StartGravityIgnoresTheWaterJumpTimerAndFinishGravityDoesNot()
    {
        var startVel = Vector.Zero;
        var baseA = Vector.Zero;
        var originA = Vector.Zero;
        MoveMath.Gravity(MoveMath.GravityPhase.Start, ref startVel, ref baseA, ref originA,
                         1f, 600f, 0.015f, 3500f, waterJumping: true);

        var finishVel = Vector.Zero;
        var baseB = Vector.Zero;
        var originB = Vector.Zero;
        MoveMath.Gravity(MoveMath.GravityPhase.Finish, ref finishVel, ref baseB, ref originB,
                         1f, 600f, 0.015f, 3500f, waterJumping: true);

        Assert.Equal(-4.5f, startVel.Z, 4);
        Assert.Equal(0f, finishVel.Z);
    }

    /// <summary>
    /// Gravity ends in CheckVelocity, exactly where stock calls it — so a fall
    /// long enough to pass sv_maxvelocity is bounded inside the same call.
    /// </summary>
    [Fact]
    public void GravityBoundsTheResultThroughCheckVelocity()
    {
        var velocity = new Vector(0f, 0f, -3499f);
        var baseVel = Vector.Zero;
        var origin = Vector.Zero;

        MoveMath.Gravity(MoveMath.GravityPhase.Add, ref velocity, ref baseVel, ref origin,
                         1f, 600f, 0.1f, 3500f, waterJumping: false);

        Assert.Equal(-3500f, velocity.Z);
    }

    // ---- Accelerate / AirAccelerate, gamemovement.cpp:1880 / 1765 ----------

    /// <summary>
    /// A dead or water-jumping player does not accelerate — stock's
    /// <c>CanAccelerate()</c>, hoisted to an argument.
    /// </summary>
    [Fact]
    public void AccelerateRefusesWhenTheCallerSaysItCannot()
    {
        var velocity = Vector.Zero;

        MoveMath.Accelerate(ref velocity, new Vector(1f, 0f, 0f), 320f, 10f, 0.015f, 1f,
                            canAccelerate: false);

        Assert.Equal(0f, velocity.X);
    }

    /// <summary>
    /// The ordinary case: accel × frametime × wishspeed × surface friction.
    /// </summary>
    [Fact]
    public void AccelerateAddsAccelTimesFrameTimeTimesWishSpeed()
    {
        var velocity = Vector.Zero;

        MoveMath.Accelerate(ref velocity, new Vector(1f, 0f, 0f), 320f, 10f, 0.001f, 1f,
                            canAccelerate: true);

        Assert.Equal(3.2f, velocity.X, 4);
    }

    /// <summary>
    /// It never overshoots the wish speed: the step is capped at the room left.
    /// </summary>
    [Fact]
    public void AccelerateNeverOvershootsTheWishSpeed()
    {
        var velocity = new Vector(319f, 0f, 0f);

        MoveMath.Accelerate(ref velocity, new Vector(1f, 0f, 0f), 320f, 10f, 0.1f, 1f,
                            canAccelerate: true);

        Assert.Equal(320f, velocity.X, 4);
    }

    /// <summary>
    /// Already at or past the wish speed along the wish direction, nothing is
    /// added — the <c>addspeed &lt;= 0</c> early out.
    /// </summary>
    [Fact]
    public void AccelerateAddsNothingWhenAlreadyAtTheWishSpeed()
    {
        var velocity = new Vector(320f, 0f, 0f);

        MoveMath.Accelerate(ref velocity, new Vector(1f, 0f, 0f), 320f, 10f, 0.1f, 1f,
                            canAccelerate: true);

        Assert.Equal(320f, velocity.X, 4);
    }

    /// <summary>
    /// Surface friction scales the acceleration, which is how ice makes a
    /// player slow to get going as well as slow to stop.
    /// </summary>
    [Fact]
    public void AccelerateScalesWithSurfaceFriction()
    {
        var velocity = Vector.Zero;

        MoveMath.Accelerate(ref velocity, new Vector(1f, 0f, 0f), 320f, 10f, 0.001f, 0.25f,
                            canAccelerate: true);

        Assert.Equal(0.8f, velocity.X, 4);
    }

    /// <summary>
    /// <b>THE AIR-STRAFE QUIRK</b>, and the single most consequential line in
    /// this port. Stock caps the BUDGET at the air speed cap and then computes
    /// the acceleration from the UNCAPPED wish speed. With a 30 cap and a 320
    /// wish speed the step is 3.2, not the 0.3 a consistent reading would give
    /// — a factor of ten, and the reason bunny-hopping exists.
    /// </summary>
    [Fact]
    public void AirAccelerateComputesTheStepFromTheUNCAPPEDWishSpeed()
    {
        var velocity = Vector.Zero;
        var outWishVel = Vector.Zero;

        MoveMath.AirAccelerate(ref velocity, ref outWishVel, new Vector(1f, 0f, 0f),
                               wishSpeed: 320f, accel: 10f, frameTime: 0.001f,
                               surfaceFriction: 1f, airSpeedCap: 30f, canAccelerate: true);

        Assert.Equal(3.2f, velocity.X, 4);
        Assert.NotEqual(0.3f, velocity.X, 4);
    }

    /// <summary>
    /// The CAP is what limits the total: once the player is moving at the cap
    /// along the wish direction there is no budget left, however large the
    /// wish speed.
    /// </summary>
    [Fact]
    public void AirAccelerateStopsAddingOnceTheCappedBudgetIsSpent()
    {
        var velocity = new Vector(30f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.AirAccelerate(ref velocity, ref outWishVel, new Vector(1f, 0f, 0f),
                               wishSpeed: 320f, accel: 10f, frameTime: 0.001f,
                               surfaceFriction: 1f, airSpeedCap: 30f, canAccelerate: true);

        Assert.Equal(30f, velocity.X, 4);
    }

    /// <summary>
    /// Steering SIDEWAYS is what the cap does not stop: the budget is measured
    /// along the wish direction, so a player moving fast forward still has the
    /// full 30 available at ninety degrees. This is the mechanism, not a
    /// side effect of it.
    /// </summary>
    [Fact]
    public void AirAccelerateGivesTheFullBudgetPerpendicularToTheCurrentVelocity()
    {
        var velocity = new Vector(1000f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.AirAccelerate(ref velocity, ref outWishVel, new Vector(0f, 1f, 0f),
                               wishSpeed: 320f, accel: 10f, frameTime: 0.001f,
                               surfaceFriction: 1f, airSpeedCap: 30f, canAccelerate: true);

        Assert.Equal(3.2f, velocity.Y, 4);
    }

    /// <summary>
    /// STOCK ASYMMETRY, KEPT: the air path reports into m_outWishVel and the
    /// ground path does not. <see cref="MoveMath.Accelerate"/> has no such
    /// parameter, which is the same fact stated in the signature.
    /// </summary>
    [Fact]
    public void AirAccelerateReportsIntoOutWishVelAndGroundAccelerateHasNowhereTo()
    {
        var velocity = Vector.Zero;
        var outWishVel = Vector.Zero;

        MoveMath.AirAccelerate(ref velocity, ref outWishVel, new Vector(1f, 0f, 0f),
                               wishSpeed: 320f, accel: 10f, frameTime: 0.001f,
                               surfaceFriction: 1f, airSpeedCap: 30f, canAccelerate: true);

        Assert.Equal(3.2f, outWishVel.X, 4);
    }

    // ---- Friction, gamemovement.cpp:1646 -----------------------------------

    /// <summary>
    /// No friction at all during a water jump, which is what lets the player
    /// keep the lunge that clears the ledge.
    /// </summary>
    [Fact]
    public void FrictionDoesNothingDuringAWaterJump()
    {
        var velocity = new Vector(400f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.Friction(ref velocity, ref outWishVel, onGround: true,
                          friction: 4f, stopSpeed: 75f, frameTime: 0.015f, waterJumping: true);

        Assert.Equal(400f, velocity.X);
        Assert.Equal(0f, outWishVel.X);
    }

    /// <summary>
    /// Under 0.1 u/s stock returns before touching anything — including the
    /// m_outWishVel line at the bottom, which matters because that line is
    /// wrong (see below).
    /// </summary>
    [Fact]
    public void FrictionReturnsUntouchedBelowATenthOfAUnitPerSecond()
    {
        var velocity = new Vector(0.05f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.Friction(ref velocity, ref outWishVel, onGround: true,
                          friction: 4f, stopSpeed: 75f, frameTime: 0.015f, waterJumping: false);

        Assert.Equal(0.05f, velocity.X);
        Assert.Equal(0f, outWishVel.X);
    }

    /// <summary>
    /// The ordinary ground case: drop = speed × friction × dt, and the velocity
    /// is scaled by the surviving proportion.
    /// </summary>
    [Fact]
    public void FrictionBleedsSpeedTimesFrictionTimesFrameTimeOnTheGround()
    {
        var velocity = new Vector(400f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.Friction(ref velocity, ref outWishVel, onGround: true,
                          friction: 4f, stopSpeed: 75f, frameTime: 0.015f, waterJumping: false);

        // control = 400 (above stopspeed), drop = 400*4*0.015 = 24.
        Assert.Equal(376f, velocity.X, 3);
    }

    /// <summary>
    /// Below sv_stopspeed the bleed is computed from STOPSPEED rather than from
    /// the actual speed, so a nearly stopped player stops decisively instead of
    /// creeping. Without it a walk never quite ends.
    /// </summary>
    [Fact]
    public void FrictionUsesStopSpeedAsTheFloorForTheBleed()
    {
        var velocity = new Vector(10f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.Friction(ref velocity, ref outWishVel, onGround: true,
                          friction: 4f, stopSpeed: 75f, frameTime: 0.015f, waterJumping: false);

        // control = 75, not 10. drop = 75*4*0.015 = 4.5, so 10 -> 5.5.
        Assert.Equal(5.5f, velocity.X, 3);
    }

    /// <summary>
    /// Off the ground there is no drop, so the velocity is untouched — air
    /// resistance is not a thing this game has.
    /// </summary>
    [Fact]
    public void FrictionLeavesVelocityAloneInTheAir()
    {
        var velocity = new Vector(400f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.Friction(ref velocity, ref outWishVel, onGround: false,
                          friction: 4f, stopSpeed: 75f, frameTime: 0.015f, waterJumping: false);

        Assert.Equal(400f, velocity.X);
    }

    /// <summary>
    /// <b>STOCK BUG, REPRODUCED DELIBERATELY.</b> <c>newspeed</c> is turned
    /// into a ratio only inside the branch that ran; when nothing was dropped
    /// it is still a SPEED, and the last line subtracts <c>(1 - 400) *
    /// velocity</c> from m_outWishVel. Airborne, m_outWishVel therefore comes
    /// out at 399× the velocity, pointing the WRONG WAY.
    ///
    /// <para>
    /// Pinned rather than fixed. m_outWishVel feeds the player animation and
    /// both sides read stock's answer today; correcting it here corrects it on
    /// one side of a predicted loop first, which is the shape of the bug this
    /// lane exists to avoid.
    /// </para>
    /// </summary>
    [Fact]
    public void FrictionCorruptsOutWishVelInTheAirExactlyAsStockDoes()
    {
        var velocity = new Vector(400f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.Friction(ref velocity, ref outWishVel, onGround: false,
                          friction: 4f, stopSpeed: 75f, frameTime: 0.015f, waterJumping: false);

        // 0 - (1 - 400) * 400 == +159600, and every step of that is exact in
        // binary floating point, so this is an EXACT comparison on purpose: an
        // approximate one would tolerate a "fix" that merely shrank the number.
        Assert.Equal(159600f, outWishVel.X);
    }

    /// <summary>
    /// On the ground the same line behaves, because there <c>newspeed</c> IS a
    /// ratio: a 6% bleed removes 6% of the new velocity from the wish velocity.
    /// The two facts together are what makes the one above a bug rather than a
    /// design.
    /// </summary>
    [Fact]
    public void FrictionSubtractsTheBledProportionFromOutWishVelOnTheGround()
    {
        var velocity = new Vector(400f, 0f, 0f);
        var outWishVel = Vector.Zero;

        MoveMath.Friction(ref velocity, ref outWishVel, onGround: true,
                          friction: 4f, stopSpeed: 75f, frameTime: 0.015f, waterJumping: false);

        // ratio 376/400 = 0.94; -(1-0.94) * 376 = -22.56.
        Assert.Equal(-22.56f, outWishVel.X, 2);
    }

    // ---- SplineFraction, gamemovement.cpp:4178 -----------------------------

    [Fact]
    public void SplineFractionIsZeroAtZero()
        => Assert.Equal(0f, MoveMath.SplineFraction(0f, 1f), 5);

    [Fact]
    public void SplineFractionIsOneAtOne()
        => Assert.Equal(1f, MoveMath.SplineFraction(1f, 1f), 5);

    /// <summary>
    /// The midpoint is the midpoint — an ease-in/ease-out curve is symmetric
    /// about it, which is why the duck feels the same going down and coming up.
    /// </summary>
    [Fact]
    public void SplineFractionIsAHalfAtTheMidpoint()
        => Assert.Equal(0.5f, MoveMath.SplineFraction(0.5f, 1f), 5);

    /// <summary>
    /// The scale multiplies the INPUT, so it is a rate rather than an
    /// amplitude: the duck path passes a 1/duck-time scale and a remaining
    /// time.
    /// </summary>
    [Fact]
    public void SplineFractionScalesTheInputNotTheOutput()
        => Assert.Equal(MoveMath.SplineFraction(0.5f, 1f), MoveMath.SplineFraction(0.25f, 2f), 5);

    // ---- CalcRoll, gamemovement.cpp:3321 -----------------------------------

    /// <summary>
    /// At this build's default (sv_rollangle 0, movevars_shared.cpp:75) view
    /// roll is a constant zero. Worth a fact of its own: it is what the game
    /// actually does today, and a port that quietly started rolling the view
    /// would be a visible change nobody asked for.
    /// </summary>
    [Fact]
    public void CalcRollIsZeroAtThisBuildsDefaultRollAngle()
    {
        float roll = MoveMath.CalcRoll(new Vector(0f, 1f, 0f), new Vector(0f, 300f, 0f),
                                       rollAngle: 0f, rollSpeed: 200f);

        Assert.Equal(0f, roll);
    }

    /// <summary>
    /// Past sv_rollspeed the roll saturates at the full angle rather than
    /// growing.
    /// </summary>
    [Fact]
    public void CalcRollSaturatesAtTheFullAngleAboveRollSpeed()
    {
        float roll = MoveMath.CalcRoll(new Vector(0f, 1f, 0f), new Vector(0f, 300f, 0f),
                                       rollAngle: 2f, rollSpeed: 200f);

        Assert.Equal(2f, roll, 4);
    }

    /// <summary>
    /// Below it the roll is proportional.
    /// </summary>
    [Fact]
    public void CalcRollIsProportionalBelowRollSpeed()
    {
        float roll = MoveMath.CalcRoll(new Vector(0f, 1f, 0f), new Vector(0f, 100f, 0f),
                                       rollAngle: 2f, rollSpeed: 200f);

        Assert.Equal(1f, roll, 4);
    }

    /// <summary>
    /// Sideways the other way rolls the other way.
    /// </summary>
    [Fact]
    public void CalcRollFlipsSignWithTheSideOfTheMotion()
    {
        float roll = MoveMath.CalcRoll(new Vector(0f, 1f, 0f), new Vector(0f, -100f, 0f),
                                       rollAngle: 2f, rollSpeed: 200f);

        Assert.Equal(-1f, roll, 4);
    }

    /// <summary>
    /// STOCK QUIRK: the sign test is <c>side &lt; 0 ? -1 : 1</c>, so EXACTLY
    /// zero sideways motion rolls POSITIVE. At a rollangle of 0 that is
    /// invisible; with one set it is a discontinuity, and a "tidier" sign
    /// function would silently remove it.
    /// </summary>
    [Fact]
    public void CalcRollTreatsExactlyZeroSideAsPositive()
    {
        float roll = MoveMath.CalcRoll(new Vector(0f, 1f, 0f), new Vector(300f, 0f, 0f),
                                       rollAngle: 2f, rollSpeed: 200f);

        Assert.Equal(0f, roll, 4);        // side 0 -> 0 * 2 / 200 == 0, but signed +
        Assert.False(float.IsNegative(roll));
    }

    // ---- ReduceTimers, gamemovement.cpp:1113 -------------------------------

    /// <summary>
    /// The timers are in MILLISECONDS against a seconds frame time, which is
    /// the 1000× stock applies once at the top.
    /// </summary>
    [Fact]
    public void ReduceTimersCountsInMillisecondsAgainstASecondsFrameTime()
    {
        float duck = 400f, duckJump = 0f, jump = 0f, swim = 0f;

        MoveMath.ReduceTimers(ref duck, ref duckJump, ref jump, ref swim, 0.015f);

        Assert.Equal(385f, duck, 3);
    }

    /// <summary>
    /// A timer that would go past zero stops at zero rather than going
    /// negative, so "is it running" stays a simple <c>&gt; 0</c> everywhere else.
    /// </summary>
    [Fact]
    public void ReduceTimersClampsAtZeroRatherThanGoingNegative()
    {
        float duck = 5f, duckJump = 0f, jump = 0f, swim = 0f;

        MoveMath.ReduceTimers(ref duck, ref duckJump, ref jump, ref swim, 0.5f);

        Assert.Equal(0f, duck);
    }

    /// <summary>
    /// STOCK QUIRK: the guard is <c>&gt; 0</c>, so a timer already negative is
    /// never touched and never recovers. Nothing in the loop sets one negative,
    /// which is exactly why the behaviour has never been noticed.
    /// </summary>
    [Fact]
    public void ReduceTimersLeavesAnAlreadyNegativeTimerAlone()
    {
        float duck = 0f, duckJump = -25f, jump = 0f, swim = 0f;

        MoveMath.ReduceTimers(ref duck, ref duckJump, ref jump, ref swim, 0.015f);

        Assert.Equal(-25f, duckJump);
    }

    /// <summary>
    /// All four timers count down, not just the duck one — the swim sound timer
    /// is on the same clock and is the one a partial port would forget.
    /// </summary>
    [Fact]
    public void ReduceTimersCountsAllFourDown()
    {
        float duck = 100f, duckJump = 100f, jump = 100f, swim = 100f;

        MoveMath.ReduceTimers(ref duck, ref duckJump, ref jump, ref swim, 0.015f);

        Assert.Equal(85f, duck, 3);
        Assert.Equal(85f, duckJump, 3);
        Assert.Equal(85f, jump, 3);
        Assert.Equal(85f, swim, 3);
    }
}

/// <summary>
/// The fall-damage rule, which the policy-player lane moved out of
/// <c>CStandaloneMoveHelper::PlayerFallingDamage</c>.
///
/// <para>
/// Every number below is read off <c>shareddefs.h:415-421</c> and
/// <c>multiplay_gamerules.cpp:647</c> — stock — and not off
/// <see cref="MoveMath.FallDamage"/>. The two facts either side of
/// <c>PLAYER_MAX_SAFE_FALL_SPEED</c> are the lane's gate, and moving the
/// constant flips exactly one of them.
/// </para>
/// </summary>
public class FallDamageTests
{
    /// <summary>
    /// One unit UNDER the threshold is a landing you walk away from. Stock
    /// arms the ask on <c>m_flFallVelocity &gt; PLAYER_MAX_SAFE_FALL_SPEED</c>
    /// (gamemovement.cpp:4381), so 579 is a fall nobody is charged for.
    /// </summary>
    [Fact]
    public void AFallOneUnitUnderTheSafeSpeedDoesNoDamage()
    {
        Assert.Equal(0f, MoveMath.FallDamage(579f));
    }

    /// <summary>
    /// One unit OVER it hurts. The pair is the boundary, and it is stated as
    /// two facts because a rule that returned 0 for everything would pass the
    /// first one alone.
    /// </summary>
    [Fact]
    public void AFallOneUnitOverTheSafeSpeedDoesDamage()
    {
        Assert.True(MoveMath.FallDamage(581f) > 0f,
            "a fall past PLAYER_MAX_SAFE_FALL_SPEED must cost health");
    }

    /// <summary>
    /// AT the threshold, nothing. Stock's test is strictly greater-than, so
    /// exactly 580 is the fastest survivable-for-free landing there is.
    /// </summary>
    [Fact]
    public void AFallExactlyAtTheSafeSpeedDoesNoDamage()
    {
        Assert.Equal(0f, MoveMath.FallDamage(580f));
    }

    /// <summary>
    /// <c>PLAYER_FATAL_FALL_SPEED</c> is named for what it does: the slope is
    /// <c>100 / (fatal - safe)</c> per unit, so a landing at the fatal speed
    /// takes exactly 100 — a full health bar. That identity is the reason the
    /// two constants are not free parameters.
    /// </summary>
    [Fact]
    public void AFallAtTheFatalSpeedDoesExactlyOneHundred()
    {
        Assert.Equal(100f, MoveMath.FallDamage(1024f), 3);
    }

    /// <summary>
    /// Halfway up the survivable range costs half a health bar — the rule is
    /// LINEAR, which is what distinguishes it from every curve it could have
    /// been ported as.
    /// </summary>
    [Fact]
    public void TheFallDamageRuleIsLinearBetweenTheTwoSpeeds()
    {
        // (580 + 1024) / 2, written out because the point of the fact is that
        // the port agrees with stock's numbers and not with its own.
        Assert.Equal(50f, MoveMath.FallDamage(802f), 3);
    }

    /// <summary>
    /// THE MULTIPLY COMES BEFORE THE DIVIDE. <c>DAMAGE_FOR_FALL_SPEED</c> is an
    /// unparenthesised macro, so stock's expansion is
    /// <c>(v - 580) * 100.0f / 444</c>; folding the constant to
    /// <c>(v - 580) * (100.0f / 444)</c> is a different float. This fact is bit-
    /// exact against the expansion and goes red if anyone "simplifies" it.
    /// </summary>
    [Fact]
    public void TheFallDamageRuleMatchesTheMacroExpansionBitForBit()
    {
        // 587.46875 IS CHOSEN AND NOT ARBITRARY. At most inputs the two
        // orderings agree to the last bit, so a value picked for looking like a
        // fall speed makes a fact that cannot fail. This one is a value at
        // which they DIFFER — folded gives 1.6821508407592773, stock's ordering
        // 1.682150959968567 — which is what makes replacing the expression with
        // the folded constant go red here and nowhere else.
        const float v = 587.46875f;

        float stock = (v - 580f) * 100.0f / (1024f - 580f);

        Assert.Equal(BitConverter.SingleToInt32Bits(stock),
                     BitConverter.SingleToInt32Bits(MoveMath.FallDamage(v)));
    }

    /// <summary>
    /// A rule that answered a negative number below its own boundary would heal
    /// a player who stepped off a kerb. The floor is at the rule, not only at
    /// the call site that arms it.
    /// </summary>
    [Fact]
    public void AStandingStillPlayerIsNeverHealedByTheFallRule()
    {
        Assert.Equal(0f, MoveMath.FallDamage(0f));
    }

    /// <summary>
    /// <see cref="GameMovement.PlayerMaxSafeFallSpeed"/> — the number the loop
    /// ARMS on — and <see cref="MoveMath.PlayerMaxSafeFallSpeed"/> — the number
    /// the damage is SCALED by — are one constant. They were two before this
    /// lane, in two languages, over two different fall velocities.
    /// </summary>
    [Fact]
    public void TheArmingThresholdAndTheDamageSlopeShareOneConstant()
    {
        Assert.Equal(MoveMath.PlayerMaxSafeFallSpeed, GameMovement.PlayerMaxSafeFallSpeed);

        // And it is STOCK's number, not merely a shared one. Two identical
        // wrong constants would satisfy the line above on its own.
        Assert.Equal(580f, MoveMath.PlayerMaxSafeFallSpeed);
        Assert.Equal(1024f, MoveMath.PlayerFatalFallSpeed);
    }
}
