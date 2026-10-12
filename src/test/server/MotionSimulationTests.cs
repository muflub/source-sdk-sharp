using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The four controllers' arithmetic (plan_physics_bones_native_zero 2.4), held
/// to shell_motioncontrollers.cpp's Simulates at 4202f899b with hand-worked
/// numbers: constant force, CMotorController's feedback loop,
/// ComputeRotSpeedToAlignAxes / CKeepUpright, and CTriggerVPhysicsMotion's medium.
/// </summary>
public class MotionSimulationTests
{
    private const nint Obj = 0x2300;

    private static MotionState Force(bool active, bool local, bool acceleration) => new(MotionControllerKind.ConstantForce)
    {
        Active = active, Local = local, Acceleration = acceleration,
        Linear = new Vector(1f, 2f, 3f), Angular = new Vector(4f, 5f, 6f),
    };

    [Theory]
    [InlineData(false, false, MotionSimResult.GlobalForce)]
    [InlineData(true, false, MotionSimResult.LocalForce)]
    [InlineData(false, true, MotionSimResult.GlobalAcceleration)]
    [InlineData(true, true, MotionSimResult.LocalAcceleration)]
    public void AConstantForcesTwoChoicesAreFourAnswers(bool local, bool acceleration, MotionSimResult expected)
    {
        var result = MotionSimulation.ConstantForce(Force(true, local, acceleration), out Vector linear, out Vector angular);
        Assert.True(result == expected && linear.Z == 3f && angular.X == 4f);
    }

    [Fact]
    public void AnInactiveConstantForcePushesNothing()
    {
        var result = MotionSimulation.ConstantForce(Force(false, true, true), out Vector linear, out Vector angular);
        Assert.True(result == MotionSimResult.Nothing && linear.X == 0f && angular.X == 0f);
    }

    private static MotionState Motor(float speed) => new(MotionControllerKind.Motor)
    {
        Axis = new Vector(0f, 0f, 1f), Speed = speed, InertiaFactor = 1f,
    };

    [Fact]
    public void AMotorAtRestPushesItsWholeErrorThroughTheInertia()
    {
        MotionState s = Motor(30f);
        var result = MotionSimulation.Motor(s, Float3x4.Identity, new Vector(0f, 0f, 10f), new Vector(2f, 3f, 4f),
                                            out _, out Vector angular);

        // accel 20, inertia along z 4: 80. No history yet, so no heuristic.
        Assert.True(result == MotionSimResult.LocalForce && angular.Z == 80f);
        Assert.True(s.LastForce == 80f && s.LastAcceleration == 20f && s.LastSpeed == 10f);
    }

    [Fact]
    public void AMotorGainingAgainstResistanceAddsBackWhatTheResistanceAte()
    {
        MotionState s = Motor(30f);
        MotionSimulation.Motor(s, Float3x4.Identity, new Vector(0f, 0f, 10f), new Vector(2f, 3f, 4f), out _, out _);

        // Now at 15: accel 15 -> 60, gained 5 of the 20 asked (factor 1 - 0.25),
        // so 80 * 0.75 is added back: 120.
        MotionSimulation.Motor(s, Float3x4.Identity, new Vector(0f, 0f, 15f), new Vector(2f, 3f, 4f), out _, out Vector angular);
        Assert.Equal(120f, angular.Z);
    }

    [Fact]
    public void AMotorStuckShortOfItsTargetPushesHarder()
    {
        MotionState s = Motor(30f);
        MotionSimulation.Motor(s, Float3x4.Identity, new Vector(0f, 0f, 10f), new Vector(2f, 3f, 4f), out _, out _);

        // Still at 10: no gain (increase 0 < 0.05), a steady state that is not
        // the target, so the last force is added: 80 + 80.
        MotionSimulation.Motor(s, Float3x4.Identity, new Vector(0f, 0f, 10f), new Vector(2f, 3f, 4f), out _, out Vector angular);
        Assert.Equal(160f, angular.Z);
    }

    [Fact]
    public void AMotorsForceIsClampedToItsTorque()
    {
        MotionState s = Motor(30f);
        s.MaxTorque = 50f;
        MotionSimulation.Motor(s, Float3x4.Identity, Vector.Zero, new Vector(2f, 3f, 4f), out _, out Vector angular);
        Assert.Equal(50f, angular.Z);
    }

