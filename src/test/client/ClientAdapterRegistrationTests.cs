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

    /// <summary>
    /// The client's registration of <typeparamref name="T"/> over a root that
    /// serves <paramref name="slot"/> of <paramref name="version"/> as
    /// <paramref name="fn"/>, called once.
    /// </summary>
    private static TResult Through<T, TResult>(string version, string slot, nint fn, Func<T, TResult> call)
        where T : class
    {
        using var root = new FakeShimRoot();
        root.Live(version, slot, fn);
        return call(Composed(Attached(root)).Require<T>());
    }

    private const string Fx = "SourceSharpFxClient001";

    [UnmanagedCallersOnly] private static int One(void* self) => 1;
    [UnmanagedCallersOnly] private static int Seven(void* self) => 7;
    [UnmanagedCallersOnly] private static int Nine(void* self, void* output) => 9;
    [UnmanagedCallersOnly] private static float HalfAboveZ(void* self, float* position) => position[2] + 0.5f;

    [Fact]
    public void TheRegisteredFxSuppressionAsksTheFxArea()
        => Assert.True(Through<IFxSuppression, bool>(Fx, nameof(ShimFxClientFwd.FxCanPredict),
                                                     (nint)(delegate* unmanaged<void*, int>)&One, s => s.CanPredict()));

    [Fact]
    public void TheRegisteredFxRenderAsksTheFxArea()
        => Assert.Equal(7, Through<IFxRender, int>(Fx, nameof(ShimFxClientFwd.FxLocalPlayer),
                                                   (nint)(delegate* unmanaged<void*, int>)&Seven, r => r.LocalPlayer()));

    [Fact]
    public void TheRegisteredFlashReadsAskTheFxArea()
        => Assert.Equal(7, Through<IFxFlashReads, int>(Fx, nameof(ShimFxClientFwd.FxMaterialsCached),
                                                       (nint)(delegate* unmanaged<void*, int>)&Seven, r => r.MaterialsCached()));

    [Fact]
    public void TheRegisteredFxPrecacheAsksTheFxArea()
        => Assert.Equal(7, Through<IFxPrecache, int>(Fx, nameof(ShimFxClientFwd.FxPrecacheCount),
                                                     (nint)(delegate* unmanaged<void*, int>)&Seven, p => p.Count()));

    [Fact]
    public void TheRegisteredSpriteGlowAsksTheFxArea()
        => Assert.Equal(3.5f, Through<IFxSpriteGlow, float>(Fx, nameof(ShimFxClientFwd.FxGlowSightDistance),
                                                            (nint)(delegate* unmanaged<void*, float*, float>)&HalfAboveZ,
                                                            g => g.SightDistance(new Vector(1, 2, 3))));

    [Fact]
    public void TheRegisteredSpriteDrawAsksTheFxArea()
        => Assert.Equal(9, Through<IFxSpriteDraw, int>(Fx, nameof(ShimFxClientFwd.FxSpriteDrawEnd),
                                                       (nint)(delegate* unmanaged<void*, void*, int>)&Nine,
                                                       d => d.End(new float[ClientSpriteRequest.OutFloats])));

    /// <summary>A guarded call over an unattached engine answers its default, as the attach guard made it.</summary>
    [Fact]
    public void OverAnUnattachedEngineAGuardedFxCallAnswersItsDefault()
        => Assert.Equal(-1, Composed(new EngineSide(Side.Client)).Require<IFxFlashReads>().EntityIndex(5));

    [Fact]
    public void TheRegisteredUiSurfaceCallsThroughTheUiArea()
    {
        Assert.Equal(17, Through<IUiSurface, int>("SourceSharpUiClient001", nameof(ShimUiClientFwd.UiTextureCreate),
                                                  (nint)(delegate* unmanaged<void*, int>)&TextureSeventeen,
                                                  s => s.TextureCreate()));
    }

    [Fact]
    public void OverAnUnattachedEngineTheUiSurfaceRefuses()
    {
        var surface = Composed(new EngineSide(Side.Client)).Require<IUiSurface>();

        var refusal = Assert.Throws<InvalidOperationException>(() => surface.TextureCreate());
        Assert.Contains("UiClient", refusal.Message);
    }
}
