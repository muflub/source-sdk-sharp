using System.Runtime;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;
using SourceSharp.Shared.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables D6: what a managed-built table must survive, driven
/// through the real generated <c>IManagedRoot</c> table the way native calls it;
/// and plan_managed_vtable_reload.md L3: the side's registry owns every block
/// and frees it on the one release.
/// In ProcessWide because the GC fact forces full compacting collections.
/// </summary>
[Collection(ProcessWide.Name)]
public unsafe partial class ManagedVtableTests
{
    private static ManagedVtable Stand(ManagedVtables vtables, string name)
        => vtables.Create<object>(name, new object(), [(nint)1, (nint)2], [0u, 0u]);

    // Slot 0, called the way native's ManagedRelease calls it. Only for a
    // block the generator built: a stand-in's slots are not code.
    private static void ReleaseThroughTheTable(ManagedVtable obj)
    {
        var self = (nint*)obj.Pointer;
        ((delegate* unmanaged<void*, void>)((nint*)self[0])[0])(self);
    }

    // What every generated Release thunk runs, for a stand-in block.
    private static void ReleaseAsTheThunkDoes(ManagedVtable obj) => ManagedVtable.ReleaseBlock<object>(obj.Pointer);

    // Builds a root whose only reference to its ManagedRoot is the block's
    // handle, so a collection that drops or loses the target is visible.
    private static (ManagedVtable Root, nint Expected) Detached(ManagedVtables vtables)
    {
        var bindings = new ManagedBindings();
        bindings.Add("SourceSharpProbe001", v => Stand(v, "SourceSharpProbe001"));
        var root = new ManagedRoot(bindings, vtables).Bind();
        return (root, (nint)bindings.Acquire("SourceSharpProbe001", vtables)!.Pointer);
    }

    [Fact]
    public void ACompactingCollectionMovesTheTargetAndTheCallStillLands()
    {
        using var vtables = new ManagedVtables();
        var (root, expected) = Detached(vtables);

        // Churn, then a forced, blocking, compacting collection of every
        // generation: the target is reachable only through the block's handle.
        for (int i = 0; i < 4; i++)
        {
            _ = new byte[64 * 1024][];
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(expected, Acquire(root, "SourceSharpProbe001"));
    }

    [Fact]
    public void TwoSidesRegistriesAreIndependent()
    {
        using var serverVtables = new ManagedVtables();
        using var clientVtables = new ManagedVtables();
        var (server, serverArea) = Detached(serverVtables);
        var (client, clientArea) = Detached(clientVtables);

        Assert.NotEqual(serverArea, clientArea);
        Assert.Equal(serverArea, Acquire(server, "SourceSharpProbe001"));

        Assert.Equal(2, clientVtables.FreeRemaining());

        // Freeing one side's blocks leaves the other's answering.
        Assert.True(client.Released);
        Assert.Equal(0, clientVtables.Live);
        Assert.Equal(2, serverVtables.Live);
        Assert.Equal(serverArea, Acquire(server, "SourceSharpProbe001"));
    }

    // ---- L3: the registry owns, releases and frees ------------------------

    [Fact]
    public void EveryBlockIsFreedOnceAndTheCountReturnsToZero()
    {
        using var vtables = new ManagedVtables();
        var a = Stand(vtables, "A");
        var b = Stand(vtables, "B");
        var c = Stand(vtables, "C");
        Assert.Equal(3, vtables.Live);   // the control: three allocated

        ReleaseAsTheThunkDoes(a);         // native's slot 0
        Assert.True(b.Release());         // the managed safety net
        Assert.Equal(1, vtables.FreeRemaining());

        Assert.Equal(0, vtables.Live);
        Assert.Equal(3, vtables.Freed);
        Assert.True(a.Released && b.Released && c.Released);
    }

    // Regression (live gate 9884cec7, "still bound x2"): vphysics never
    // releases its handlers, so nothing freed their blocks and the final
    // unload's safety net reported both as leaks.
    [Fact]
    public void CallerHeldBlocksAreFreedOnceNativeReleasesTheRoot()
    {
        using var vtables = new ManagedVtables();
        var root = new ManagedRoot(new ManagedBindings(), vtables).Bind();
        var objectEvents = Stand(vtables, "PhysicsObjectEvent");
        var collisionEvents = Stand(vtables, "PhysicsCollisionEvent");
        ReleaseThroughTheTable(root);

        Assert.Equal(2, vtables.ReleaseCallerHeld(root, objectEvents, collisionEvents));

        Assert.True(objectEvents.Released && collisionEvents.Released);
        Assert.Equal(0, vtables.FreeRemaining());
    }

    [Fact]
    public void CallerHeldBlocksAreKeptWhileTheRootIsHeldAsOnAReload()
    {
        using var vtables = new ManagedVtables();
        var root = new ManagedRoot(new ManagedBindings(), vtables).Bind();
        var objectEvents = Stand(vtables, "PhysicsObjectEvent");

        Assert.Equal(0, vtables.ReleaseCallerHeld(root, objectEvents, null));

        Assert.False(objectEvents.Released);
    }

    [Fact]
    public void TheNativeReleaseSlotFreesTheBlock()
    {
        using var vtables = new ManagedVtables();
        var root = new ManagedRoot(new ManagedBindings(), vtables).Bind();
        Assert.False(root.Released);

        ReleaseThroughTheTable(root);

        Assert.True(root.Released);
        Assert.Equal(0, vtables.Live);
        Assert.Equal(1, vtables.Freed);
        Assert.Null(vtables.HashesOf("IManagedRoot"));
    }

    [Fact]
    public void TheSafetyNetAfterANativeReleaseTouchesNothing()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        bindings.Add("A", v => Stand(v, "A"));
        var a = bindings.Acquire("A", vtables)!;
        ReleaseAsTheThunkDoes(a);
        Assert.Equal(1, vtables.Freed);

        // Asked the registry, not the freed block: nothing to catch, nothing freed again.
        Assert.Equal(0, vtables.FreeRemaining());
        Assert.Equal(1, vtables.Freed);
    }

