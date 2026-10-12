using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The think heap and the directory that runs it — <c>plan_entity.md</c> §3.4
/// and step 1g.
///
/// <para>
/// The step's gate is three properties, and each has a fact of its own below:
/// N thinking entities cost ONE crossing per frame regardless of N; two thinks
/// due at the same instant run in booking order; and a named context can be
/// cancelled without touching the default schedule or any other context.
/// </para>
/// </summary>
public class ThinkSchedulerTests
{
    private static EntityHandle H(uint entry, uint serial = 1)
        => new(entry | (serial << 16));

    private static List<ScheduledThink> Due(ThinkScheduler s, float now)
    {
        var into = new List<ScheduledThink>();
        s.TakeDue(now, into);
        return into;
    }

    //-------------------------------------------------------------------------
    // Booking, and what supersedes what
    //-------------------------------------------------------------------------

    [Fact]
    public void AScheduledThinkComesDueAtItsTime()
    {
        var s = new ThinkScheduler();
        s.Schedule(H(4), null, 10.0f);

        Assert.Empty(Due(s, 9.99f));
        Assert.Single(Due(s, 10.0f));
    }

    [Fact]
    public void ATakenThinkIsUnbookedSoItDoesNotRunTwice()
    {
        var s = new ThinkScheduler();
        s.Schedule(H(4), null, 1.0f);

        Assert.Single(Due(s, 5.0f));
        Assert.Empty(Due(s, 5.0f));
    }

    /// <summary>
    /// Rescheduling REPLACES rather than adding, or an entity that books every
    /// tick would come due once per booking it ever made.
    /// </summary>
    [Fact]
    public void RescheduleSupersedesTheEarlierBooking()
    {
        var s = new ThinkScheduler();
        var e = H(7);

        s.Schedule(e, null, 1.0f);
        s.Schedule(e, null, 2.0f);

        // BOTH entries are in the heap — the heap is append-only — and only one
        // of them is live.
        Assert.Equal(2, s.HeapDepth);
        Assert.Equal(1, s.Pending);

        Assert.Single(Due(s, 5.0f));

        // And the superseded one was dropped on the way past rather than run.
        Assert.Equal(0, s.HeapDepth);
    }

    [Fact]
    public void TheSupersededBookingIsNotWhatComesDue()
    {
        var s = new ThinkScheduler();
        var e = H(7);

        s.Schedule(e, null, 1.0f);
        s.Schedule(e, null, 2.0f);

        // At 1.5 the FIRST booking's time has passed and the second's has not.
        // A scheduler that did not supersede would answer one think here.
        Assert.Empty(Due(s, 1.5f));
    }

    [Fact]
    public void ANegativeTimeCancelsRatherThanSchedulingInThePast()
    {
        var s = new ThinkScheduler();
        var e = H(3);

        s.Schedule(e, null, 1.0f);
        s.Schedule(e, null, -1.0f);

        Assert.Empty(Due(s, 100.0f));
        Assert.False(s.IsScheduled(e, null));
    }

    [Fact]
    public void NextThinkTimeIsMinusOneWhenNothingIsBooked()
    {
        var s = new ThinkScheduler();
        Assert.Equal(-1.0f, s.NextThinkTime(H(1), null));
    }

    [Fact]
    public void NextThinkTimeAnswersTheLiveBooking()
    {
        var s = new ThinkScheduler();
        s.Schedule(H(1), null, 3.5f);
        Assert.Equal(3.5f, s.NextThinkTime(H(1), null));
    }

    //-------------------------------------------------------------------------
    // Equal-time ordering — the gate's second property
    //-------------------------------------------------------------------------

    /// <summary>
    /// Two thinks booked for the SAME instant run in the order they were
    /// booked. Without the sequence tie-break a binary heap's answer among
    /// equal keys is its internal array order, which is stable only by accident
    /// and changes when an unrelated third entity books.
    /// </summary>
    [Fact]
    public void TwoThinksAtTheSameTimeRunInBookingOrder()
    {
        var s = new ThinkScheduler();

        s.Schedule(H(11), null, 5.0f);
        s.Schedule(H(12), null, 5.0f);
        s.Schedule(H(13), null, 5.0f);

        List<ScheduledThink> due = Due(s, 5.0f);

        Assert.Equal(new uint[] { H(11).Value, H(12).Value, H(13).Value },
                     due.Select(d => d.Entity.Value).ToArray());
    }

