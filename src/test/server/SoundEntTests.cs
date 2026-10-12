using System;

using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>CSoundEnt</c>'s list, in C# — plan_gamedll_after.md Phase A3, second pair.
///
/// <para>
/// What is worth pinning is not that a sound goes in, but the four rules a port
/// can lose without anything saying so: the free list and the active list are
/// ONE array threaded two ways, so a slot leaked from either is a pool that
/// shrinks until it stops answering; the per-client reserve never expires; a
/// channel that is already speaking for an owner is UPDATED rather than added
/// to; and a sound whose owner has gone is thrown away even if its time has not
/// come.
/// </para>
/// </summary>
public class SoundEntTests
{
    /// <summary>A clock a test can move. Time is the subject of half of these.</summary>
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>
    /// The list under test, with a strict engine that is allowed to be told
    /// things and nothing else.
    ///
    /// <para>
    /// <c>Console.Msg</c> and <c>Console.Warning</c> are void, so they are
    /// recorded rather than stubbed; <c>Console.FindConVarValue</c> is a READ
    /// and is named here because these tests are not about
    /// <c>displaysoundlist</c>. Written down rather than defaulted quietly, per
    /// docs/writing-tests.md.
    /// </para>
    /// </summary>
    private static SoundEnt New(out Clock clock, out RecordingEngine engine)
    {
        clock = new Clock();
        engine = Fake();
        engine.Ignores("Console.FindConVarValue");
        return new SoundEnt(engine, clock, engine);
    }

    private static SoundEnt Up(int maxClients, out Clock clock)
    {
        var list = New(out clock, out _);
        list.Initialize(maxClients, clock.Now);
        return list;
    }

    private static void Sweep(SoundEnt list, float frameTime = 0.015f)
        => ((IFrameLifecycle)list).OnPreThink(frameTime);

    private static int CountActive(SoundEnt list)
    {
        Span<HeardSound> heard = stackalloc HeardSound[SoundEnt.MaxWorldSoundsMp];
        return list.Query((SoundType)(-1), heard);
    }

    // ---- the pool ----------------------------------------------------------

    [Fact]
    public void ABareListIsDownAndAnswersNothing()
    {
        var list = New(out _, out _);

        Assert.True(!list.IsUp, "a list nobody has initialised is down");
        Assert.True(list.ActiveList == SoundEnt.ListEmpty, "a list that is down has no active head");
        Assert.True(list.FreeList == SoundEnt.ListEmpty, "a list that is down has no free head");
    }

    [Fact]
    public void ASinglePlayerPoolIsSixtyFourSlots()
    {
        var list = Up(1, out _);

        // maxClients 1 reserves one, so the free list is the pool minus that.
        Assert.True(list.FreeCount == SoundEnt.MaxWorldSoundsSp - 1,
            "a single-player pool is 64 slots, one of them reserved for the one client");
    }

    [Fact]
    public void AMultiplayerPoolIsMaxClientsPlusThirtyTwo()
    {
        var list = Up(16, out _);

        Assert.True(list.FreeCount + list.ActiveCount == 16 + 32,
            "a 16-player pool is maxClients + 32 slots");
    }

    [Fact]
    public void TheMultiplayerPoolIsCappedAtTheArray()
    {
        // 128 + 32 would be 160, and the array is 128.
        var list = Up(128, out _);

        Assert.True(list.FreeCount + list.ActiveCount == SoundEnt.MaxWorldSoundsMp,
            "the pool never exceeds MAX_WORLD_SOUNDS_MP however many clients there are");
    }

    [Fact]
    public void EveryClientGetsAReservedSlot()
    {
        var list = Up(16, out _);

        Assert.True(list.ActiveCount == 16,
            "initialising reserves one active slot per client and no more");
    }

    [Fact]
    public void ACrowdedServerIsWarnedAboutRatherThanLeftToRunOut()
    {
        var list = New(out var clock, out var engine);
        list.Initialize(120, clock.Now);

        // 120 + 16 > 128, which is stock's own test.
        Assert.True(engine.CountOf("Console.Warning") == 1,
            "a client count that leaves under sixteen spare slots is warned about once");
    }

    [Fact]
    public void AComfortableServerIsNotWarned()
    {
        var list = New(out var clock, out var engine);
        list.Initialize(16, clock.Now);

        Assert.True(engine.CountOf("Console.Warning") == 0,
            "a 16-player server has room and is told nothing");
    }