    [Fact]
    public void ASecondReleaseIsRefused()
    {
        using var vtables = new ManagedVtables();
        var a = Stand(vtables, "A");

        Assert.True(a.Release());          // the control: the first is honoured
        Assert.False(a.Release());
        Assert.False(vtables.Free((nint)a.Pointer));
        Assert.Equal(1, vtables.Freed);
    }

    [Fact]
    public void AContractHasOneLiveBlockAtATime()
    {
        using var vtables = new ManagedVtables();
        var first = Stand(vtables, "A");

        Assert.Throws<InvalidOperationException>(() => Stand(vtables, "A"));

        first.Release();
        var again = Stand(vtables, "A");   // released, the name is free again
        Assert.False(again.Released);
    }

    [Fact]
    public void TheRegistryKeepsEachContractsHashes()
    {
        using var vtables = new ManagedVtables();
        vtables.Create<object>("A", new object(), [(nint)1, (nint)2, (nint)3], [0x10u, 0x11u, 0x12u]);

        Assert.Equal(new uint[] { 0x10, 0x11, 0x12 }, vtables.HashesOf("A"));
        Assert.Null(vtables.HashesOf("B"));
    }

    [Fact]
    public void ABlockWithoutItsTwoFixedSlotsOrWithMismatchedHashesIsRefused()
    {
        using var vtables = new ManagedVtables();

        Assert.Throws<ArgumentException>(() => vtables.Create<object>("A", new object(), [(nint)1], [0u]));
        Assert.Throws<ArgumentException>(() => vtables.Create<object>("A", new object(), [(nint)1, (nint)2], [0u]));
        Assert.Equal(0, vtables.Live);
    }

    [Fact]
    public void DisposingTheRegistryFreesWhatIsLeft()
    {
        var vtables = new ManagedVtables();
        var a = Stand(vtables, "A");

        vtables.Dispose();

        Assert.True(a.Released);
        Assert.Equal(1, vtables.Freed);
    }

