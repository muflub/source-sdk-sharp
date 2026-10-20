using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using SourceSharp.Abi;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A volume that remembers every edge it was told about, in order.
///
/// <para>
/// Declared here rather than reusing <c>trigger_multiple</c> so that a failure
/// points at the touch diff and not at a change in what a trigger does.
/// </para>
/// </summary>
[ScriptedEntity("test_touch_volume", ScriptedEntityKind.Brush)]
public sealed class TestTouchVolume : ScriptedEntity
{
    /// <summary>Every edge, spelled "start:HANDLE", "touch:HANDLE", "end:HANDLE".</summary>
    public readonly List<string> Edges = new();

    /// <summary>Set by a test that wants the every-tick level as well as the edges.</summary>
    public bool EveryTick;

    /// <summary>An entity this volume removes the moment it is touched by it.</summary>
    public EntityHandle RemoveOnTouch = EntityHandle.None;

    /// <summary>Where a removal is performed, when <see cref="RemoveOnTouch"/> is set.</summary>
    public EntityDirectory? Directory;

    public override bool TouchedByAnything => true;

    public override bool TouchEveryTick => EveryTick;

    public override void OnTouch(EntityHandle toucher)
    {
        // START and TOUCH share OnTouch, which is the managed contract's own
        // shape (EntityOp's remarks: three ops, one handler). The two are told
        // apart by the routing the test installs, not here.
        Edges.Add($"touch:{toucher.Value:X}");

        if (RemoveOnTouch != EntityHandle.None && Directory is not null)
            Directory.Unbind(RemoveOnTouch);
    }

    public override void OnEndTouch(EntityHandle toucher) => Edges.Add($"end:{toucher.Value:X}");
}

/// <summary>An item, so the kind's own every-tick default has a subject.</summary>
[ScriptedEntity("test_touch_item", ScriptedEntityKind.Item)]
public sealed class TestTouchItem : ScriptedEntity
{
    public override bool TouchedByAnything => true;
}

/// <summary>A prop, which is the other side of that default.</summary>
[ScriptedEntity("test_touch_prop", ScriptedEntityKind.Prop)]
public sealed class TestTouchProp : ScriptedEntity
{
    public override bool TouchedByAnything => true;
}

/// <summary>
/// THE TOUCH HALF — <c>plan_entity.md</c> step 2b. Native reports overlapping
/// pairs once per frame from a partition walk over solid-trigger shells;
/// managed diffs that against the link table and raises start, stay and end.
///
/// <para>
/// <b>EVERY FACT HERE IS ABOUT A TRANSITION, not about an end state.</b> The
/// diff IS the step: "these two are touching" is what the report says and what
/// the table already held, and the only thing this code produces is the
/// difference between the two. A test that asserted "A and B are linked" after
/// one report would pass against an implementation that raised no edge at all.
/// So each fact drives at least two reports and reads what was RAISED between
/// them.
/// </para>
///
/// <para>
/// <b>The directory is driven directly and the engine is not asked.</b>
/// <see cref="EntityDirectory.TouchFrame"/> takes a span, so a test writes the
/// report itself. A fake that answered pairs would be a second implementation
/// of the thing under test; <c>RecordingEngine.OverlapReport</c> answers none
/// and says so, and <see cref="OverlapReader"/> — the one thing that does talk
/// to the engine — is tested separately below, on what it does with an answer
/// that will not fit.
/// </para>
/// </summary>
public class TouchLinkTests
{
    private static readonly EntityHandle Volume = new(0x0001_0001u);
    private static readonly EntityHandle Walker = new(0x0002_0002u);
    private static readonly EntityHandle Second = new(0x0003_0003u);

    /// <summary>What the routing recorded, so a fact can read the edges as raised.</summary>
    private sealed class Raised
    {
        public readonly List<string> Log = new();

