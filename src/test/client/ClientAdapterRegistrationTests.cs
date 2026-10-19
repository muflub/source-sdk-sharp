using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 5: each client adapter takes the shim areas it
/// calls, handed over by the client's composition root. A fact per seam reaches
/// the adapter through that registration, as its interface, over a
/// <see cref="FakeShimRoot"/>, so it covers both that the root passes the right
/// area and that the adapter calls the right slot. Over an engine that never
/// attached, the root hands the adapter no area and a call refuses.
/// </summary>
public unsafe class ClientAdapterRegistrationTests
{
    // A slot declared SuppressGCTransition cannot call a managed callback, so
    // each fact drives one that is not.
    [UnmanagedCallersOnly]
    private static int TextureSeventeen(void* self) => 17;

    /// <summary>The client's adapters, composed as the client's root composes them, over <paramref name="side"/>.</summary>
    private static ServiceContainer Composed(EngineSide side)
    {
        var services = new ServiceContainer();
        ClientEntityFetches.AddTo(services, new ModuleEngine(side));
        return services;
    }

    private static EngineSide Attached(FakeShimRoot root)
    {
        var side = new EngineSide(Side.Client);
        Assert.True(side.AttachClientShims(root.Bind(), out string why), why);
        return side;
    }

    [Fact]
    public void TheRegisteredUiSurfaceCallsThroughTheUiArea()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpUiClient001", nameof(ShimUiClientFwd.UiTextureCreate),
                  (nint)(delegate* unmanaged<void*, int>)&TextureSeventeen);

        Assert.Equal(17, Composed(Attached(root)).Require<IUiSurface>().TextureCreate());
    }

    [Fact]
    public void OverAnUnattachedEngineTheUiSurfaceRefuses()
    {
        var surface = Composed(new EngineSide(Side.Client)).Require<IUiSurface>();

        var refusal = Assert.Throws<InvalidOperationException>(() => surface.TextureCreate());
        Assert.Contains("UiClient", refusal.Message);
    }
}
