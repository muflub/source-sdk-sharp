using SourceSharp.Abi;
using SourceSharp.Client;

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
/// <b>The client's half.</b> The server's is <c>TwoInstanceTests</c> in the
/// cross-side suite. Each side compiles its own copy of the shared interop
/// (plan_shim_areas_split phase 3), and a client composition is built from
/// the client's, so this lives in the client suite.
/// </para>
/// </summary>
public class TwoClientInstanceTests
{
    private sealed class ClientInstance
    {
        public ClientInstance()
        {
            Engine = TestSides.Create(Side.Client);
            Module = new ClientModule(new ModuleEngine(Engine), new FakeClientEngineAdapters());
            Module.ComposeEngineAreas();
            Module.Services.Require<ConsoleLines>().Tap = (text, _) => Lines.Add(text);
        }

        public EngineSide Engine { get; }

        public ClientModule Module { get; }

        public SideState State => Engine.Attachment.State;

        public List<string> Lines { get; } = new();
    }

    [Fact]
    public void TwoClientInstancesInOneProcessShareNothing()
    {
        var a = new ClientInstance();
        var b = new ClientInstance();

        b.State.Build.SetManaged(new BuildHalf(new BuildStamp("bbbb2222", DateTime.UnixEpoch, 1),
                                               GitProvenance.Unknown, "/b", false), Array.Empty<string>());

        // A crossing through each instance's own runtime binding.
        a.Module.NewRuntimeBinding().ClientInvalidateMdlCache();
        a.Module.NewRuntimeBinding().ClientInvalidateMdlCache();
        b.Module.NewRuntimeBinding().ClientInvalidateMdlCache();

        // A log line each.
        a.Module.Services.Require<ILog>().Msg("client a\n");
        b.Module.Services.Require<ILog>().Warning("client b\n");

        // A reload save each.
        a.Module.ReloadSave();
        b.Module.ReloadSave();

        Assert.Equal(2, a.State.Crossings.Read(CrossingOp.ClientInvalidateMdlCache));
        Assert.Equal(1, b.State.Crossings.Read(CrossingOp.ClientInvalidateMdlCache));
        // Logs: each console saw its own lines (the flush each binding call
        // reports, and the composition's own line), none of the other's.
        Assert.Equal(2, a.Lines.Count(l => l.Contains("model cache flushed")));
        Assert.Equal(1, b.Lines.Count(l => l.Contains("model cache flushed")));
        Assert.Contains("client a\n", a.Lines);
        Assert.DoesNotContain("client b\n", a.Lines);
        Assert.Contains("client b\n", b.Lines);
        Assert.DoesNotContain("client a\n", b.Lines);
        Assert.Equal(BuildHalf.Unknown, a.State.Build.Managed);
        Assert.Equal("bbbb2222", b.State.Build.Managed.Stamp.Hash);

        // Each reload save went into its own store: the mirror section is there
        // in each, as two byte arrays, not one shared.
        byte[]? mirrorA = a.State.Reload.Peek(StandaloneMirror.ReloadSection);
        byte[]? mirrorB = b.State.Reload.Peek(StandaloneMirror.ReloadSection);
        Assert.NotNull(mirrorA);
        Assert.NotNull(mirrorB);
        Assert.NotSame(mirrorA, mirrorB);

        // The per-composition services are two, not one.
        Assert.NotSame(a.Module.Services.Require<PredictionErrorRing>(), b.Module.Services.Require<PredictionErrorRing>());
        Assert.NotSame(a.Module.Services.Require<NetStateCatalog>(), b.Module.Services.Require<NetStateCatalog>());
        Assert.NotSame(a.State.Vtables, b.State.Vtables);
    }
}