    // ---- L4: a reload detaches, then the next build patches in place ------

    private static nint Slot(ManagedVtable obj, int i) => ((nint*)((nint*)obj.Pointer)[0])[i];
    private static nint Handle(ManagedVtable obj) => ((nint*)obj.Pointer)[1];

    [Fact]
    public void DetachFreesTheHandleAndTrapsEverySlotButKeepsTheBlock()
    {
        using var vtables = new ManagedVtables();
        var a = vtables.Create<object>("A", new object(), [(nint)11, (nint)12, (nint)13], [1u, 2u, 3u]);
        Assert.NotEqual(0, Handle(a));   // the control: attached, it has a target

        Assert.Equal(1, vtables.Detach());

        Assert.Equal(0, Handle(a));
        for (int i = 0; i < 3; i++)
            Assert.Equal(ManagedVtableTraps.Address(i), Slot(a, i));
        Assert.Equal(1, vtables.Live);
        Assert.Equal(1, vtables.Detached);
        Assert.False(a.Released);
    }

    [Fact]
    public void TheNextBuildPatchesTheSameBlockWithItsHandleAndEverySlot()
    {
        using var vtables = new ManagedVtables();
        var before = vtables.Create<object>("A", new object(), [(nint)11, (nint)12, (nint)13], [1u, 2u, 3u]);
        vtables.Detach();

        var after = vtables.Create<object>("A", new object(), [(nint)21, (nint)22, (nint)23], [1u, 2u, 3u]);

        Assert.Equal((nint)before.Pointer, (nint)after.Pointer);
        Assert.NotEqual(0, Handle(after));
        Assert.Equal(new nint[] { 21, 22, 23 }, new[] { Slot(after, 0), Slot(after, 1), Slot(after, 2) });
        Assert.Equal(0, vtables.Detached);
        Assert.Equal(1, vtables.Live);
    }

    [Fact]
    public void ThePatchRefusesABuildWithAnotherSlotCount()
    {
        using var vtables = new ManagedVtables();
        vtables.Create<object>("A", new object(), [(nint)11, (nint)12, (nint)13], [1u, 2u, 3u]);
        vtables.Detach();

        Assert.Throws<InvalidOperationException>(
            () => vtables.Create<object>("A", new object(), [(nint)21, (nint)22], [1u, 2u]));
        Assert.Equal(1, vtables.Detached);   // still waiting, still trapped
    }

    [Fact]
    public void DetachLetsTheOldTargetBeCollected()
    {
        using var vtables = new ManagedVtables();
        var weak = CreateDetachable(vtables);
        Assert.Equal(1, vtables.Detach());

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive, "a detached block still holds its old target");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateDetachable(ManagedVtables vtables)
    {
        var target = new object();
        vtables.Create<object>("A", target, [(nint)11, (nint)12], [1u, 2u]);
        return new WeakReference(target);
    }

    [Fact]
    public void ASlotReachedWhileDetachedNamesItsContractAndSlot()
    {
        var trapped = new List<(string, int)>();
        using var vtables = new ManagedVtables((contract, slot) => trapped.Add((contract, slot)));
        var root = new ManagedRoot(new ManagedBindings(), vtables).Bind();
        Assert.Equal(nint.Zero, Acquire(root, "nothing"));   // the control: attached, the real Acquire answers
        Assert.Empty(trapped);

        vtables.Detach();
        Acquire(root, "nothing");   // slot 2 through the detached block

        Assert.Equal(new[] { ("IManagedRoot", 2) }, trapped);
    }

