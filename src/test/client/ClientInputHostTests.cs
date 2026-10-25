using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Input;
using SourceSharp.Client.Interop;
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
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
        return Composed(new ModuleEngine(side));
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
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
        return Composed(new ModuleEngine(side));
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

    // The host as the client's composition builds it (phase 11 review: by
    // injection, no static): the side's engine interfaces register the engine
    // client (EngineServices.AddForwarders, as AddTo does), the game's
    // adapters the rest, and the module builds the host over the side's
    // input area and them.
    private static IInputHost Composed(ModuleEngine engine)
    {
        var services = new ServiceContainer().AddInstance<IEngineConsole>(new NullConsole());
        EngineServices.AddForwarders(services, engine.Api);
        new ClientEntityFetches().AddTo(services, engine);
        // By type: the census lets a fact read the class's shape, never hold it.
        return (IInputHost)engine.Create(typeof(ClientInputHost), services);
    }

    /// <summary>
    /// The host is only ever the client's, so its engine client is never null:
    /// the constructor refuses one rather than answering defaults for it.
    /// </summary>
    [Fact]
    public void TheHostRefusesANullEngineClient()
    {
        var side = TestSides.Create(Side.Client);
        var input = side.Attachment.Areas.Require<IInputClientArea>();
        var constructor = Assert.Single(typeof(ClientInputHost).GetConstructors());

        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => constructor.Invoke([input, new NullConsole(), null]));
        var refusal = Assert.IsType<ArgumentNullException>(thrown.InnerException);
        Assert.Equal("engineClient", refusal.ParamName);
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
