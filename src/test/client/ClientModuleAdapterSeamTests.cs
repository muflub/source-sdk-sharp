using SourceSharp.Abi;
using SourceSharp.Client;
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
        var engine = new ModuleEngine(EngineSide.Create(Side.Client));
        var adapters = new FakeClientEngineAdapters();

        new ClientModule(engine, adapters).ComposeEngineAreas();

        Assert.Equal(1, adapters.Calls);
        Assert.Same(engine, adapters.Engine);
    }

    [Fact]
    public void TheGameConstructorComposesTheRealAdaptersWhichRefuseAnUnattachedEngine()
    {
        var module = new ClientModule(new ModuleEngine(EngineSide.Create(Side.Client)));

        var refusal = Assert.Throws<InvalidOperationException>(() => module.ComposeEngineAreas());
        Assert.Contains("before its shim areas were attached", refusal.Message);
    }
}