    [Fact]
    public void AReloadedSideComesBackOnTheBlocksNativeHolds()
    {
        using var vtables = new ManagedVtables();
        var first = new ManagedBindings();
        first.Add("SourceSharpProbe001", v => Stand(v, "SourceSharpProbe001"));
        var root = new ManagedRoot(first, vtables).Bind();
        nint area = Acquire(root, "SourceSharpProbe001");
        Assert.NotEqual(0, area);

        Assert.Equal(2, vtables.Detach());   // the root and the area

        // The next build: new bindings, new root object, the same registry.
        var second = new ManagedBindings();
        second.Add("SourceSharpProbe001", v => Stand(v, "SourceSharpProbe001"));
        var reloaded = new ManagedRoot(second, vtables).Bind();

        Assert.Equal((nint)root.Pointer, (nint)reloaded.Pointer);
        Assert.Equal(0, vtables.Detached);    // the area was rebuilt without native asking
        Assert.Equal(area, Acquire(reloaded, "SourceSharpProbe001"));
    }

    [Fact]
    public void TheTrapTableCoversEveryManagedBuiltLayout()
    {
        // The Abi's Valve layouts, the root's (Shared.Interop) and the server's
        // (Server.Interop); the client's are held by ClientManagedTablesTests,
        // in the project that sees them.
        var widest = new[] { typeof(ManagedVtable).Assembly, typeof(ManagedRootTable).Assembly, typeof(ManagedRuntimeServerTable).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttributes(typeof(ImplementedByManagedAttribute), false).Length > 0)
            .Select(t => (t.Name, Slots: t.GetFields().Length))
            .ToList();

        Assert.True(widest.Count >= 17, $"found {widest.Count} managed-built layouts");
        Assert.All(widest, w => Assert.True(w.Slots <= ManagedVtableTraps.Count,
            $"{w.Name} has {w.Slots} slots; the trap table covers {ManagedVtableTraps.Count}"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedVtableTraps.Address(ManagedVtableTraps.Count));
    }

    // ---- L5: a reload must keep every registered hash ----------------------

    private static ManagedVtables Registered()
    {
        var vtables = new ManagedVtables();
        vtables.Create<object>("A", new object(), [(nint)1, (nint)2, (nint)3], [0x10u, 0x11u, 0x12u]);
        vtables.Create<object>("B", new object(), [(nint)1, (nint)2], [0x20u, 0x21u]);
        return vtables;
    }

    private static Dictionary<string, uint[]> Next(uint[] a, uint[]? b = null)
    {
        var next = new Dictionary<string, uint[]> { ["A"] = a };
        if (b is not null)
            next["B"] = b;
        return next;
    }

    [Fact]
    public void AnIdenticalBuildMayReload()
    {
        using var vtables = Registered();
        Assert.Null(vtables.CheckReload(Next([0x10, 0x11, 0x12], [0x20, 0x21])));
    }

    [Fact]
    public void AChangedSlotHashRefusesNamingTheContractAndSlot()
    {
        using var vtables = Registered();
        Assert.Null(vtables.CheckReload(Next([0x10, 0x11, 0x12], [0x20, 0x21])));   // the control

        string? why = vtables.CheckReload(Next([0x10, 0x99, 0x12], [0x20, 0x21]));

        Assert.NotNull(why);
        Assert.StartsWith("A slot 1:", why);
        Assert.Contains("0x00000099", why);
    }

    [Fact]
    public void AChangedSlotCountRefuses()
    {
        using var vtables = Registered();
        Assert.Equal("A: the new build has 4 slot(s) and native holds 3",
                     vtables.CheckReload(Next([0x10, 0x11, 0x12, 0x13], [0x20, 0x21])));
    }

    [Fact]
    public void ABuildThatDropsAHeldContractRefuses()
    {
        using var vtables = Registered();
        Assert.Equal("B: the new build does not implement it, and native holds it",
                     vtables.CheckReload(Next([0x10, 0x11, 0x12])));
    }

    [Fact]
    public void ARefusedCheckLeavesTheRunningBuildAnswering()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        bindings.Add("SourceSharpProbe001", v => Stand(v, "SourceSharpProbe001"));
        var root = new ManagedRoot(bindings, vtables).Bind();
        nint area = Acquire(root, "SourceSharpProbe001");

        Assert.NotNull(vtables.CheckReload(new Dictionary<string, uint[]>()));

        Assert.Equal(0, vtables.Detached);
        Assert.Equal(area, Acquire(root, "SourceSharpProbe001"));
    }

