using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The powered entities across a hot reload: the axis, the running flag, the
/// scale, the stop timer, and the one controller.
///
/// Stock saves the motor family wholesale — m_motor is DEFINE_PHYSPTR
/// rebuilt on restore, m_fOn/m_scale/m_flStopTime are saved fields
/// (phys_controller.cpp's DATADESCs). The native controller outlives the
/// assembly swap (shell_motioncontrollers.cpp clears the table only at
/// LevelShutdown), so the pre-fix rebuild came back with none of it:
/// <c>_direction</c> is computed only in OnSpawn, which a reload does not
/// run, so the rebuilt fan pushed its motor along (0,0,0) — a silent stop —
/// and re-attached a SECOND controller beside the first.
/// </summary>
public class ForceEntitiesReloadTests
{
    private static readonly EntityHandle Motor = new(0x0001_0090);
    private static readonly EntityHandle Fan = new(0x0001_0091);
    private static readonly EntityHandle Thruster = new(0x0001_0092);
    private static readonly EntityHandle Torquer = new(0x0001_0094);

    private const uint MotorController = 0x0002_0010u;
    private const uint ThrusterController = 0x0002_0011u;

    private readonly ReloadStore _reload = new(Abi.Side.Server);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine)
        Side(float now = 10f, uint controller = MotorController)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysMotor).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Clock.Now", now);
        engine.Returns("Entities.IsValid", true);
        engine.ReturnsEachCall("Entities.FindByName", () => (object?)Fan);
        engine.Returns("EntityPhysics.GetMass", 10f);
        engine.ReturnsEachCall("MotionControllers.Create", () => (object?)controller);
        return (registry, engine);
    }

    [Fact]
    public void A_running_fan_keeps_its_axis_and_its_one_controller()
    {
        var (before, engine) = Side();
        before.KeyValue(Motor, EntityNames.PhysMotor, "targetname", "motor");
        before.KeyValue(Motor, EntityNames.PhysMotor, "attach1", "fan");
        before.KeyValue(Motor, EntityNames.PhysMotor, "axis", "0 0 64");
        before.KeyValue(Motor, EntityNames.PhysMotor, "speed", "1500");
        before.KeyValue(Motor, EntityNames.PhysMotor, "spinup", "3");
        engine.Returns("Entities.GetSpawnFlags", 1); // start on
        before.Spawn(Motor, EntityNames.PhysMotor);
        before.Think(Motor); // attach + first push

        var first = engine.Calls.Where(c => c.Member == "MotionControllers.SetMotor").ToArray();
        Assert.True(first.Length >= 1, "the control: it attached and pushed");
        Assert.Equal(new Vector(0f, 0f, 1f), first[^1].ArgAs<Vector>(1));
        Assert.True(engine.Called("MotionControllers.Create"), "the control: it attached");
        Assert.True(engine.Called("MotionControllers.Create"), "the control: it attached and pushed");

        _reload.Clear();
        Assert.Equal(1, before.SaveForReload(_reload));

        var (after, back) = Side();
        Assert.Equal(1, after.RestoreAfterReload(_reload));
        after.Think(Motor); // the fan's think still runs: it re-pushes

        Assert.False(back.Called("MotionControllers.Create"),
                    "the reload re-adopts the surviving controller; it does not stack a second");

        var push = Assert.Single(back.Calls, c => c.Member == "MotionControllers.SetMotor");
        Assert.Equal(MotorController, push.ArgAs<uint>(0));
        Assert.True(push.ArgAs<Vector>(1).Equals(new Vector(0f, 0f, 1f)),
                    $"a running fan re-pushes along its axis, not the zeroed default ({push.ArgAs<Vector>(1)})");
    }

    [Fact]
    public void A_thruster_mid_stop_timer_stops_after_the_reload()
    {
        var (before, engine) = Side(controller: ThrusterController);
        before.KeyValue(Thruster, EntityNames.PhysThruster, "targetname", "thruster");
        before.KeyValue(Thruster, EntityNames.PhysThruster, "attach1", "fan");
        before.KeyValue(Thruster, EntityNames.PhysThruster, "force", "100");
        before.KeyValue(Thruster, EntityNames.PhysThruster, "forcetime", "0.5");
        engine.Returns("Entities.GetSpawnFlags", 2); // ApplyForce (the timed start comes below)
        engine.Returns("Entities.GetSpawnAngles", new Vector(0f, 0f, 0f));
        engine.Returns("MotionControllers.SetForceAtPoint", (new Vector(10f, 0f, 0f), new Vector(0f, 0f, 4f)));
        before.Spawn(Thruster, EntityNames.PhysThruster);
        before.Think(Thruster); // attach
        before.Input(Thruster, "Activate", "", 0f, EntityHandle.None); // ForceOn: decompose, burn for 0.5 s

        Assert.Single(engine.Calls, c => c.Member == "MotionControllers.SetForceAtPoint");

        _reload.Clear();
        before.SaveForReload(_reload);

        // Still inside the burn window when it comes back; the carried
        // _stopping must make the next think cut it.
        var (after, back) = Side(now: 10.1f, controller: ThrusterController);
        Assert.Equal(1, after.RestoreAfterReload(_reload));

        // The activation's answer is carried (stock saves m_linearSave and
        // m_angularSave): a scale inside the burn scales it, with no new
        // decomposition. The amnesiac instance scaled a zero.
        after.Input(Thruster, "scale", "0.5", 0.5f, EntityHandle.None);
        Assert.False(back.Called("MotionControllers.SetForceAtPoint"), "a scale does not decompose again");
        var scaled = Assert.Single(back.Calls, c => c.Member == "MotionControllers.SetForce");
        Assert.Equal(new Vector(5f, 0f, 0f), scaled.ArgAs<Vector>(1));
        Assert.Equal(new Vector(0f, 0f, 2f), scaled.ArgAs<Vector>(2));

        after.Think(Thruster);

        Assert.False(back.Called("MotionControllers.Create"),
                    "it drives the controller the reload left standing, not a new one");

        var off = Assert.Single(back.Calls, c => c.Member == "MotionControllers.SetActive");
        Assert.Equal(ThrusterController, off.ArgAs<uint>(0));
        Assert.True(off.ArgAs<bool>(1) == false, "the carried stop timer still fires: the thruster cuts out");
    }

    [Fact]
    public void A_torque_keeps_its_controller_and_its_ramp()
    {
        var (before, engine) = Side();
        before.KeyValue(Torquer, EntityNames.PhysTorque, "targetname", "torque");
        before.KeyValue(Torquer, EntityNames.PhysTorque, "attach1", "fan");
        before.KeyValue(Torquer, EntityNames.PhysTorque, "force", "200");
        before.KeyValue(Torquer, EntityNames.PhysTorque, "axis", "0 0 64");
        engine.Returns("Entities.GetSpawnFlags", 1); // StartActive
        before.Spawn(Torquer, EntityNames.PhysTorque);
        before.Think(Torquer);

        before.Input(Torquer, "scale", "0.2", 0.2f, EntityHandle.None);
        var scaled = engine.Calls.Where(c => c.Member == "MotionControllers.SetForce").ToArray();
        Assert.NotEmpty(scaled);
        float scaledTwist = scaled[^1].ArgAs<Vector>(2).Z;
        Assert.True(MathF.Abs(MathF.Abs(scaledTwist) - 40f) < 0.01f,
                    $"the control: scale ramped the twist down ({scaledTwist})");

        _reload.Clear();
        before.SaveForReload(_reload);

        var (after, back) = Side();
        Assert.Equal(1, after.RestoreAfterReload(_reload));
        after.Think(Torquer);

        Assert.False(back.Called("MotionControllers.Create"),
                    "no second controller beside the one still driving the fan");

        // The scale is carried; and because the native controller KEEPS its
        // force across the reload (only managed code could re-issue it), a
        // restored torque that re-pushed would be pushing at the forgotten
        // scale — so asserting the rebuilt instance stays silent until asked
        // is what pins the whole pair.
        Assert.False(back.Called("MotionControllers.SetForce"),
                    "a restored torque does not re-issue at a forgotten scale; native holds the scaled twist");
    }
}
