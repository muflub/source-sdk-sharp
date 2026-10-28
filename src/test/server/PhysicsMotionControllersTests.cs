using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The motion-controller table (plan_physics_bones_native_zero 2.4), held to
/// shell_motioncontrollers.cpp at 4202f899b: creation, the setters, the
/// dispatch of IMotionEvent::Simulate by controller, teardown and the reload.
/// </summary>
public class PhysicsMotionControllersTests
{
    private static readonly EntityHandle Crate = new(0x0001_0030);
    private const nint CrateObj = 0x2200;
    private const nint Handler = 0x5500;

    private static (PhysicsMotionControllers Table, FakeVPhysicsJoints V, FakeJointWorld W, RecordingLog Log) Make()
    {
        var v = new FakeVPhysicsJoints();
        var w = new FakeJointWorld();
        w.Objects[Crate] = CrateObj;
        v.Add(CrateObj);
        var log = new RecordingLog();
        return (new PhysicsMotionControllers(new PhysicsJointTable(), v, w, log) { Handler = Handler }, v, w, log);
    }

    private static IEngineMotionControllers M(PhysicsMotionControllers t) => t;

    [Fact]
    public void AControllerIsBuiltAroundTheManagedMotionEventAndAttachedToItsEntity()
    {
        var (t, v, _, _) = Make();
        uint id = M(t).Create(Crate, (int)MotionControllerKind.ConstantForce);
        Assert.NotEqual(0u, id);
        Assert.Equal($"CreateMotionController {Handler:x}", v.Calls.Single());
        Assert.Equal(CrateObj, v.Attached.Values.Single().Single());
    }

    [Fact]
    public void TheEmptyHandleAsksForAControllerWithNothingAttached()
    {
        var (t, v, _, log) = Make();
        Assert.NotEqual(0u, M(t).Create(EntityHandle.None, (int)MotionControllerKind.Medium));
        Assert.True(v.Attached.Values.Single().Count == 0 && log.Lines.Count == 0);
    }

    [Fact]
    public void AnEntityWithNoPhysicsIsAWarningAndNoController()
    {
        var (t, v, _, log) = Make();
        Assert.Equal(0u, M(t).Create(new EntityHandle(0x0001_0099), (int)MotionControllerKind.Motor));
        Assert.True(v.Calls.Count == 0 && log.Text.Contains("entity has no physics object"));
    }

    [Fact]
    public void AnUnknownKindIsRefused()
    {
        var (t, v, _, log) = Make();
        Assert.Equal(0u, M(t).Create(Crate, 4));
        Assert.True(v.Calls.Count == 0 && log.Text.Contains("unknown kind 4"));
    }

    [Fact]
    public void WithoutAMotionEventTableNoControllerIsMade()
    {
        var (t, v, _, _) = Make();
        t.Handler = 0;
        Assert.Equal(0u, M(t).Create(Crate, (int)MotionControllerKind.Motor));
        Assert.Empty(v.Calls);
    }

    [Fact]
    public void SimulateIsAnsweredByTheControllerItIsHanded()
    {
        var (t, v, _, _) = Make();
        uint push = M(t).Create(Crate, (int)MotionControllerKind.ConstantForce);
        uint other = M(t).Create(Crate, (int)MotionControllerKind.ConstantForce);
        M(t).SetForce(push, new Vector(1f, 2f, 3f), new Vector(4f, 5f, 6f), localSpace: true);
        M(t).SetActive(push, true);
        M(t).SetForce(other, new Vector(9f, 9f, 9f), Vector.Zero, localSpace: false);

        nint pushController = v.Attached.Keys.First();
        int result = t.Simulate(pushController, CrateObj, 0.015f, out Vector linear, out Vector angular);

        Assert.True(result == (int)MotionSimResult.LocalForce && linear.Y == 2f && angular.Z == 6f);
    }

