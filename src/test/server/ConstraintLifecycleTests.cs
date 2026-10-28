using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A constraint entity's life against stock's CPhysConstraint
/// (physconstraint.cpp). Found beside stock (2026-10-27): TurnOff destroyed
/// the joint and TurnOn built a new one from wherever the ends had drifted to
/// (stock deactivates and reactivates the same one, so the ends return to
/// their joined pose); a joint that could not be made stayed instead of being
/// removed; SF_CONSTRAINT_NO_CONNECT_UNTIL_ACTIVATED and START_INACTIVE were
/// read by no kind but the ragdoll; only the hinge kept its pair apart.
/// </summary>
public class ConstraintLifecycleTests
{
    private const uint JointId = 0x0001_0005u;
    private const int DisableCollision = 0x01, StartInactive = 0x04, NoConnect = 0x10;

    private static readonly EntityHandle Socket = new(0x0001_0050);
    private static readonly EntityHandle Crate = new(0x0001_0051);
    private static readonly EntityHandle Plank = new(0x0001_0052);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public required EntityPairCollisions Pairs;

        public void Input(string name) => Registry.Input(Socket, name, "", 0f, EntityHandle.None);

        public int Count(string member) => Engine.Calls.Count(c => c.Member == member);

        public bool Removed => Registry.Directory.PendingRemovalCount > 0;
    }

    private static Rig Spawned(int flags, uint created = JointId)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysBallSocket).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        engine.Returns("Constraints.Create", created)
              .Returns("Constraints.IsValid", true)
              .Returns("Entities.IsValid", true)
              .ReturnsEachCall("Entities.FindByName", () => (string?)engine.Calls[^1].Args[0] == "crate" ? Crate : Plank)
              .ReturnsEachCall("Entities.GetSpawnFlags",
                  () => engine.Calls[^1].ArgAs<EntityHandle>(0) == Socket ? flags : 0);

        registry.KeyValue(Socket, EntityNames.PhysBallsocket, "attach1", "crate");
        registry.KeyValue(Socket, EntityNames.PhysBallsocket, "attach2", "plank");
        registry.Spawn(Socket, EntityNames.PhysBallsocket);
        registry.Think(Socket);

        return new Rig { Registry = registry, Engine = engine, Pairs = services.Require<EntityPairCollisions>() };
    }

    [Fact]
    public void A_joint_that_cannot_be_made_takes_its_entity_with_it()
    {
        var rig = Spawned(0, created: 0u);

        Assert.Equal(1, rig.Count("Constraints.Create"));
        Assert.True(rig.Removed);
    }

    [Fact]
    public void TurnOff_and_TurnOn_deactivate_and_reactivate_the_same_joint()
    {
        var rig = Spawned(0);

        rig.Input("TurnOff");
        Assert.Equal(1, rig.Count("Constraints.Deactivate"));
        Assert.Equal(0, rig.Count("Constraints.Destroy"));

        rig.Input("TurnOn");
        rig.Registry.Think(Socket);
        Assert.Equal(1, rig.Count("Constraints.Activate"));
        Assert.Equal(1, rig.Count("Constraints.Create"));
    }

    [Fact]
    public void A_no_connect_joint_is_made_by_TurnOn_and_not_before_nor_removed_for_it()
    {
        var rig = Spawned(NoConnect);
        Assert.Equal(0, rig.Count("Constraints.Create"));
        Assert.False(rig.Removed);

        rig.Input("TurnOn");

        Assert.Equal(1, rig.Count("Constraints.Create"));
        Assert.Equal(1, rig.Count("Constraints.Activate"));
    }

    [Fact]
    public void A_start_inactive_joint_is_made_and_deactivated()
    {
        var rig = Spawned(StartInactive);

        string[] order = rig.Engine.Calls.Select(c => c.Member)
                            .Where(m => m is "Constraints.Create" or "Constraints.Deactivate").ToArray();
        Assert.Equal(["Constraints.Create", "Constraints.Deactivate"], order);
    }

    [Fact]
    public void Any_kind_keeps_its_pair_apart_and_TurnOff_lets_them_collide_again()
    {
        var rig = Spawned(DisableCollision);
        Assert.True(rig.Pairs.AreDisabled(Crate, Plank));

        rig.Input("TurnOff");

        Assert.False(rig.Pairs.AreDisabled(Crate, Plank));
    }

    [Fact]
    public void Without_the_flag_the_pair_collides()
        => Assert.False(Spawned(0).Pairs.AreDisabled(Crate, Plank));

    [Fact]
    public void The_break_deactivates_now_and_the_joint_goes_with_the_entity()
    {
        var rig = Spawned(0);

        rig.Input("Break");
        Assert.Equal(1, rig.Count("Constraints.Deactivate"));
        Assert.Equal(0, rig.Count("Constraints.Destroy"));

        rig.Registry.Directory.FreeRemoved();
        Assert.Equal(1, rig.Count("Constraints.Destroy"));
    }
}