    /// <summary>
    /// And booking order does NOT beat time order: an entity booked later for
    /// an earlier instant still runs first.
    /// </summary>
    [Fact]
    public void AnEarlierTimeBeatsAnEarlierBooking()
    {
        var s = new ThinkScheduler();

        s.Schedule(H(11), null, 9.0f);
        s.Schedule(H(12), null, 1.0f);

        List<ScheduledThink> due = Due(s, 10.0f);

        Assert.Equal(new uint[] { H(12).Value, H(11).Value },
                     due.Select(d => d.Entity.Value).ToArray());
    }

    /// <summary>
    /// The equal-time order survives an unrelated booking landing between the
    /// two, which is what tells a real tie-break from a heap that happens to be
    /// stable for two elements.
    /// </summary>
    [Fact]
    public void EqualTimeOrderSurvivesAnUnrelatedBookingBetweenThem()
    {
        var s = new ThinkScheduler();

        s.Schedule(H(11), null, 5.0f);
        s.Schedule(H(99), null, 1.0f);   // earlier, and booked in between
        s.Schedule(H(12), null, 5.0f);

        List<ScheduledThink> due = Due(s, 5.0f);

        Assert.Equal(new uint[] { H(99).Value, H(11).Value, H(12).Value },
                     due.Select(d => d.Entity.Value).ToArray());
    }

    //-------------------------------------------------------------------------
    // Named contexts — the gate's third property
    //-------------------------------------------------------------------------

    [Fact]
    public void ANamedContextIsScheduledIndependentlyOfTheDefault()
    {
        var s = new ThinkScheduler();
        var e = H(21);

        s.Schedule(e, null, 1.0f);
        s.Schedule(e, "door", 2.0f);

        Assert.Equal(1.0f, s.NextThinkTime(e, null));
        Assert.Equal(2.0f, s.NextThinkTime(e, "door"));
        Assert.Equal(2, s.Pending);
    }

    [Fact]
    public void CancellingANamedContextLeavesTheDefaultStanding()
    {
        var s = new ThinkScheduler();
        var e = H(21);

        s.Schedule(e, null, 1.0f);
        s.Schedule(e, "door", 2.0f);

        Assert.True(s.Cancel(e, "door"));

        Assert.True(s.IsScheduled(e, null));
        Assert.False(s.IsScheduled(e, "door"));

        Assert.Equal(new string?[] { null },
                     Due(s, 100.0f).Select(d => d.Context).ToArray());
    }

    [Fact]
    public void CancellingANamedContextLeavesEveryOtherContextStanding()
    {
        var s = new ThinkScheduler();
        var e = H(21);

        s.Schedule(e, "open", 1.0f);
        s.Schedule(e, "close", 2.0f);
        s.Schedule(e, "blocked", 3.0f);

        Assert.True(s.Cancel(e, "close"));

        Assert.Equal(new[] { "open", "blocked" },
                     Due(s, 100.0f).Select(d => d.Context).ToArray());
    }

    [Fact]
    public void CancellingTheDefaultLeavesTheNamedContextsStanding()
    {
        var s = new ThinkScheduler();
        var e = H(21);

        s.Schedule(e, null, 1.0f);
        s.Schedule(e, "door", 2.0f);

        Assert.True(s.Cancel(e, null));

        Assert.Equal(new string?[] { "door" },
                     Due(s, 100.0f).Select(d => d.Context).ToArray());
    }

    [Fact]
    public void CancellingSomethingNotBookedAnswersFalse()
    {
        var s = new ThinkScheduler();
        Assert.False(s.Cancel(H(1), "door"));
        Assert.False(s.Cancel(H(1), null));
    }

    [Fact]
    public void CancelAllTakesEveryContextOfOneEntityAndNoOther()
    {
        var s = new ThinkScheduler();
        var mine = H(21);
        var other = H(22);

        s.Schedule(mine, null, 1.0f);
        s.Schedule(mine, "door", 1.0f);
        s.Schedule(other, null, 1.0f);

        s.CancelAll(mine);

        Assert.Equal(new uint[] { other.Value },
                     Due(s, 100.0f).Select(d => d.Entity.Value).ToArray());
    }

    /// <summary>
    /// Context names are compared ORDINALLY and case matters, because a class
    /// dispatches <c>OnThink(string)</c> on the name it wrote.
    /// </summary>
    [Fact]
    public void ContextNamesAreOrdinalAndCaseSensitive()
    {
        var s = new ThinkScheduler();
        var e = H(21);

        s.Schedule(e, "Door", 1.0f);
        s.Schedule(e, "door", 2.0f);

        Assert.Equal(2, s.Pending);
    }