    [Fact]
    public void SimulateForAControllerTheTableDoesNotHoldAnswersNothing()
    {
        var (t, _, _, _) = Make();
        int result = t.Simulate(0x4242, CrateObj, 0.015f, out Vector linear, out Vector angular);
        Assert.True(result == (int)MotionSimResult.Nothing && linear.X == 0f && angular.X == 0f);
    }

    [Fact]
    public void ASetterForAnotherKindIsIgnored()
    {
        var (t, v, _, _) = Make();
        uint motor = M(t).Create(Crate, (int)MotionControllerKind.Motor);
        M(t).SetForce(motor, new Vector(1f, 0f, 0f), Vector.Zero, true);
        M(t).SetActive(motor, true);
        Assert.Empty(v.ControllerWakes);
    }

    [Fact]
    public void AMotorsTorqueCeilingIsTheInertiaAlongItsLocalAxis()
    {
        var (t, v, w, _) = Make();
        w.Inertias[CrateObj] = new Vector(2f, 3f, 4f);
        uint motor = M(t).Create(Crate, (int)MotionControllerKind.Motor);

        M(t).SetMotor(motor, new Vector(0f, 0f, 1f), targetSpeed: 30f, inertiaFactor: 2f, torqueAcceleration: 5f);

        // |4 * 1| * 2 * 5 = 40; the first tick with speed 0 needs 4*30*2 = 240, clamped to 40.
        int result = t.Simulate(v.Attached.Keys.Single(), CrateObj, 0.015f, out _, out Vector angular);
        Assert.True(result == (int)MotionSimResult.LocalForce && angular.Z == 40f);
        Assert.Equal(1, v.ControllerWakes.Values.Single());
    }

    [Fact]
    public void AForceAtAPointIsDecomposedAndEitherHalfCanBeDropped()
    {
        var (t, v, _, _) = Make();
        v.Of(CrateObj).VelocityOffset = (new Vector(1f, 0f, 0f), new Vector(0f, 0f, 2f));
        uint push = M(t).Create(Crate, (int)MotionControllerKind.ConstantForce);

        M(t).SetForceAtPoint(push, new Vector(10f, 0f, 0f), new Vector(0f, 5f, 0f), (int)ForceComponents.Twist, localSpace: false);
        M(t).SetActive(push, true);

        Assert.Equal((new Vector(10f, 0f, 0f), new Vector(0f, 5f, 0f)), v.Of(CrateObj).VelocityOffsetAsked!.Value);
        t.Simulate(v.Attached.Keys.Single(), CrateObj, 0.015f, out Vector linear, out Vector angular);
        Assert.True(linear.X == 0f && angular.Z == 2f, "the push half dropped, the twist kept");
    }

    /// <summary>
    /// In the object's frame the push is turned into it (stock's
    /// CThrustController::CalcThrust / CalculateVelocityOffsetLocal:
    /// WorldToLocalVector), so SIM_LOCAL_* reads the numbers it was meant to.
    /// </summary>
    [Fact]
    public void ALocalForceAtAPointIsPushedInTheObjectsOwnFrame()
    {
        var (t, v, _, _) = Make();
        var pose = Float3x4.Identity; // turned 90 about z: rows (0,-1,0) (1,0,0) (0,0,1)
        pose[0, 0] = 0f; pose[0, 1] = -1f; pose[1, 0] = 1f; pose[1, 1] = 0f;
        v.Of(CrateObj).Pose = pose;
        v.Of(CrateObj).VelocityOffset = (new Vector(1f, 0f, 0f), new Vector(0f, 0f, 2f));
        uint push = M(t).Create(Crate, (int)MotionControllerKind.ConstantForce);

        M(t).SetForceAtPoint(push, new Vector(10f, 0f, 0f), Vector.Zero, (int)ForceComponents.Both, localSpace: true);
        M(t).SetActive(push, true);

        int result = t.Simulate(v.Attached.Keys.Single(), CrateObj, 0.015f, out Vector linear, out Vector angular);
        Assert.True(result == (int)MotionSimResult.LocalForce && linear.Y == -1f && linear.X == 0f && angular.Z == 2f,
                    $"world +x is the object's -y ({linear})");
    }