    [Fact]
    public void ThePatchItselfRefusesChangedHashes()
    {
        using var vtables = new ManagedVtables();
        vtables.Create<object>("A", new object(), [(nint)1, (nint)2], [0x10u, 0x11u]);
        vtables.Detach();

        var e = Assert.Throws<InvalidOperationException>(
            () => vtables.Create<object>("A", new object(), [(nint)1, (nint)2], [0x10u, 0x12u]));
        Assert.StartsWith("A slot 1:", e.Message);
    }

    [Fact]
    public void TheBuiltServerCarriesEveryContractsHashesInItsFiles()
    {
        string dir = Path.GetDirectoryName(typeof(SourceSharp.Server.ServerModule).Assembly.Location)!;

        var hashes = ManagedVtableHashFiles.Read(dir, ["SourceSharp.Server"]);

        // The root comes from SourceSharp.Shared, reached through the server's references.
        Assert.Equal(LayoutHashes(typeof(ManagedRootTable)), hashes["IManagedRoot"]);
        Assert.Equal(LayoutHashes(typeof(ManagedRuntimeServerTable)), hashes["IManagedRuntimeServer"]);
        // IManagedFrame STOOD HERE: its last slot, FramePostThink, retired at
        // plan_server_native_v2 T4e (managed calls the frame points itself).
        Assert.DoesNotContain("IManagedFrame", hashes.Keys);
        // vphysics' callbacks: managed writes a table of its own for each and
        // libss_fwd builds the Valve object over it (plan_forwarders_everywhere.md
        // phase 5), so their hashes ride along under the managed names.
        Assert.Equal(LayoutHashes(typeof(ManagedPhysicsObjectEventTable)), hashes["IManagedPhysicsObjectEvent"]);
        Assert.Equal(LayoutHashes(typeof(ManagedPhysicsCollisionEventTable)), hashes["IManagedPhysicsCollisionEvent"]);
        Assert.Equal(LayoutHashes(typeof(ManagedPhysicsCollisionSolverTable)), hashes["IManagedPhysicsCollisionSolver"]);
        Assert.Equal(LayoutHashes(typeof(ManagedBonesTable)), hashes["IManagedBones"]);
        Assert.Equal(LayoutHashes(typeof(ManagedPhysicsConstraintEventTable)), hashes["IManagedPhysicsConstraintEvent"]);
        Assert.Equal(LayoutHashes(typeof(ManagedMotionEventTable)), hashes["IManagedMotionEvent"]);
        // The server's solid key table (stock's g_SolidSetup, 2.6): the
        // contract lives in shared interop and the server builds one.
        Assert.Equal(LayoutHashes(typeof(ManagedVPhysicsKeyHandlerTable)), hashes["IManagedVPhysicsKeyHandler"]);
        // The recipient filter EmitSound calls back (plan_server_native_v2 T4a),
        // libss_fwd_server's object over managed's table, the same shape.
        Assert.Equal(LayoutHashes(typeof(ManagedRecipientFilterTable)), hashes["IManagedRecipientFilter"]);
        // The trace filter TraceRay calls per candidate (plan_server_native_v2 T4f), the same shape.
        Assert.Equal(LayoutHashes(typeof(ManagedTraceFilterTable)), hashes["IManagedTraceFilter"]);
        Assert.DoesNotContain("IPhysicsObjectEvent", hashes.Keys);   // no Valve layout is written by managed any more
        Assert.DoesNotContain("IManagedSoundEmitter", hashes.Keys);  // its three slots had no native caller (2026-10-20)
        Assert.DoesNotContain("IManagedTempEnts", hashes.Keys);      // the native temp-entity forward retired (plan_server_native_v2 T4)
        Assert.Equal(25, hashes.Count);   // 25 with IManagedTempEnts retired (plan_server_native_v2 T4, 2026-10-26); 26 with IManagedSoundEmitter retired (the serialised ABI pass, 2026-10-20); 27 with IManagedTraceFilter (plan_server_native_v2 T4f); 26 with IManagedFrame retired (plan_server_native_v2 T4e); 27 with IManagedRecipientFilter and IManagedSoundEmitter, IManagedSoundController retired (plan_server_native_v2 T4a); 18 since plan_side_isolation P2: IManagedUserMessages; 19 with B2.1; 20 with B2.2; 21 with IManagedConsoleOutput (docs/todo.md's console capture); 22 with IPhysicsCollisionSolver (plan_physics_bones_native_zero 2.1); 23 with IManagedBones (physics_bones_native_zero 1.1); 24 with IPhysicsConstraintEvent (2.2); 25 with IMotionEvent (2.4); 26 with IVPhysicsKeyHandler (2.6)
    }

