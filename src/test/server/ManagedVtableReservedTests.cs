using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The reserved-words block (client Steam, Decision 1a): a Valve base class
/// with data members after its vptr (CCallbackBase's flags and callback id)
/// that the caller writes, so the block keeps N caller-owned words between the
/// vptr and our header. And instances: one contract, many live blocks, each
/// keyed by its own name, each patched in place on a reload.
/// </summary>
public unsafe partial class ManagedVtableTests
{
    // Our header, read the way the runtime reads it: back from the vtable.
    private static nint* Table(ManagedVtable obj) => (nint*)((nint*)obj.Pointer)[0];
    private static nint ReservedHandle(ManagedVtable obj) => Table(obj)[-3];

    // Steam's registered flag at byte 8 and callback id 0x151 at byte 12.
    private static readonly nint SteamWord = unchecked((nint)0x0000_0151_0000_0001L);

    private static ManagedVtable Reserved(ManagedVtables vtables, object target, string? instance, int words = 1)
        => vtables.Create(contract: "R", target, [(nint)11, (nint)12, (nint)13], [1u, 2u, 3u],
                          reservedWords: words, instance: instance);

    [Fact]
    public void AReservedBlockPutsTheCallersWordsBetweenTheVptrAndTheHeader()
    {
        using var vtables = new ManagedVtables();
        var plain = vtables.Create<object>("P", new object(), [(nint)11, (nint)12], [1u, 2u]);
        var obj = Reserved(vtables, new object(), instance: null, words: 2);

        // The control: a plain block's table follows its four-word header.
        Assert.Equal((nint)((nint*)plain.Pointer + 4), (nint)Table(plain));
        Assert.Equal((nint)((nint*)obj.Pointer + 1 + 2 + 3), (nint)Table(obj));
        Assert.Equal(2, obj.ReservedWords);
        Assert.Equal(0, ((nint*)obj.Pointer)[1]);
        Assert.Equal(0, ((nint*)obj.Pointer)[2]);
        Assert.NotEqual(0, ReservedHandle(obj));
        Assert.Equal(new nint[] { 11, 12, 13 }, new[] { Table(obj)[0], Table(obj)[1], Table(obj)[2] });
    }

    [Fact]
    public void AThunkOnAReservedBlockFindsItsTargetAndItsRegistry()
    {
        using var vtables = new ManagedVtables();
        var target = new object();
        var obj = Reserved(vtables, target, instance: null);

        Assert.Same(target, ManagedVtable.Target<object>(obj.Pointer));
        Assert.Same(vtables, ManagedVtable.Enter(obj.Pointer));
        Assert.Equal(1, vtables.Depth);
        ManagedVtable.Leave(vtables);
    }

    [Fact]
    public void TheCallersWordsSurviveADetachAndThePatch()
    {
        using var vtables = new ManagedVtables();
        var before = Reserved(vtables, new object(), instance: null);
        ((nint*)before.Pointer)[1] = SteamWord;   // Steam's registered flag and callback id

        vtables.Detach();
        Assert.Equal(0, ReservedHandle(before));
        Assert.Equal(ManagedVtableTraps.Address(1), Table(before)[1]);

        var after = Reserved(vtables, new object(), instance: null);
        Assert.Equal((nint)before.Pointer, (nint)after.Pointer);
        Assert.Equal(SteamWord, ((nint*)after.Pointer)[1]);
        Assert.NotEqual(0, ReservedHandle(after));
        Assert.Equal(12, Table(after)[1]);
    }

    [Fact]
    public void APatchWithADifferentReservedCountIsRefused()
    {
        using var vtables = new ManagedVtables();
        Reserved(vtables, new object(), instance: null, words: 1);
        vtables.Detach();

        var e = Assert.Throws<InvalidOperationException>(() => Reserved(vtables, new object(), instance: null, words: 2));
        Assert.Contains("reserves 2 word(s) after the vptr and native holds a block with 1", e.Message);
    }

    [Fact]
    public void ReleaseFreesAReservedBlockThroughItsOwnHeader()
    {
        using var vtables = new ManagedVtables();
        var obj = Reserved(vtables, new object(), instance: null);

        ManagedVtable.ReleaseBlock<object>(obj.Pointer);

        Assert.True(obj.Released);
        Assert.Equal(1, vtables.Freed);
        Assert.Equal(0, vtables.Live);
    }