    [Fact]
    public void AMotorWithNoTargetSpeedDoesNothing()
    {
        var result = MotionSimulation.Motor(Motor(0f), Float3x4.Identity, Vector.Zero, Vector.Zero, out _, out _);
        Assert.Equal(MotionSimResult.Nothing, result);
    }

    [Fact]
    public void AMotorReadsItsAxisInTheObjectsFrame()
    {
        // The object turned 90 degrees about z: world x is its local -y.
        var m = Float3x4.Identity;
        m[0, 0] = 0f; m[0, 1] = -1f; m[1, 0] = 1f; m[1, 1] = 0f;
        MotionState s = new(MotionControllerKind.Motor) { Axis = new Vector(1f, 0f, 0f), Speed = 10f, InertiaFactor = 1f };

        MotionSimulation.Motor(s, m, Vector.Zero, new Vector(1f, 1f, 1f), out _, out Vector angular);
        Assert.True(angular.X == 0f && angular.Y == -10f, "VectorIRotate: (1,0,0) through the transpose is (0,-1,0)");
    }

    [Fact]
    public void AligningOneAxisOntoAPerpendicularOneTurnsNinetyDegrees()
    {
        Vector w = MotionSimulation.ComputeRotSpeedToAlignAxes(new Vector(0f, 0f, 1f), new Vector(1f, 0f, 0f), Vector.Zero,
                                                               damping: 0f, scale: 1f, maxSpeed: 1000f);
        Assert.True(MathF.Abs(w.Y - 90f) < 1e-2f && MathF.Abs(w.X) < 1e-6f && MathF.Abs(w.Z) < 1e-6f,
                    "about +y, by 90 degrees at scale 1");
    }

    [Fact]
    public void AligningIsCappedAtTheMaximumSpeed()
    {
        Vector w = MotionSimulation.ComputeRotSpeedToAlignAxes(new Vector(0f, 0f, 1f), new Vector(1f, 0f, 0f), Vector.Zero,
                                                               damping: 0f, scale: 1f, maxSpeed: 30f);
        Assert.True(MathF.Abs(w.Y - 30f) < 1e-3f);
    }

    [Fact]
    public void AligningIsDampedByTheSpinAlreadyAboutTheAxis()
    {
        Vector w = MotionSimulation.ComputeRotSpeedToAlignAxes(new Vector(0f, 0f, 1f), new Vector(1f, 0f, 0f), new Vector(0f, 40f, 0f),
                                                               damping: 1f, scale: 1f, maxSpeed: 1000f);
        Assert.True(MathF.Abs(w.Y - 50f) < 1e-2f, "90 asked, 40 already turning: 50");
    }

    [Fact]
    public void KeepUprightRightsAToppledObjectAsAnAcceleration()
    {
        var v = new FakeVPhysicsJoints();
        var body = v.Add(Obj);
        // Toppled onto its side: local z points along world x.
        body.Pose[0, 0] = 0f; body.Pose[0, 2] = 1f; body.Pose[2, 0] = -1f; body.Pose[2, 2] = 0f;
        var s = new MotionState(MotionControllerKind.KeepUpright) { Active = true, AngularLimit = 1000f };

        var result = MotionSimulation.KeepUpright(s, Obj, 0.5f, v, out _, out Vector angular);

        // World up seen locally is (-1,0,0): 90 degrees about -y, scaled by 1/dt
        // twice (the scale, then the acceleration): 90 * 2 * 2.
        Assert.True(result == MotionSimResult.LocalAcceleration && MathF.Abs(angular.Y - -360f) < 0.1f,
                    $"got {angular}");
    }

    [Fact]
    public void KeepUprightDampingAllRotationCancelsTheSpin()
    {
        var v = new FakeVPhysicsJoints();
        v.Add(Obj).AngularVelocity = new Vector(5f, 0f, 0f);
        var s = new MotionState(MotionControllerKind.KeepUpright) { Active = true, AngularLimit = 1000f, DampAllRotation = true };

        MotionSimulation.KeepUpright(s, Obj, 0.5f, v, out _, out Vector angular);
        Assert.True(MathF.Abs(angular.X - -10f) < 1e-4f, "already upright: only the spin, removed, per second");
    }

