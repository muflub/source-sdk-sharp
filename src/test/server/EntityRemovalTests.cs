using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// DEFERRED REMOVAL — <c>plan_entity.md</c> step 2c and §3.5's "removal:
/// <c>Remove(handle)</c> marks; the directory frees at end of frame, as
/// <c>UTIL_Remove</c> does, so a handle stays valid for the rest of the frame".
///
/// <para>
/// The step's gate is three properties and each has facts of its own here: a
/// handle stays valid until end of frame; a double remove is idempotent; and a
/// clean shutdown destroys every entity (<see cref="EntityDirectory.RemoveAll"/>,
/// whose live half is the gate's own shutdown step).
/// </para>
/// </summary>
public class EntityRemovalTests
{
    /// <summary>
    /// A handle with an explicit serial. <b>Never <c>default</c></b>: entry 0 is
    /// a real, bindable slot and <c>default(EntityHandle)</c> is a handle naming
    /// it with serial 0 — the invalid sentinel is
    /// <see cref="EntityHandle.None"/> (<c>INVALID_EHANDLE_INDEX</c>), which is
    /// a different value entirely. Two facts below turn that on its head
    /// deliberately.
    /// </summary>
    private static EntityHandle H(uint entry, uint serial = 1)
        => new(entry | (serial << 16));

    private sealed class Fixture : ScriptedEntity
    {
        public int RemovedCount { get; private set; }

        /// <summary>What the world looked like from inside this entity's teardown.</summary>
        public Func<int>? Look { get; init; }

        public int SawLive { get; private set; } = -1;

        public override void OnRemoved()
        {
            RemovedCount++;

            if (Look is not null)
                SawLive = Look();
        }
    }

    /// <summary>
    /// A directory wired the way the server wires one: the notify tells the
    /// entity and the destroyer records the batch it was handed.
    /// </summary>
    private sealed class Rig
    {
        public EntityDirectory Directory { get; } = new(new RecordingLog());

        /// <summary>One entry per <c>ShellDestroyer</c> CALL, holding that call's batch.</summary>
        public List<EntityHandle[]> Batches { get; } = new();

        public List<EntityHandle> Detached { get; } = new();
        public List<EntityHandle> Notified { get; } = new();

        public Rig()
        {
            Directory.RemovalNotify = h =>
            {
                Notified.Add(h);
                (Directory.Resolve(h) as Fixture)?.OnRemoved();
            };

            Directory.ShellDestroyer = batch =>
            {
                Batches.Add(batch.ToArray());
            };
        }

        public Fixture Add(EntityHandle handle)
        {
            var e = new Fixture();
            Assert.True(Directory.Bind(handle, e));
            return e;
        }

        public int Destroyed
        {
            get
            {
                int n = 0;
                foreach (var b in Batches)
                    n += b.Length;
                return n;
            }
        }
    }

    //-------------------------------------------------------------------------
    // GATE 1 — a handle stays valid until end of frame
    //-------------------------------------------------------------------------

    [Fact]
    public void AMarkedEntityStillResolves()
    {
        var rig = new Rig();
        var h = H(64);
        var e = rig.Add(h);

        Assert.Equal(RemovalMark.Marked, rig.Directory.Remove(h));
        Assert.Same(e, rig.Directory.Resolve(h));
    }

    [Fact]
    public void AMarkedEntityIsStillCounted()
    {
        var rig = new Rig();
        rig.Add(H(64));

        rig.Directory.Remove(H(64));
        Assert.Equal(1, rig.Directory.Count);
    }

    [Fact]
    public void TheFreePassIsWhatStopsItResolving()
    {
        var rig = new Rig();
        var h = H(64);
        rig.Add(h);

        rig.Directory.Remove(h);
        Assert.Equal(1, rig.Directory.FreeRemoved());

        Assert.Null(rig.Directory.Resolve(h));
        Assert.Equal(0, rig.Directory.Count);
    }