    //-------------------------------------------------------------------------
    // The snapshot rule
    //-------------------------------------------------------------------------

    /// <summary>
    /// A think that reschedules itself for NOW runs next frame, not twice in
    /// this one. <see cref="ThinkScheduler.TakeDue"/> is a snapshot, and this
    /// is the property that makes <c>SetNextThink(0)</c> a think that runs
    /// every tick rather than an infinite loop inside one.
    /// </summary>
    [Fact]
    public void AThinkThatRebooksForNowDoesNotRunAgainInTheSamePass()
    {
        var s = new ThinkScheduler();
        var e = H(31);

        s.Schedule(e, null, 5.0f);

        var into = new List<ScheduledThink>();
        Assert.Equal(1, s.TakeDue(5.0f, into));

        // What the think would do, from inside the pass.
        s.Schedule(e, null, 5.0f);

        // The pass is over; the snapshot it is walking still holds one entry
        // and the new booking is not in it.
        Assert.Single(into);

        // And it IS booked, for the next pass.
        Assert.True(s.IsScheduled(e, null));
    }

    [Fact]
    public void ClearDropsEveryBooking()
    {
        var s = new ThinkScheduler();
        s.Schedule(H(1), null, 1.0f);
        s.Schedule(H(2), "a", 1.0f);

        s.Clear();

        Assert.Equal(0, s.Pending);
        Assert.Equal(0, s.HeapDepth);
        Assert.Empty(Due(s, 100.0f));
    }

    //-------------------------------------------------------------------------
    // The directory: the array, the serial, and the one-crossing frame
    //-------------------------------------------------------------------------

    [Fact]
    public void TheEntryIndexIsStocksIncludingItsAnswerForTheInvalidHandle()
    {
        // ENT_ENTRY_MASK is the SERIAL width (16 bits), not the entry width.
        Assert.Equal(0x1234, EntityDirectory.EntryIndex(new EntityHandle(0x0007_1234u)));

        // basehandle.h:143-153 answers NUM_ENT_ENTRIES-1 for the invalid handle
        // ON PURPOSE, and the directory refuses that handle rather than
        // addressing entry 8191 with it.
        Assert.Equal(EntityDirectory.EntryCount - 1,
                     EntityDirectory.EntryIndex(EntityHandle.None));
    }

    [Fact]
    public void TheInvalidHandleIsRefusedRatherThanBoundToEntry8191()
    {
        var d = new EntityDirectory(new RecordingLog());

        Assert.False(d.Bind(EntityHandle.None, new ThinkFixture()));
        Assert.Equal(0, d.Count);
        Assert.Null(d.Resolve(EntityHandle.None));
    }

    [Fact]
    public void ABoundEntityResolvesByItsHandle()
    {
        var d = new EntityDirectory(new RecordingLog());
        var e = new ThinkFixture();
        var h = H(64, serial: 3);

        Assert.True(d.Bind(h, e));
        Assert.Same(e, d.Resolve(h));
        Assert.Equal(1, d.Count);
    }

    /// <summary>
    /// A stale handle — same entry, older serial — resolves to null. The serial
    /// is checked because the whole handle is stored, which is the point of
    /// storing it rather than the entity alone.
    /// </summary>
    [Fact]
    public void AStaleSerialDoesNotResolveToTheEntrysNewOccupant()
    {
        var d = new EntityDirectory(new RecordingLog());
        var old = H(64, serial: 3);
        var now = H(64, serial: 4);

        d.Bind(old, new ThinkFixture());
        d.Unbind(old);
        d.Bind(now, new ThinkFixture());

        Assert.Null(d.Resolve(old));
        Assert.NotNull(d.Resolve(now));
    }

    [Fact]
    public void UnbindingOnAStaleHandleDoesNotEvictTheLiveOccupant()
    {
        var d = new EntityDirectory(new RecordingLog());
        var old = H(64, serial: 3);
        var now = H(64, serial: 4);

        d.Bind(old, new ThinkFixture());
        d.Unbind(old);

        var live = new ThinkFixture();
        d.Bind(now, live);

        Assert.False(d.Unbind(old));
        Assert.Same(live, d.Resolve(now));
    }

    [Fact]
    public void UnbindingCancelsTheEntitysThinks()
    {
        var d = new EntityDirectory(new RecordingLog());
        var h = H(64);

        d.Bind(h, new ThinkFixture());
        d.Think.Schedule(h, null, 1.0f);
        d.Think.Schedule(h, "door", 1.0f);

        d.Unbind(h);

        Assert.Equal(0, d.Think.Pending);
    }

