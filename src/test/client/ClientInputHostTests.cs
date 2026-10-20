using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Input;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engine_forwarders.md: the client's input host reads the mouse delta
/// through IVEngineClient's forwarders, and GetMouseDelta is a method the
/// engine has only where it is built with SDL. Its forwarder slot is NULL
/// where it is not (Valve's Windows engine), and the host then answers no
/// delta rather than calling a slot that is not there -- where it once read
/// slot 127 on every platform, which on Windows is another method.
/// </summary>
public sealed unsafe class ClientInputHostTests
{
    private static int s_DeltaCalls;

    [UnmanagedCallersOnly]
    private static void MouseDeltaAnswers(void* self, void* x, void* y, byte ignoreNext)
    {
        s_DeltaCalls++;
        *(int*)x = 5;
        *(int*)y = -3;
    }

    // A client engine whose own root serves IVEngineClient and its
    // forwarders, with GetMouseDelta's slot as `withDelta` says.
    private static IInputHost HostOver(FakeShimRoot root, bool withDelta)
    {
        var self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
        root.Interface(EngineClient.InterfaceVersion, self)
            .Forwarders(out EngineClientFwd* fwd);
        fwd->GetMouseDelta = withDelta
            ? (delegate* unmanaged<void*, void*, void*, byte, void>)&MouseDeltaAnswers
            : null;

        var side = TestSides.Create(Side.Client);
        side.Initialize();
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        return ClientInputHost.For(new ModuleEngine(side), new NullConsole());
    }

    [Fact]
    public void TheMouseDeltaComesFromTheEngineWhereItHasTheMethod()
    {
        using var root = new FakeShimRoot();
        IInputHost host = HostOver(root, withDelta: true);
        s_DeltaCalls = 0;

        host.GetMouseDelta(out int x, out int y, ignoreNextMouseDelta: false);

        Assert.Equal((5, -3), (x, y));
        Assert.Equal(1, s_DeltaCalls);
    }

    [Fact]
    public void TheMouseDeltaIsZeroAndNothingIsCalledWhereTheEngineHasNoSuchMethod()
    {
        using var root = new FakeShimRoot();
        IInputHost host = HostOver(root, withDelta: false);
        s_DeltaCalls = 0;

        host.GetMouseDelta(out int x, out int y, ignoreNextMouseDelta: false);

        Assert.Equal((0, 0), (x, y));
        Assert.Equal(0, s_DeltaCalls);
    }

    private static float[] s_Angles = new float[3];
    private static string? s_Command;

    [UnmanagedCallersOnly]
    private static void GetAngles(void* self, void* angles)
    {
        ((float*)angles)[0] = s_Angles[0];
        ((float*)angles)[1] = s_Angles[1];
        ((float*)angles)[2] = s_Angles[2];
    }

    [UnmanagedCallersOnly]
    private static void SetAngles(void* self, void* angles)
        => s_Angles = [((float*)angles)[0], ((float*)angles)[1], ((float*)angles)[2]];

    [UnmanagedCallersOnly]
    private static void ScreenIs640By480(void* self, void* width, void* height)
    {
        *(int*)width = 640;
        *(int*)height = 480;
    }

    [UnmanagedCallersOnly]
    private static byte Paused(void* self) => 1;

    [UnmanagedCallersOnly]
    private static void Unrestricted(void* self, byte* command)
        => s_Command = Marshal.PtrToStringUTF8((nint)command);

    // The host over an engine client whose view angles, screen, pause and
    // unrestricted command slots are live (plan_shim_areas_split phase 11
    // review: the host is handed the engine client rather than binding its
    // table itself, so these reach the engine through it).
    private static IInputHost HostWithInputSlots(FakeShimRoot root)
    {
        var self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
        root.Interface(EngineClient.InterfaceVersion, self)
            .Forwarders(out EngineClientFwd* fwd);
        fwd->GetViewAngles = &GetAngles;
        fwd->SetViewAngles = &SetAngles;
        fwd->GetScreenSize = &ScreenIs640By480;
        fwd->IsPaused = &Paused;
        fwd->ClientCmd_Unrestricted = &Unrestricted;

        var side = TestSides.Create(Side.Client);
        side.Initialize();
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        return ClientInputHost.For(new ModuleEngine(side), new NullConsole());
    }

    [Fact]
    public void TheViewAnglesRoundTripThroughTheEngineClient()
    {
        using var root = new FakeShimRoot();
        IInputHost host = HostWithInputSlots(root);

        host.SetViewAngles(new InputAngles(10f, 20f, 30f));
        InputAngles read = host.GetViewAngles();

        Assert.Equal((10f, 20f, 30f), (read.Pitch, read.Yaw, read.Roll));
    }

    [Fact]
    public void TheScreenPauseAndUnrestrictedCommandReachTheEngineClient()
    {
        using var root = new FakeShimRoot();
        IInputHost host = HostWithInputSlots(root);
        s_Command = null;

        host.GetScreenSize(out int width, out int height);
        host.ClientCmdUnrestricted("+jump");

        Assert.Equal((640, 480), (width, height));
        Assert.True(host.IsPaused);
        Assert.Equal("+jump", s_Command);
    }

    /// <summary>
    /// A side whose engine serves no engine client (the server, a test's side
    /// with no engine) hands the host none, and every engine-client answer is
    /// its default rather than a crash.
    /// </summary>
    [Fact]
    public void WithNoEngineClientEveryAnswerIsItsDefault()
    {
        IInputHost host = ClientInputHost.For(new ModuleEngine(TestSides.Create(Side.Client)), new NullConsole());

        host.GetScreenSize(out int width, out int height);
        host.GetMouseDelta(out int x, out int y, ignoreNextMouseDelta: false);
        host.ClientCmdUnrestricted("+jump");
        host.ConsolePrintAt(0, "nothing");

        Assert.Equal(default, host.GetViewAngles());
        Assert.Equal((0, 0, 0, 0), (width, height, x, y));
        Assert.False(host.IsPaused);
    }

    private sealed class NullConsole : IEngineConsole
    {
        public void Msg(string message) { }
        public void ExecuteServerCommand(string command) { }
        public string? FindConVarValue(string name) => null;
        public string GetConVarValue(int id) => string.Empty;
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => 0;
        public int RegisterConVar(string name, string defaultValue, string help, int flags,
                                  float? min = null, float? max = null) => 0;
        public void SendClientCommand(EntityHandle player, string command) { }
        public void SetConVarValue(int id, string value) { }
        public void SetConVarValueByName(string name, string value) { }
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }
}
