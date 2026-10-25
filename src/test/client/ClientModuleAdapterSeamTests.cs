using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client composition root registers its engine adapters (the seams whose
/// real implementations call shim areas) through the
/// <c>IClientEngineAdapters</c> it was handed, so a test composition hands
/// fakes and needs no attached areas, while the game's constructor composes
/// the real ones, which refuse an engine that never attached.
/// </summary>
public class ClientModuleAdapterSeamTests
{
    [Fact]
    public void ComposingAModuleAsksTheAdaptersItWasHandedOnceOverItsOwnEngine()
    {
        var engine = new ClientModuleEngine(TestSides.Create(Side.Client));
        var adapters = new FakeClientEngineAdapters();

        new ClientModule(engine, adapters).ComposeEngineAreas();

        Assert.Equal(1, adapters.Calls);
        Assert.Same(engine, adapters.Engine);
    }

    [Fact]
    public void TheGameConstructorComposesTheRealAdapters()
    {
        // The real adapters over the side's own areas: here the default ones,
        // so composing succeeds and an adapter answers its default. Nothing
        // asks whether an engine is attached (plan_shim_areas_split phase 11
        // review); FakeClientEngineAdapters above is the double a fact hands.
        var module = new ClientModule(new ClientModuleEngine(TestSides.Create(Side.Client)));

        module.ComposeEngineAreas();

        Assert.Equal(0, module.Services.Require<IUiSurface>().TextureCreate());
    }
}