    [Fact]
    public void TheEngineInvisibleRangeIsAddressable()
    {
        // Entries 2048..8191 are not edicts (slot_table.h, §3.3). A directory
        // sized to MAX_EDICTS would refuse every one of them.
        var d = new EntityDirectory(new RecordingLog());
        var h = H(8191);

        Assert.True(d.Bind(h, new ThinkFixture()));
        Assert.NotNull(d.Resolve(h));
    }

    [Fact]
    public void AnEntryBeyondTheTableIsRefusedRatherThanWrapped()
    {
        // ENT_ENTRY_MASK admits 65,535 and the table holds 8,192, so a handle
        // CAN name an entry that does not exist. Binding it somewhere else
        // would be a silent mis-addressing.
        var d = new EntityDirectory(new RecordingLog());

        Assert.False(d.Bind(new EntityHandle(0x0001_2000u), new ThinkFixture()));
        Assert.Null(d.Resolve(new EntityHandle(0x0001_2000u)));
    }

    [Fact]
    public void IterationCostsTheLiveEntitiesAndNotTheWholeTable()
    {
        var d = new EntityDirectory(new RecordingLog());

        d.Bind(H(1), new ThinkFixture());
        d.Bind(H(4000), new ThinkFixture());
        d.Bind(H(8000), new ThinkFixture());
        d.Unbind(H(4000));

        Assert.Equal(2, d.Entities.Count());
        Assert.Equal(2, d.Count);
    }

    [Fact]
    public void ClearEmptiesTheDirectoryAndItsHeap()
    {
        var d = new EntityDirectory(new RecordingLog());

        d.Bind(H(1), new ThinkFixture());
        d.Think.Schedule(H(1), null, 1.0f);

        d.Clear();

        Assert.Equal(0, d.Count);
        Assert.Empty(d.Entities);
        Assert.Equal(0, d.Think.Pending);
    }

    //-------------------------------------------------------------------------
    // THE GATE: N thinking entities, ONE pass
    //-------------------------------------------------------------------------

    /// <summary>
    /// <b>N thinking entities cost ONE frame pass, whatever N is.</b>
    ///
    /// <para>
    /// This is the step's gate, and the reason it can be asserted here rather
    /// than only on a live server is that the pass is what the crossing WAS: the
    /// native frame used to walk the shells and call into managed code once per
    /// entity whose think had come due, so N due thinks were N crossings.
    /// <see cref="EntityDirectory.RunFrame"/> is called once from inside the
    /// <c>GameFrame</c> crossing that already exists, so N due thinks are N
    /// managed calls and no crossings at all. The live half of the gate reads
    /// the 0c counters on a running server.
    /// </para>
    /// </summary>
    [Fact]
    public void RunFrameRunsEveryDueThinkInOnePass()
    {
        var d = new EntityDirectory(new RecordingLog());
        var seen = new List<int>();

        for (int i = 1; i <= 64; i++)
        {
            var h = H((uint)i);
            var e = new ThinkFixture(seen, i);
            d.Bind(h, e);
            d.Think.Schedule(h, null, 5.0f);
        }

        Assert.Equal(64, d.RunFrame(5.0f));
        Assert.Equal(64, seen.Count);
    }

    [Fact]
    public void RunFrameRunsNothingBeforeItsTime()
    {
        var d = new EntityDirectory(new RecordingLog());
        var seen = new List<int>();

        var h = H(9);
        d.Bind(h, new ThinkFixture(seen, 9));
        d.Think.Schedule(h, null, 5.0f);

        Assert.Equal(0, d.RunFrame(4.9f));
        Assert.Empty(seen);
    }

    [Fact]
    public void RunFrameDeliversInEqualTimeBookingOrder()
    {
        var d = new EntityDirectory(new RecordingLog());
        var seen = new List<int>();

        foreach (int i in new[] { 3, 1, 2 })
        {
            var h = H((uint)i);
            d.Bind(h, new ThinkFixture(seen, i));
            d.Think.Schedule(h, null, 5.0f);
        }

        d.RunFrame(5.0f);

        Assert.Equal(new[] { 3, 1, 2 }, seen);
    }

