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

    private sealed class SevenRandom : IUniformRandom
    {
        public float RandomFloat(float min, float max) => min;
        public int RandomInt(int min, int max) => min;
        public float RandomFloatExp(float min, float max, float exponent) => min;
    }

    /// <summary>One random stream that binds; every other interface refused with its own reason.</summary>
    private sealed class Interfaces : IServerEngineInterfaces
    {
        public IUniformRandom? RandomStream(out string why) { why = ""; return new SevenRandom(); }
        public IFwdModelInfo? ModelInfo(out string why) { why = "no model info"; return null; }
        public IFwdBaseFileSystem? BaseFileSystem(out string why) { why = "no filesystem"; return null; }
        public IFwdEngineServer? EngineServer(out string why) { why = "no engine server"; return null; }
        public string EngineClientProbe() => "client probe";
        public string FrameProbe() => "frame probe";
        public string LevelProbe() => "level probe";
    }

    [Fact]
    public void ABoundInterfaceIsReportedWithWhatItAnswered()
    {
        var log = new ListLog();

        new VtableAcquireCommand(new Interfaces(), log).Execute("");

        Assert.Contains("[SourceSharp/Vtable] IUniformRandomStream: bound, RandomInt(7,7) = 7\n", log.Lines);
    }

    [Fact]
    public void ARefusedInterfaceIsReportedWithTheEnginesReason()
    {
        var log = new ListLog();

        new VtableAcquireCommand(new Interfaces(), log).Execute("");

        Assert.Contains("[SourceSharp/Vtable] IVModelInfo: REFUSED — no model info\n", log.Lines);
        Assert.Contains("[SourceSharp/Vtable] IBaseFileSystem: REFUSED — no filesystem\n", log.Lines);
        Assert.Contains("[SourceSharp/Vtable] IVEngineServer: REFUSED — no engine server\n", log.Lines);
    }

    [Fact]
    public void TheShimProbeLinesArePrintedAsTheSeamComposedThem()
    {
        var log = new ListLog();

        new VtableAcquireCommand(new Interfaces(), log).Execute("");

        Assert.Equal(["[SourceSharp/Vtable] client probe\n", "[SourceSharp/Vtable] frame probe\n",
                      "[SourceSharp/Vtable] level probe\n"],
                     log.Lines.Skip(log.Lines.Count - 3));
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
        root.Interface(UniformRandomStream.InterfaceVersion, &word).Forwarders<UniformRandomStreamFwd>();
        Assert.True(served.AttachOwnShims(root.Bind(), out string attachWhy), attachWhy);

        Assert.NotNull(answered.RandomStream(out _));
        Assert.Null(refused.RandomStream(out string why));
        Assert.False(string.IsNullOrEmpty(why), "a refusal carries the engine's reason");
    }

    [Fact]
    public void TheRegisteredProbesAskTheRootsEngine()
    {
        IServerEngineInterfaces interfaces = Composed(TestSides.Create(Side.Server));

        Assert.StartsWith("IShimFrame: REFUSED", interfaces.FrameProbe(), StringComparison.Ordinal);
        Assert.StartsWith("IShimLevel: REFUSED", interfaces.LevelProbe(), StringComparison.Ordinal);
        Assert.Contains("REFUSED", interfaces.EngineClientProbe(), StringComparison.Ordinal);
        Assert.Null(interfaces.ModelInfo(out _));
        Assert.Null(interfaces.BaseFileSystem(out _));
        Assert.Null(interfaces.EngineServer(out _));
    }

    private static IServerEngineInterfaces Composed(EngineSide side)
    {
        var module = new ServerModule(new ModuleEngine(side));
        module.ComposeEngineAreas();
        return module.Services.Require<IServerEngineInterfaces>();
    }
}
