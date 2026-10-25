using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A line of world text through IVDebugOverlay's forwarders (2026-10-19), where
/// the DrawWorldText slot made the same <c>AddTextOverlayRGB( ..., "%s", text )</c>
/// through overlay_library. The forwarder fixes the format; the text crosses
/// as the one argument, so a '%' in it is printed, never interpreted.
/// </summary>
public sealed unsafe class DrawWorldTextFoldTests : System.IDisposable
{
    private static string? s_Text;
    private static (float X, float Y, float Z, int Line, float Duration, int R, int G, int B, int A) s_Args;
    private static int s_Calls;

    [UnmanagedCallersOnly]
    private static void AddText(void* self, void* origin, int line, float duration, int r, int g, int b, int a, byte* text)
    {
        var v = (float*)origin;
        s_Args = (v[0], v[1], v[2], line, duration, r, g, b, a);
        s_Text = Marshal.PtrToStringUTF8((nint)text);
        s_Calls++;
    }

    private readonly FakeShimRoot _root = new();
    private readonly nint* _self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));

    public DrawWorldTextFoldTests()
    {
        s_Calls = 0;
        s_Text = null;
    }

    public void Dispose()
    {
        _root.Dispose();
        NativeMemory.Free(_self);
    }

    private IEngineHud Hud(bool overlay)
    {
        if (overlay)
        {
            _root.Interface(EngineDebugOverlay.InterfaceVersion, _self).Forwarders(out DebugOverlayFwd* fwd);
            fwd->AddTextOverlayRGB_2 = &AddText;
        }

        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(_root.Bind(), out string why), why);
        return SideBindings.Of<IEngineHud>(side);
    }

    [Fact]
    public void TheTextAndItsPlacementReachTheOverlay()
    {
        Hud(overlay: true).DrawWorldText(new Vector(1, 2, 3), 4, "100% health", 0.5f, 10, 20, 30, 40);

        Assert.Equal("100% health", s_Text);
        Assert.Equal((1f, 2f, 3f, 4, 0.5f, 10, 20, 30, 40), s_Args);
    }

    [Fact]
    public void AServerWithNoOverlayDrawsNothing()
    {
        var hud = Hud(overlay: false);

        hud.DrawWorldText(Vector.Zero, 0, "x", 1f);
        hud.DrawWorldText(Vector.Zero, 0, "y", 1f);

        Assert.Equal(0, s_Calls);
    }
}