    /// <summary>
    /// The whole point of the deferral, as one fact: an entity that removes
    /// itself from inside its own think is still there for the rest of that
    /// think, and for everything else the frame does after it.
    /// </summary>
    [Fact]
    public void AnEntityThatRemovesItselfSurvivesTheRestOfTheFrame()
    {
        var rig = new Rig();
        var h = H(21);
        rig.Add(h);

        rig.Directory.Remove(h);

        // Every later step of the same frame — the motion pass, another
        // entity's think, the parenting sweep — still sees it.
        Assert.NotNull(rig.Directory.Resolve(h));
        Assert.True(rig.Directory.IsRemoved(h));

        rig.Directory.FreeRemoved();
        Assert.Null(rig.Directory.Resolve(h));
    }

    [Fact]
    public void IsRemovedIsFalseBeforeTheMark()
    {
        var rig = new Rig();
        rig.Add(H(64));

        Assert.False(rig.Directory.IsRemoved(H(64)));
    }

    [Fact]
    public void PendingRemovalCountReportsTheMarksWaiting()
    {
        var rig = new Rig();
        rig.Add(H(1));
        rig.Add(H(2));

        rig.Directory.Remove(H(1));
        rig.Directory.Remove(H(2));

        Assert.Equal(2, rig.Directory.PendingRemovalCount);
        rig.Directory.FreeRemoved();
        Assert.Equal(0, rig.Directory.PendingRemovalCount);
    }

    //-------------------------------------------------------------------------
    // GATE 2 — a double remove is idempotent
    //-------------------------------------------------------------------------

    [Fact]
    public void ASecondRemoveAnswersAlreadyMarked()
    {
        var rig = new Rig();
        rig.Add(H(64));

        Assert.Equal(RemovalMark.Marked, rig.Directory.Remove(H(64)));
        Assert.Equal(RemovalMark.AlreadyMarked, rig.Directory.Remove(H(64)));
    }

    [Fact]
    public void ASecondRemoveQueuesNothing()
    {
        var rig = new Rig();
        rig.Add(H(64));

        rig.Directory.Remove(H(64));
        rig.Directory.Remove(H(64));
        rig.Directory.Remove(H(64));

        Assert.Equal(1, rig.Directory.PendingRemovalCount);
    }

    /// <summary>
    /// And the observable half of it: three removes, one shell freed, and the
    /// entity told once. A second entry in the batch would ask native code to
    /// <c>delete</c> the same pointer twice.
    /// </summary>
    [Fact]
    public void ADoubleRemoveFreesTheShellExactlyOnce()
    {
        var rig = new Rig();
        var e = rig.Add(H(64));

        rig.Directory.Remove(H(64));
        rig.Directory.Remove(H(64));
        rig.Directory.Remove(H(64));

        Assert.Equal(1, rig.Directory.FreeRemoved());
        Assert.Equal(1, rig.Destroyed);
        Assert.Equal(1, e.RemovedCount);
    }

    /// <summary>
    /// A remove AFTER the free pass is not a second removal of the same entity:
    /// the entry is empty, so the directory does not claim it at all.
    /// </summary>
    [Fact]
    public void RemovingSomethingAlreadyFreedIsNotMine()
    {
        var rig = new Rig();
        rig.Add(H(64));

        rig.Directory.Remove(H(64));
        rig.Directory.FreeRemoved();

        Assert.Equal(RemovalMark.NotMine, rig.Directory.Remove(H(64)));
        Assert.Equal(1, rig.Destroyed);
    }

    /// <summary>
    /// <b>THE TRAP THIS ENUM EXISTS FOR.</b> The route reads "claimed", not
    /// "newly marked" — a second remove in the same frame is claimed, because
    /// the entity is already going. Read as "newly marked" the second call would
    /// fall through to native <c>UTIL_Remove</c> and the entity would be removed
    /// twice through two different mechanisms.
    /// </summary>
    /// <remarks>
    /// Each fact binds a route of its own (plan_abi_hotreload D7): the route
    /// is an instance per engine side, so there is no global to save, pin or
    /// put back, and no side check that another class's leftover
    /// <c>Engine.ActiveSide</c> could turn green without testing anything.
    /// </remarks>
    [Fact]
    public void TheRouteClaimsAnAlreadyMarkedHandle()
    {
        var rig = new Rig();
        rig.Add(H(64));

        var route = new EntityRemovalRoutes();
        route.Bind(rig.Directory.Remove, rig.Directory.FreeRemoved);

        Assert.True(route.Remove(H(64)));
        Assert.True(route.Remove(H(64)));
    }

