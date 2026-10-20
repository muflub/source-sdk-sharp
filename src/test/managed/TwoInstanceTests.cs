extern alias client;
using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE TWO-INSTANCE FACT (plan_abi_hotreload §0, exit criterion 2): two managed
/// game instances of one side run side by side in one process with no
/// conflicts. Each is composed over its own engine side and side state (what
/// the Bootstrap keeps per instance), driven through the same steps, and
/// nothing of one is visible in the other: counters, registries, logs, saved
/// bytes, build stamps, crossings.
///
/// <para>
/// <b>At the managed level, by necessity.</b> A live run of two servers in one
/// process is not possible: the engine is one process's, with one server.dll
/// and one client.dll and their globals. What this composes is every managed
/// piece above that line twice over, with no native half behind either (the
/// engine areas answer their defaults), which is exactly the managed state the
/// rule is about. Native globals that hold one process's engine interfaces are
/// out of scope (plan_abi_hotreload §0).
/// </para>
/// <para>
/// <b>The server's half.</b> The client's is <c>TwoClientInstanceTests</c> in
/// the client suite: each side compiles its own copy of the shared interop
/// (plan_shim_areas_split phase 3), and a client composition is built from
/// the client's.
/// </para>
/// </summary>
public class TwoInstanceTests
{
    private sealed class ServerInstance
    {
        public ServerInstance()
        {
            Engine = EngineSide.Create(Side.Server, new SideState(Side.Server));
            Module = new ServerModule(new ModuleEngine(Engine));
            Module.ComposeEngineAreas();

            Registry.RegisterAll(typeof(TwoInstanceProbe).Assembly);
            Registry.Services = TestServices.Services();

            Module.Services.Require<ConsoleLines>().Tap = (text, _) => Lines.Add(text);
            Registry.OutputFired += (_, output, _) => Fired.Add(output);
        }

        public EngineSide Engine { get; }

        public ServerModule Module { get; }

        public ScriptedEntityRegistry Registry => Module.Registry;

        public SideState State => Engine.State;

        public List<string> Lines { get; } = new();

        public List<string> Fired { get; } = new();
    }

    private static readonly EntityHandle Probe = new(0x0007_0011u);

    private static unsafe void DispatchThink(ServerModule module, EntityHandle entity)
        => module.NewEntitiesBinding().Dispatch(entity.Value, (int)EntityOp.Think, null);

    /// <summary>One instance driven through every step the fact names.</summary>
    private static void Drive(ServerInstance instance, int thinks, int pokes)
    {
        // Spawn, with a connection on its output (the connection's id stamp is
        // drawn from the instance's own counter).
        Assert.True(instance.Registry.KeyValue(Probe, "two_instance_probe", "OnPoked", "nobody,Use,,0,-1"));
        instance.Registry.Spawn(Probe, "two_instance_probe");

        // Think, through the instance's own entity binding: a crossing each.
        for (int i = 0; i < thinks; i++)
            DispatchThink(instance.Module, Probe);

        // Entity I/O: an input that fires an output.
        for (int i = 0; i < pokes; i++)
            Assert.True(instance.Registry.Input(Probe, "Poke", "", 0f, EntityHandle.None));

        // A log line through the composition's own ILog.
        instance.Module.Services.Require<ILog>().Msg($"instance with {thinks} thinks\n");

        // A reload save, into the instance's own store.
        instance.Module.ReloadSave();
    }

    [Fact]
    public void TwoServerInstancesInOneProcessShareNothing()
    {
        var a = new ServerInstance();
        var b = new ServerInstance();

        // Build info: only A's managed half is stamped.
        a.State.Build.SetManaged(new BuildHalf(new BuildStamp("aaaa1111", DateTime.UnixEpoch, 1),
                                               GitProvenance.Unknown, "/a", false), Array.Empty<string>());

        Drive(a, thinks: 3, pokes: 2);
        Drive(b, thinks: 1, pokes: 5);

        var probeA = (TwoInstanceProbe)a.Registry.Get(Probe)!;
        var probeB = (TwoInstanceProbe)b.Registry.Get(Probe)!;

        // Registries: one entity each, each its own, each with its own counts.
        Assert.NotSame(probeA, probeB);
        Assert.Equal((3, 2), (probeA.Thought, probeA.Pokes));
        Assert.Equal((1, 5), (probeB.Thought, probeB.Pokes));

        // Crossings: each instance counted only its own thinks.
        Assert.Equal(3, a.State.Crossings.Read(CrossingOp.ServerDispatch));
        Assert.Equal(1, b.State.Crossings.Read(CrossingOp.ServerDispatch));
        Assert.Equal(3, a.State.Crossings.Read(EntityOp.Think));
        Assert.Equal(1, b.State.Crossings.Read(EntityOp.Think));

        // Outputs: each registry heard only its own firings.
        Assert.Equal(2, a.Fired.Count);
        Assert.Equal(5, b.Fired.Count);

        // Connection id stamps: each instance's counter started at 1.
        Assert.Equal(2, a.State.Stamps.Next());
        Assert.Equal(2, b.State.Stamps.Next());

        // Logs: each console saw its own lines and none of the other's.
        Assert.Contains("instance with 3 thinks\n", a.Lines);
        Assert.Contains("instance with 1 thinks\n", b.Lines);
        Assert.DoesNotContain("instance with 1 thinks\n", a.Lines);
        Assert.DoesNotContain("instance with 3 thinks\n", b.Lines);
        Assert.Equal(3, a.Lines.Count(l => l.StartsWith("probe ", StringComparison.Ordinal)));
        Assert.Equal(1, b.Lines.Count(l => l.StartsWith("probe ", StringComparison.Ordinal)));

        // Saved bytes: each store carries its own entity, with its own pokes.
        var savedA = ReloadCarrier.Take(a.State.Reload);
        var savedB = ReloadCarrier.Take(b.State.Reload);
        Assert.Equal(2, Assert.Single(savedA).GetInt("pokes"));
        Assert.Equal(5, Assert.Single(savedB).GetInt("pokes"));
        Assert.Equal(1, a.State.Reload.Saved.Entities);
        Assert.Equal(1, b.State.Reload.Saved.Entities);

        // Build info: A's stamp, B's unknown.
        Assert.Equal("aaaa1111", a.State.Build.Managed.Stamp.Hash);
        Assert.Equal(BuildHalf.Unknown, b.State.Build.Managed);

        // And the shared plumbing objects are two, not one.
        Assert.NotSame(a.State.Vtables, b.State.Vtables);
        Assert.NotSame(a.Module.Services.Require<ILog>(), b.Module.Services.Require<ILog>());
    }
}
