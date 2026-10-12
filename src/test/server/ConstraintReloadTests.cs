using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A constraint entity that survives the reload still owns its joint.
///
/// Stock keeps the joint in DEFINE_PHYSPTR m_pConstraint — a SAVED field
/// rebuilt in Activate (physconstraint.cpp) — so after any restore the
/// entity can still break its own joint. The port dropped the id: the
/// rebuilt entity had Joint == null, and the "Break" input then no-oped on
/// Joint?.Destroy() while still firing OnBreak and removing itself — the
/// map was told the joint broke and the objects stayed welded. The spring
/// is the same shape on the solver's spring table.
/// </summary>
public class ConstraintReloadTests
{
    private static readonly EntityHandle Socket = new(0x0001_00A0);
    private static readonly EntityHandle Crate = new(0x0001_00A1);
    private static readonly EntityHandle Springy = new(0x0001_00A2);

    private const uint JointId = 0x0001_0030u;
    private const uint SpringId = 0x0003_0004u;

    private readonly ReloadStore _reload = new(Abi.Side.Server);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) SocketSide(bool build = true)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysBallSocket).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Constraints.Create", JointId);
        engine.Returns("Constraints.IsValid", true);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        engine.ReturnsEachCall("Entities.FindByName", () => (object?)Crate);

        registry.KeyValue(Socket, EntityNames.PhysBallsocket, "attach1", "crate");
        registry.KeyValue(Socket, EntityNames.PhysBallsocket, "attach2", "crate");
        registry.Spawn(Socket, EntityNames.PhysBallsocket);
        if (!build)
            return (registry, engine); // the rebuilt side: restored, not yet thought

        registry.Think(Socket); // build the joint
        Assert.True(engine.Called("Constraints.Create"), "the control: the joint was built");
        return (registry, engine);
    }

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) SpringSide(bool build = true)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysSpring).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Springs.Create", SpringId);
        engine.Returns("Springs.IsValid", true);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        engine.ReturnsEachCall("Entities.FindByName", () => (object?)Crate);

        registry.KeyValue(Springy, EntityNames.PhysSpring, "attach1", "crate");
        registry.KeyValue(Springy, EntityNames.PhysSpring, "attach2", "crate");
        registry.Spawn(Springy, EntityNames.PhysSpring);
        if (!build)
            return (registry, engine); // the rebuilt side: restored, not yet thought

        registry.Think(Springy); // build the spring
        Assert.True(engine.Called("Springs.Create"), "the control: the spring was made");
        return (registry, engine);
    }

    [Fact]
    public void Break_after_the_reload_destroys_the_real_joint()
    {
        var (before, _) = SocketSide();

        _reload.Clear();
        Assert.Equal(1, before.SaveForReload(_reload));

        var (after, back) = SocketSide(build: false);
        Assert.Equal(1, after.RestoreAfterReload(_reload));

        // The deferred build think must not weld a second joint over the one
        // the reload left standing.
        after.Think(Socket);
        Assert.False(back.Called("Constraints.Create"), "no second joint after the reload");

        // The pre-fix shape: this fired OnBreak and removed the entity while
        // the joint stayed in the solver — objects welded, map told.
        after.Input(Socket, "Break", "", 0f, EntityHandle.None);

        var destroy = Assert.Single(back.Calls, c => c.Member == "Constraints.Destroy");
        Assert.Equal(JointId, destroy.ArgAs<uint>(0));
        Assert.True(((PhysBallSocket)after.Get(Socket)!).OnBreak.FireCount == 1,
                    "and the break still announced itself");
    }

    [Fact]
    public void The_solver_breaking_a_reloaded_joint_is_owned_by_the_entity()
    {
        // OnPhysicsConstraintBroken compares against the wrapper's id; with no
        // carried id the rebuilt entity disowned its own joint, so the break
        // went unanswered forever.
        var (before, _) = SocketSide();

        _reload.Clear();
        before.SaveForReload(_reload);

        var (after, _) = SocketSide(build: false);
        Assert.Equal(1, after.RestoreAfterReload(_reload));

        Assert.True(after.ConstraintBroken(new PhysicsId(JointId)),
                    "the rebuilt entity still owns its joint");
    }

    [Fact]
    public void The_solver_still_owns_the_spring_and_its_settings_reach_native()
    {
        var (before, _) = SpringSide();

        _reload.Clear();
        before.SaveForReload(_reload);

        var (after, back) = SpringSide(build: false);
        Assert.Equal(1, after.RestoreAfterReload(_reload));

        after.Input(Springy, "SetSpringConstant", "300", 0f, EntityHandle.None);

        var set = Assert.Single(back.Calls, c => c.Member == "Springs.SetConstant");
        Assert.Equal(SpringId, set.ArgAs<uint>(0));
        Assert.True(MathF.Abs(set.ArgAs<float>(1) - 300f) < 0.01f,
                    "the input reaches the spring the reload left standing");
    }
}