    [Fact]
    public void TheRouteDoesNotClaimAHandleTheDirectoryDoesNotHold()
    {
        var rig = new Rig();
        rig.Add(H(64));

        var route = new EntityRemovalRoutes();
        route.Bind(rig.Directory.Remove, rig.Directory.FreeRemoved);

        Assert.True(route.Remove(H(64)));    // the control: this route does claim
        Assert.False(route.Remove(H(65)));
    }

    [Fact]
    public void AnUnboundRouteClaimsNothing()
    {
        var rig = new Rig();
        rig.Add(H(64));

        var route = new EntityRemovalRoutes();
        route.Bind(rig.Directory.Remove, rig.Directory.FreeRemoved);
        route.Unbind();

        Assert.False(route.Remove(H(64)));
        Assert.Equal(0, route.FreeNow());
        Assert.False(rig.Directory.IsRemoved(H(64)));
    }

    /// <summary>
    /// The other half of the route: stock's four <c>CleanupDeleteList</c> points
    /// drain the managed list too, so WHEN an entity stops existing does not
    /// depend on which hierarchy it was on.
    /// </summary>
    [Fact]
    public void TheFreeRouteDrainsTheDirectory()
    {
        var rig = new Rig();
        rig.Add(H(64));

        var route = new EntityRemovalRoutes();
        route.Bind(rig.Directory.Remove, rig.Directory.FreeRemoved);

        route.Remove(H(64));
        Assert.NotNull(rig.Directory.Resolve(H(64)));

        Assert.Equal(1, route.FreeNow());
        Assert.Null(rig.Directory.Resolve(H(64)));
    }

    /// <summary>
    /// A LISTEN SERVER IS ONE PROCESS. The client must not be able to remove a
    /// server entity through the engine surface, and before step 2c it could
    /// not: the client's engine table has no <c>EntityRemove</c> at all. It was
    /// kept inert by a side check on one static route; since
    /// plan_abi_hotreload D7 each side has its own route, and the client's is
    /// one no registry binds.
    /// </summary>
    [Fact]
    public void TheClientDoesNotReachTheServersDirectory()
    {
        var rig = new Rig();
        rig.Add(H(64));

        var server = new EngineSide(Side.Server);
        var client = new EngineSide(Side.Client);
        server.Removal.Bind(rig.Directory.Remove, rig.Directory.FreeRemoved);

        Assert.False(client.Removal.Remove(H(64)));
        Assert.Equal(0, client.Removal.FreeNow());
        Assert.False(rig.Directory.IsRemoved(H(64)));

        Assert.True(server.Removal.Remove(H(64)));     // the control
    }

    //-------------------------------------------------------------------------
    // The invalid handle, and the entry-0 trap it is confused with
    //-------------------------------------------------------------------------

    [Fact]
    public void TheInvalidHandleIsNotMine()
    {
        var rig = new Rig();
        rig.Add(H(64));

        Assert.Equal(RemovalMark.NotMine, rig.Directory.Remove(EntityHandle.None));
        Assert.Equal(0, rig.Directory.PendingRemovalCount);
    }

    /// <summary>
    /// <c>EntityHandle.None</c> decomposes to entry 8191, which is a perfectly
    /// bindable entry. It must be refused BY HANDLE and never addressed by that
    /// entry — the same rule <see cref="EntityDirectory.EntryIndex"/> states.
    /// </summary>
    [Fact]
    public void TheInvalidHandleDoesNotMarkEntry8191()
    {
        var rig = new Rig();
        var last = H(EntityDirectory.EntryCount - 1, serial: 7);
        rig.Add(last);

        rig.Directory.Remove(EntityHandle.None);

        Assert.False(rig.Directory.IsRemoved(last));
        Assert.Equal(0, rig.Directory.FreeRemoved());
        Assert.NotNull(rig.Directory.Resolve(last));
    }