    /// <summary>
    /// An entity removed by an EARLIER think in the same pass is skipped rather
    /// than dispatched: it was already in the snapshot when it went.
    /// </summary>
    [Fact]
    public void AThinkOnAnEntityRemovedEarlierInTheSamePassIsSkipped()
    {
        var d = new EntityDirectory(new RecordingLog());
        var seen = new List<int>();

        var first = H(1);
        var second = H(2);

        d.Bind(second, new ThinkFixture(seen, 2));
        d.Bind(first, new ThinkFixture(seen, 1) { OnRun = () => d.Unbind(second) });

        d.Think.Schedule(first, null, 5.0f);
        d.Think.Schedule(second, null, 5.0f);

        Assert.Equal(1, d.RunFrame(5.0f));
        Assert.Equal(new[] { 1 }, seen);
    }

    /// <summary>
    /// <b>THE PASS RECORDS HOW MUCH WORK IT DID, and that is what keeps this
    /// step's crossing claim from being vacuous.</b> "Think costs zero
    /// crossings now" is trivially true of a build that stopped thinking, and
    /// <c>crossings.entityOps</c> reads zero either way. <c>ranLastFrame</c> in
    /// <c>debug-api status</c> comes from here, and it is the N the old build paid
    /// N crossings for.
    /// </summary>
    [Fact]
    public void RunFrameRecordsHowManyThinksItRan()
    {
        var d = new EntityDirectory(new RecordingLog());

        for (int i = 1; i <= 5; i++)
        {
            var h = H((uint)i);
            d.Bind(h, new ThinkFixture());
            d.Think.Schedule(h, null, 5.0f);
        }

        d.RunFrame(5.0f);

        Assert.Equal(5, d.ThinksRanLastFrame);
        Assert.Equal(5L, d.ThinksRunTotal);
    }

    [Fact]
    public void ThinksRunTotalAccumulatesAcrossFrames()
    {
        var d = new EntityDirectory(new RecordingLog());
        var h = H(1);
        d.Bind(h, new ThinkFixture());

        d.Think.Schedule(h, null, 1.0f);
        d.RunFrame(1.0f);
        d.Think.Schedule(h, null, 2.0f);
        d.RunFrame(2.0f);

        Assert.Equal(1, d.ThinksRanLastFrame);
        Assert.Equal(2L, d.ThinksRunTotal);
    }

    /// <summary>
    /// A frame that ran nothing reports zero rather than last frame's count —
    /// or a server that stopped thinking would go on reporting the last busy
    /// frame forever, which is exactly the reading this counter exists to
    /// refuse.
    /// </summary>
    [Fact]
    public void AFrameThatRanNothingReportsZeroRatherThanLastFramesCount()
    {
        var d = new EntityDirectory(new RecordingLog());
        var h = H(1);
        d.Bind(h, new ThinkFixture());
        d.Think.Schedule(h, null, 1.0f);

        d.RunFrame(1.0f);
        Assert.Equal(1, d.ThinksRanLastFrame);

        d.RunFrame(2.0f);
        Assert.Equal(0, d.ThinksRanLastFrame);
        Assert.Equal(1L, d.ThinksRunTotal);
    }

    [Fact]
    public void RunFrameDeliversTheContextItWasBookedUnder()
    {
        var d = new EntityDirectory(new RecordingLog());
        var e = new ThinkFixture();
        var h = H(1);

        d.Bind(h, e);
        d.Think.Schedule(h, null, 1.0f);
        d.Think.Schedule(h, "door", 1.0f);

        d.RunFrame(1.0f);

        Assert.Equal(new string?[] { null, "door" }, e.Contexts);
    }

    /// <summary>
    /// A fixture entity that records the thinks it is handed.
    ///
    /// <para>
    /// NO <c>[ScriptedEntity]</c> ATTRIBUTE, deliberately: these tests drive the
    /// directory and the heap directly and never a registry, and an attributed
    /// classname with no stock counterpart is what
    /// <c>ContractDiffTests.EveryManagedClassnameStockDoesNotDeclareIsAccountedFor</c>
    /// exists to refuse.
    /// </para>
    /// </summary>
    public sealed class ThinkFixture : ScriptedEntity
    {
        private readonly List<int>? _seen;
        private readonly int _id;

        public List<string?> Contexts { get; } = new();
        public Action? OnRun { get; init; }

        public ThinkFixture() { }

        public ThinkFixture(List<int> seen, int id)
        {
            _seen = seen;
            _id = id;
        }

        public override void OnThink()
        {
            _seen?.Add(_id);
            Contexts.Add(null);
            OnRun?.Invoke();
        }

        public override void OnThink(string context)
        {
            _seen?.Add(_id);
            Contexts.Add(context);
            OnRun?.Invoke();
        }
    }
}
