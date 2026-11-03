using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A viewer-local model override crosses from the seam to the client's
/// standalone area (the tf2 disguise fix: stock draws a disguise per viewer
/// on the client, c_tf_player.cpp ValidateModelIndex, never on the server).
///
/// <para>
/// The client had no way to set an entity's drawn model locally, so a
/// per-viewer body was impossible without a server model change that moves
/// hitboxes and shows team-mates the enemy. The crossing is a name, resolved
/// to an index by native (stock's mechanism), served here as a managed
/// recorder: the slot is a plain call, so the fact sees the handle and the
/// exact bytes the binding sent. The server's answer is the control — its
/// <c>SetModelOverride</c> is a stub and must never cross.
/// </para>
/// </summary>
public sealed unsafe class ClientModelOverrideTests : IDisposable
{
    private static readonly EntityHandle Spy = new(0x0002_0009u);

    private const string Slot = nameof(ShimStandaloneClientFwd.EntitySetModelOverride);
    private const string Version = "SourceSharpStandaloneClient001";

    private readonly FakeShimRoot _root = new();

    public void Dispose() => _root.Dispose();

    [ThreadStatic] private static uint t_handle;
    [ThreadStatic] private static string? t_model;

    [UnmanagedCallersOnly]
    private static void SeeOverride(void* self, uint handle, byte* pModel)
    {
        t_handle = handle;
        t_model = Marshal.PtrToStringUTF8((nint)pModel);
    }

    private static nint Recorder()
        => (nint)(delegate* unmanaged<void*, uint, byte*, void>)&SeeOverride;

    [Fact]
    public void AViewSetModelOverrideReachesTheStandaloneSlot()
    {
        _root.Live(Version, Slot, Recorder());
        var side = Attached();

        t_handle = 0;
        t_model = null;
        SideBindings.Of<IEngineEntityRender>(side, new StandaloneMirror())
            .SetModelOverride(Spy, "models/player/heavy.mdl");

        Assert.Equal(Spy.Value, t_handle);
        Assert.Equal("models/player/heavy.mdl", t_model);
    }

    [Fact]
    public void AnEmptyNameClearsTheOverride()
    {
        _root.Live(Version, Slot, Recorder());
        var side = Attached();

        t_model = "models/player/heavy.mdl";
        SideBindings.Of<IEngineEntityRender>(side, new StandaloneMirror()).SetModelOverride(Spy, "");

        Assert.Equal("", t_model);
    }

    [Fact]
    public void AServerSideNeverOverridesAModel()
    {
        t_model = null;

        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();

        SideBindings.Of<IEngineEntityRender>(side, new StandaloneMirror())
            .SetModelOverride(Spy, "models/player/heavy.mdl");

        Assert.Null(t_model);
    }

    private EngineSide Attached()
    {
        var side = TestSides.Create(Side.Client);
        side.Attachment.Initialize();

        Assert.True(side.Attachment.AttachShims(_root.Bind(), out string why), why);
        return side;
    }
}
