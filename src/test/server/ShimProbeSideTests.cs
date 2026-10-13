using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload D8: the cs_vtable probes and the module-level acquires
/// ask the interface factory of the module they are handed, not the
/// process-wide one that followed the thread's active side. Two engines in one
/// process each answer from their own shim root.
/// </summary>
public sealed unsafe class ShimProbeSideTests
{
    [Fact]
    public void TheLevelProbeAsksTheEngineItIsHanded()
    {
        var served = new EngineSide(Side.Server);
        var bare = new EngineSide(Side.Server);

        using var root = new FakeShimRoot();
        int word = 0;
        root.Interface(EngineLevel.InterfaceVersion, &word);

        served.Initialize();
        Assert.True(served.AttachShims(root.Bind(), out string why), why);

        string answered = ShimProbes.Level(new ModuleEngine(served));
        string refused = ShimProbes.Level(new ModuleEngine(bare));

        Assert.True(answered.StartsWith("IShimLevel: bound", StringComparison.Ordinal),
                    $"the engine whose root serves the level interface binds it: {answered}");
        Assert.True(refused.StartsWith("IShimLevel: REFUSED", StringComparison.Ordinal),
                    $"and the other engine, with no root, refuses rather than borrowing the first's: {refused}");
    }

    [Fact]
    public void TheStringTableAcquireAsksTheEngineItIsHanded()
    {
        var served = new EngineSide(Side.Server);
        var bare = new EngineSide(Side.Server);

        using var root = new FakeShimRoot();
        var self = stackalloc nint[1];
        root.Interface(EngineStringTableDirectory.ServerInterfaceVersion, self)
            .Forwarders<NetworkStringTableContainerFwd>()
            .Forwarders<NetworkStringTableFwd>();

        served.Initialize();
        Assert.True(served.AttachShims(root.Bind(), out string why), why);

        // The served engine's own root answers the container and its
        // forwarders, so its acquire binds; the bare engine has no root, and
        // does not borrow the first engine's.
        Assert.True(EngineStringTableDirectory.AcquireServer(new ModuleEngine(served), out string servedWhy) is not null,
                    $"the served engine reached its own root: {servedWhy}");
        Assert.True(EngineStringTableDirectory.AcquireServer(new ModuleEngine(bare), out string bareWhy) is null,
                    $"the other engine reached no root at all, not the first's: {bareWhy}");
    }

    [Fact]
    public void TheEngineServerAcquireAsksTheEngineItIsHanded()
    {
        var served = new EngineSide(Side.Server);
        var bare = new EngineSide(Side.Server);

        using var root = new FakeShimRoot();
        var self = stackalloc nint[1];
        root.Interface(EngineServer.InterfaceVersion, self)
            .Forwarders<EngineServerFwd>()
            .Forwarders<NetChannelInfoFwd>();

        served.Initialize();
        Assert.True(served.AttachShims(root.Bind(), out string why), why);

        // As above: only the engine whose own root answers binds. cs_vtable
        // acquires this way, for the module it runs in.
        Assert.True(EngineServer.Acquire(new ModuleEngine(served), out string servedWhy) is not null,
                    $"the served engine reached its own root: {servedWhy}");
        Assert.True(EngineServer.Acquire(new ModuleEngine(bare), out string bareWhy) is null,
                    $"the other engine reached no root at all, not the first's: {bareWhy}");
    }
}
