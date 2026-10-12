using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Re-adopting a live native joint/controller by its saved id — the reload
/// spelling of Make/Attach.
///
/// Stock re-wires its native pointers in OnRestore: CPhysMagnet's
/// m_MagnettedEntities (physobj.cpp:1494-1512, the constraint pointers rebuilt
/// from the saved entities) and CPhysThruster/CPhysTorque's DEFINE_PHYSPTR
/// m_motor (phys_controller.cpp's DATADESC, the controller rebuilt in
/// Activate). The managed equivalents need the same re-adopt: the solver's
/// objects survive a cs_sv_reload while the wrappers do not, and without
/// Adopt the rebuilt entity can only destroy-and-recreate — which re-captures
/// joint poses and leaves the OLD native joint unbreakable.
/// </summary>
public class AdoptWrapperTests
{

    private static RecordingEngine Engine()
    {
        _ = Services(out var engine);
        engine.Strict = false;
        return engine;
    }

    private static IEngineConstraints Constraints(RecordingEngine engine) => new RecordingJointTables(engine);
    private static IEngineMotionControllers Motion(RecordingEngine engine) => new RecordingJointTables(engine);

    [Fact]
    public void An_adopted_joint_answers_and_destroys()
    {
        var engine = Engine();
        var id = new PhysicsId(0x0001_0003u);
        engine.Returns("Constraints.IsValid", true);

        Constraint joint = Constraint.Adopt(Constraints(engine), id, ConstraintKind.Fixed);

        Assert.True(joint.Id == id, "the id it was adopted with is its own");
        Assert.True(joint.IsValid, "and a live native entry reads live");

        joint.Destroy();

        var destroy = Assert.Single(engine.Calls, c => c.Member == "Constraints.Destroy");
        Assert.Equal(id.Value, destroy.ArgAs<uint>(0));
    }

    [Fact]
    public void An_adopted_joint_whose_native_entry_is_gone_answers_invalid()
    {
        // The level change already dropped the table: a saved id that no longer
        // resolves must read invalid, exactly as a made-then-dropped joint
        // would — no throw anywhere on the way.
        var engine = Engine();
        engine.Returns("Constraints.IsValid", false);

        Constraint joint = Constraint.Adopt(Constraints(engine), new PhysicsId(0x0001_0003u),
                                            ConstraintKind.Hinge);

        Assert.True(!joint.IsValid, "the native side says the slot is gone");

        // Destroy on it is safe (the guard is the wrapper's own Id, still live
        // until cleared), and a NONE id is inert.
        Constraint nothing = Constraint.Adopt(Constraints(engine), PhysicsId.None, ConstraintKind.Fixed);
        Assert.True(nothing.Id.IsNone, "adopting NONE is legal and makes nothing");
        nothing.Destroy();
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Constraints.Destroy"
                                                 && c.ArgAs<uint>(0) == 0u);
    }

    [Fact]
    public void An_adopted_controller_answers_and_drives()
    {
        var engine = Engine();
        var id = new PhysicsId(0x0002_0005u);

        MotionController controller = MotionController.Adopt(Motion(engine), id,
                                                              MotionControllerKind.ConstantForce);

        Assert.True(controller.Id == id, "the saved id came back whole");

        controller.SetForce(new Vector(1f, 2f, 3f), Vector.Zero);

        var call = Assert.Single(engine.Calls, c => c.Member == "MotionControllers.SetForce");
        Assert.Equal(id.Value, call.ArgAs<uint>(0));
    }

    [Fact]
    public void Adopting_makes_no_create_call()
    {
        // The control that Adopt is not a second Make: the native table gets
        // one query at most (IsValid) and no Create — the whole point is the
        // object already exists.
        var engine = Engine();
        engine.Returns("MotionControllers.IsValid", true);

        Constraint.Adopt(Constraints(engine), new PhysicsId(0x0001_0003u), ConstraintKind.Fixed);
        MotionController.Adopt(Motion(engine), new PhysicsId(0x0002_0005u), MotionControllerKind.Motor);

        Assert.False(engine.Called("Constraints.Create"));
        Assert.False(engine.Called("MotionControllers.Create"));
    }

    [Fact]
    public void An_adopted_spring_answers_destroys_and_a_stale_one_reads_invalid()
    {
        var engine = Engine();
        IEngineSprings springs = new RecordingJointTables(engine);
        var id = new PhysicsId(0x0003_0004u);
        engine.Returns("Springs.IsValid", true);

        Spring spring = Spring.Adopt(springs, id);
        Assert.True(spring.Id == id, "the saved id came back whole");
        Assert.True(spring.IsValid, "a live native entry reads live");
        spring.Destroy();
        var destroy = Assert.Single(engine.Calls, c => c.Member == "Springs.Destroy");
        Assert.Equal(id.Value, destroy.ArgAs<uint>(0));

        engine.Returns("Springs.IsValid", false);
        Assert.False(Spring.Adopt(springs, new PhysicsId(0x0003_0009u)).IsValid,
                     "a slot the level change dropped reads invalid without throwing");
        Assert.False(Spring.Adopt(springs, PhysicsId.None).IsValid, "NONE is inert");
        Assert.False(engine.Called("Springs.Create"), "adopting makes nothing");
    }
}