    [Fact]
    public void AttachAndDetachChangeWhatAVolumeActsOn()
    {
        var (t, v, _, _) = Make();
        uint medium = M(t).Create(EntityHandle.None, (int)MotionControllerKind.Medium);
        M(t).Attach(medium, Crate);
        Assert.Single(v.Attached.Values.Single());
        M(t).Detach(medium, Crate);
        Assert.Empty(v.Attached.Values.Single());
    }

    [Fact]
    public void ARampStartsFromWhereTheLastRampHadReached()
    {
        var (t, v, w, _) = Make();
        uint medium = M(t).Create(EntityHandle.None, (int)MotionControllerKind.Medium);
        M(t).SetMedium(medium, 1f, 0f, 100f, 0f, 1f, 0f, 1f, 0f, Vector.Zero);

        w.Now = 10f;
        M(t).RampLinearLimit(medium, 200f, 2f);   // 100 -> 200 over 2s
        w.Now = 11f;
        M(t).RampLinearLimit(medium, 0f, 4f);     // from 150 halfway

        var state = StateOf(t, medium);
        Assert.True(state.LinearLimitStart == 150f && state.LinearLimit == 0f && state.LinearLimitTime == 4f);
    }

    [Fact]
    public void DestroyFreesTheControllerAndSimulateThenAnswersNothing()
    {
        var (t, v, _, _) = Make();
        uint push = M(t).Create(Crate, (int)MotionControllerKind.ConstantForce);
        nint controller = v.Attached.Keys.Single();
        M(t).SetActive(push, true);
        M(t).SetForce(push, new Vector(1f, 0f, 0f), Vector.Zero, false);

        M(t).Destroy(push);
        M(t).Destroy(push);

        Assert.Single(v.Calls, c => c == $"DestroyMotionController {controller:x}");
        Assert.Equal((int)MotionSimResult.Nothing, t.Simulate(controller, CrateObj, 0.015f, out _, out _));
        Assert.Equal(0, M(t).Count());
    }

    [Fact]
    public void LevelShutdownDropsEveryControllerWithoutTouchingVphysics()
    {
        var (t, v, _, _) = Make();
        M(t).Create(Crate, (int)MotionControllerKind.ConstantForce);
        int calls = v.Calls.Count;
        t.LevelShutdown();
        Assert.True(M(t).Count() == 0 && v.Calls.Count == calls);
    }

    [Fact]
    public void AReloadCarriesEachControllerItsIdAndItsState()
    {
        var (t, v, w, log) = Make();
        uint motor = M(t).Create(Crate, (int)MotionControllerKind.Motor);
        nint controller = v.Attached.Keys.Single();
        M(t).SetMotor(motor, new Vector(0f, 0f, 1f), 30f, 1f, 0f);
        t.Simulate(controller, CrateObj, 0.015f, out _, out _); // feedback state: LastForce etc.

        var record = new ReloadRecord("motion");
        ((ISaveCustom)t).OnSave(new SaveRecordAccess(record, "motion", log));

        var next = new PhysicsMotionControllers(new PhysicsJointTable(), v, w, log) { Handler = Handler };
        ((ISaveCustom)next).OnRestore(new SaveRecordAccess(record, "motion", log));

        MotionState before = StateOf(t, motor), after = StateOf(next, motor);
        Assert.True(after.Speed == 30f && after.LastForce == before.LastForce && after.LastSpeed == before.LastSpeed
                    && after.Kind == MotionControllerKind.Motor);
        Assert.Equal((int)MotionSimResult.LocalForce, next.Simulate(controller, CrateObj, 0.015f, out _, out _));
    }

    private static MotionState StateOf(PhysicsMotionControllers t, uint id)
        => t.StateOf(id) ?? throw new InvalidOperationException("no controller");

    /// <summary>A custom Simulate that says what it was asked and answers a fixed push.</summary>
    private sealed class Pusher : IMotionSimulate
    {
        public readonly List<(nint Object, float Dt)> Asked = [];