    // ---- insert ------------------------------------------------------------

    [Fact]
    public void AnInsertOnADownListGoesNowhere()
    {
        var list = New(out _, out _);

        list.Insert((int)SoundType.Combat, new Vector(1, 2, 3), 400, 1.0f);

        Assert.True(CountActive(list) == 0, "a noise made before the level is up is not recorded");
    }

    [Fact]
    public void AnInsertedSoundIsReadBackWithItsPlaceLoudnessAndKind()
    {
        var list = Up(0, out _);

        list.Insert((int)SoundType.Combat, new Vector(10, 20, 30), 512, 1.0f);

        Span<HeardSound> heard = stackalloc HeardSound[8];
        int n = list.Query(SoundType.Combat, heard);

        Assert.True(n == 1, "one noise was made, so one comes back");
        Assert.True(heard[0].Volume == 512, "the loudness is the one the caller gave");
        Assert.True(heard[0].Origin.X == 10 && heard[0].Origin.Y == 20 && heard[0].Origin.Z == 30,
            "the place is the one the caller gave");
        Assert.True(heard[0].Type == SoundType.Combat, "the kind is the one the caller gave");
    }

    [Fact]
    public void AQueryKeepsOnlyTheKindsItsMaskNames()
    {
        var list = Up(0, out _);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 1.0f);
        list.Insert((int)SoundType.World, Vector.Zero, 100, 1.0f);

        Span<HeardSound> heard = stackalloc HeardSound[8];

