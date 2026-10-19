extern alias client;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The legacy temp entity pool's spine (plan_tmp.md Part 5): allocation, the
/// free list, the priority classes and their eviction, the lifetime and expiry,
/// the update loop, and <c>KillAttachedTents</c>.
///
/// <para>
/// THE DOUBLE MODELS THE SLOT TABLE, and that is what makes these tests worth
/// anything. A double that only recorded calls could not tell the difference
/// between a pool that frees the right slot and one that frees any slot, or
/// between a free list that reuses a slot and one that hands the same slot out
/// twice. <see cref="FakeNativePool"/> holds a <see cref="TempEntLife"/> per
/// bound slot and answers false for an unbound one, exactly as
/// <c>shell_te_pool_client.cpp</c> does, so what these assert is the pool's
/// EFFECT on the tempents and not its conversation.
/// </para>
///
/// <para>
/// AND TWO OF THEM READ THE C++ OFF DISK. <see cref="TheFlagBitsAreTempentHs"/>
/// and <see cref="TheLifeStructMatchesTheHeader"/> are there because everything
/// else in this file would agree with itself: a managed pool tested against a
/// managed double is a closed loop, and the two places it can be wrong without
/// any of that noticing are a flag bit that means something else in
/// <c>tempent.h</c> and a struct field that moved in <c>sourcesharp.h</c>.
/// This tree's memory calls that "a stand-in for native drifts silently; assert
/// the agreement".
/// </para>
/// </summary>
public class TempEntPoolTests
{
    // ---------------------------------------------------------------------
    // The doubles.
    // ---------------------------------------------------------------------

    /// <summary>A clock a test sets. <c>RecordingEngine</c>'s answers a stub.</summary>
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>
    /// <c>shell_te_pool_client.cpp</c>'s slot table, in managed code.
    ///
    /// <para>
    /// A slot is BOUND when native has put a <c>C_LocalTempEntity</c> in it and
    /// EMPTY otherwise, and every entry point answers false for an empty one —
    /// which is the contract the real slots keep and the thing the pool's own
    /// recovery is built on.
    /// </para>
    /// </summary>
    /// <summary>
    /// A simulation that never runs, for a rig whose subject is a SPAWN.
    ///
    /// <para>
    /// Its Step is unreachable in those tests -- nothing calls
    /// <c>Update</c> -- and it throws rather than answering, so a rig that grows
    /// an <c>Update</c> later says which double it needs instead of quietly
    /// stepping a tempent that no test set a velocity on.
    /// </para>
    /// </summary>
    internal sealed class NoSimulation : IClientTempEntSimulation
    {
        public bool Step(int slot, float frameTime, int frameNumber, int frameCount,
                         ref TempEntLife life)
            => throw new InvalidOperationException(
                "this rig has no physics in it; use TempEntFrameTests' engine double instead");

        public void Forget(int slot) { }
        public void LevelInit() { }
    }

    private sealed class FakeNativePool : EngineEffectsForwarder, IClientTempEntSimulation
    {
        private readonly TempEntLife[] _life;
        private readonly bool[] _bound;

        public FakeNativePool(IEngineEffects inner, int capacity) : base(inner)
        {
            Capacity = capacity;
            _life = new TempEntLife[Math.Max(capacity, 1)];
            _bound = new bool[Math.Max(capacity, 1)];
        }

        /// <summary>What <c>TempEntPoolCapacity</c> answers. Zero is "not wired".</summary>
        public int Capacity { get; set; }

        /// <summary>Slots destroyed, in order.</summary>
        public List<int> Destroyed { get; } = new();

        /// <summary>Slots simulated this run, in order, with the frame number they got.</summary>
        public List<(int Slot, int Frame)> Simulated { get; } = new();

        /// <summary>Slots handed to the leaf system, in order.</summary>
        public List<int> MadeVisible { get; } = new();

        /// <summary>Slots read, in order. One entry per read, so a double read shows.</summary>
        public List<int> Read { get; } = new();

        /// <summary>Slots written, in order.</summary>
        public List<int> Written { get; } = new();

        /// <summary>Which slots <c>Frame()</c> says asked to die.</summary>
        public HashSet<int> AsksToDie { get; } = new();

        /// <summary>Which slots the leaf system refuses.</summary>
        public HashSet<int> Invisible { get; } = new();

        /// <summary>Put an object in a slot, as native's <c>TempEntAlloc</c> does after the reservation.</summary>
        public void Bind(int slot, TempEntLife life)
        {
            _bound[slot] = true;
            _life[slot] = life;
        }

        /// <summary>Take the object away without telling the pool — a level torn down under it.</summary>
        public void Unbind(int slot) => _bound[slot] = false;

        public bool IsBound(int slot) => _bound[slot];

        public TempEntLife LifeOf(int slot) => _life[slot];

        public override int TempEntPoolCapacity() => Capacity;

        public override void TempEntPoolDestroy(int slot)
        {
            Destroyed.Add(slot);
            _bound[slot] = false;
        }

        public override bool TempEntPoolReadLife(int slot, out TempEntLife life)
        {
            Read.Add(slot);
            life = _life[slot];
            return _bound[slot];
        }

        public override bool TempEntPoolWriteLife(int slot, in TempEntLife life)
        {
            Written.Add(slot);

            if (!_bound[slot])
                return false;

            _life[slot] = life;
            return true;
        }

        /// <summary>
        /// Run inside <c>Frame()</c>, once, on the first slot stepped. That is
        /// where a <c>DispatchEffect</c> on a collision would spawn a tempent.
        /// </summary>
        public Action<int>? DuringSimulate { get; set; }