        public MotionSimResult Simulate(nint physicsObject, float deltaTime, out Vector linear, out Vector angular)
        {
            Asked.Add((physicsObject, deltaTime));
            linear = new Vector(7f, 0f, 0f);
            angular = Vector.Zero;
            return MotionSimResult.GlobalAcceleration;
        }
    }

    [Fact]
    public void ACustomControllerIsAttachedToItsEntityAndSimulatedByItsOwner()
    {
        var (t, v, _, _) = Make();
        var pusher = new Pusher();

        uint id = t.CreateCustom(Crate, pusher);
        int result = t.Simulate(v.Attached.Keys.Single(), CrateObj, 0.015f, out Vector linear, out _);

        Assert.True(id != 0u && v.Attached.Values.Single().Single() == CrateObj,
                    "the controller exists and holds the crate's object");
        Assert.Equal(((int)MotionSimResult.GlobalAcceleration, 7f), (result, linear.X));
        Assert.Equal((CrateObj, 0.015f), pusher.Asked.Single());
    }

    [Fact]
    public void ACustomControllerForAnEntityWithNoPhysicsIsNotMade()
    {
        var (t, v, _, _) = Make();
        Assert.Equal(0u, t.CreateCustom(new EntityHandle(0x0001_0099), new Pusher()));
        Assert.Empty(v.Calls);
    }

    [Fact]
    public void AReloadDoesNotCarryACustomController()
    {
        var (t, v, w, log) = Make();
        M(t).Create(Crate, (int)MotionControllerKind.Motor);
        uint custom = t.CreateCustom(Crate, new Pusher());

        var record = new ReloadRecord("motion");
        ((ISaveCustom)t).OnSave(new SaveRecordAccess(record, "motion", log));

        var next = new PhysicsMotionControllers(new PhysicsJointTable(), v, w, log) { Handler = Handler };
        ((ISaveCustom)next).OnRestore(new SaveRecordAccess(record, "motion", log));

        Assert.True(M(next).Count() == 1 && next.StateOf(custom) is null,
                    "the motor crossed and the custom controller, whose Simulate is its owner's object, did not");
    }

    [Theory]
    [InlineData(MotionControllerKind.ConstantForce, MotionSimResult.GlobalForce)]
    [InlineData(MotionControllerKind.Motor, MotionSimResult.LocalForce)]
    [InlineData(MotionControllerKind.KeepUpright, MotionSimResult.LocalAcceleration)]
    [InlineData(MotionControllerKind.Medium, MotionSimResult.GlobalAcceleration)]
    public void EachKindsSimulateAllocatesNothing(MotionControllerKind kind, MotionSimResult expected)
    {
        // plan_physics_bones_perf B3: vphysics asks once per object per tick.
        var (t, _, _, _) = Make();
        MotionState state = PhysicsCostBench.BenchState(kind);
        MotionSimResult result = default;
        long allocated = SteadyAllocation.LeastAllocated(
            () => result = t.SimulateState(state, CrateObj, 0.015f, out _, out _));

        Assert.Equal((expected, 0L), (result, allocated));
    }

    [Theory]
    [InlineData(MotionControllerKind.ConstantForce, MotionSimResult.GlobalForce)]
    [InlineData(MotionControllerKind.Motor, MotionSimResult.LocalForce)]
    [InlineData(MotionControllerKind.KeepUpright, MotionSimResult.LocalAcceleration)]
    [InlineData(MotionControllerKind.Medium, MotionSimResult.GlobalAcceleration)]
    public void EachBenchStateTimesAnAppliedAnswerOfItsKind(MotionControllerKind kind, MotionSimResult expected)
    {
        // cs_sv_physics_bench times SimulateState from these states: each must
        // reach its kind's applied answer, or the bench times the early out.
        var (t, _, _, _) = Make();
        MotionState state = PhysicsCostBench.BenchState(kind);

        Assert.Equal(kind, state.Kind);
        Assert.Equal(expected, t.SimulateState(state, CrateObj, 0.015f, out _, out _));
    }
}