    private static uint[] LayoutHashes(Type layout)
        => layout.GetFields()
                 .Select(f => ((SlotHashAttribute)f.GetCustomAttributes(typeof(SlotHashAttribute), false).Single()).Hash)
                 .ToArray();

    // ---- L6: the managed-call depth ---------------------------------------

    [Fact]
    public void AThunkRaisesTheDepthForItsCallAndLowersItAfter()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        int inside = -1;
        bindings.Add("SourceSharpProbe001", v => { inside = vtables.Depth; return Stand(v, "SourceSharpProbe001"); });
        var root = new ManagedRoot(bindings, vtables).Bind();
        Assert.Equal(0, vtables.Depth);

        Acquire(root, "SourceSharpProbe001");   // the builder runs inside the root's Acquire thunk

        Assert.Equal(1, inside);
        Assert.Equal(0, vtables.Depth);
    }

    [Fact]
    public void TheDepthIsBackToZeroAfterAThrowingSlot()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        bindings.Add("SourceSharpProbe001", _ => throw new InvalidOperationException("thrown on purpose"));
        var root = new ManagedRoot(bindings, vtables).Bind();

        Assert.Equal(0, Acquire(root, "SourceSharpProbe001"));   // the thunk caught it and answered null

        Assert.Equal(0, vtables.Depth);
    }

    /// <summary>
    /// The depth is the registry's own (statics to zero): a thunk on one
    /// game instance's registry raises that registry's depth and leaves a
    /// second registry in the same process at 0, so the second's reload check
    /// is not refused by the first's call.
    /// </summary>
    [Fact]
    public void AThunkRaisesOnlyItsOwnRegistrysDepth()
    {
        using var mine = new ManagedVtables();
        using var other = new ManagedVtables();
        var bindings = new ManagedBindings();
        int insideMine = -1, insideOther = -1;
        bindings.Add("SourceSharpProbe001", v =>
        {
            insideMine = mine.Depth;
            insideOther = other.Depth;
            return Stand(v, "SourceSharpProbe001");
        });
        var root = new ManagedRoot(bindings, mine).Bind();

        Acquire(root, "SourceSharpProbe001");

        Assert.Equal(1, insideMine);    // the stimulus reached the registry it ran on
        Assert.Equal(0, insideOther);
    }

    [Fact]
    public void AReloadAskedForFromInsideAManagedCallIsRefused()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        string? insideWhy = "not run";
        var next = new Dictionary<string, uint[]>();
        bindings.Add("SourceSharpProbe001", v =>
        {
            insideWhy = vtables.CheckReload(next);
            return Stand(v, "SourceSharpProbe001");
        });
        var root = new ManagedRoot(bindings, vtables).Bind();
        next["IManagedRoot"] = vtables.HashesOf("IManagedRoot")!;
        Assert.Null(vtables.CheckReload(next));   // the control: at depth 0 the same check passes

        Acquire(root, "SourceSharpProbe001");

        Assert.NotNull(insideWhy);
        Assert.Contains("from inside a managed call (depth 1)", insideWhy);
    }
}