        /// <summary>Slots the pool told the simulation to forget, in order.</summary>
        public List<int> Forgotten { get; } = new();

        /// <summary>How many times the pool announced a new level.</summary>
        public int LevelInits { get; private set; }

        //---------------------------------------------------------------------
        // IClientTempEntSimulation. THE PHYSICS IS C# NOW, so this is a seam
        // rather than an ABI slot -- and this double implements BOTH halves on
        // purpose. Every assertion in this file about which slot was stepped on
        // which frame was written against the ABI call and is unchanged; what
        // moved is only where the call arrives.
        //---------------------------------------------------------------------
        public bool Step(int slot, float frameTime, int frameNumber, int frameCount,
                         ref TempEntLife life)
        {
            Simulated.Add((slot, frameNumber));
            FrameCounts.Add(frameCount);

            var reenter = DuringSimulate;
            DuringSimulate = null;
            reenter?.Invoke(slot);

            if (FlagsAfterStep.TryGetValue(slot, out int flags))
                life.Flags = flags;

            return _bound[slot] && !AsksToDie.Contains(slot);
        }

        /// <summary>The monotonic frame count the pool handed each step.</summary>
        public List<int> FrameCounts { get; } = new();

        /// <summary>
        /// Flags the step WRITES INTO the record it was handed, by slot. This
        /// is what a real Frame does on a gib that lands -- it clears
        /// FTENT_ROTATE and three more -- and it is how a test says the pool
        /// judged a tempent on the flags it came OUT of its step with.
        /// </summary>
        public Dictionary<int, int> FlagsAfterStep { get; } = new();

        public void Forget(int slot) => Forgotten.Add(slot);

        public void LevelInit() => LevelInits++;

        public override bool TempEntPoolAddVisible(int slot)
        {
            MadeVisible.Add(slot);
            return _bound[slot] && !Invisible.Contains(slot);
        }
    }

    private static (ClientTempEntPool Pool, FakeNativePool Native, Clock Clock) Rig(int capacity = 4)
    {
        var native = new FakeNativePool(TestServices.Fake(), capacity);
        var clock = new Clock();

        return (new ClientTempEntPool(new RecordingLog(), native, clock, native), native, clock);
    }

    /// <summary>
    /// A tempent that lives forever and does nothing: the neutral subject for a
    /// test about the LIST rather than about the lifetime.
    /// </summary>
    private static TempEntLife Immortal(float die = 1000f, int priority = TempEntPriority.Low)
        => new()
        {
            Die = die,
            Flags = (int)TempEntFlags.None,
            FadeSpeed = 0.5f,
            RenderAmt = 255,
            Priority = priority,
            ClientIndex = -1,
            RenderMode = (int)RenderMode.Normal,
            Alpha = 255,
        };

    /// <summary>Reserve one and bind it, which is what native does in two steps.</summary>
    private static int Spawn(ClientTempEntPool pool, FakeNativePool native, TempEntLife life)
    {
        int slot = pool.Reserve(life.Priority);

        if (slot >= 0)
            native.Bind(slot, life);

        return slot;
    }

    // ---------------------------------------------------------------------
    // Allocation and the free list.
    // ---------------------------------------------------------------------

    [Fact]
    public void ReserveHandsOutASlotInsideTheCapacityNativeAnswered()
    {
        var (pool, native, _) = Rig(capacity: 4);

        int slot = pool.Reserve(TempEntPriority.Low);

        Assert.True(slot >= 0 && slot < 4,
            $"the slot is inside native's own table (got {slot} of 4) — a slot outside it is a "
          + "write past the end of g_pTempEntSlots");
        Assert.True(pool.Count == 1, "one tempent is live");
        Assert.True(pool.Capacity == 4, "the capacity is native's answer, not a managed constant");
        _ = native;
    }

    [Fact]
    public void ThereIsNoPoolAtAllUntilNativeAnswersACapacity()
    {
        var (pool, native, _) = Rig(capacity: 0);

        Assert.True(pool.Reserve(TempEntPriority.Low) == -1,
            "a pool whose capacity slot is not wired refuses rather than inventing a size");
        Assert.True(pool.Refused == 1, "and counts the refusal");

        // NOT CACHED AS ZERO, which is the point: asking too early must not
        // leave the pool dead for the life of the process.
        native.Capacity = 4;

        Assert.True(pool.Reserve(TempEntPriority.Low) >= 0,
            "and asks again once native can answer");
    }

    [Fact]
    public void ALowPriorityReservationRefusesWhenThePoolIsFull()
    {
        var (pool, native, _) = Rig(capacity: 2);

        Spawn(pool, native, Immortal());
        Spawn(pool, native, Immortal());

        Assert.True(pool.Reserve(TempEntPriority.Low) == -1,
            "stock's TempEntAlloc gives up when the list is full — it does not evict");
        Assert.True(native.Destroyed.Count == 0, "and nothing was freed to make room");
        Assert.True(pool.Count == 2, "both live tempents survived");
    }