    /// <summary>
    /// <b>AND THE RAW HANDLE 0 IS NOT THE INVALID HANDLE.</b> It is entry 0
    /// with serial 0 — a real slot — so a directory that has bound it removes
    /// exactly that entity, and one that has not answers
    /// <see cref="RemovalMark.NotMine"/>. "Removed" and "never existed" must not
    /// both read as entry 0.
    /// </summary>
    [Fact]
    public void TheRawZeroHandleNamesEntryZeroAndNotNothing()
    {
        var rig = new Rig();
        var zero = new EntityHandle(0u);
        var e = rig.Add(zero);

        Assert.Equal(RemovalMark.Marked, rig.Directory.Remove(zero));
        Assert.Same(e, rig.Directory.Resolve(zero));

        rig.Directory.FreeRemoved();
        Assert.Equal(1, e.RemovedCount);
    }

    [Fact]
    public void TheRawZeroHandleIsNotMineWhenEntryZeroIsEmpty()
    {
        var rig = new Rig();
        rig.Add(H(1));

        Assert.Equal(RemovalMark.NotMine, rig.Directory.Remove(new EntityHandle(0u)));
    }

    //-------------------------------------------------------------------------
    // The serial, which is what makes a stale handle safe
    //-------------------------------------------------------------------------

    [Fact]
    public void AStaleHandleIsNotMine()
    {
        var rig = new Rig();
        var old = H(64, serial: 3);
        var now = H(64, serial: 4);

        rig.Add(old);
        rig.Directory.Unbind(old);
        rig.Add(now);

        Assert.Equal(RemovalMark.NotMine, rig.Directory.Remove(old));
        Assert.NotNull(rig.Directory.Resolve(now));
    }

    /// <summary>
    /// The mark is a HANDLE table and not a <c>bool[]</c>, and this is the bug a
    /// bit-per-entry would have: mark entry 64, let the entry be rebound in the
    /// same frame, and the free pass must not take the new occupant.
    /// </summary>
    [Fact]
    public void AnEntryRebndAfterItsMarkIsNotFreedByThatMark()
    {
        var rig = new Rig();
        var old = H(64, serial: 3);
        var now = H(64, serial: 4);

        rig.Add(old);
        rig.Directory.Remove(old);

        // The engine bumps the serial and hands the entry to something else.
        rig.Directory.Unbind(old);
        var live = rig.Add(now);

        Assert.False(rig.Directory.IsRemoved(now));

        rig.Directory.FreeRemoved();

        Assert.Same(live, rig.Directory.Resolve(now));
        Assert.Equal(0, live.RemovedCount);
    }

    [Fact]
    public void UnbindingDropsTheMarkWithTheBinding()
    {
        var rig = new Rig();
        var h = H(64);

        rig.Add(h);
        rig.Directory.Remove(h);
        rig.Directory.Unbind(h);

        Assert.False(rig.Directory.IsRemoved(h));
    }

    [Fact]
    public void ClearDropsEveryMarkAndTheQueue()
    {
        var rig = new Rig();
        rig.Add(H(1));
        rig.Add(H(2));
        rig.Directory.Remove(H(1));
        rig.Directory.Remove(H(2));

        rig.Directory.Clear();

        Assert.Equal(0, rig.Directory.PendingRemovalCount);
        Assert.False(rig.Directory.IsRemoved(H(1)));
        Assert.Equal(0, rig.Directory.FreeRemoved());
        Assert.Empty(rig.Batches);
    }

    //-------------------------------------------------------------------------
    // ONE CROSSING PER BATCH — §3.4
    //-------------------------------------------------------------------------

    /// <summary>
    /// Forty removals in a frame is ONE call, not forty. This is §3.4's
    /// "per-frame work is never one crossing per entity" applied to the one pass
    /// this step owns.
    /// </summary>
    [Fact]
    public void AWholeFramesRemovalsAreOneCall()
    {
        var rig = new Rig();

        for (uint i = 1; i <= 40; i++)
            rig.Add(H(i));

        for (uint i = 1; i <= 40; i++)
            rig.Directory.Remove(H(i));

        Assert.Equal(40, rig.Directory.FreeRemoved());
        Assert.Single(rig.Batches);
        Assert.Equal(40, rig.Batches[0].Length);
    }

