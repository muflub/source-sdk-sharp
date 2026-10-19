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

    // The root's table is Shared.Interop's, the server's runtime and areas are
    // Server.Interop's (tools/managed-areas writes each table into the assembly
    // that builds it), and the Valve layouts are still the Abi's. The client's
    // are ClientManagedTablesTests'.
    private static IEnumerable<(Type Layout, ImplementedByManagedAttribute Attribute)> ManagedWrittenLayouts()
        => new[] { typeof(ManagedVtable).Assembly, typeof(ManagedRootTable).Assembly,
                   typeof(SourceSharp.Server.Interop.ServerRuntimeBinding).Assembly }
               .SelectMany(a => a.GetTypes())
               .Select(t => (t, t.GetCustomAttributes(typeof(ImplementedByManagedAttribute), false)
                                 .Cast<ImplementedByManagedAttribute>().SingleOrDefault()))
               .Where(x => x.Item2 is not null)
               .Select(x => (x.t, x.Item2!));

    [Fact]
    public void OnlyTheVphysicsCallbacksReserveWordsAndEachReservesTheObjectsSize()
    {
        // CCallbackBase's layout once reserved its flags and callback id;
        // libss_fwd_client builds that object now (plan_engine_forwarders.md
        // phase 5). What reserves words since plan_forwarders_everywhere.md
        // phase 5 is each vphysics callback: libss_fwd builds the Valve object
        // (a vptr and its managed target) in the words after the table, so it
        // lives and dies with the block. Exactly those six, and exactly the
        // object's two words (kManagedPhysicsCallbackWords).
        string[] callbacks =
        [
            "ManagedPhysicsObjectEventTable", "ManagedPhysicsCollisionEventTable", "ManagedPhysicsCollisionSolverTable",
            "ManagedPhysicsConstraintEventTable", "ManagedMotionEventTable", "ManagedVPhysicsKeyHandlerTable",
        ];
        var layouts = ManagedWrittenLayouts().ToList();

        Assert.NotEmpty(layouts);   // the stimulus: the managed-written layouts are read
        Assert.Equal(callbacks.Order(), layouts.Where(x => x.Attribute.ReservedWords != 0).Select(x => x.Layout.Name).Order());
        Assert.All(layouts.Where(x => callbacks.Contains(x.Layout.Name)),
            x => Assert.True(x.Attribute.ReservedWords == 2, $"{x.Layout.Name} reserves {x.Attribute.ReservedWords} word(s)"));
    }

    [Fact]
    public void EachSidesTablesAreInItsOwnInterop()
    {
        // The root's table is the one both sides build, so Shared.Interop's
        // (its ManagedRoot builds it); every other table is the side's whose
        // code builds it. None is the Abi's, which holds plumbing only.
        bool IsTable(Type t) => t.GetCustomAttributes(typeof(ImplementedByManagedAttribute), false).Length > 0
                                && t.Name.EndsWith("Table", StringComparison.Ordinal);

        Assert.DoesNotContain(typeof(ManagedVtable).Assembly.GetTypes(), IsTable);
        Assert.Equal(typeof(SourceSharp.Shared.Interop.ManagedRoot).Assembly, typeof(ManagedRootTable).Assembly);
        Assert.Contains(ManagedWrittenLayouts(), x => x.Layout.Name == "ManagedRuntimeServerTable"
                                                       && x.Layout.Assembly == typeof(SourceSharp.Server.Interop.ServerRuntimeBinding).Assembly);
    }
}