    [Fact]
    public void AHighPriorityReservationGivesUpTheOLDESTLowPriorityTempent()
    {
        var (pool, native, _) = Rig(capacity: 3);

        int first = Spawn(pool, native, Immortal(priority: TempEntPriority.High));
        int second = Spawn(pool, native, Immortal(priority: TempEntPriority.Low));
        int third = Spawn(pool, native, Immortal(priority: TempEntPriority.Low));

        int made = pool.Reserve(TempEntPriority.High);

        Assert.True(made >= 0, "a high-priority reservation gets a slot from a full pool");

        // THE ORDER IS THE ASSERTION. Stock's FreeLowPriorityTempEnt walks its
        // list from the head and takes the FIRST low-priority tempent, which is
        // the oldest. A pool that took the newest would sacrifice the gib that
        // just spawned instead of the one about to expire, and every count in
        // this file would still agree.
        Assert.True(native.Destroyed.Count == 1 && native.Destroyed[0] == second,
            $"the OLDEST low-priority tempent went (slot {second}); "
          + $"destroyed {string.Join(",", native.Destroyed)}");
        Assert.True(native.IsBound(first), "the high-priority one was not touched");
        Assert.True(native.IsBound(third), "nor was the newer low-priority one");
        Assert.True(pool.Evicted == 1, "and the eviction is counted");
    }

    [Fact]
    public void AHighPriorityReservationRefusesWhenEveryLiveTempentIsHighPriority()
    {
        var (pool, native, _) = Rig(capacity: 2);

        Spawn(pool, native, Immortal(priority: TempEntPriority.High));
        Spawn(pool, native, Immortal(priority: TempEntPriority.High));

        Assert.True(pool.Reserve(TempEntPriority.High) == -1,
            "stock's own comment: the list is either full of high-priority tents or they are all "
          + "still due to live");
        Assert.True(native.Destroyed.Count == 0, "and no high-priority tempent was sacrificed");
    }

    [Fact]
    public void TheEvictionReadsThePriorityOffTheOBJECTRatherThanARememberedCopy()
    {
        var (pool, native, _) = Rig(capacity: 1);

        // Reserved as LOW, and then native makes it HIGH -- which is exactly
        // what CTempEnts::TempEntAllocHigh does, one line after the
        // reservation returns.
        int slot = pool.Reserve(TempEntPriority.Low);
        native.Bind(slot, Immortal(priority: TempEntPriority.High));

        Assert.True(pool.Reserve(TempEntPriority.High) == -1,
            "the live tempent is HIGH priority on the object, so it must not be evicted — a pool "
          + "that remembered the priority it handed the slot out under would sacrifice it");
    }

    [Fact]
    public void AFreedSlotComesBackAroundAndIsNeverHandedOutTwice()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        int first = Spawn(pool, native, Immortal(die: 1f));
        int second = Spawn(pool, native, Immortal(die: 1000f));

        Assert.True(first != second, "two live tempents never share a slot");

        clock.Now = 2f;
        pool.Update(0.1f);

        Assert.True(pool.Count == 1, "the expired one went");

        int third = Spawn(pool, native, Immortal());

