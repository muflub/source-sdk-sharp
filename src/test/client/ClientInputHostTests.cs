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

        var side = new EngineSide(Side.Client);
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
