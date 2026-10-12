using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The motor's spin-up, which is the half of phys_motor that is managed.
///
/// The other half — working out what torque reaches a given speed against a
/// load — is a feedback loop that runs every physics tick and stays native, so
/// it is not tested here and could not usefully be: it is only meaningful
/// against a real physics object.
///
/// What IS here is the arithmetic that decides how fast the ramp climbs and
/// where the axis points, both of which are ordinary functions of the map's
/// numbers and both of which would be wrong silently.
/// </summary>
public class MotorTests
{
    private static readonly EntityHandle Ent = new(1);

    [Fact]
    public void Ramp()
    {
        // Approach is a magnitude step towards a target, and the case that
        // matters is the last one: overshooting the target and oscillating
        // around it would leave a fan that never settles.
        Assert.True(PhysMotor.Approach(100f, 0f, 25f) == 25f, "the ramp climbs by the step");
        Assert.True(PhysMotor.Approach(100f, 90f, 25f) == 100f, "the ramp lands exactly on the target");
        Assert.True(PhysMotor.Approach(100f, 99f, 25f) == 100f, "the ramp does not overshoot");

        // Slowing down is the same step in the other direction, which is what
        // lets one function serve both spinning up and spinning down.
        Assert.True(PhysMotor.Approach(0f, 100f, 25f) == 75f, "the ramp falls towards a lower target");
        Assert.True(PhysMotor.Approach(0f, 10f, 25f) == 0f, "the ramp lands exactly when falling");

        // A zero step happens on the first think of a resumed motor, when no
        // time has passed yet. It must hold, not jump.
        Assert.True(PhysMotor.Approach(100f, 40f, 0f) == 40f, "a zero step does not move");
    }

    [Fact]
    public void Acceleration()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysMotor).Assembly);
        registry.Services = Ignoring("Entities.GetOrigin");
        registry.Spawn(Ent, EntityNames.PhysMotor);

        var motor = (PhysMotor)registry.Get(Ent)!;

        // Spin-up is a TIME, so the acceleration is the speed divided by it.
        motor.Configure(targetSpeed: 1500f, spinUp: 3f);
        motor.RecalculateAcceleration();
        Assert.True(motor.Acceleration == 500f, "spin-up time divides the target speed");

        // No spin-up means arrive at once. Stock expresses that as an
        // acceleration equal to the speed, which covers it in a single second —
        // near enough to immediate, and it avoids a division by zero.
        motor.Configure(targetSpeed: 1500f, spinUp: 0f);
        motor.RecalculateAcceleration();
        Assert.True(motor.Acceleration == 1500f, "no spin-up accelerates at the target speed");

        // Always a magnitude. The controller decides direction from the sign of
        // the error, and a negative acceleration would ramp the wrong way.
        motor.Configure(targetSpeed: -1500f, spinUp: 3f);
        motor.RecalculateAcceleration();
        Assert.True(motor.Acceleration == 500f, "a reversed motor still accelerates positively");
    }

    [Fact]
    public void AxisSnapping()
    {
        // A mapper places the axis by eye. A hinge a thousandth off vertical
        // makes the thing wander as well as turn, which is why both the hinge
        // and the motor snap.
        var nearlyUp = new Vector(0.0005f, -0.0005f, 0.9999f).Normalized;
        var snapped = PhysMotor.SnapToAxis(nearlyUp);

        Assert.True(snapped.X == 0f && snapped.Y == 0f && snapped.Z == 1f, "a nearly vertical axis snaps to vertical");

        var nearlyBack = new Vector(-0.99999f, 0.0001f, 0f).Normalized;
        Assert.True(PhysMotor.SnapToAxis(nearlyBack).X == -1f, "the snap keeps the direction's sign");

        // A deliberately diagonal axis is left alone: snapping it would silently
        // rebuild the map's geometry.
        var diagonal = new Vector(1f, 1f, 0f).Normalized;
        var kept = PhysMotor.SnapToAxis(diagonal);
        Assert.True(kept.X == diagonal.X && kept.Y == diagonal.Y, "a diagonal axis is left as drawn");
    }

    // plan_physics_managed A26: CPhysTorque::Spawn reads "axis" as a POINT, as
    // Hammer writes it: m_axis -= GetAbsOrigin(); VectorNormalize;
    // UTIL_SnapDirectionToAxis (phys_controller.cpp:437-447). Taken as a
    // direction, nearly every map spun its prop about the wrong axis.
    [Fact]
    public void A_torques_axis_is_the_point_minus_its_origin_normalised_and_snapped()
    {
        var origin = new Vector(10f, 20f, 30f);

        Assert.Equal(new Vector(0f, 0f, 1f), PhysTorque.TwistAxis(origin + new Vector(0f, 0f, 64f), origin));
        Assert.Equal(new Vector(-1f, 0f, 0f), PhysTorque.TwistAxis(origin + new Vector(-50f, 0.01f, 0f), origin));

        Vector diagonal = PhysTorque.TwistAxis(origin + new Vector(3f, 4f, 0f), origin);
        Assert.Equal(0.6f, diagonal.X, 5);
        Assert.Equal(0.8f, diagonal.Y, 5);
    }

    // A27: CKeepUpright's constructor sets m_angularLimit = 15
    // (phys_controller.cpp:909), the value a map that omits "angularlimit" gets.
    // A default of 0 righted nothing.
    [Fact]
    public void Keep_upright_defaults_to_stocks_fifteen_degrees_per_second()
        => Assert.Equal(15f, new PhysKeepUprightVars().AngularLimit);

    // A27: CPhysMotor::InputSetTargetSpeed -> TargetSpeedChanged schedules the
    // think, and Think ramps toward the speed with no "on" gate
    // (phys_controller.cpp:640-680, 825-837). SF_MOTOR_START_ON only decides
    // whether the motor turns by itself at activation. A motor a map drives
    // with SetSpeed never turned here: OnThink returned while not running.
    [Fact]
    public void A_motor_without_start_on_turns_when_a_map_sets_its_speed()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysMotor).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        var crate = new EntityHandle(0x0001_0060);
        engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        engine.Returns("Entities.FindByName", crate);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("MotionControllers.Create", 0x0001_0007u);
        engine.Returns("Entities.GetSpawnFlags", 0);
        engine.Returns("Clock.Now", 10f);

        registry.KeyValue(Ent, EntityNames.PhysMotor, "attach1", "crate");
        registry.KeyValue(Ent, EntityNames.PhysMotor, "axis", "0 0 64");
        registry.KeyValue(Ent, EntityNames.PhysMotor, "spinup", "1");
        registry.Spawn(Ent, EntityNames.PhysMotor);
        registry.Think(Ent);   // attaches
        Assert.True(engine.Called("MotionControllers.Create"), "the control: the motor attached");

        registry.Input(Ent, "SetSpeed", "100", 100f, EntityHandle.None);
        engine.Returns("Clock.Now", 10.5f);
        registry.Think(Ent);

        float lastSpeed = engine.Calls.Last(c => c.Member == "MotionControllers.SetMotor").ArgAs<float>(2);
        Assert.True(lastSpeed > 0f, $"the motor ramps toward 100 (pushed {lastSpeed})");
    }
}