        Assert.True(list.Query(SoundType.Combat, heard) == 1, "a Combat mask keeps only the combat noise");
        Assert.True(list.Query(SoundType.World, heard) == 1, "a World mask keeps only the world noise");
        Assert.True(list.Query(SoundType.Combat | SoundType.World, heard) == 2, "a mask of both keeps both");
        Assert.True(list.Query(SoundType.None, heard) == 0, "an empty mask keeps nothing");
    }

    [Fact]
    public void AQueryStopsAtTheCallersBuffer()
    {
        var list = Up(0, out _);

        for (int i = 0; i < 8; i++)
            list.Insert((int)SoundType.Combat, Vector.Zero, 100, 1.0f);

        Span<HeardSound> heard = stackalloc HeardSound[3];

        Assert.True(list.Query(SoundType.Combat, heard) == 3,
            "a caller with room for three is given three and not asked to grow");
    }

    [Fact]
    public void AFullPoolRefusesRatherThanOverwriting()
    {
        var list = Up(0, out _);

        int slots = list.FreeCount;

        for (int i = 0; i < slots + 10; i++)
            list.Insert((int)SoundType.Combat, Vector.Zero, 100, 10.0f);

        Assert.True(CountActive(list) == slots,
            "a pool with 64 slots records 64 noises and refuses the rest rather than reusing a live one");
    }

    // ---- channels ----------------------------------------------------------

    [Fact]
    public void AChannellessInsertAllocatesEveryTime()
    {
        var list = Up(0, out _);
        var owner = new EntityHandle(0x0001_0001u);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 1.0f, owner);
        list.Insert((int)SoundType.Combat, Vector.Zero, 200, 1.0f, owner);

        Assert.True(CountActive(list) == 2,
            "two noises on no channel are two entries even from one owner");
    }

    [Fact]
    public void AChannelUpdatesTheOwnersOwnEntryRatherThanAddingOne()
    {
        var list = Up(0, out _);
        var owner = new EntityHandle(0x0001_0001u);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 1.0f, owner, channel: 4);
        list.Insert((int)SoundType.Combat, Vector.Zero, 200, 1.0f, owner, channel: 4);

        Span<HeardSound> heard = stackalloc HeardSound[8];
        int n = list.Query(SoundType.Combat, heard);

        Assert.True(n == 1, "a repeated noise on one channel stays one entry, which is what stops it filling the pool");
        Assert.True(heard[0].Volume == 200, "and the entry carries the newest loudness, not the first");
    }

    [Fact]
    public void TwoOwnersOnTheSameChannelDoNotShareAnEntry()
    {
        var list = Up(0, out _);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 1.0f, new EntityHandle(0x0001_0001u), channel: 4);
        list.Insert((int)SoundType.Combat, Vector.Zero, 200, 1.0f, new EntityHandle(0x0001_0002u), channel: 4);

        Assert.True(CountActive(list) == 2,
            "the channel is the OWNER's, so two owners on channel 4 are two entries");
    }

    [Fact]
    public void OneOwnerOnTwoChannelsGetsTwoEntries()
    {
        var list = Up(0, out _);
        var owner = new EntityHandle(0x0001_0001u);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 1.0f, owner, channel: 4);
        list.Insert((int)SoundType.Combat, Vector.Zero, 200, 1.0f, owner, channel: 5);

        Assert.True(CountActive(list) == 2, "a weapon and an injury are different channels and different entries");
    }

    // ---- expiry ------------------------------------------------------------

    [Fact]
    public void TheFirstSweepIsAWholeSecondOut()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;
        list.Initialize(0, clock.Now);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 0.1f);

        // The sound expired at 100.1 and the sweep is not due until 101.
        clock.Now = 100.5f;
        Sweep(list);

        Assert.True(CountActive(list) == 1,
            "the first sweep is armed a second out, so a sound that expired before then is still listed");
    }

    [Fact]
    public void AnExpiredSoundIsSweptAway()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;
        list.Initialize(0, clock.Now);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 0.5f);

        clock.Now = 101.5f;
        Sweep(list);

        Assert.True(CountActive(list) == 0, "a noise whose duration has passed is gone from the list");
    }

    [Fact]
    public void ALiveSoundSurvivesTheSweep()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;
        list.Initialize(0, clock.Now);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 30.0f);

        clock.Now = 101.5f;
        Sweep(list);

        Assert.True(CountActive(list) == 1, "a noise still within its duration stays");
    }

    [Fact]
    public void ASweptSoundGoesBackOnTheFreeListRatherThanBeingLost()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;
        list.Initialize(0, clock.Now);

        int before = list.FreeCount;

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 0.5f);

        Assert.True(list.FreeCount == before - 1, "an allocated slot leaves the free list");

        clock.Now = 101.5f;
        Sweep(list);

        Assert.True(list.FreeCount == before,
            "and a swept slot comes back to it -- a pool that leaked would shrink until it stopped answering");
    }

    [Fact]
    public void TheSweepDoesNotRunMoreOftenThanItsInterval()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;
        list.Initialize(0, clock.Now);

        clock.Now = 101f;
        Sweep(list);                       // arms the next for 101.1

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 0.01f);

        clock.Now = 101.05f;               // the sound has expired; the sweep has not come round
        Sweep(list);

        Assert.True(CountActive(list) == 1,
            "the list is swept ten times a second, not sixty -- a frame between sweeps changes nothing");

        clock.Now = 101.2f;
        Sweep(list);

        Assert.True(CountActive(list) == 0, "and the next sweep takes it");
    }

    [Fact]
    public void TheMiddleOfTheActiveListCanBeSweptWithoutBreakingIt()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;
        list.Initialize(0, clock.Now);

        // Three noises, the MIDDLE one short-lived. The active list is a
        // singly-linked chain and unlinking an interior node is where a port
        // loses the tail.
        list.Insert((int)SoundType.Combat, Vector.Zero, 1, 30.0f);
        list.Insert((int)SoundType.Combat, Vector.Zero, 2, 0.5f);
        list.Insert((int)SoundType.Combat, Vector.Zero, 3, 30.0f);

        clock.Now = 101.5f;
        Sweep(list);

        Span<HeardSound> heard = stackalloc HeardSound[8];
        int n = list.Query(SoundType.Combat, heard);

        Assert.True(n == 2, "the two long noises survive and the short one does not");
        Assert.True(heard[0].Volume + heard[1].Volume == 1 + 3,
            "and the two that survive are the FIRST and the LAST -- an unlink that lost the tail would leave one");
    }

    [Fact]
    public void AReservedClientSlotNeverExpires()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;
        list.Initialize(4, clock.Now);

        clock.Now = 100000f;
        Sweep(list);

        Assert.True(list.ActiveCount == 4,
            "the four client-reserved slots have no expiry time and outlive any sweep");
    }

    // ---- owners ------------------------------------------------------------

    [Fact]
    public void ANoiseWithNoOwnerIsNotTreatedAsOneWhoseOwnerDied()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;
        list.Initialize(0, clock.Now);

        list.Insert((int)SoundType.World, Vector.Zero, 100, 30.0f);

        clock.Now = 101.5f;
        Sweep(list);

        Assert.True(CountActive(list) == 1,
            "a world noise belongs to nobody, which is not the same as belonging to something that has gone");
    }

    [Fact]
    public void AnOwnedNoiseIsDroppedWhenItsOwnerStopsResolving()
    {
        var clock = new Clock { Now = 100f };
        var engine = Fake();
        engine.Ignores("Console.FindConVarValue");
        engine.Returns("Entities.IsValid", false);

        var list = new SoundEnt(engine, clock, engine);
        list.Initialize(0, clock.Now);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 30.0f, new EntityHandle(0x0001_0001u));

        clock.Now = 101.5f;
        Sweep(list);

        Assert.True(CountActive(list) == 0,
            "a noise credited to something that no longer exists is thrown away even though its time has not come");
    }

    /// <summary>
    /// Stock's <c>m_bHasOwner</c> is <c>pOwner != NULL</c>, so an owner in
    /// entry 0 is an owner like any other: the raw handle 0 is not "nobody".
    /// </summary>
    [Fact]
    public void ANoiseOwnedByEntryZeroIsDroppedWhenItStopsResolving()
    {
        var clock = new Clock { Now = 100f };
        var engine = Fake();
        engine.Ignores("Console.FindConVarValue");
        engine.Returns("Entities.IsValid", false);

        var list = new SoundEnt(engine, clock, engine);
        list.Initialize(0, clock.Now);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 30.0f, new EntityHandle(0u));

        clock.Now = 101.5f;
        Sweep(list);

        Assert.True(CountActive(list) == 0, "an owner in entry zero is still an owner");
    }

    [Fact]
    public void AnOwnedNoiseSurvivesWhileItsOwnerDoes()
    {
        var clock = new Clock { Now = 100f };
        var engine = Fake();
        engine.Ignores("Console.FindConVarValue");
        engine.Returns("Entities.IsValid", true);

        var list = new SoundEnt(engine, clock, engine);
        list.Initialize(0, clock.Now);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 30.0f, new EntityHandle(0x0001_0001u));

        clock.Now = 101.5f;
        Sweep(list);

        Assert.True(CountActive(list) == 1, "a noise whose owner is still there stays");
    }

    [Fact]
    public void TheOwnerIsReadBackAsTheHandleItWasGiven()
    {
        var list = Up(0, out _);
        var owner = new EntityHandle(0x0042_0007u);

        list.Insert((int)SoundType.Combat, Vector.Zero, 100, 30.0f, owner);

        Span<HeardSound> heard = stackalloc HeardSound[4];
        list.Query(SoundType.Combat, heard);

        Assert.True(heard[0].Owner.Value == owner.Value,
            "the owner crosses as a handle and comes back as the same number -- a listen filter tests it");
    }

    // ---- the client reserve ------------------------------------------------

    [Fact]
    public void AClientsReservedIndexIsOneLessThanItsEntityIndex()
    {
        Assert.True(SoundEnt.ClientSoundIndex(1) == 0,
            "clients are numbered from one and their reserved slots from zero");
        Assert.True(SoundEnt.ClientSoundIndex(16) == 15, "and the mapping holds across the roster");
    }

    [Fact]
    public void ResettingAClientsSlotClearsItWithoutUnlistingIt()
    {
        var list = Up(4, out _);

        int before = list.ActiveCount;

        list.ResetClientSound(0);

        Assert.True(list.ActiveCount == before,
            "a leaving client's slot stays reserved -- Reset is not Free, and stock's comment says so");
    }

    [Fact]
    public void ResettingAnIndexOutsideThePoolIsSilentlyNothing()
    {
        var list = Up(4, out _);

        int before = list.ActiveCount;

        list.ResetClientSound(-1);
        list.ResetClientSound(SoundEnt.MaxWorldSoundsMp);
        list.ResetClientSound(9999);

        Assert.True(list.ActiveCount == before,
            "an index off either end is the nothing SoundPointerForIndex's NULL already meant");
    }

    // ---- shutdown ----------------------------------------------------------

    [Fact]
    public void ShutdownTakesTheListDown()
    {
        var list = Up(4, out _);

        list.Shutdown();

        Assert.True(!list.IsUp, "a shut list is down");
        Assert.True(CountActive(list) == 0, "and answers no query");
    }

    [Fact]
    public void ShutdownTwiceIsNotAnError()
    {
        var list = Up(4, out _);

        list.Shutdown();
        list.Shutdown();

        Assert.True(!list.IsUp,
            "two callers spell ShutdownSoundEnt on the way out of a level, so the second must be a no-op");
    }

    [Fact]
    public void AListCanBeBroughtBackUpForTheNextMap()
    {
        var list = Up(4, out var clock);

        list.Shutdown();
        list.Initialize(4, clock.Now);

        Assert.True(list.IsUp, "the next map brings it back");
        Assert.True(list.ActiveCount == 4, "with its client reserve rebuilt");
    }

    [Fact]
    public void ASweepOnADownListDoesNothingRatherThanThrowing()
    {
        var list = New(out var clock, out _);
        clock.Now = 100f;

        Sweep(list);

        Assert.True(!list.IsUp,
            "the frame runs from the moment the DLL loads, including before a level is up");
    }

    // ---- the loudest -------------------------------------------------------

    /// <summary>
    /// <b>The answer is an INDEX, and the two orders are both here on purpose.</b>
    /// Allocation pops the head of the free list, so the first insert of a fresh
    /// pool is slot 0 and the second is slot 1 — which means a query that simply
    /// returned "the first one" or "the last one" would satisfy exactly one of
    /// the two facts below. Only picking by distance satisfies both.
    /// </summary>
    [Fact]
    public void TheLoudestOfATypeIsTheNearestThatStillReaches_FarInsertedFirst()
    {
        var list = Up(0, out _);

        // Volume is how far a noise CARRIES, so a distant loud one and a near
        // quiet one are both candidates and the NEAR one wins.
        list.Insert((int)SoundType.Combat, new Vector(500, 0, 0), 4000, 30.0f);   // slot 0
        list.Insert((int)SoundType.Combat, new Vector(100, 0, 0), 200, 30.0f);    // slot 1

        Assert.True(list.LoudestOfType((int)SoundType.Combat, Vector.Zero) == 1,
            "the near quiet noise wins over the far loud one, which is slot 1 here");
    }

    /// <inheritdoc cref="TheLoudestOfATypeIsTheNearestThatStillReaches_FarInsertedFirst"/>
    [Fact]
    public void TheLoudestOfATypeIsTheNearestThatStillReaches_NearInsertedFirst()
    {
        var list = Up(0, out _);

        list.Insert((int)SoundType.Combat, new Vector(100, 0, 0), 200, 30.0f);    // slot 0
        list.Insert((int)SoundType.Combat, new Vector(500, 0, 0), 4000, 30.0f);   // slot 1

        Assert.True(list.LoudestOfType((int)SoundType.Combat, Vector.Zero) == 0,
            "the same noise wins with the inserts swapped, so the answer is distance and not position");
    }

    [Fact]
    public void ANoiseThatDoesNotCarryFarEnoughIsNotHeard()
    {
        var list = Up(0, out _);

        list.Insert((int)SoundType.Combat, new Vector(500, 0, 0), 100, 30.0f);

        Assert.True(list.LoudestOfType((int)SoundType.Combat, Vector.Zero) == SoundEnt.ListEmpty,
            "a noise 500 units away that carries 100 does not reach the ear");
    }

    [Fact]
    public void TheLoudestMatchesTheTypeEXACTLYRatherThanByMask()
    {
        var list = Up(0, out _);

        // Stock's own pSound->m_iType == iType. It reads as a bug beside
        // Query's mask and is not this port's to fix: a caller asking for
        // SOUND_COMBAT deliberately does not want the one that also carries
        // SOUND_CONTEXT_GUNFIRE.
        list.Insert((int)SoundType.Combat | (int)SoundType.ContextExplosion,
                    new Vector(10, 0, 0), 4000, 30.0f);

        Assert.True(list.LoudestOfType((int)SoundType.Combat, Vector.Zero) == SoundEnt.ListEmpty,
            "a combat noise carrying a context bit is not a plain combat noise to this query");

        Assert.True(
            list.LoudestOfType((int)SoundType.Combat | (int)SoundType.ContextExplosion, Vector.Zero)
                != SoundEnt.ListEmpty,
            "and asking for exactly what it is finds it");
    }

    // ---- the numbers this side shares with soundent.h ----------------------
    // SoundEntNumbersMatchTheHeader MOVED to the oracle suite
    // (SoundEntOracleTests): it compares these constants against stock's
    // soundent.h, which no binary compiles since plan_server_novalve.md P5 and
    // which left the farm in P6. The oracle reads the pristine clone.
}