    [Fact]
    public void TheBatchIsInMarkOrder()
    {
        var rig = new Rig();
        rig.Add(H(9));
        rig.Add(H(3));
        rig.Add(H(7));

        rig.Directory.Remove(H(7));
        rig.Directory.Remove(H(9));
        rig.Directory.Remove(H(3));

        rig.Directory.FreeRemoved();

        Assert.Equal(new[] { H(7), H(9), H(3) }, rig.Batches[0]);
    }

    /// <summary>A frame that removed nothing does not cross at all.</summary>
    [Fact]
    public void AFrameThatRemovedNothingDoesNotCross()
    {
        var rig = new Rig();
        rig.Add(H(64));

        Assert.Equal(0, rig.Directory.FreeRemoved());
        Assert.Empty(rig.Batches);
    }

    //-------------------------------------------------------------------------
    // The order inside the pass
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every entity in a batch still resolves while any of them is being told,
    /// so what a teardown can see does not depend on where it sat in the batch.
    /// </summary>
    [Fact]
    public void EveryEntityInABatchStillResolvesDuringEveryNotify()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var seen = new List<int>();

        var a = new Fixture { Look = () => Live(directory) };
        var b = new Fixture { Look = () => Live(directory) };
        var c = new Fixture { Look = () => Live(directory) };

        directory.Bind(H(1), a);
        directory.Bind(H(2), b);
        directory.Bind(H(3), c);

        directory.RemovalNotify = h => (directory.Resolve(h) as Fixture)?.OnRemoved();
        directory.ShellDestroyer = _ => { };

        directory.Remove(H(1));
        directory.Remove(H(2));
        directory.Remove(H(3));
        directory.FreeRemoved();

        seen.Add(a.SawLive);
        seen.Add(b.SawLive);
        seen.Add(c.SawLive);

        Assert.Equal(new[] { 3, 3, 3 }, seen);