    [Fact]
    public void KeepUprightInactiveOrWithNoStepDoesNothing()
    {
        var v = new FakeVPhysicsJoints();
        v.Add(Obj);
        var s = new MotionState(MotionControllerKind.KeepUpright) { Active = false };
        Assert.Equal(MotionSimResult.Nothing, MotionSimulation.KeepUpright(s, Obj, 0.5f, v, out _, out _));
        s.Active = true;
        Assert.Equal(MotionSimResult.Nothing, MotionSimulation.KeepUpright(s, Obj, 0f, v, out _, out _));
    }

    [Fact]
    public void AMediumsGravityScaleAddsTheExtraGravitiesOnly()
    {
        var v = new FakeVPhysicsJoints();
        v.Add(Obj);
        var w = new FakeJointWorld { GravityValue = 600f };
        var s = new MotionState(MotionControllerKind.Medium) { GravityScale = 0.5f };

        var result = MotionSimulation.Medium(s, Obj, 0.015f, v, w, out Vector linear, out _);
        Assert.True(result == MotionSimResult.GlobalAcceleration && linear.Z == 300f, "-(0.5-1)*600");
    }

    [Fact]
    public void AMediumsLinearForceFollowsItsAngles()
    {
        var v = new FakeVPhysicsJoints();
        v.Add(Obj);
        var s = new MotionState(MotionControllerKind.Medium) { LinearForce = 50f, LinearForceAngles = new Vector(-90f, 0f, 0f) };

        MotionSimulation.Medium(s, Obj, 0.015f, v, new FakeJointWorld(), out Vector linear, out _);
        Assert.True(MathF.Abs(linear.Z - 50f) < 1e-4f, "pitch -90 is straight up");
    }

    [Fact]
    public void AMediumsSpeedLimitIsApproachedNoFasterThanItsDelta()
    {
        var v = new FakeVPhysicsJoints();
        v.Add(Obj).Velocity = new Vector(100f, 0f, 0f);
        var s = new MotionState(MotionControllerKind.Medium) { LinearLimit = 40f, LinearLimitDelta = 0.3f };

        MotionSimulation.Medium(s, Obj, 0.1f, v, new FakeJointWorld(), out Vector linear, out _);

        // (40-100)/0.1 = -600, but no faster than -0.3/0.1 = -3.
        Assert.True(MathF.Abs(linear.X - -3f) < 1e-3f, $"got {linear}");
    }

    [Fact]
    public void AMediumsScaleMultipliesTheSpeedAsAnAcceleration()
    {
        var v = new FakeVPhysicsJoints();
        v.Add(Obj).Velocity = new Vector(10f, 0f, 0f);
        var s = new MotionState(MotionControllerKind.Medium) { LinearScale = 0.5f };

        MotionSimulation.Medium(s, Obj, 0.1f, v, new FakeJointWorld(), out Vector linear, out _);
        Assert.True(MathF.Abs(linear.X - -5f) < 1e-4f, "((0+1)*0.5)-1 = -0.5 of the speed");
    }

    [Fact]
    public void AMediumThatDoesNothingAnswersAZeroAcceleration()
    {
        var v = new FakeVPhysicsJoints();
        v.Add(Obj).Velocity = new Vector(10f, 0f, 0f);
        var result = MotionSimulation.Medium(new MotionState(MotionControllerKind.Medium), Obj, 0.1f, v, new FakeJointWorld(),
                                             out Vector linear, out Vector angular);
        Assert.True(result == MotionSimResult.GlobalAcceleration && linear.X == 0f && angular.X == 0f);
    }

    [Fact]
    public void ARampInterpolatesThenSettlesOnItsTarget()
    {
        var s = new MotionState(MotionControllerKind.Medium) { LinearLimit = 100f };
        MotionSimulation.RampLinearLimit(s, 200f, 2f, curTime: 10f);

        Assert.Equal(150f, MotionSimulation.CurrentLinearLimit(s, 11f));
        Assert.Equal(200f, MotionSimulation.CurrentLinearLimit(s, 12f));
        Assert.Equal(0f, s.LinearLimitTime);
    }

    [Fact]
    public void DotProductAbsIsTheSumOfTheAbsoluteProducts()
        => Assert.Equal(2f + 6f + 12f, MotionSimulation.DotProductAbs(new Vector(-1f, 2f, -3f), new Vector(2f, 3f, 4f)));
}
