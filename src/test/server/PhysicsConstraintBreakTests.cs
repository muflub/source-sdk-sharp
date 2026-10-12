using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed A23: a constraint the solver broke is the owning
/// entity's break. Stock: CPhysConstraintEvents::ConstraintBroken sends the
/// owner "ConstraintBroken" (physconstraint.cpp:1602-1618), which is
/// CPhysConstraint::OnBreak (:270-317): Deactivate, the breaksound at the
/// midpoint of the two objects, m_OnBreak, and the entity removed
/// (PhysCallbackRemove). The Break input is the same path (:319).
/// </summary>
public class PhysicsConstraintBreakTests
{
    private const uint JointId = 0x0001_0003u;
    private static readonly EntityHandle Socket = new(0x0001_0040);
    private static readonly EntityHandle Crate = new(0x0001_0041);
    private static readonly EntityHandle Plank = new(0x0001_0042);

    private static (ScriptedEntityRegistry Registry, PhysBallSocket Entity, RecordingEngine Engine) Built(string breakSound = "",
        params (string Key, string Value)[] keys)
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
        // A factory reads the call it answers: the call is recorded first.
        object? LastArg() => engine.Calls[^1].Args[0];
        engine.ReturnsEachCall("Entities.FindByName", () => (string?)LastArg() == "crate" ? Crate : Plank);
        engine.ReturnsEachCall("Entities.GetOrigin", () =>
            (EntityHandle?)LastArg() == Crate ? new Vector(0f, 0f, 0f)
          : (EntityHandle?)LastArg() == Plank ? new Vector(100f, 0f, 20f)
          : new Vector(7f, 7f, 7f));

        registry.KeyValue(Socket, EntityNames.PhysBallsocket, "attach1", "crate");
        registry.KeyValue(Socket, EntityNames.PhysBallsocket, "attach2", "plank");
        if (breakSound.Length > 0)
            registry.KeyValue(Socket, EntityNames.PhysBallsocket, "breaksound", breakSound);
        foreach (var (key, value) in keys)
            registry.KeyValue(Socket, EntityNames.PhysBallsocket, key, value);
        registry.Spawn(Socket, EntityNames.PhysBallsocket);
        registry.Think(Socket);

        var socket = (PhysBallSocket)registry.Get(Socket)!;
        Assert.True(engine.Called("Constraints.Create"), "the control: the joint was built");
        return (registry, socket, engine);
    }

    // The registry's own removal route marks it in its directory; an unbound
    // one would ask the engine.
    private static bool Removed(ScriptedEntityRegistry registry, RecordingEngine engine)
        => registry.Directory.PendingRemovalCount > 0
           || engine.Calls.Any(c => c.Member == "Entities.Remove" && Equals(c.Args[0], Socket));

    [Fact]
    public void A_joint_the_solver_broke_fires_OnBreak_once_and_the_entity_goes()
    {
        var (registry, socket, engine) = Built();

        Assert.True(registry.ConstraintBroken(new PhysicsId(JointId)));

        Assert.Equal(1, socket.OnBreak.FireCount);
        Assert.True(Removed(registry, engine), "PhysCallbackRemove: the entity goes");
    }

    [Fact]
    public void Another_joint_breaking_is_not_this_ones()
    {
        var (registry, socket, engine) = Built();

        Assert.False(registry.ConstraintBroken(new PhysicsId(JointId + 1)));

        Assert.Equal(0, socket.OnBreak.FireCount);
        Assert.False(Removed(registry, engine), "and the entity stays");
    }

    [Fact]
    public void The_break_input_takes_the_same_path_and_removes_the_entity()
    {
        var (registry, socket, engine) = Built();

        registry.Input(Socket, "Break", "", 0f, EntityHandle.None);

        Assert.Equal(1, socket.OnBreak.FireCount);
        Assert.True(Removed(registry, engine), "PhysCallbackRemove: the entity goes");
    }

    [Fact]
    public void The_break_sound_plays_between_the_two_ends()
    {
        var (registry, _, engine) = Built(breakSound: "Metal_Box.Break");

        registry.ConstraintBroken(new PhysicsId(JointId));

        var sound = Assert.Single(engine.Calls, c => c.Member == "Sound.EmitSoundAtPosition");
        Assert.Equal("Metal_Box.Break", sound.ArgAs<string>(0));
        Assert.Equal(new Vector(50f, 0f, 10f), sound.ArgAs<Vector>(1));
    }

    [Fact]
    public void No_break_sound_is_played_when_none_is_named()
    {
        var (registry, socket, engine) = Built();

        registry.ConstraintBroken(new PhysicsId(JointId));

        Assert.Equal(1, socket.OnBreak.FireCount);   // the control: it broke
        Assert.False(engine.Called("Sound.EmitSoundAtPosition"));
    }

    // A24: CPhysConstraint::GetBreakParams converts the map's limits from pounds,
    // params.forceLimit = lbs2kg( m_forceLimit ) (physconstraint.cpp:379-380).
    [Fact]
    public void The_force_and_torque_limits_reach_the_solver_in_kilograms()
    {
        var (_, _, engine) = Built("", ("forcelimit", "100"), ("torquelimit", "200"));

        var create = Assert.Single(engine.Calls, c => c.Member == "Constraints.Create");
        float[] parameters = create.ArgAs<float[]>(3);
        Assert.Equal(100f * 0.45359237f, parameters[0], 4);
        Assert.Equal(200f * 0.45359237f, parameters[1], 4);
    }
}