        static int Live(EntityDirectory d) => d.Count;
    }

    /// <summary>The links go before the entity is told, which is the order the
    /// native destructor kept.</summary>
    [Fact]
    public void TheLinksAreDetachedBeforeTheEntityIsTold()
    {
        var rig = new Rig();
        var order = new List<string>();

        rig.Directory.RemovalDetach = _ => order.Add("detach");
        rig.Directory.RemovalNotify = _ => order.Add("notify");

        rig.Add(H(64));
        rig.Directory.Remove(H(64));
        rig.Directory.FreeRemoved();

        Assert.Equal(new[] { "detach", "notify" }, order);
    }

    /// <summary>The side forgets the entity (its managed skeleton) after the
    /// entity's last look and before its shell is freed.</summary>
    [Fact]
    public void TheSideForgetsTheEntityBetweenItsLastLookAndItsShell()
    {
        var rig = new Rig();
        var order = new List<string>();

        rig.Directory.RemovalNotify = _ => order.Add("notify");
        rig.Directory.RemovalForget = h => order.Add(h == H(64) ? "forget" : "forget-wrong");
        rig.Directory.ShellDestroyer = _ => order.Add("destroy");

        rig.Add(H(64));
        rig.Directory.Remove(H(64));
        rig.Directory.FreeRemoved();

        Assert.Equal(new[] { "notify", "forget", "destroy" }, order);
    }

    [Fact]
    public void TheShellIsFreedAfterTheEntityIsTold()
    {
        var rig = new Rig();
        var order = new List<string>();

        rig.Directory.RemovalNotify = _ => order.Add("notify");
        rig.Directory.ShellDestroyer = _ => order.Add("destroy");

        rig.Add(H(64));
        rig.Directory.Remove(H(64));
        rig.Directory.FreeRemoved();

        Assert.Equal(new[] { "notify", "destroy" }, order);
    }

    /// <summary>The binding is gone by the time the shell is freed, so nothing
    /// can resolve a handle whose native object no longer exists.</summary>
    [Fact]
    public void TheBindingIsGoneBeforeTheShellIs()
    {
        var rig = new Rig();
        bool resolvedAtDestroy = true;

        rig.Directory.ShellDestroyer = _ => resolvedAtDestroy = rig.Directory.Resolve(H(64)) is not null;

        rig.Add(H(64));
        rig.Directory.Remove(H(64));
        rig.Directory.FreeRemoved();

        Assert.False(resolvedAtDestroy);
    }

    //-------------------------------------------------------------------------
    // The cascade — a removal that removes something else
    //-------------------------------------------------------------------------

    /// <summary>
    /// A parent takes its children, and they go in the SAME frame. Step 2h's
    /// gate ("removal of a parent detaches children in one frame") depends on
    /// the free pass being a loop rather than a single walk.
    /// </summary>
    [Fact]
    public void AChildMarkedByItsParentsDetachGoesInTheSameFrame()
    {
        var rig = new Rig();
        var parent = H(10);
        var child = H(11);

        rig.Add(parent);
        var c = rig.Add(child);

        rig.Directory.RemovalDetach = h =>
        {
            if (h == parent)
                rig.Directory.Remove(child);
        };

        rig.Directory.Remove(parent);

        Assert.Equal(2, rig.Directory.FreeRemoved());
        Assert.Null(rig.Directory.Resolve(child));
        Assert.Equal(1, c.RemovedCount);
    }

    /// <summary>And it is a second BATCH, so the child is freed after the
    /// parent rather than beside it.</summary>
    [Fact]
    public void ACascadeIsASecondBatchAndNotALongerFirstOne()
    {
        var rig = new Rig();
        var parent = H(10);
        var child = H(11);

        rig.Add(parent);
        rig.Add(child);

        rig.Directory.RemovalDetach = h =>
        {
            if (h == parent)
                rig.Directory.Remove(child);
        };

        rig.Directory.Remove(parent);
        rig.Directory.FreeRemoved();

        Assert.Equal(2, rig.Batches.Count);
        Assert.Equal(new[] { parent }, rig.Batches[0]);
        Assert.Equal(new[] { child }, rig.Batches[1]);
    }

    /// <summary>
    /// A child ALREADY in the batch is not queued a second time by its parent's
    /// detach — the mark is what makes the cascade idempotent as well.
    /// </summary>
    [Fact]
    public void ACascadeDoesNotRequeueSomethingAlreadyInTheBatch()
    {
        var rig = new Rig();
        var parent = H(10);
        var child = H(11);

        rig.Add(parent);
        rig.Add(child);

        rig.Directory.RemovalDetach = h =>
        {
            if (h == parent)
                rig.Directory.Remove(child);
        };

        rig.Directory.Remove(parent);
        rig.Directory.Remove(child);

        Assert.Equal(2, rig.Directory.FreeRemoved());
        Assert.Single(rig.Batches);
        Assert.Equal(2, rig.Batches[0].Length);
    }

    //-------------------------------------------------------------------------
    // GATE 3 — the clean shutdown
    //-------------------------------------------------------------------------

    [Fact]
    public void RemoveAllMarksEveryBoundEntity()
    {
        var rig = new Rig();

        for (uint i = 1; i <= 12; i++)
            rig.Add(H(i));

        Assert.Equal(12, rig.Directory.RemoveAll());
        Assert.Equal(12, rig.Directory.PendingRemovalCount);
    }

    /// <summary>
    /// THE STEP'S LIVE GATE, as a unit fact: a level shutdown leaves nothing
    /// behind. A world that outlived its level is the bug that lived on the
    /// integration branch for weeks because it is invisible on the first map.
    /// </summary>
    [Fact]
    public void AShutdownDestroysEveryEntity()
    {
        var rig = new Rig();
        var all = new List<Fixture>();

        for (uint i = 1; i <= 12; i++)
            all.Add(rig.Add(H(i)));

        rig.Directory.RemoveAll();

        Assert.Equal(12, rig.Directory.FreeRemoved());
        Assert.Equal(0, rig.Directory.Count);
        Assert.Equal(12, rig.Destroyed);

        foreach (var e in all)
            Assert.Equal(1, e.RemovedCount);
    }

    /// <summary>
    /// And every one of them was TOLD. This is what the destructor's
    /// <c>OnManagedRemoved</c> did before step 2c deleted it, and it is why the
    /// teardown goes through the directory rather than leaving the native walk
    /// to delete the shells behind managed code's back.
    /// </summary>
    [Fact]
    public void AShutdownTellsEveryEntityItIsGoing()
    {
        var rig = new Rig();

        for (uint i = 1; i <= 12; i++)
            rig.Add(H(i));

        rig.Directory.RemoveAll();
        rig.Directory.FreeRemoved();

        Assert.Equal(12, rig.Notified.Count);
    }

    [Fact]
    public void RemoveAllOnAnEmptyDirectoryMarksNothing()
    {
        var rig = new Rig();

        Assert.Equal(0, rig.Directory.RemoveAll());
        Assert.Equal(0, rig.Directory.FreeRemoved());
        Assert.Empty(rig.Batches);
    }

    [Fact]
    public void RemoveAllTwiceMarksNothingTheSecondTime()
    {
        var rig = new Rig();

        for (uint i = 1; i <= 4; i++)
            rig.Add(H(i));

        Assert.Equal(4, rig.Directory.RemoveAll());
        Assert.Equal(0, rig.Directory.RemoveAll());
    }

    //-------------------------------------------------------------------------
    // The rest of the directory keeps its promises
    //-------------------------------------------------------------------------

    [Fact]
    public void FreeingAnEntityCancelsItsThinks()
    {
        var rig = new Rig();
        var h = H(64);

        rig.Add(h);
        rig.Directory.Think.Schedule(h, null, 1.0f);
        Assert.Equal(1, rig.Directory.Think.Pending);

        rig.Directory.Remove(h);
        rig.Directory.FreeRemoved();

        Assert.Equal(0, rig.Directory.Think.Pending);
    }

    [Fact]
    public void TheCountersReportWhatTheFreePassDid()
    {
        var rig = new Rig();
        rig.Add(H(1));
        rig.Add(H(2));

        rig.Directory.Remove(H(1));
        rig.Directory.Remove(H(2));
        rig.Directory.FreeRemoved();

        Assert.Equal(2, rig.Directory.RemovedLastFrame);
        Assert.Equal(2L, rig.Directory.RemovedTotal);

        rig.Add(H(3));
        rig.Directory.Remove(H(3));
        rig.Directory.FreeRemoved();

        Assert.Equal(1, rig.Directory.RemovedLastFrame);
        Assert.Equal(3L, rig.Directory.RemovedTotal);
    }

    [Fact]
    public void AFrameThatFreedNothingReportsZeroRatherThanLastFramesCount()
    {
        var rig = new Rig();
        rig.Add(H(1));
        rig.Directory.Remove(H(1));
        rig.Directory.FreeRemoved();

        Assert.Equal(1, rig.Directory.RemovedLastFrame);

        rig.Directory.FreeRemoved();
        Assert.Equal(0, rig.Directory.RemovedLastFrame);
    }

    /// <summary>
    /// A cascade that will not settle is bounded and PRINTED, never a frame that
    /// never returns. The guard is for the case where "every pass unbinds what
    /// it freed" turns out to be wrong.
    /// </summary>
    [Fact]
    public void ARunawayCascadeIsBoundedRatherThanSpinningTheFrame()
    {
        var rig = new Rig();

        // Every entity, on being detached, marks the next one — a chain longer
        // than the pass limit.
        for (uint i = 1; i <= EntityDirectory.MaxRemovalPasses + 10; i++)
            rig.Add(H(i));

        rig.Directory.RemovalDetach = h =>
        {
            uint next = (h.Value & 0xFFFF) + 1;
            rig.Directory.Remove(H(next));
        };

        rig.Directory.Remove(H(1));

        int freed = rig.Directory.FreeRemoved();

        Assert.Equal(EntityDirectory.MaxRemovalPasses, freed);
        Assert.Equal(0, rig.Directory.PendingRemovalCount);
    }
}
