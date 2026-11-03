using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE UI CROSSINGS THAT LOAD OR RENDER MATERIALS RUN A FULL GC TRANSITION.
///
/// <para>
/// Loading a material constructs its proxies, and a proxy answers the engine
/// through a managed table: CManagedMaterialProxy::Init calls the ProxyInit
/// thunk, a reverse P/Invoke, which may only be entered from a thread the
/// runtime believes is in native code. Under [SuppressGCTransition] the thread
/// stays flagged cooperative for the whole native call, and the runtime fails
/// the process on the spot (0x80131506, "attempted to call a
/// UnmanagedCallersOnly method from managed code"). The sniper scope's first
/// zoomed frame died exactly there, twice: once on the material load that
/// names the texture, once on a draw's refract refresh.
/// </para>
/// </summary>
public class UiMaterialCrossingTests
{
    /// <summary>
    /// <c>UiMaterialTexture</c> is stock's DrawSetTextureFile, which LOADS the
    /// .vmt, so it may reach a managed proxy's ProxyInit.
    /// </summary>
    [Fact]
    public void TheSlotThatLoadsMaterialsDoesNotSuppressTheGcTransition()
    {
        var field = EngineApiAreasTests.SlotField("UiMaterialTexture");

        Assert.NotNull(field);
        Assert.False(EngineApiAreasTests.IsSuppressed(field),
            "UiMaterialTexture must not carry [SuppressGCTransition]: DrawSetTextureFile loads a material, "
            + "and the load constructs its proxies; a managed proxy's ProxyInit entered with the thread "
            + "still flagged cooperative is an instant fatal, not a slow call");
    }

    /// <summary>
    /// The textured draws may refresh the refract texture (stock's
    /// UpdateRefractTexture, tf_hud_scope.cpp), which renders the refract
    /// scene; a scene render constructs world materials, and any carrying a
    /// managed proxy calls ProxyInit mid-render. The plain colour draws stay
    /// suppressed.
    /// </summary>
    [Theory]
    [InlineData("UiDrawTexturedRect")]
    [InlineData("UiDrawPolygon")]
    [InlineData("UiDrawTexturedQuad")]
    public void TheSlotsThatRefreshTheRefractDoNotSuppressTheGcTransition(string slot)
    {
        var field = EngineApiAreasTests.SlotField(slot);

        Assert.NotNull(field);
        Assert.False(EngineApiAreasTests.IsSuppressed(field),
            $"{slot} must not carry [SuppressGCTransition]: its body may refresh the refract texture, "
            + "whose scene render constructs world materials; a managed proxy's ProxyInit entered with "
            + "the thread flagged cooperative is an instant fatal (0x80131506)");
    }

    /// <summary>
    /// The arm the two facts above need to mean anything: the colour-only draws
    /// still carry the attribute, so the reading sees a suppressed slot.
    /// </summary>
    [Theory]
    [InlineData("UiDrawFilledRect")]
    [InlineData("UiDrawLine")]
    public void TheColourOnlyDrawsStaySuppressed(string slot)
    {
        var field = EngineApiAreasTests.SlotField(slot);

        Assert.NotNull(field);
        Assert.True(EngineApiAreasTests.IsSuppressed(field), $"{slot} binds a colour and emits: it stays suppressed");
    }
}
