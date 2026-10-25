using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's halves of EngineSide and the engine bindings
/// (plan_shim_areas_split phase 7): a client-only member of a shared seam
/// answers on the server what its attach guard answered there, where the
/// client's areas are never attached, without calling the engine.
/// </summary>
public class EngineSideServerHalfTests
{
    private static readonly EntityHandle Prop = new(0x0004_0021u);

    private static T Binding<T>(EngineSide side) where T : class
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, side);
        return services.Require<T>();
    }

    private static EngineSide Attached(FakeShimRoot root)
    {
        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
        return side;
    }

    [Fact]
    public void AttachOwnShimsAttachesTheServersAreas()
    {
        using var root = new FakeShimRoot();
        Assert.NotNull(Attached(root).Attachment.Root);
    }

    [Fact]
    public void TheLocalPlayersReadsAnswerTheGuardsValues()
    {
        using var root = new FakeShimRoot();
        var hud = Binding<IEngineHud>(Attached(root));

        Assert.Equal(-1, hud.LocalPlayerHealth);
        Assert.Null(hud.LocalPlayerOrigin);
    }

    [Fact]
    public void AClientTraceHitsNothingAndEndsAtTheEnd()
    {
        using var root = new FakeShimRoot();
        var world = Binding<IEngineWorldQuery>(Attached(root));
        var end = new Vector(1, 2, 3);

        ClientTraceResult three = world.ClientTraceLine(Vector.Zero, end, TraceMask.Solid);
        ClientTraceResult five = world.ClientTraceLine(Vector.Zero, end, TraceMask.Solid, TraceFilter.HitAll, 4);

        Assert.Equal(new ClientTraceResult(false, 1f, end, Vector.Zero, 0, -1, -1, 0), three);
        Assert.Equal(three, five);
    }

    /// <summary>
    /// Dormancy's shared half reads the mirror when it is rerouted; otherwise
    /// the server's helper answers awake, as the client slot's guard did here.
    /// </summary>
    [Fact]
    public void AnUnmirroredEntityIsNotDormant()
    {
        using var root = new FakeShimRoot();
        Assert.False(Binding<IEngineEntityRender>(Attached(root)).IsDormant(Prop));
    }
}
