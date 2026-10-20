using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// phys_thruster and phys_torque against stock's CPhysForce family
/// (phys_controller.cpp): an ACCELERATION integrator (CPhysForce::Spawn), the
/// thruster's frame taken once at activation (CPhysThruster::OnActivate), its
/// push and twist from a force at that point (SetupForces), and the torque's
/// axis in the attached object's frame. Stock computes SetupForces once per
/// activation, not per tick: a local-orientation thruster turns with the
/// object because its answer is in the object's frame (SIM_LOCAL_ACCELERATION).
/// </summary>
public class ThrusterStockTests
{
    private static readonly EntityHandle Crate = new(0x0001_00B1);
    private static readonly EntityHandle Thruster = new(0x0001_00B2);
    private static readonly EntityHandle Torquer = new(0x0001_00B3);

    private const int StartActive = 1, ApplyForce = 2, ApplyTorque = 4, LocalOrientation = 8, IgnorePosition = 32;

    /// <summary>A side where the crate and the thruster each have their own pose, read per handle.</summary>
    private sealed class Poses
    {
        public readonly Dictionary<EntityHandle, Vector> Origin = new();
        public readonly Dictionary<EntityHandle, Vector> Angles = new();
    }

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine, Poses Poses) Side()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysThruster).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        var poses = new Poses();
        engine.Returns("Entities.IsValid", true);
        engine.ReturnsEachCall("Entities.FindByName", () => (object?)Crate);
        engine.Returns("EntityPhysics.GetMass", 10f);
        engine.Returns("MotionControllers.Create", 0x0002_0040u);
        engine.ReturnsEachCall("Entities.GetOrigin", () => poses.Origin.GetValueOrDefault(engine.Calls[^1].ArgAs<EntityHandle>(0)));
        engine.ReturnsEachCall("Entities.GetAngles", () => poses.Angles.GetValueOrDefault(engine.Calls[^1].ArgAs<EntityHandle>(0)));
        return (registry, engine, poses);
    }

    /// <summary>What the fake controller answers a force at a point decomposes to.</summary>
    private static readonly (Vector Linear, Vector Angular) Answer = (new Vector(5f, 0f, 0f), new Vector(0f, 0f, 2f));

    private static int Count(RecordingEngine engine, string member) => engine.Calls.Count(c => c.Member == member);

    private static EngineCall ThrusterAttached(int flags, Vector thrusterAt, Vector thrusterAngles,
                                               out ScriptedEntityRegistry registry, out RecordingEngine engine, out Poses poses)
    {
        (registry, engine, poses) = Side();
        poses.Origin[Crate] = new Vector(100f, 0f, 0f);
        poses.Origin[Thruster] = thrusterAt;
        registry.KeyValue(Thruster, EntityNames.PhysThruster, "attach1", "crate");
        registry.KeyValue(Thruster, EntityNames.PhysThruster, "force", "50");
        // Started on: stock decomposes only at ForceOn (CPhysForce::ActivateForce).
        engine.Returns("Entities.GetSpawnFlags", flags | StartActive);
        engine.Returns("Entities.GetSpawnAngles", thrusterAngles);
        engine.Returns("MotionControllers.SetForceAtPoint", (Answer.Linear, Answer.Angular));
        registry.Spawn(Thruster, EntityNames.PhysThruster);
        registry.Think(Thruster);
        return engine.Last("MotionControllers.SetForceAtPoint")!.Value;
    }

    [Fact]
    public void AThrustersForceIsAnAccelerationAsStocksIntegratorIs()
    {
        ThrusterAttached(ApplyForce, new Vector(100f, 0f, 10f), new Vector(0f, 0f, 0f), out _, out var engine, out _);
        var flag = engine.Last("MotionControllers.SetForceIsAcceleration")!.Value;
        Assert.True(flag.ArgAs<bool>(1), "CPhysForce::Spawn: SIM_*_ACCELERATION");
    }

    [Fact]
    public void AThrusterPushesAtItsOwnPointAlongItsFacing()
    {
        var push = ThrusterAttached(ApplyForce | ApplyTorque, new Vector(100f, 0f, 10f), new Vector(0f, 90f, 0f),
                                    out _, out _, out _);
        Vector force = push.ArgAs<Vector>(1), point = push.ArgAs<Vector>(2);
        Assert.True(MathF.Abs(force.Y - 50f) < 1e-3f && MathF.Abs(point.Z - 10f) < 1e-4f && MathF.Abs(point.X - 100f) < 1e-4f,
                    $"yaw 90 pushes along +y at the thruster ({force} at {point})");
        Assert.False(push.ArgAs<bool>(4), "without SF_THRUST_LOCAL_ORIENTATION the answer is the world's");
    }

    [Fact]
    public void IgnoringPositionPushesAtTheAttachedObjectsOrigin()
    {
        var push = ThrusterAttached(ApplyForce | IgnorePosition, new Vector(100f, 0f, 10f), Vector.Zero,
                                    out _, out _, out _);
        Vector point = push.ArgAs<Vector>(2);
        Assert.True(point.X == 100f && point.Z == 0f, "stock zeroes m_localOrigin: the attached object's own origin");
        Assert.Equal((int)ForceComponents.Push, push.ArgAs<int>(3));
    }

    [Fact]
    public void ALocalThrustersFrameIsTheOneItHadAtActivation()
    {
        // Activated with the crate unrotated, thruster 10 above it, pushing +x.
        ThrusterAttached(ApplyForce | LocalOrientation, new Vector(100f, 0f, 10f), Vector.Zero,
                         out var registry, out var engine, out var poses);

        // The crate has since turned 90 about z; switching off and on again
        // decomposes afresh (stock's ForceOn runs ActivateForce each time).
        poses.Angles[Crate] = new Vector(0f, 90f, 0f);
        registry.Input(Thruster, "Deactivate", "", 0f, EntityHandle.None);
        registry.Input(Thruster, "Activate", "", 0f, EntityHandle.None);

        var push = engine.Last("MotionControllers.SetForceAtPoint")!.Value;
        Vector force = push.ArgAs<Vector>(1);
        Assert.True(push.ArgAs<bool>(4), "SF_THRUST_LOCAL_ORIENTATION: answered in the object's frame");
        Assert.True(MathF.Abs(force.Y - 50f) < 1e-3f && MathF.Abs(force.X) < 1e-3f,
                    $"the push turned with the crate it is bolted to ({force})");
    }

    [Fact]
    public void AScaleScalesTheActivationAnswerAndDoesNotDecomposeAgain()
    {
        // CPhysForce::ScaleForce -> ScaleConstantForce: m_linearSave * scale,
        // the answer SetupForces gave at ForceOn, whatever the object has done since.
        ThrusterAttached(ApplyForce | ApplyTorque, new Vector(100f, 0f, 10f), Vector.Zero,
                         out var registry, out var engine, out var poses);
        Assert.Equal(1, Count(engine, "MotionControllers.SetForceAtPoint"));

        poses.Angles[Crate] = new Vector(0f, 90f, 0f);
        registry.Input(Thruster, "scale", "0.5", 0.5f, EntityHandle.None);

        Assert.Equal(1, Count(engine, "MotionControllers.SetForceAtPoint"));
        var scaled = engine.Last("MotionControllers.SetForce")!.Value;
        Assert.Equal(new Vector(2.5f, 0f, 0f), scaled.ArgAs<Vector>(1));
        Assert.Equal(new Vector(0f, 0f, 1f), scaled.ArgAs<Vector>(2));
        Assert.False(scaled.ArgAs<bool>(3), "a world-frame thruster's answer stays the world's");
    }

    [Fact]
    public void AScaleOnAnOffThrusterSwitchesItOnFirst()
    {
        // ScaleForce: if ( !m_pController ) ForceOn();
        var (registry, engine, poses) = Side();
        poses.Origin[Crate] = new Vector(100f, 0f, 0f);
        poses.Origin[Thruster] = new Vector(100f, 0f, 10f);
        registry.KeyValue(Thruster, EntityNames.PhysThruster, "attach1", "crate");
        registry.KeyValue(Thruster, EntityNames.PhysThruster, "force", "50");
        engine.Returns("Entities.GetSpawnFlags", ApplyForce);
        engine.Returns("MotionControllers.SetForceAtPoint", (Answer.Linear, Answer.Angular));
        registry.Spawn(Thruster, EntityNames.PhysThruster);
        registry.Think(Thruster);
        Assert.Equal(0, Count(engine, "MotionControllers.SetForceAtPoint"));

        registry.Input(Thruster, "scale", "2", 2f, EntityHandle.None);

        Assert.Equal(1, Count(engine, "MotionControllers.SetForceAtPoint"));
        Assert.True(engine.Last("MotionControllers.SetActive")!.Value.ArgAs<bool>(1), "the force is on");
        Assert.Equal(new Vector(10f, 0f, 0f), engine.Last("MotionControllers.SetForce")!.Value.ArgAs<Vector>(1));
    }

    [Fact]
    public void ActivatingAThrusterThatIsOnDoesNothing()
    {
        // ForceOn: if ( m_pController ) return;
        ThrusterAttached(ApplyForce, new Vector(100f, 0f, 10f), Vector.Zero, out var registry, out var engine, out _);
        int active = Count(engine, "MotionControllers.SetActive");

        registry.Input(Thruster, "Activate", "", 0f, EntityHandle.None);

        Assert.Equal(1, Count(engine, "MotionControllers.SetForceAtPoint"));
        Assert.Equal(active, Count(engine, "MotionControllers.SetActive"));
    }

    [Fact]
    public void ATorquesAxisIsInTheAttachedObjectsFrameAndAnAcceleration()
    {
        var (registry, engine, poses) = Side();
        poses.Origin[Torquer] = Vector.Zero;
        poses.Angles[Crate] = new Vector(0f, 90f, 0f); // the crate yawed 90
        registry.KeyValue(Torquer, EntityNames.PhysTorque, "attach1", "crate");
        registry.KeyValue(Torquer, EntityNames.PhysTorque, "force", "20");
        registry.KeyValue(Torquer, EntityNames.PhysTorque, "axis", "64 0 0"); // about world +x
        engine.Returns("Entities.GetSpawnFlags", StartActive);
        registry.Spawn(Torquer, EntityNames.PhysTorque);
        registry.Think(Torquer);

        var twist = engine.Last("MotionControllers.SetForce")!.Value;
        Vector angular = twist.ArgAs<Vector>(2);

        // World +x seen from a crate yawed 90 is its local -y (y is its left).
        Assert.True(twist.ArgAs<bool>(3) && MathF.Abs(angular.Y - -20f) < 1e-3f && MathF.Abs(angular.X) < 1e-3f,
                    $"VectorIRotate( m_axis, matrix ) * force ({angular})");
        Assert.True(engine.Last("MotionControllers.SetForceIsAcceleration")!.Value.ArgAs<bool>(1));
    }
}
