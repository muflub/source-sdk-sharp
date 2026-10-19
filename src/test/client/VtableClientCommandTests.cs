using SourceSharp.Abi;
using SourceSharp.Client;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>cs_vtable_client</c> takes the engine client through a seam
/// (plan_shim_areas_split phase 8): the command's facts drive a fake of it, and
/// the real one is held through the client root's registration.
/// </summary>
public unsafe class VtableClientCommandTests
{
    private sealed class Source(IVtableEngineClient? client, string why) : IVtableEngineClientSource
    {
        public IVtableEngineClient? Acquire(out string reason) { reason = why; return client; }
    }

    private sealed class Client : IVtableEngineClient
    {
        public List<string> Printed { get; } = new();
        public void SubmitConsoleLine(string line) { }
        public void SpectatorCommand(SpectatorKeyCommand command) { }
        public void SpectatorServerCommand(string line) { }
        public bool IsInGame() => true;
        public void VoiceServerCommand(string line, bool reliable) { }
        public string? LevelName() => "maps/ss_sandbox.bsp";
        public string? GameDirectory() => "mod_test";
        public uint ProtocolVersion() => 24;
        public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info) { info = default; return false; }
        public void JoinTeam(int team) { }
        public void ConnectFromSteamLaunch(string connectLine) { }
        public void Reconnect() { }
        public void RequestManagedReload() { }
        public void ConsolePrintAt(int position, string text) => Printed.Add(text);
        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) => Printed.Add(text);
        public bool TryLightForPoint(in Vector position, bool clamp, out Vector light) { light = new Vector(0.5f, 0.25f, 1f); return true; }
        public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light) { light = default; return false; }
        public int SentenceIndexFromName(string name) => -1;
        public int LocalPlayerSlot() => 0;
    }

    [Fact]
    public void ARefusedEngineClientIsReportedWithTheEnginesReasonAndNothingElse()
    {
        var log = new RecordingLog();

        new VtableClientCommand(new Source(null, "not served"), log).Execute("");

        Assert.Equal(["[SourceSharp/Vtable] IVEngineClient: REFUSED — not served\n"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void ABoundEngineClientIsAskedThroughEveryProxy()
    {
        var log = new RecordingLog();
        var client = new Client();

        new VtableClientCommand(new Source(client, ""), log).Execute("");

        Assert.Contains("[SourceSharp/Vtable] IVEngineClient: bound, LevelName() = maps/ss_sandbox.bsp, "
                        + "GameDirectory() = mod_test, ProtocolVersion() = 24\n", log.Lines.Select(l => l.Text));
        Assert.Equal(["cs_vtable_client: Con_NPrintf reached", "cs_vtable_client: Con_NXPrintf reached"], client.Printed);
        Assert.Contains("[SourceSharp/Vtable] GetLightForPoint(0,0,64) = 0.5 0.25 1\n", log.Lines.Select(l => l.Text));
        Assert.Contains("[SourceSharp/Vtable] GetLightForPointFast: no answer\n", log.Lines.Select(l => l.Text));
    }

    /// <summary>
    /// The root registers the real source over its own engine: of two
    /// compositions, only the one whose root serves the engine client binds it,
    /// and the other refuses with a reason rather than borrowing the first's.
    /// </summary>
    [Fact]
    public void TheRootRegistersTheSourceOverItsOwnEngine()
    {
        var served = new EngineSide(Side.Client);
        var bare = new EngineSide(Side.Client);
        IVtableEngineClientSource servedSource = Composed(served);
        IVtableEngineClientSource bareSource = Composed(bare);

        // Attached after the composition, which the source allows because it
        // acquires when asked: a composition over a fake root would build
        // services that call slots the fake leaves unfilled.
        using var root = new FakeShimRoot();
        int word = 0;
        root.Interface(EngineClient.InterfaceVersion, &word).Forwarders<EngineClientFwd>();
        Assert.True(served.AttachClientShims(root.Bind(), out string attachWhy), attachWhy);

        IVtableEngineClient? answered = servedSource.Acquire(out _);
        IVtableEngineClient? refused = bareSource.Acquire(out string why);

        Assert.NotNull(answered);
        Assert.Null(refused);
        Assert.False(string.IsNullOrEmpty(why), "a refusal carries the engine's reason");
    }

    private static IVtableEngineClientSource Composed(EngineSide side)
    {
        var module = new ClientModule(new ModuleEngine(side), new FakeClientEngineAdapters());
        module.ComposeEngineAreas();
        return module.Services.Require<IVtableEngineClientSource>();
    }
}
