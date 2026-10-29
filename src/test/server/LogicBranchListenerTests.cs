using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// logic_branch_listener and logic_branch against stock's CLogicBranchList and
/// CLogicBranch (logicentities.cpp). The listener names its branches in
/// Branch01..16 and registers with each on Activate. A branch whose value
/// changes queues _OnLogicBranchChanged to each listener. The listener fires
/// OnAllTrue, OnAllFalse or OnMixed only when that answer changes, and Test
/// forces one. Found in the contract diff (2026-10-28): ours took no Branch
/// keys and no branch told any listener anything, so a logic_branch_listener
/// never fired.
/// </summary>
public class LogicBranchListenerTests
{
    private static readonly EntityHandle Listener = new(0x0012_0101u);
    private static readonly EntityHandle First = new(0x0012_0102u);
    private static readonly EntityHandle Second = new(0x0012_0103u);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;

        public LogicBranchListener Entity => (LogicBranchListener)Registry.Get(Listener)!;

        public void Set(EntityHandle branch, bool value, bool test = false)
        {
            Registry.Input(branch, test ? "SetValueTest" : "SetValue", value ? "1" : "0", value ? 1f : 0f, EntityHandle.None);
            Registry.Directory.ServiceIo(Registry.Now);
        }

        public (int AllTrue, int AllFalse, int Mixed) Fired
            => (Entity.OnAllTrue.FireCount, Entity.OnAllFalse.FireCount, Entity.OnMixed.FireCount);
    }

    private static Rig Spawned(bool firstValue = false, bool secondValue = false)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(LogicBranch).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true);

        foreach (var (handle, name, value) in new[] { (First, "gate_a", firstValue), (Second, "gate_b", secondValue) })
        {
            registry.KeyValue(handle, EntityNames.LogicBranch, "targetname", name);
            registry.KeyValue(handle, EntityNames.LogicBranch, "InitialValue", value ? "1" : "0");
            registry.Spawn(handle, EntityNames.LogicBranch);
        }

        registry.KeyValue(Listener, EntityNames.LogicBranchListener, "Branch01", "gate_a");
        registry.KeyValue(Listener, EntityNames.LogicBranchListener, "Branch02", "gate_b");
        registry.Spawn(Listener, EntityNames.LogicBranchListener);

        var rig = new Rig { Registry = registry };
        rig.Entity.OnServerActivate();
        return rig;
    }

    [Fact]
    public void Its_branches_changing_tell_it_the_answer_each_time_it_changes()
    {
        var rig = Spawned();

        rig.Set(First, true);
        Assert.Equal((0, 0, 1), rig.Fired);

        rig.Set(Second, true);
        Assert.Equal((1, 0, 1), rig.Fired);

        rig.Set(First, false);
        rig.Set(Second, false);
        Assert.Equal((1, 1, 2), rig.Fired);
    }

    /// <summary>Only a change of value is reported: setting what it already holds tells no listener.</summary>
    [Fact]
    public void A_branch_set_to_what_it_holds_tells_nobody()
    {
        var rig = Spawned();

        rig.Set(First, false);

        Assert.Equal((0, 0, 0), rig.Fired);
    }

    [Fact]
    public void Test_reports_the_answer_even_when_it_has_not_changed()
    {
        var rig = Spawned(firstValue: true, secondValue: true);

        rig.Registry.Input(Listener, "Test", "", 0f, EntityHandle.None);
        rig.Registry.Input(Listener, "Test", "", 0f, EntityHandle.None);

        Assert.Equal((2, 0, 0), rig.Fired);
    }

    /// <summary>The listener registered only with the branches its keys name.</summary>
    [Fact]
    public void A_branch_it_does_not_name_is_not_heard()
    {
        var rig = Spawned();
        var stranger = new EntityHandle(0x0012_0104u);
        rig.Registry.KeyValue(stranger, EntityNames.LogicBranch, "targetname", "gate_c");
        rig.Registry.Spawn(stranger, EntityNames.LogicBranch);

        rig.Set(stranger, true);

        Assert.Equal((0, 0, 0), rig.Fired);
    }

    /// <summary>SetValueTest reports the branch's own output too, after storing.</summary>
    [Fact]
    public void A_branchs_test_form_fires_its_own_output()
    {
        var rig = Spawned();
        var branch = (LogicBranch)rig.Registry.Get(First)!;

        rig.Set(First, true, test: true);

        Assert.Equal((1, 0), (branch.OnTrue.FireCount, branch.OnFalse.FireCount));
        Assert.True(branch.Value);
    }

    /// <summary>DoTest fires only when the answer differs from the last one it gave.</summary>
    [Fact]
    public void A_notice_that_leaves_the_answer_as_it_was_fires_nothing()
    {
        var rig = Spawned(firstValue: true);

        rig.Registry.Input(Listener, "_OnLogicBranchChanged", "", 0f, First);
        rig.Registry.Input(Listener, "_OnLogicBranchChanged", "", 0f, First);
        Assert.Equal((0, 0, 1), rig.Fired);

        var agreed = Spawned(firstValue: true, secondValue: true);
        agreed.Registry.Input(Listener, "_OnLogicBranchChanged", "", 0f, First);
        agreed.Registry.Input(Listener, "_OnLogicBranchChanged", "", 0f, First);
        Assert.Equal((1, 0, 0), agreed.Fired);
    }

    /// <summary>A branch removed is dropped from what it weighs: the rest decide.</summary>
    [Fact]
    public void A_removed_branch_no_longer_counts()
    {
        var rig = Spawned(firstValue: true);

        rig.Registry.Input(Listener, "_OnLogicBranchRemoved", "", 0f, Second);

        Assert.Equal((1, 0, 0), rig.Fired);
    }
}
