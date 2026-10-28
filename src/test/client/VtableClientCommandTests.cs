using SourceSharp.Abi;
using SourceSharp.Client;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>cs_vtable_client</c> takes a probe of the engine client and the engine
/// client itself (plan_shim_areas_split phase 8): the command's facts drive
/// fakes of both, and the real probe is held through the client root's registration.
/// </summary>
public unsafe class VtableClientCommandTests
{
    private sealed class Probe(bool served, string why) : IFwdEngineClientProbe
    {
        public bool Served(out string reason) { reason = why; return served; }
    }

    private sealed class Client : IFwdEngineClient
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
        public bool CopyLocalFile(string source, string destination) => false;
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

        var client = new Client();

        new VtableClientCommand(new Probe(false, "not served"), client, log).Execute("");

        Assert.Equal(["[SourceSharp/Vtable] IVEngineClient: REFUSED — not served\n"], log.Lines.Select(l => l.Text));
        Assert.Empty(client.Printed);
    }

    [Fact]
    public void ABoundEngineClientIsAskedThroughEveryProxy()
    {
        var log = new RecordingLog();
        var client = new Client();

        new VtableClientCommand(new Probe(true, ""), client, log).Execute("");

        Assert.Contains("[SourceSharp/Vtable] IVEngineClient: bound, LevelName() = maps/ss_sandbox.bsp, "
                        + "GameDirectory() = mod_test, ProtocolVersion() = 24\n", log.Lines.Select(l => l.Text));
        Assert.Equal(["cs_vtable_client: Con_NPrintf reached", "cs_vtable_client: Con_NXPrintf reached"], client.Printed);
        Assert.Contains("[SourceSharp/Vtable] GetLightForPoint(0,0,64) = 0.5 0.25 1\n", log.Lines.Select(l => l.Text));
        Assert.Contains("[SourceSharp/Vtable] GetLightForPointFast: no answer\n", log.Lines.Select(l => l.Text));
    }

    /// <summary>
    /// The root registers the real probe over its own engine: of two
    /// compositions, only the one whose root serves the engine client says so,
    /// and the other refuses with a reason rather than borrowing the first's.
    /// </summary>
    [Fact]
    public void TheRootRegistersTheProbeOverItsOwnEngine()
    {
        var served = TestSides.Create(Side.Client);
        var bare = TestSides.Create(Side.Client);
        IFwdEngineClientProbe servedProbe = Composed(served);
        IFwdEngineClientProbe bareProbe = Composed(bare);

        // Attached after the composition, which the probe allows because it
        // asks when asked: a composition over a fake root would build
        // services that call slots the fake leaves unfilled.
        using var root = new FakeShimRoot();
        int word = 0;
        root.Interface(EngineClient.InterfaceVersion, &word).Forwarders<EngineClientFwd>();
        Assert.True(served.Attachment.AttachShims(root.Bind(), out string attachWhy), attachWhy);

        Assert.True(servedProbe.Served(out _));
        Assert.False(bareProbe.Served(out string why));
        Assert.False(string.IsNullOrEmpty(why), "a refusal carries the engine's reason");
    }

    private static IFwdEngineClientProbe Composed(EngineSide side)
    {
        var module = new ClientModule(new ClientModuleEngine(side), new FakeClientEngineAdapters());
        module.ComposeEngineAreas();
        return module.Services.Require<IFwdEngineClientProbe>();
    }
}