        public EntityDirectory.TouchDelivery Routing(ScriptedEntityRegistry registry)
            => (receiver, toucher, edge) =>
            {
                Log.Add($"{edge}:{receiver.Value:X}<-{toucher.Value:X}");

                if (edge == EntityDirectory.TouchEdge.End)
                    registry.EndTouch(receiver, toucher);
                else
                    registry.Touch(receiver, toucher);
            };
    }

    private static ScriptedEntityRegistry Fresh(out Raised raised)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TouchLinkTests).Assembly);
        registry.Services = Services();

        raised = new Raised();
        registry.Directory.TouchRouting = raised.Routing(registry);

        return registry;
    }

    private static T Spawn<T>(ScriptedEntityRegistry registry, EntityHandle handle,
                              string className) where T : ScriptedEntity
    {
        registry.Spawn(handle, className);
        return (T)registry.Get(handle)!;
    }

    private static OverlapPair[] Report(params (EntityHandle A, EntityHandle B)[] pairs)
    {
        var report = new OverlapPair[pairs.Length];

        for (int i = 0; i < pairs.Length; i++)
            report[i] = new OverlapPair(pairs[i].A, pairs[i].B);

        return report;
    }

    //=========================================================================
    // ENTER, STAY AND LEAVE FROM TWO REPORTS — the step's own gate.
    //=========================================================================

    [Fact]
    public void APairInTheFirstReportEnters()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));

        Assert.Equal(new[] { $"Start:{Volume.Value:X}<-{Walker.Value:X}",
                             $"Start:{Walker.Value:X}<-{Volume.Value:X}" },
                     raised.Log);
    }

    [Fact]
    public void APairInBothReportsEntersOnceAndDoesNotEnterAgain()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        registry.Directory.TouchFrame(Report((Volume, Walker)));

        // The SECOND report of a pair that was already there raises nothing:
        // the volume does not ask for the level, so a continuing overlap is
        // silent. This is the fact a naive "raise on every report" passes the
        // enter test and fails.
        Assert.Empty(raised.Log);
        Assert.True(registry.Directory.IsTouching(Volume, Walker));
    }

    [Fact]
    public void APairMissingFromTheSecondReportLeaves()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        registry.Directory.TouchFrame(Report());

        Assert.Equal(new[] { $"End:{Volume.Value:X}<-{Walker.Value:X}",
                             $"End:{Walker.Value:X}<-{Volume.Value:X}" },
                     raised.Log);
        Assert.False(registry.Directory.IsTouching(Volume, Walker));
    }

    [Fact]
    public void AStayIsRaisedForAClassThatAsksForEveryTick()
    {
        var registry = Fresh(out var raised);
        var volume = Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");
        volume.EveryTick = true;

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        registry.Directory.TouchFrame(Report((Volume, Walker)));

        Assert.Equal(new[] { $"Stay:{Volume.Value:X}<-{Walker.Value:X}" }, raised.Log);
    }

    [Fact]
    public void TheThreeEdgesOfOneVisitComeInOrderFromThreeReports()
    {
        var registry = Fresh(out var raised);
        var volume = Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");
        volume.EveryTick = true;

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        registry.Directory.TouchFrame(Report((Volume, Walker)));
        registry.Directory.TouchFrame(Report());

        // What the ENTITY saw, which is the contract a class is written
        // against: arrival, arrival's own level, the next frame's level, and
        // the departure.
        Assert.Equal(new[] { $"touch:{Walker.Value:X}",
                             $"touch:{Walker.Value:X}",
                             $"touch:{Walker.Value:X}",
                             $"end:{Walker.Value:X}" },
                     volume.Edges);
    }

    [Fact]
    public void AnEnterRaisesTheStayOnTheSameFrameForAnEveryTickClass()
    {
        var registry = Fresh(out var raised);
        var volume = Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");
        volume.EveryTick = true;

        registry.Directory.TouchFrame(Report((Volume, Walker)));

        // The native shell did exactly this: OnManagedStartTouch, then
        // OnManagedTouch if the class asked for every tick. An item that heard
        // only the arrival cannot be collected by somebody standing on it when
        // it respawned.
        Assert.Equal(new[] { $"Start:{Volume.Value:X}<-{Walker.Value:X}",
                             $"Start:{Walker.Value:X}<-{Volume.Value:X}",
                             $"Stay:{Volume.Value:X}<-{Walker.Value:X}" },
                     raised.Log);
    }

    [Fact]
    public void AReportNamingOnePairTwiceEntersOnce()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        // The same two reach the report by more than one route -- a trigger
        // that moved and a solid that moved into it are both asked.
        registry.Directory.TouchFrame(Report((Volume, Walker), (Walker, Volume)));

        Assert.Equal(2, raised.Log.Count);
        Assert.Equal(1, registry.Directory.TouchLinkCount);
    }

    [Fact]
    public void APairNamedTwiceInOneReportRaisesOneStayAndNotTwo()
    {
        var registry = Fresh(out var raised);
        var item = Spawn<TestTouchItem>(registry, Volume, "test_touch_item");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        // The duplicate is absorbed by the frame stamp, and an EVERY-TICK class
        // is what makes that visible: for a class that hears only the edges a
        // second mention is silent whether or not anything absorbed it, so the
        // brush version of this fact cannot fail.
        registry.Directory.TouchFrame(Report((Volume, Walker), (Walker, Volume)));

        Assert.Equal(new[] { $"Stay:{Volume.Value:X}<-{Walker.Value:X}" }, raised.Log);
        Assert.NotNull(item);
    }

    [Fact]
    public void APairIsTheSameLinkWhicheverWayRoundTheReportNamesIt()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        // Reversed. A table keyed by the ordered pair would call this a new
        // arrival AND sweep the old one away as a departure.
        registry.Directory.TouchFrame(Report((Walker, Volume)));

        Assert.Empty(raised.Log);
        Assert.Equal(1, registry.Directory.TouchLinkCount);
    }

    [Fact]
    public void OneLeavingPairDoesNotDisturbAnotherThatStayed()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker), (Volume, Second)));
        raised.Log.Clear();

        registry.Directory.TouchFrame(Report((Volume, Second)));

        Assert.Equal(new[] { $"End:{Volume.Value:X}<-{Walker.Value:X}",
                             $"End:{Walker.Value:X}<-{Volume.Value:X}" },
                     raised.Log);
        Assert.True(registry.Directory.IsTouching(Volume, Second));
    }

    [Fact]
    public void AnEntityThatLeavesAndComesBackEntersAgain()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        registry.Directory.TouchFrame(Report());
        raised.Log.Clear();

        registry.Directory.TouchFrame(Report((Volume, Walker)));

        Assert.Equal(new[] { $"Start:{Volume.Value:X}<-{Walker.Value:X}",
                             $"Start:{Walker.Value:X}<-{Volume.Value:X}" },
                     raised.Log);
    }

    [Fact]
    public void BothEndsOfAPairAreTold()
    {
        var registry = Fresh(out var raised);
        var volume = Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");
        var other = Spawn<TestTouchVolume>(registry, Walker, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));

        // A touch is symmetric and stock has always treated it so. Half of that
        // went missing the moment one side stopped being a CBaseEntity.
        Assert.Equal(new[] { $"touch:{Walker.Value:X}" }, volume.Edges);
        Assert.Equal(new[] { $"touch:{Volume.Value:X}" }, other.Edges);
    }

    //=========================================================================
    // bTouchEveryTick AS A MANAGED PER-CLASS CHOICE.
    //=========================================================================

    [Fact]
    public void AnItemHearsEveryTickByDefault()
    {
        var registry = Fresh(out _);
        var item = Spawn<TestTouchItem>(registry, Volume, "test_touch_item");

        // scripted_kinds.h set bTouchEveryTick TRUE for the item kind and false
        // for every other, in C++, for every entity in the mod. The default
        // reproduces that table so that moving the decision moved nothing.
        Assert.Equal(ScriptedEntityKind.Item, item.RegisteredKind);
        Assert.True(item.TouchEveryTick);
    }

    [Fact]
    public void APropDoesNot()
    {
        var registry = Fresh(out _);
        var prop = Spawn<TestTouchProp>(registry, Volume, "test_touch_prop");

        Assert.Equal(ScriptedEntityKind.Prop, prop.RegisteredKind);
        Assert.False(prop.TouchEveryTick);
    }

    [Fact]
    public void AnItemIsToldOnEveryFrameOfAnOverlapAndAPropIsNot()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchItem>(registry, Volume, "test_touch_item");
        Spawn<TestTouchProp>(registry, Second, "test_touch_prop");

        registry.Directory.TouchFrame(Report((Volume, Walker), (Second, Walker)));
        raised.Log.Clear();

        registry.Directory.TouchFrame(Report((Volume, Walker), (Second, Walker)));

        // The item's level, and the prop's silence, from ONE report — the two
        // classes differ in nothing but the kind they were registered as.
        Assert.Equal(new[] { $"Stay:{Volume.Value:X}<-{Walker.Value:X}" }, raised.Log);
    }

    [Fact]
    public void AClassOverridesTheKindsAnswer()
    {
        var registry = Fresh(out var raised);
        var volume = Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        Assert.Equal(ScriptedEntityKind.Brush, volume.RegisteredKind);
        Assert.False(volume.TouchEveryTick);

        volume.EveryTick = true;
        Assert.True(volume.TouchEveryTick);

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();
        registry.Directory.TouchFrame(Report((Volume, Walker)));

        Assert.Equal(new[] { $"Stay:{Volume.Value:X}<-{Walker.Value:X}" }, raised.Log);
    }

    [Fact]
    public void AHandleWithNoManagedInstanceGetsTheEdgesButNoLevel()
    {
        var registry = Fresh(out var raised);
        var item = Spawn<TestTouchItem>(registry, Volume, "test_touch_item");

        // Walker is bound to nothing: a native entity, an SDK player, a shell
        // whose class is not registered. Both edges name it, because those are
        // facts about the PAIR; the level is a question about a managed class
        // and there is none here to answer it.
        registry.Directory.TouchFrame(Report((Volume, Walker)));
        registry.Directory.TouchFrame(Report((Volume, Walker)));
        registry.Directory.TouchFrame(Report());

        Assert.Equal(new[]
        {
            $"Start:{Volume.Value:X}<-{Walker.Value:X}",
            $"Start:{Walker.Value:X}<-{Volume.Value:X}",
            $"Stay:{Volume.Value:X}<-{Walker.Value:X}",
            $"Stay:{Volume.Value:X}<-{Walker.Value:X}",
            $"End:{Volume.Value:X}<-{Walker.Value:X}",
            $"End:{Walker.Value:X}<-{Volume.Value:X}",
        }, raised.Log);

        Assert.NotNull(item);
    }

    //=========================================================================
    // WHAT THE DIFF REFUSES.
    //=========================================================================

    [Fact]
    public void TheInvalidHandleIsNotRecordedAsALink()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, EntityHandle.None)));

        // EntryIndex answers 8191 for the invalid handle — a perfectly bindable
        // entry — so a link keyed by it would be swept and re-entered forever.
        Assert.Empty(raised.Log);
        Assert.Equal(0, registry.Directory.TouchLinkCount);
    }

    [Fact]
    public void AnEntityDoesNotTouchItself()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Volume)));

        Assert.Empty(raised.Log);
        Assert.Equal(0, registry.Directory.TouchLinkCount);
    }

    [Fact]
    public void AStaleSerialIsADifferentPairRatherThanTheSameOne()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        var reborn = new EntityHandle(Walker.Value + 0x0001_0000u);

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        // Same ENTRY, new serial: the entry was freed and re-occupied. The link
        // is keyed by the whole handle, so this is an arrival and a departure
        // and not a continuation.
        registry.Directory.TouchFrame(Report((Volume, reborn)));

        Assert.Equal(new[]
        {
            $"Start:{Volume.Value:X}<-{reborn.Value:X}",
            $"Start:{reborn.Value:X}<-{Volume.Value:X}",
            $"End:{Volume.Value:X}<-{Walker.Value:X}",
            $"End:{Walker.Value:X}<-{Volume.Value:X}",
        }, raised.Log);
    }

    //=========================================================================
    // AN ENTITY THAT GOES WHILE IT IS STANDING IN SOMETHING.
    //=========================================================================

    [Fact]
    public void UnbindingAnEntityTellsWhateverItWasTouchingThatItHasLeft()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        // Walker is SPAWNED, and that is the point rather than setup: Unbind
        // refuses a handle its entry does not hold, so an entity that was never
        // bound is not an entity going away. This fact is about one that is.
        Spawn<TestTouchProp>(registry, Walker, "test_touch_prop");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        registry.Directory.Unbind(Walker);

        // PhysicsLink_RemoveTouchedList, reached from
        // CBaseEntity::UpdateOnRemove, walks the dying entity's links and
        // notifies the other end of each: "Every start Touch gets a
        // corresponding end touch". A trigger counting what is inside it leaks
        // one per removal otherwise, and the count is what fires OnEndTouchAll.
        Assert.Equal(new[] { $"End:{Volume.Value:X}<-{Walker.Value:X}" }, raised.Log);
    }

    [Fact]
    public void UnbindingAnEntityDropsEveryLinkItWasIn()
    {
        var registry = Fresh(out _);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");
        Spawn<TestTouchProp>(registry, Walker, "test_touch_prop");

        registry.Directory.TouchFrame(Report((Volume, Walker), (Second, Walker)));
        Assert.Equal(2, registry.Directory.TouchLinkCount);

        registry.Directory.Unbind(Walker);

        Assert.Equal(0, registry.Directory.TouchLinkCount);
    }

    //=========================================================================
    // THE ONE REMOVAL NATIVE STARTS -- a client leaving.
    //
    // THE TWO FACTS ABOVE DRIVE `Directory.Unbind` DIRECTLY, and that is one
    // link short of the obligation. `CServerGameClients::ClientDisconnect` is
    // the only removal in this build that managed code does not begin: it calls
    // `SourceSharp_AnnounceStandaloneRemoved`, which is
    // `DispatchOp( ENTITY_OP_REMOVED )`, which `ServerModule` routes to
    // `_entities.Removed(it)` -- and THAT is what calls `Unbind`, and only
    // through it `ForgetTouches`. Every other removal arrives by the
    // directory's own free pass instead.
    //
    // WHY THE LINK IS LOAD-BEARING NOW AND WAS NOT BEFORE. Stock discharged it
    // natively, in that same function:
    //
    //     // Make sure all Untouch()'s are called for this client leaving
    //     PhysicsLink_RemoveTouchedList( player );
    //     PhysicsLink_RemoveGroundList( player );
    //
    // Those two calls are in `shell_gameclients.cpp`'s SDK arm, which
    // `UTIL_SDKPlayerFromEdict` never claims an edict for in this build, and
    // both are declared `(void)ent;` refusals in `shell_physicsobjects.cpp`
    // besides -- the native touch-link pool lives in
    // `game/shared/physics_main_shared.cpp`, which has no row in either vpc and
    // produces no object on either link line. So the managed chain is not a
    // second answer, it is the only one.
    //
    // A TEST OF `Unbind` ALONE WOULD STAY GREEN IF THAT CHAIN BROKE, which is
    // the shape of the defect this fact exists for: the announcement was itself
    // added after two live runs showed a departed player's instance surviving
    // into a hot reload, and nothing then pinned what it reaches.
    //=========================================================================

    [Fact]
    public void TheOpADepartingClientSendsDropsItsTouchLinks()
    {
        var registry = Fresh(out _);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");
        Spawn<TestTouchProp>(registry, Walker, "test_touch_prop");

        registry.Directory.TouchFrame(Report((Volume, Walker), (Second, Walker)));
        Assert.Equal(2, registry.Directory.TouchLinkCount);

        // `registry.Removed(handle)` is the managed entry point the inbound
        // ENTITY_OP_REMOVED thunk lands on, called with the leaver's handle
        // exactly as ServerModule's `case EntityOp.Removed` calls it.
        Assert.True(registry.Removed(Walker), "the op reached a managed instance");

        Assert.Equal(0, registry.Directory.TouchLinkCount);
    }

    [Fact]
    public void TheOpADepartingClientSendsRaisesTheLeavingEdgeToTheSurvivor()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");
        Spawn<TestTouchProp>(registry, Walker, "test_touch_prop");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        registry.Removed(Walker);

        // Stock's rule, which the deleted native call carried: "Every start
        // Touch gets a corresponding end touch". A trigger counting what is
        // inside it leaks one per departing client otherwise, and that count is
        // what fires OnEndTouchAll.
        Assert.Equal(new[] { $"End:{Volume.Value:X}<-{Walker.Value:X}" }, raised.Log);
    }

    [Fact]
    public void UnbindingAHandleTheDirectoryDoesNotHoldChangesNothing()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        // Walker was never bound — a native entity, an SDK player, something on
        // the other hierarchy. Unbind refuses a handle its entry does not hold,
        // and it must not fabricate a departure for one: the link stands until
        // the report stops mentioning it, which is the only thing that knows.
        Assert.False(registry.Directory.Unbind(Walker));
        Assert.Empty(raised.Log);
        Assert.True(registry.Directory.IsTouching(Volume, Walker));
    }

    [Fact]
    public void ADroppedLinkIsNotSweptASecondTimeOnTheNextFrame()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");
        Spawn<TestTouchProp>(registry, Walker, "test_touch_prop");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        registry.Directory.Unbind(Walker);
        raised.Log.Clear();

        registry.Directory.TouchFrame(Report());

        Assert.Empty(raised.Log);
    }

    [Fact]
    public void RemovingAnEntityFromInsideATouchDoesNotBreakTheSweep()
    {
        var registry = Fresh(out var raised);
        var volume = Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker), (Volume, Second)));

        // The volume removes the OTHER toucher the moment it is told about a
        // departure. Both links expire on the same frame, so the sweep is
        // walking its own list when the removal reaches back into the table.
        volume.Directory = registry.Directory;
        volume.RemoveOnTouch = Second;
        raised.Log.Clear();

        registry.Directory.TouchFrame(Report());

        Assert.Equal(0, registry.Directory.TouchLinkCount);
    }

    [Fact]
    public void ClearingTheDirectoryDropsTheLinksWithoutReportingDepartures()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        raised.Log.Clear();

        registry.Directory.Clear();

        // A level change is not something anything is told it has left.
        Assert.Empty(raised.Log);
        Assert.Equal(0, registry.Directory.TouchLinkCount);
    }

    [Fact]
    public void ALinkDoesNotSurviveALevelChange()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        registry.Directory.Clear();
        raised.Log.Clear();

        // The next map's first frame. If the table had survived, this would
        // sweep a link naming two entities that no longer exist.
        registry.Directory.TouchFrame(Report());

        Assert.Empty(raised.Log);
    }

    //=========================================================================
    // COUNTERS, so a live claim about this step is a comparison and not an
    // absence — step 1g's lesson, applied.
    //=========================================================================

    [Fact]
    public void TheEdgeCountIsWhatWasActuallyRaised()
    {
        var registry = Fresh(out var raised);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        int entered = registry.Directory.TouchFrame(Report((Volume, Walker)));
        int stayed = registry.Directory.TouchFrame(Report((Volume, Walker)));
        int left = registry.Directory.TouchFrame(Report());

        Assert.Equal(2, entered);
        Assert.Equal(0, stayed);
        Assert.Equal(2, left);
        Assert.Equal(2, registry.Directory.TouchEdgesLastFrame);
        Assert.Equal(4, registry.Directory.TouchEdgesTotal);
        Assert.Equal(raised.Log.Count, registry.Directory.TouchEdgesTotal);
    }

    [Fact]
    public void AFrameThatRaisedNothingReportsZeroRatherThanLastFramesCount()
    {
        var registry = Fresh(out _);
        Spawn<TestTouchVolume>(registry, Volume, "test_touch_volume");

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        Assert.Equal(2, registry.Directory.TouchEdgesLastFrame);

        registry.Directory.TouchFrame(Report((Volume, Walker)));
        Assert.Equal(0, registry.Directory.TouchEdgesLastFrame);
    }

    //=========================================================================
    // THE READER — what happens when the report will not fit.
    //=========================================================================

    /// <summary>
    /// A stand-in partition. It answers a FIXED list of pairs however many
    /// times it is asked, which is exactly the property the ABI promises: the
    /// native list is built once per frame and answered as many times as it is
    /// asked for.
    /// </summary>
    private sealed class Overlaps
    {
        private readonly OverlapPair[] _pairs;

        /// <summary>How many pairs to CLAIM, when that is not how many there are.</summary>
        public int Overstate = -1;

        /// <summary>
        /// What to claim from the SECOND ask onwards, when that differs.
        ///
        /// The ABI says the list is built once per frame and answered the same
        /// way every time, so a report that grows between two asks is a broken
        /// answer — which is exactly the case the refusal path exists for.
        /// </summary>
        public int OverstateAfterGrow = -1;

        public int Asks { get; private set; }

        public Overlaps(int pairs)
        {
            _pairs = new OverlapPair[pairs];

            for (int i = 0; i < pairs; i++)
                _pairs[i] = new OverlapPair(new EntityHandle((uint)(0x1000 + i)),
                                            new EntityHandle((uint)(0x9000 + i)));
        }

        public int Report(Span<OverlapPair> into)
        {
            Asks++;

            int claim = Asks > 1 && OverstateAfterGrow >= 0
                            ? OverstateAfterGrow
                            : Overstate >= 0 ? Overstate : _pairs.Length;
            int write = Math.Min(_pairs.Length, into.Length);

            _pairs.AsSpan(0, write).CopyTo(into);
            return claim;
        }
    }

    [Fact]
    public void AReportThatFitsIsReadInOneAsk()
    {
        var engine = new Overlaps(3);
        var reader = new OverlapReader(new RecordingLog(), engine.Report);

        Assert.True(reader.TryRead(out var pairs));
        Assert.Equal(3, pairs.Length);
        Assert.Equal(1, engine.Asks);
        Assert.Equal(0, reader.Regrows);
        Assert.Equal(OverlapReader.InitialPairs, reader.Capacity);
    }

    [Fact]
    public void AReportBiggerThanTheBufferGrowsItAndAsksAgain()
    {
        var engine = new Overlaps(OverlapReader.InitialPairs + 1);
        var reader = new OverlapReader(new RecordingLog(), engine.Report);

        Assert.True(reader.TryRead(out var pairs));

        // Two asks, and the SECOND one is what the caller reads. The first
        // answer was short, and taking it would read to the diff as every pair
        // past the end having left.
        Assert.Equal(2, engine.Asks);
        Assert.Equal(1, reader.Regrows);
        Assert.Equal(OverlapReader.InitialPairs + 1, pairs.Length);
        Assert.True(reader.Capacity >= OverlapReader.InitialPairs + 1);
    }

    [Fact]
    public void AReportThatStillDoesNotFitAfterGrowingRefusesTheFrameRatherThanTruncatingIt()
    {
        var engine = new Overlaps(4)
        {
            Overstate = OverlapReader.InitialPairs + 1,
            OverstateAfterGrow = OverlapReader.MaxPairs - 1,
        };

        var reader = new OverlapReader(new RecordingLog(), engine.Report);

        Assert.False(reader.TryRead(out var pairs));

        // It grew — the first answer was one past the buffer — and the second
        // answer was bigger again. FALSE IS NOT "NO PAIRS", and the caller must
        // not treat it as one: no pairs means sweep everything, and this means
        // change nothing.
        Assert.Equal(2, engine.Asks);
        Assert.Equal(1, reader.Regrows);
        Assert.Equal(0, pairs.Length);
        Assert.Equal(1, reader.Refused);
    }

    [Fact]
    public void ACountBiggerThanTheCeilingIsRefusedRatherThanDoubledIntoAnOverflow()
    {
        var engine = new Overlaps(4) { Overstate = int.MaxValue };
        var reader = new OverlapReader(new RecordingLog(), engine.Report);

        Assert.False(reader.TryRead(out _));

        // ONE ask, not two: a count past the ceiling is a broken answer and
        // there is nothing to grow to. Without the ceiling the doubling loop
        // runs 256 -> 2^30 -> int.MinValue -> 0 -> 0 -> 0 and never leaves —
        // which is how this class first HUNG the suite rather than failing it.
        Assert.Equal(1, engine.Asks);
        Assert.Equal(OverlapReader.InitialPairs, reader.Capacity);
        Assert.Equal(0, reader.Regrows);
        Assert.Equal(1, reader.Refused);
    }

    [Fact]
    public void TheBufferNeverGrowsPastTheCeiling()
    {
        var engine = new Overlaps(4) { Overstate = OverlapReader.MaxPairs + 1 };
        var reader = new OverlapReader(new RecordingLog(), engine.Report);

        Assert.False(reader.TryRead(out _));
        Assert.Equal(OverlapReader.InitialPairs, reader.Capacity);
        Assert.Equal(0, reader.Regrows);
    }

    [Fact]
    public void ANegativeCountIsRefusedRatherThanThrown()
    {
        var reader = new OverlapReader(new RecordingLog(), _ => -1);

        Assert.False(reader.TryRead(out var pairs));
        Assert.Equal(0, pairs.Length);
        Assert.Equal(1, reader.Refused);
    }

    [Fact]
    public void TheBufferIsKeptBetweenFrames()
    {
        var engine = new Overlaps(2);
        var reader = new OverlapReader(new RecordingLog(), engine.Report);

        Assert.True(reader.TryRead(out _));
        int capacity = reader.Capacity;

        Assert.True(reader.TryRead(out _));

        Assert.Equal(capacity, reader.Capacity);
        Assert.Equal(0, reader.Regrows);
    }

    [Fact]
    public void ARecordedEngineAnswersNoPairsAndRecordsThatItWasAsked()
    {
        var engine = Fake();
        var reader = new OverlapReader(new RecordingLog(), engine);

        Assert.True(reader.TryRead(out var pairs));
        Assert.Equal(0, pairs.Length);
        Assert.True(engine.Called("Entities.OverlapReport"));
    }

    //=========================================================================
    // THE PAIR'S OWN SHAPE — it is written by native code, byte for byte.
    //=========================================================================

    [Fact]
    public void APairIsTwoHandlesWide()
    {
        // The native side writes 2 * nMaxPairs uint32 straight into this array,
        // so a field added, reordered or widened here is a report read at the
        // wrong offsets — which is a set of pairs made of nonsense handles, and
        // a nonsense handle resolves to nothing rather than to an error.
        Assert.Equal(2 * sizeof(uint), Unsafe.SizeOf<OverlapPair>());
    }

    [Fact]
    public void APairIsEqualToItsOwnReverse()
    {
        var forward = new OverlapPair(Volume, Walker);
        var backward = new OverlapPair(Walker, Volume);

        // The report may name a pair either way round and it is one pair. The
        // table keys on the same rule.
        Assert.Equal(forward, backward);
        Assert.Equal(forward.GetHashCode(), backward.GetHashCode());
    }

    [Fact]
    public void TwoDifferentPairsAreNotEqual()
    {
        Assert.NotEqual(new OverlapPair(Volume, Walker), new OverlapPair(Volume, Second));
    }
}