        Assert.True(third == first,
            $"the freed slot came back around (expected {first}, got {third})");
        Assert.True(third != second, "and the live one's slot was not handed out under it");
    }

    // ---------------------------------------------------------------------
    // Lifetime and expiry -- stock C_LocalTempEntity::IsActive.
    // ---------------------------------------------------------------------

    [Fact]
    public void AnExpiredTempentIsFreedAndTheOthersAreNot()
    {
        var (pool, native, clock) = Rig(capacity: 3);

        int dying = Spawn(pool, native, Immortal(die: 1f));
        int living = Spawn(pool, native, Immortal(die: 100f));

        clock.Now = 5f;
        pool.Update(0.1f);

        Assert.True(native.Destroyed.Count == 1 && native.Destroyed[0] == dying,
            "only the one whose die has passed");
        Assert.True(native.IsBound(living), "the other is untouched");
        Assert.True(pool.Count == 1, "and the list is one shorter");
    }

    [Fact]
    public void AnExpiredTempentIsNeverSimulated()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        int slot = Spawn(pool, native, Immortal(die: 1f));

        clock.Now = 5f;
        pool.Update(0.1f);

        // Stock's `!IsActive() || !Frame()` short-circuits, so a tempent that
        // has expired never runs a physics step. It matters: Frame() moves the
        // thing, plays a bounce sound and can dispatch an impact effect.
        Assert.True(!native.Simulated.Any(s => s.Slot == slot),
            "an expired tempent is freed without a physics step");
    }

    [Fact]
    public void AFadingTempentRampsItsAlphaAndMovesIntoATranslucentPass()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        var life = Immortal(die: 10f);
        life.Flags = (int)TempEntFlags.FadeOut;
        life.FadeSpeed = 0.5f;
        life.RenderAmt = 200;

        int slot = Spawn(pool, native, life);

        // One second past its die, at fadeSpeed 0.5: 200 * (1 + -1 * 0.5) = 100.
        clock.Now = 11f;
        pool.Update(0.1f);

        Assert.True(pool.Count == 1, "it is still alive — fading is not dying");

        TempEntLife after = native.LifeOf(slot);

        Assert.True(after.Alpha == 100,
            $"the ramp is renderamt * (1 + life * fadeSpeed) = 100, got {after.Alpha}");

        // NOT COSMETIC. A kRenderNormal renderable draws opaque whatever its
        // alpha says, so a fading tempent left in the opaque pass stays solid
        // and then vanishes in one frame.
        Assert.True(after.RenderMode == (int)RenderMode.TransTexture,
            "and it has moved out of the opaque pass");
    }

    [Fact]
    public void AFadingTempentIsFreedWhenTheRampReachesZero()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        var life = Immortal(die: 10f);
        life.Flags = (int)TempEntFlags.FadeOut;
        life.FadeSpeed = 0.5f;
        life.RenderAmt = 200;

        int slot = Spawn(pool, native, life);

        // Two seconds past: 200 * (1 + -2 * 0.5) = 0.
        clock.Now = 12f;
        pool.Update(0.1f);

        Assert.True(native.Destroyed.Contains(slot), "the ramp reached zero and it went");
        Assert.True(pool.Count == 0, "and the list is empty");
    }

    [Fact]
    public void ATempentWithNoFadeoutFlagJustStopsExisting()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        int slot = Spawn(pool, native, Immortal(die: 10f));

        clock.Now = 10.001f;
        pool.Update(0.1f);

        Assert.True(native.Destroyed.Contains(slot),
            "without FTENT_FADEOUT there is no ramp — it is gone the frame its die passes");
    }

    [Fact]
    public void ANeverDieTempentOutlivesItsDieTimeAndGoesWhenDieIsCleared()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        var life = Immortal(die: 1f);
        life.Flags = (int)TempEntFlags.NeverDie;

        int slot = Spawn(pool, native, life);

        clock.Now = 1000f;
        pool.Update(0.1f);

        Assert.True(pool.Count == 1,
            "FTENT_NEVERDIE ignores the ordinary expiry entirely — stock answers `die != 0`");

        // The one way to kill one, and the reason KillAttachedTents sets die
        // rather than clearing it: a player attachment is FTENT_PERSIST, not
        // FTENT_NEVERDIE.
        life.Die = 0f;
        native.Bind(slot, life);

        pool.Update(0.1f);

        Assert.True(native.Destroyed.Contains(slot), "and dies when its die is CLEARED");
    }

    [Fact]
    public void ATempentWithTimeLeftCostsOneReadAndNoWriteAtAll()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        Spawn(pool, native, Immortal(die: 100f));

        clock.Now = 1f;
        pool.Update(0.1f);

        Assert.True(native.Read.Count == 1,
            $"one read for the one tempent, not two ({native.Read.Count})");

        // THE ORDINARY CASE IS EVERY LIVE TEMPENT, EVERY FRAME. At 500 of them
        // a write-back that changed nothing would be 30,000 crossings a second
        // to store the values that were just read.
        Assert.True(native.Written.Count == 0,
            "and no write at all, because nothing about it changed");
    }

    // ---------------------------------------------------------------------
    // The update loop.
    // ---------------------------------------------------------------------

    [Fact]
    public void ATempentThatAsksToDieDuringItsPhysicsStepIsFreed()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        int slot = Spawn(pool, native, Immortal(die: 100f));
        native.AsksToDie.Add(slot);

        clock.Now = 1f;
        pool.Update(0.1f);

        // Frame() answering false is an animating sprite that ran off its last
        // frame -- stock's `!current->Frame(...)` arm, which is a different
        // death from expiry and would be invisible to a test that only aged the
        // clock.
        Assert.True(native.Destroyed.Contains(slot), "Frame() said no and it went");
    }

    [Fact]
    public void ATempentTheLeafSystemRefusesIsDroppedUnlessItPersists()
    {
        var (pool, native, clock) = Rig(capacity: 3);

        int ordinary = Spawn(pool, native, Immortal(die: 100f));

        var persistent = Immortal(die: 100f);
        persistent.Flags = (int)TempEntFlags.Persist;
        int kept = Spawn(pool, native, persistent);

        native.Invisible.Add(ordinary);
        native.Invisible.Add(kept);

        clock.Now = 1f;
        pool.Update(0.1f);

        Assert.True(native.Destroyed.Contains(ordinary),
            "stock: if we can't draw it this frame, just dump it");
        Assert.True(!native.Destroyed.Contains(kept),
            "unless FTENT_PERSIST, which is what a player attachment carries");
        Assert.True(pool.Count == 1, "one of the two survived");
    }

    /// <summary>
    /// The not-visible decision reads the flags the STEP produced, not the ones
    /// the tempent had before it moved.
    ///
    /// <para>
    /// It used to be spelled as a COUNT — "two ABI reads on this path" — because
    /// Frame was native and mutated the object behind the pool's back, so a
    /// second crossing was the only way to see what it had done. Frame is C#
    /// now and writes into the record the pool handed it, so the count is one
    /// and the property is the same. Written as the VALUE rather than the count
    /// deliberately: a count can be satisfied by a read that ignores its answer,
    /// and this cannot.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePersistFlagComesFromAfterTheStepAndNotFromBeforeIt()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        // It goes IN without FTENT_PERSIST and comes OUT with it, which is the
        // direction that can only pass if the decision is made afterwards.
        int gained = Spawn(pool, native, Immortal(die: 100f));
        native.FlagsAfterStep[gained] = (int)TempEntFlags.Persist;
        native.Invisible.Add(gained);

        // And the other way round, so the test cannot pass by ignoring the
        // flags altogether: in WITH the flag, out without it.
        var persistent = Immortal(die: 100f);
        persistent.Flags = (int)TempEntFlags.Persist;
        int lost = Spawn(pool, native, persistent);
        native.FlagsAfterStep[lost] = (int)TempEntFlags.None;
        native.Invisible.Add(lost);

        clock.Now = 1f;
        pool.Update(0.1f);

        Assert.True(!native.Destroyed.Contains(gained),
            "a tempent that GAINED FTENT_PERSIST during its own step is kept, which is only "
          + "true if the flags are read after the step");
        Assert.True(native.Destroyed.Contains(lost),
            "and one that LOST it during its step is dumped, which is what stops this passing "
          + "by never looking at the flags at all");
    }

    [Fact]
    public void APausedFrameOnlyPutsEverythingBackInTheLeafSystem()
    {
        var (pool, native, clock) = Rig(capacity: 3);

        int a = Spawn(pool, native, Immortal(die: 1f));
        int b = Spawn(pool, native, Immortal(die: 1f));

        // Both are long expired, and a paused client must not notice.
        clock.Now = 1000f;
        pool.Update(0f);

        Assert.True(native.MadeVisible.Count == 2 && native.MadeVisible.Contains(a)
                 && native.MadeVisible.Contains(b),
            "stock's paused branch adds every tempent to the leaf system, or they stop being drawn");
        Assert.True(native.Simulated.Count == 0, "and simulates nothing");
        Assert.True(native.Destroyed.Count == 0, "and frees nothing, however long it has been paused");
        Assert.True(pool.Count == 2, "so the list is unchanged");
    }

    [Fact]
    public void AnEmptyPoolDoesNotAdvanceTheFrameCounter()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        clock.Now = 1f;

        // Stock steps gTempEntFrame AFTER the "no tempents or not in game"
        // early-out, so ten idle frames must not consume ten of the thirty-two
        // FTENT_FLICKER phases.
        for (int i = 0; i < 10; i++)
            pool.Update(0.1f);

        Spawn(pool, native, Immortal(die: 100f));
        pool.Update(0.1f);

        Assert.True(native.Simulated.Count == 1 && native.Simulated[0].Frame == 1,
            $"the first frame the pool actually ran is frame 1, not 11 "
          + $"(got {native.Simulated.FirstOrDefault().Frame})");
    }

    [Fact]
    public void TheFrameCounterWrapsAtThirtyTwo()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        clock.Now = 1f;
        Spawn(pool, native, Immortal(die: 10000f));

        for (int i = 0; i < 33; i++)
            pool.Update(0.1f);

        Assert.True(native.Simulated[31].Frame == 0,
            $"(gTempEntFrame + 1) & 31 wraps to 0 on the thirty-second ({native.Simulated[31].Frame})");
        Assert.True(native.Simulated[32].Frame == 1, "and carries on from there");
    }

    [Fact]
    public void ASlotWhoseObjectHasGoneIsDroppedFromTheListRatherThanBeingAFault()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        int slot = Spawn(pool, native, Immortal(die: 100f));

        // The state a reservation the memory pool could not fill leaves behind,
        // and the state a level torn down under a live list leaves behind.
        native.Unbind(slot);

        clock.Now = 1f;
        pool.Update(0.1f);

        Assert.True(pool.Count == 0, "the pool noticed and let it go");
        Assert.True(!native.Simulated.Any(s => s.Slot == slot),
            "and did not try to simulate an object that is not there");

        Assert.True(Spawn(pool, native, Immortal()) == slot, "and the slot is reusable");
    }

    // ---------------------------------------------------------------------
    // Re-entering the loop.
    //
    // Frame() runs inside the update loop, Frame() can DispatchEffect on a
    // collision, and an effect can allocate a tempent. It is not reachable in
    // THIS build -- m_pszImpactEffect is only set by ClientProjectile and both
    // of its live callers pass six arguments -- but the loop is walking the
    // list it would be modifying, and plan Part 5 names a pool that half works
    // as the worst outcome of the phase. These two are about the list surviving
    // it, which is not something a live join could ever show.
    // ---------------------------------------------------------------------

    [Fact]
    public void ATempentAllocatedFromInsideAStepJoinsTheListWithoutDisturbingIt()
    {
        var (pool, native, clock) = Rig(capacity: 4);

        int first = Spawn(pool, native, Immortal(die: 100f));
        int second = Spawn(pool, native, Immortal(die: 100f));

        int spawned = -1;
        native.DuringSimulate = _ =>
        {
            spawned = pool.Reserve(TempEntPriority.Low);
            if (spawned >= 0)
                native.Bind(spawned, Immortal(die: 100f));
        };

        clock.Now = 1f;
        pool.Update(0.1f);

        Assert.True(spawned >= 0, "the reservation succeeded from inside the step");
        Assert.True(pool.Count == 3, $"all three are live ({pool.Count})");
        Assert.True(native.Destroyed.Count == 0, "and nothing was freed");

        // NOT STEPPED ON ITS SPAWN FRAME, which is stock's behaviour and for
        // the same structural reason: its linked-list walk captured `next`
        // before running the body, so an AddToTail during a step was not
        // reached until the following frame.
        Assert.True(!native.Simulated.Any(x => x.Slot == spawned),
            "the newcomer was not simulated on the frame it spawned");

        // And the list is still a list: the next frame steps all three, in
        // allocation order, exactly once each.
        native.Simulated.Clear();
        pool.Update(0.1f);

        Assert.True(native.Simulated.Select(x => x.Slot).SequenceEqual(new[] { first, second, spawned }),
            "the next frame steps all three in allocation order: "
          + string.Join(",", native.Simulated.Select(x => x.Slot)));
    }

    [Fact]
    public void AnEvictionFromInsideAStepDoesNotSkipOrDoubleStepAnything()
    {
        var (pool, native, clock) = Rig(capacity: 3);

        int a = Spawn(pool, native, Immortal(die: 100f));
        int b = Spawn(pool, native, Immortal(die: 100f));
        int c = Spawn(pool, native, Immortal(die: 100f));

        // The pool is full, so a high-priority reservation from inside the loop
        // has to give up the oldest low-priority tempent -- which is the one
        // the loop is standing on.
        int spawned = -1;
        native.DuringSimulate = _ =>
        {
            spawned = pool.Reserve(TempEntPriority.High);
            if (spawned >= 0)
                native.Bind(spawned, Immortal(die: 100f, priority: TempEntPriority.High));
        };

        clock.Now = 1f;
        pool.Update(0.1f);

        Assert.True(spawned >= 0, "the high-priority reservation got a slot");

        // THE OLDEST LOW-PRIORITY ONE *THAT IS NOT THE ONE BEING STEPPED*. `a`
        // is older and is low priority, and it is skipped because the loop is
        // standing on it: freeing it would hand its slot straight back to this
        // reservation, so the step would finish on a different tempent and then
        // file the slot as a survivor while the reservation had already put it
        // in the list too. One slot, twice, freed twice.
        Assert.True(native.Destroyed.Count == 1 && native.Destroyed[0] == b,
            $"the oldest EVICTABLE low-priority one went ({string.Join(",", native.Destroyed)})");
        Assert.True(!native.Destroyed.Contains(a),
            "and the one the loop was standing on was not taken");

        // THE ASSERTION THAT MATTERS: no slot was visited twice and none was
        // skipped. An eviction that had done _active.RemoveAt under the index
        // walk would have shifted b down into the slot the loop had already
        // passed, so c would never be stepped at all -- and it would look like
        // one gib that stopped animating, once, in a firefight.
        var stepped = native.Simulated.Select(x => x.Slot).ToList();

        Assert.True(stepped.Count == stepped.Distinct().Count(),
            "nothing was stepped twice: " + string.Join(",", stepped));
        Assert.True(stepped.Contains(a) && stepped.Contains(c),
            "and nothing outside the eviction was skipped: " + string.Join(",", stepped));
        Assert.True(!stepped.Contains(spawned),
            "the tempent that took the evicted slot was not stepped on its spawn frame");
        Assert.True(pool.Count == 3, $"three live, the evicted one replaced ({pool.Count})");

        // AND THE LIST HAS NO DUPLICATE IN IT, which is the corruption this
        // whole guard exists to prevent: a slot in the list twice is freed
        // twice, and the second free is a C_LocalTempEntity handed back to the
        // memory pool while something else is using it.
        native.Simulated.Clear();
        pool.Update(0.1f);

        var next = native.Simulated.Select(x => x.Slot).ToList();

        Assert.True(next.Count == next.Distinct().Count(),
            "no slot is in the active list twice: " + string.Join(",", next));
        Assert.True(next.Count == 3, $"and all three are still there ({next.Count})");
    }

    // ---------------------------------------------------------------------
    // Clear.
    // ---------------------------------------------------------------------

    [Fact]
    public void ClearFreesEveryLiveTempentThroughTheFullTeardown()
    {
        var (pool, native, _) = Rig(capacity: 3);

        int a = Spawn(pool, native, Immortal());
        int b = Spawn(pool, native, Immortal());

        pool.Clear();

        // THROUGH TempEntPoolDestroy, which is RemoveFromLeafSystem +
        // OnRemoveTempEntity + the pool free. Stock's Clear() skipped the first
        // two and freed the memory straight out of the pool.
        Assert.True(native.Destroyed.Contains(a) && native.Destroyed.Contains(b),
            "both went through the teardown, not just out of the list");
        Assert.True(pool.Count == 0, "and the list is empty");

        Assert.True(Spawn(pool, native, Immortal()) >= 0, "and the slots came back");
    }

    // ---------------------------------------------------------------------
    // KillAttachedTents -- CTEKillPlayerAttachments' whole effect.
    // ---------------------------------------------------------------------

    [Fact]
    public void KillAttachedTentsSetsDieOnTheAttachmentsOfThatPlayerOnly()
    {
        var (pool, native, clock) = Rig(capacity: 4);

        var mine = Immortal(die: 500f);
        mine.Flags = (int)TempEntFlags.PlyrAttachment;
        mine.ClientIndex = 3;
        int slotMine = Spawn(pool, native, mine);

        var theirs = Immortal(die: 500f);
        theirs.Flags = (int)TempEntFlags.PlyrAttachment;
        theirs.ClientIndex = 4;
        int slotTheirs = Spawn(pool, native, theirs);

        var unattached = Immortal(die: 500f);
        unattached.ClientIndex = 3;
        int slotLoose = Spawn(pool, native, unattached);

        clock.Now = 9f;
        pool.KillAttachedTents(3);

        Assert.True(native.LifeOf(slotMine).Die == 9f,
            "player 3's attachment dies now — good enough, it will die on the next tent update");
        Assert.True(native.LifeOf(slotTheirs).Die == 500f,
            "player 4's is untouched");

        // THE FLAG AND THE INDEX ARE BOTH REQUIRED. A pool matching on the
        // index alone would kill every FTENT_COLLIDEALL tempent whose owner is
        // that player -- clientIndex carries two meanings, and stock's own
        // comment says so.
        Assert.True(native.LifeOf(slotLoose).Die == 500f,
            "and a tempent with the same clientIndex but no FTENT_PLYRATTACHMENT is not an "
          + "attachment at all — clientIndex is the IGNORE subject for a colliding one");
    }

    [Fact]
    public void KillAttachedTentsRefusesAPlayerIndexOfZeroOrLess()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        var attached = Immortal(die: 500f);
        attached.Flags = (int)TempEntFlags.PlyrAttachment;
        attached.ClientIndex = 0;
        int slot = Spawn(pool, native, attached);

        clock.Now = 9f;
        pool.KillAttachedTents(0);

        Assert.True(native.LifeOf(slot).Die == 500f,
            "stock refuses `client <= 0` before it walks anything — entity 0 is the world");
        Assert.True(native.Written.Count == 0, "and writes nothing");
    }

    [Fact]
    public void KillAttachedTentsDoesNotFreeAnythingItself()
    {
        var (pool, native, clock) = Rig(capacity: 2);

        var attached = Immortal(die: 500f);
        attached.Flags = (int)TempEntFlags.PlyrAttachment;
        attached.ClientIndex = 1;
        Spawn(pool, native, attached);

        clock.Now = 9f;
        pool.KillAttachedTents(1);

        // Stock's comment is "good enough, it will die on next tent update".
        // A version that freed immediately would drop a tempent mid-frame, out
        // of the update loop that owns the list.
        Assert.True(native.Destroyed.Count == 0, "nothing is freed here");
        Assert.True(pool.Count == 1, "the tempent is still live until the next update");

        // AND IT SURVIVES THE UPDATE AT EXACTLY THAT INSTANT, which is stock's
        // arithmetic and not an off-by-one: IsActive tests `die - curtime < 0`,
        // so a tempent whose die IS the current time is still alive. In the
        // game that lasts one frame; here it has to be written down, because a
        // pool that used <= would kill it half a frame early and no counting
        // test would see the difference.
        pool.Update(0.1f);

        Assert.True(pool.Count == 1, "die == curtime is not yet expired");

        clock.Now = 9.1f;
        pool.Update(0.1f);

        Assert.True(pool.Count == 0, "and it goes on the next update after that");
    }

    // ---------------------------------------------------------------------
    // The class this lane unblocked, over the pool it now drives.
    // ---------------------------------------------------------------------

    /// <summary>
    /// <c>CTEKillPlayerAttachments</c>'s player asks the pool, and asks it for
    /// the player the wire named.
    ///
    /// <para>
    /// A SEPARATE ASSERTION FROM THE POOL'S OWN, and not a duplicate of it: the
    /// pool tests above prove the RULE, and this proves the CONNECTION —
    /// registered, decoded, and the five-bit field carried into the call rather
    /// than a zero. Those are the two halves the console cannot tell apart, and
    /// gate 3 says so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheKillPlayerAttachmentsPlayerDrivesThePoolWithTheWiresPlayer()
    {
        var (pool, native, clock) = Rig(capacity: 4);

        var mine = Immortal(die: 500f);
        mine.Flags = (int)TempEntFlags.PlyrAttachment;
        mine.ClientIndex = 7;
        int slotMine = Spawn(pool, native, mine);

        var theirs = Immortal(die: 500f);
        theirs.Flags = (int)TempEntFlags.PlyrAttachment;
        theirs.ClientIndex = 8;
        int slotTheirs = Spawn(pool, native, theirs);

        clock.Now = 3f;

        var player = new ClientTeKillPlayerAttachments(pool);

        // The blob DT_TEKillPlayerAttachments declares, written by the shared
        // declaration and read back through it -- so a field name that stopped
        // matching would read a default rather than 7.
        var registry = new TempEntRegistry(new RecordingLog());
        int registered = registry.RegisterAll("Client", TestServices.ClientGame,
                                              TestServices.Fake().Returns("Effects.TempEntRegister", 1));

        Assert.True(registered > 0, "the shared temp entity declarations registered");

        TempEntType type = registry.All.Single(t => t.Class.ClassName == "CTEKillPlayerAttachments");

        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        Assert.True(writer.Int("Player", 7),
            "Player -- stock's m_nPlayer -- is the field DT_TEKillPlayerAttachments declares");

        // THROUGH THE INTERFACE, which is the entry the registry calls and the
        // only one that takes a reader. Play(TeKillPlayerAttachments) is the
        // typed override; going in by the untyped door is what makes this a test
        // of the decode as well as of the effect.
        ((client::SourceSharp.Client.TempEnts.IClientTempEnt)player).Play(new TempEntReader(type, blob));

        Assert.True(native.LifeOf(slotMine).Die == 3f,
            "player 7's attachment was told to die now");
        Assert.True(native.LifeOf(slotTheirs).Die == 500f,
            "and player 8's was not — the wire's player reached the call, not a default");
    }

    // ---------------------------------------------------------------------
    // The two that read the C++ off disk.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Every <see cref="TempEntFlags"/> bit is <c>tempent.h</c>'s bit.
    ///
    /// <para>
    /// THE ONE THING NOTHING ELSE HERE COULD CATCH. Every other test in this
    /// file uses <see cref="TempEntFlags"/> on both sides of the comparison, so
    /// a bit whose value is wrong would agree with itself perfectly — the pool
    /// would look for <c>FTENT_PERSIST</c> at the bit <c>FTENT_COLLIDEKILL</c>
    /// actually occupies and every assertion above would still pass, while a
    /// player's spray was thrown away the first frame it left the PVS.
    /// </para>
    ///
    /// <para>
    /// The header is read and the numbers compared, NOT the count: a lane that
    /// added a flag to <c>tempent.h</c> and not here is a different (and much
    /// milder) problem from one that got a value wrong, and folding the two
    /// would make this fail for the wrong reason. Both are asserted, separately.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFlagBitsAreTempentHs()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine("game", "client", "tempent.h"));

        if (header is null)
        {
            Assert.True(true, "tempent.h found (skipped, no source tree beside the test binary)");
            return;
        }

        var native = new Dictionary<string, uint>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(File.ReadAllText(header),
                     @"#define\s+FTENT_([A-Z0-9_]+)\s+(0x[0-9A-Fa-f]+)"))
        {
            native[m.Groups[1].Value] = Convert.ToUInt32(m.Groups[2].Value, 16);
        }

        // FTENT_NONE is the empty set and not a bit, so it is checked here and
        // then taken out of both sides -- leaving it in would compare "no flags"
        // against a name in a [Flags] enum where zero is a value rather than a
        // member.
        Assert.True(native.TryGetValue("NONE", out uint none) && none == 0,
            "FTENT_NONE is zero");
        Assert.True((uint)TempEntFlags.None == 0, "and so is TempEntFlags.None");
        native.Remove("NONE");

        Assert.True(native.Count >= 30,
            $"tempent.h declares the FTENT_ set ({native.Count} found) — a parse that found "
          + "nothing would make every comparison below vacuous");

        // FTENT_SPRANIMATELOOP -> SpriteAnimateLoop: the header is
        // ALLCAPSRUNTOGETHER and the enum is PascalCase, so the names are
        // compared with the case and the underscores taken out of both.
        static string Squash(string s) => s.Replace("_", string.Empty).ToUpperInvariant();

        // OVER THE DECLARED FIELDS, not Enum.GetValues. Two enum members with
        // the SAME value collapse to one there and ToString() answers whichever
        // the runtime picked, so a mutation that gave Persist CollideKill's bit
        // made this throw a dictionary-key exception instead of naming the two
        // bits that disagreed -- a red for the wrong reason, which is not
        // evidence. Found by mutating it.
        var managed = new Dictionary<string, uint>(StringComparer.Ordinal);
        var byBit = new Dictionary<uint, string>();

        foreach (var field in typeof(TempEntFlags).GetFields(
                     System.Reflection.BindingFlags.Public
                   | System.Reflection.BindingFlags.Static))
        {
            var value = (uint)(TempEntFlags)field.GetValue(null)!;

            if (value == 0)
                continue;

            managed[Squash(field.Name)] = value;

            Assert.True(!byBit.ContainsKey(value),
                $"TempEntFlags.{field.Name} and TempEntFlags."
              + $"{(byBit.TryGetValue(value, out var other) ? other : "?")} are both "
              + $"0x{value:X8}. Two names for one bit is one of them being wrong, and the "
              + "pool would be testing the other flag's meaning");

            byBit[value] = field.Name;
        }

        foreach (var (name, bit) in native)
        {
            string key = Squash(name);

            Assert.True(managed.ContainsKey(key),
                $"tempent.h's FTENT_{name} has a managed name — a flag native sets and C# cannot "
              + "spell is a literal waiting to be written in a spawn helper");

            Assert.True(managed[key] == bit,
                $"FTENT_{name} is 0x{bit:X8} in tempent.h and 0x{managed[key]:X8} in "
              + "TempEntFlags — the pool would be reading a different bit from the one native sets");
        }

        Assert.True(managed.Count == native.Count,
            $"the two sets are the same size (tempent.h {native.Count}, TempEntFlags "
          + $"{managed.Count}) — a managed name with no header bit is a flag that means nothing");
    }

    /// <summary>
    /// <see cref="TempEntLife"/> still has <c>SourceSharpTempEntLife_t</c>'s
    /// shape.
    ///
    /// <para>
    /// The same hazard <c>AbiParityTests.TheEffectDataStructMatchesTheHeader</c>
    /// exists for, and worse here because EVERY field is one word: a field
    /// inserted on one side only shifts all the ones after it and both halves
    /// still compile. The symptom would be a tempent expiring against its own
    /// fade speed and fading at the rate of its priority.
    /// </para>
    /// </summary>
    [Fact]
    public void TheLifeStructMatchesTheHeader()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine("sourcesharp", "host", "sourcesharp.h"));

        if (header is null)
        {
            Assert.True(true, "sourcesharp.h found (skipped, no source tree beside the test binary)");
            return;
        }

        string text = File.ReadAllText(header);

        Match body = Regex.Match(text,
            @"typedef struct\s*\{(?<body>[^}]*)\}\s*SourceSharpTempEntLife_t\s*;");

        Assert.True(body.Success, "sourcesharp.h declares SourceSharpTempEntLife_t");

        var nativeFields = Regex.Matches(body.Groups["body"].Value,
                                   @"^\s*(?<type>float|int32_t)\s+(?<name>\w+)\s*;",
                                   RegexOptions.Multiline)
                                .Select(m => (Name: m.Groups["name"].Value,
                                              Type: m.Groups["type"].Value))
                                .ToList();

        var managedFields = typeof(TempEntLife)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .OrderBy(f => f.MetadataToken)
            .Select(f => (Name: f.Name,
                          Type: f.FieldType == typeof(float) ? "float" : "int32_t"))
            .ToList();

        Assert.True(nativeFields.Count == managedFields.Count,
            $"field counts match (native {nativeFields.Count}, managed {managedFields.Count})");

        for (int i = 0; i < Math.Min(nativeFields.Count, managedFields.Count); i++)
        {
            Assert.True(nativeFields[i].Name == managedFields[i].Name,
                $"field {i} is the same field (native '{nativeFields[i].Name}', "
              + $"managed '{managedFields[i].Name}')");

            // THE TYPE AND NOT ONLY THE NAME. Every field here is four bytes,
            // so a float read as an int is the one drift a width check cannot
            // see: `die` read as an integer is a bit pattern in the thousands
            // of millions, and a tempent that never expires.
            Assert.True(nativeFields[i].Type == managedFields[i].Type,
                $"field '{nativeFields[i].Name}' is {nativeFields[i].Type} on both sides "
              + $"(managed says {managedFields[i].Type})");
        }

        Assert.True(System.Runtime.CompilerServices.Unsafe.SizeOf<TempEntLife>()
                    == managedFields.Count * 4,
            $"the managed struct is {managedFields.Count * 4} bytes, with no padding the header "
          + "does not have");
    }
}
