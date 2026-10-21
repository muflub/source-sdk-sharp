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
        var served = TestSides.Create(Side.Server);
        var bare = TestSides.Create(Side.Server);

        using var root = new FakeShimRoot();
        int word = 0;
        root.Interface(EngineLevel.InterfaceVersion, &word).Forwarders<ShimLevelFwd>();

        served.Initialize();
        Assert.True(served.AttachOwnShims(root.Bind(), out string why), why);

        string answered = ShimProbes.Level(new ModuleEngine(served));
        string refused = ShimProbes.Level(new ModuleEngine(bare));

        Assert.True(answered.StartsWith("IShimLevel: bound", StringComparison.Ordinal),
                    $"the engine whose root serves the level interface binds it: {answered}");
        Assert.True(refused.StartsWith("IShimLevel: REFUSED", StringComparison.Ordinal),
                    $"and the other engine, with no root, refuses rather than borrowing the first's: {refused}");
    }

    // The level probe's span is the forwarder table's (plan_forwarders_everywhere.md
    // phase 6): what native serves under SSFwd_ShimLevel against what ShimLevelFwd reads.
    private static string Level(Action<FakeShimRoot> serve)
    {
        var side = TestSides.Create(Side.Server);
        using var root = new FakeShimRoot();
        int word = 0;
        root.Interface(EngineLevel.InterfaceVersion, &word);
        serve(root);

        side.Initialize();
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        return ShimProbes.Level(new ModuleEngine(side));
    }

    [Fact]
    public void TheLevelProbeReportsTheForwarderTablesSpanAgainstTheStruct()
    {
        int reads = Forwarders.SlotCount<ShimLevelFwd>();

        Assert.Equal($"IShimLevel: bound, native serves {reads} forwarder(s) and ShimLevelFwd reads {reads}",
                     Level(r => r.Forwarders<ShimLevelFwd>()));
    }

    [Fact]
    public void TheLevelProbeSaysWhenNativeServesFewerForwardersThanTheStructReads()
    {
        int reads = Forwarders.SlotCount<ShimLevelFwd>();

        Assert.Equal($"IShimLevel: bound, native serves {reads - 1} forwarder(s) and ShimLevelFwd reads {reads}"
                     + " — WHICH IS MORE THAN NATIVE HAS",
                     Level(r => r.Forwarders<ShimLevelFwd>(out _, reads - 1)));
    }

    [Fact]
    public void TheLevelProbeSaysWhenTheObjectIsServedAndItsForwardersAreNot()
    {
        string answer = Level(root => root.Refuse(ShimLevelFwd.TableName));

        Assert.StartsWith("IShimLevel: bound, but its forwarders are REFUSED — ", answer);
        Assert.Contains(ShimLevelFwd.TableName, answer);
    }

    [Fact]
    public void TheStringTableAcquireAsksTheEngineItIsHanded()
    {
        var served = TestSides.Create(Side.Server);
        var bare = TestSides.Create(Side.Server);

        using var root = new FakeShimRoot();
        var self = stackalloc nint[1];
        root.Interface(EngineStringTableDirectory.ServerInterfaceVersion, self)
            .Forwarders<NetworkStringTableContainerFwd>()
            .Forwarders<NetworkStringTableFwd>();

        served.Initialize();
        Assert.True(served.AttachOwnShims(root.Bind(), out string why), why);

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
        var served = TestSides.Create(Side.Server);
        var bare = TestSides.Create(Side.Server);

        using var root = new FakeShimRoot();
        var self = stackalloc nint[1];
        root.Interface(EngineServer.InterfaceVersion, self)
            .Forwarders<EngineServerFwd>()
            .Forwarders<NetChannelInfoFwd>();

        served.Initialize();
        Assert.True(served.AttachOwnShims(root.Bind(), out string why), why);

        // As above: only the engine whose own root answers binds. cs_vtable
        // acquires this way, for the module it runs in.
        Assert.True(EngineServer.Acquire(served.Interfaces, out string servedWhy) is not null,
                    $"the served engine reached its own root: {servedWhy}");
        Assert.True(EngineServer.Acquire(bare.Interfaces, out string bareWhy) is null,
                    $"the other engine reached no root at all, not the first's: {bareWhy}");
    }
}
