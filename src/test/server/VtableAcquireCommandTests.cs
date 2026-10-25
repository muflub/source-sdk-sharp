using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>cs_vtable</c> takes the engine interfaces it reports as a seam
/// (plan_shim_areas_split phase 8): the command's facts drive a fake of it, and
/// the real one is held through the server root's registration.
/// </summary>
public unsafe class VtableAcquireCommandTests
{
    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    /// <summary>Each probe answers its own name, so the order they print in is visible.</summary>
    private sealed class Interfaces : IServerEngineInterfaces
    {
        public string RandomStreamProbe() => "random probe";
        public string ModelInfoProbe() => "model info probe";
        public string BaseFileSystemProbe() => "filesystem probe";
        public string EngineServerProbe() => "engine server probe";
        public string EngineClientProbe() => "client probe";
        public string FrameProbe() => "frame probe";
        public string LevelProbe() => "level probe";
    }

    [Fact]
    public void EveryProbeLineIsPrintedAsTheSeamComposedItInOrder()
    {
        var log = new ListLog();

        new VtableAcquireCommand(new Interfaces(), log).Execute("");

        Assert.Equal(["[SourceSharp/Vtable] random probe\n", "[SourceSharp/Vtable] model info probe\n",
                      "[SourceSharp/Vtable] filesystem probe\n", "[SourceSharp/Vtable] engine server probe\n",
                      "[SourceSharp/Vtable] client probe\n", "[SourceSharp/Vtable] frame probe\n",
                      "[SourceSharp/Vtable] level probe\n"],
                     log.Lines);
    }

    /// <summary>
    /// The root registers the real seam over its own engine: of two
    /// compositions, only the one whose root serves the random stream binds it,
    /// and the other refuses with a reason rather than borrowing the first's.
    /// </summary>
    [Fact]
    public void TheRootRegistersTheInterfacesOverItsOwnEngine()
    {
        var served = TestSides.Create(Side.Server);
        var bare = TestSides.Create(Side.Server);
        IServerEngineInterfaces answered = Composed(served);
        IServerEngineInterfaces refused = Composed(bare);

        // Attached after the composition, as the client's fact does: the seam
        // acquires when asked.
        using var root = new FakeShimRoot();
        int word = 0;
        root.Interface(UniformRandomStream.InterfaceVersion, &word).Forwarders(out UniformRandomStreamFwd* stream);
        stream->RandomInt = &Low;
        Assert.True(served.Attachment.AttachShims(root.Bind(), out string attachWhy), attachWhy);

        // The line says the stream bound and what its call answered.
        Assert.Equal("IUniformRandomStream: bound, RandomInt(7,7) = 7", answered.RandomStreamProbe());
        Assert.StartsWith("IUniformRandomStream: REFUSED — ", refused.RandomStreamProbe(), StringComparison.Ordinal);
        Assert.True(refused.RandomStreamProbe().Length > "IUniformRandomStream: REFUSED — ".Length,
                    "a refusal carries the engine's reason");
    }

    // RandomInt( min, max ) answering min: the probe's call reaches the slot.
    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static int Low(void* self, int min, int max) => min;

    [Fact]
    public void TheRegisteredProbesAskTheRootsEngine()
    {
        IServerEngineInterfaces interfaces = Composed(TestSides.Create(Side.Server));

        Assert.StartsWith("IShimFrame: REFUSED", interfaces.FrameProbe(), StringComparison.Ordinal);
        Assert.StartsWith("IShimLevel: REFUSED", interfaces.LevelProbe(), StringComparison.Ordinal);
        Assert.Contains("REFUSED", interfaces.EngineClientProbe(), StringComparison.Ordinal);
        // A test side's held defaults answer calls, never the probes: a probe
        // asks the factory itself.
        Assert.StartsWith("IVModelInfo: REFUSED", interfaces.ModelInfoProbe(), StringComparison.Ordinal);
        Assert.StartsWith("IBaseFileSystem: REFUSED", interfaces.BaseFileSystemProbe(), StringComparison.Ordinal);
        Assert.StartsWith("IVEngineServer: REFUSED", interfaces.EngineServerProbe(), StringComparison.Ordinal);
    }

    private static IServerEngineInterfaces Composed(EngineSide side)
    {
        var module = new ServerModule(new ServerModuleEngine(side));
        module.ComposeEngineAreas();
        return module.Services.Require<IServerEngineInterfaces>();
    }
}