    [Fact]
    public void ATrappedSlotOnAReservedBlockNamesItsContract()
    {
        var trapped = new List<(string, int)>();
        using var vtables = new ManagedVtables((contract, slot) => trapped.Add((contract, slot)));
        var obj = Reserved(vtables, new object(), instance: "a");
        vtables.Detach();

        ((delegate* unmanaged<void*, void>)Table(obj)[2])(obj.Pointer);

        Assert.Equal(new[] { ("R", 2) }, trapped);
    }

    [Fact]
    public void OneContractHoldsAsManyInstancesAsItHasNames()
    {
        using var vtables = new ManagedVtables();
        var a = Reserved(vtables, new object(), instance: "a");
        var b = Reserved(vtables, new object(), instance: "b");

        Assert.NotEqual((nint)a.Pointer, (nint)b.Pointer);
        Assert.Equal(2, vtables.Live);
        Assert.Equal("R", a.Contract);
        Assert.True(vtables.Holds("R", "a"));
        Assert.True(vtables.Holds("R", "b"));
        Assert.False(vtables.Holds("R"));   // the unnamed instance is its own key
        Assert.Throws<InvalidOperationException>(() => Reserved(vtables, new object(), instance: "a"));
    }

    [Fact]
    public void EachInstanceIsPatchedInPlaceOnTheReload()
    {
        using var vtables = new ManagedVtables();
        var a = Reserved(vtables, new object(), instance: "a");
        var b = Reserved(vtables, new object(), instance: "b");
        Assert.Equal(2, vtables.Detach());

        var b2 = Reserved(vtables, new object(), instance: "b");
        var a2 = Reserved(vtables, new object(), instance: "a");

        Assert.Equal((nint)a.Pointer, (nint)a2.Pointer);
        Assert.Equal((nint)b.Pointer, (nint)b2.Pointer);
        Assert.Equal(0, vtables.Detached);
    }

    [Fact]
    public void AFreedInstanceLeavesItsSiblingLive()
    {
        using var vtables = new ManagedVtables();
        var a = Reserved(vtables, new object(), instance: "a");
        var b = Reserved(vtables, new object(), instance: "b");

        Assert.True(a.Release());

        Assert.False(vtables.Holds("R", "a"));
        Assert.True(vtables.Holds("R", "b"));
        Assert.False(b.Released);
    }

    [Fact]
    public void AReloadChecksEveryInstanceAgainstItsContractsHashes()
    {
        using var vtables = new ManagedVtables();
        Reserved(vtables, new object(), instance: "a");
        Reserved(vtables, new object(), instance: "b");

        Assert.Null(vtables.CheckReload(new Dictionary<string, uint[]> { ["R"] = [1u, 2u, 3u] }));
        Assert.Contains("R slot 1", vtables.CheckReload(new Dictionary<string, uint[]> { ["R"] = [1u, 9u, 3u] }));
    }

    // ---- the layouts: what reserves words, and how many --------------------

    private static IEnumerable<(Type Layout, ImplementedByManagedAttribute Attribute)> ManagedWrittenLayouts()
        => typeof(ManagedVtable).Assembly.GetTypes()
               .Select(t => (t, t.GetCustomAttributes(typeof(ImplementedByManagedAttribute), false)
                                 .Cast<ImplementedByManagedAttribute>().SingleOrDefault()))
               .Where(x => x.Item2 is not null)
               .Select(x => (x.t, x.Item2!));

    [Fact]
    public void NoGeneratedLayoutReservesWords()
    {
        // The one that did was CCallbackBase's (flags and callback id after
        // the vptr). libss_fwd_client builds that object now
        // (plan_engine_forwarders.md phase 5), and managed writes only its
        // managed side, which carries no data. The machinery above stays
        // until nothing names it.
        var layouts = ManagedWrittenLayouts().ToList();

        Assert.NotEmpty(layouts);   // the stimulus: the managed-written layouts are read
        Assert.All(layouts, x => Assert.True(x.Attribute.ReservedWords == 0,
            $"{x.Layout.Name} reserves {x.Attribute.ReservedWords} word(s)"));
    }

    [Fact]
    public void TheSteamCallbacksManagedSideIsOurLayoutWithOneRun()
    {
        var steam = ManagedWrittenLayouts().Single(x => x.Attribute.NativeClass == "IManagedSteamCallback");

        Assert.Equal("ManagedSteamCallbackVtbl", steam.Layout.Name);
        Assert.False(steam.Attribute.ValveLayout);
        Assert.Equal(new[] { "Release", "Validate", "Run" }, steam.Layout.GetFields().Select(f => f.Name));
        Assert.DoesNotContain(ManagedWrittenLayouts(), x => x.Attribute.NativeClass == "CCallbackBase");
    }
}
