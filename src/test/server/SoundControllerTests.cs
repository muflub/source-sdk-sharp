using System;

using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Sound envelopes — <c>CSoundControllerImp</c>, in C#.
/// plan_gamedll_after.md Phase A3, second pair.
///
/// <para>
/// The RAMP is what moved and the ramp is what is pinned here. A volume or a
/// pitch approaching a target over a number of seconds has four rules a port can
/// lose while every call still compiles: the rate floor of 0.1 that makes a very
/// small ramp arrive early; the volume clamp on the TARGET and not on the
/// current value; the force-update flag that makes one more emit happen after a
/// ramp has arrived and is CONSUMED by asking; and <c>SND_CHANGE_VOL</c> on
/// every emit whatever changed, because the envelope multiplies the sound
/// script's volume and an emit without it uses the script's number raw.
/// </para>
/// </summary>
public class SoundControllerTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>
    /// The controller under test, with a clock a test can move and an engine
    /// that answers the frame's two questions the way a live patch does.
    /// </summary>
    private static SoundController New(out Clock clock, out RecordingEngine engine,
                                       int entIndex = 7, bool filterActive = true)
    {
        clock = new Clock();
        engine = Fake();

        // The two the frame pass reads of every live patch. Stubbed rather than
        // ignored, because a patch whose entity is gone LEAVES THE LIST, and a
        // test that left this to a default would be testing removal by
        // accident on every frame.
        engine.Returns("Sound.SoundPatchState.entIndex", entIndex);
        engine.Returns("Sound.SoundPatchState.filterActive", filterActive);

        return new SoundController(engine, clock, engine, engine);
    }

    /// <summary>A patch that exists and is playing, which is where most rules start.</summary>
    private static SoundController Playing(out Clock clock, out RecordingEngine engine,
                                           float volume = 1.0f, float pitch = 100f)
    {
        var c = New(out clock, out engine);
        c.Create(1);
        c.Play(1, volume, pitch, 0f);
        return c;
    }

    private static void Frame(SoundController c, Clock clock, float dt)
    {
        clock.Now += dt;
        ((IFrameLifecycle)c).OnPreClientUpdate(dt);
    }

    // ---- the ramp ----------------------------------------------------------

    [Fact]
    public void ABareEnvelopeIsAtZeroAndWantsNoUpdate()
    {
        var e = new SoundController.Envelope();

        Assert.True(e.Value == 0f, "a ramp starts at zero");
        Assert.True(!e.ShouldUpdate(), "and is not going anywhere");
    }

    [Fact]
    public void AZeroTimeTargetArrivesImmediately()
    {
        var e = new SoundController.Envelope();
        e.SetTarget(0.5f, 0f);

        Assert.True(e.Value == 0.5f, "a ramp with no time to run in is set outright");
    }

    [Fact]
    public void ARampMovesTowardsItsTargetAndStops()
    {
        var e = new SoundController.Envelope();

        // 0 -> 1 over one second is a rate of 1.
        e.SetTarget(1.0f, 1.0f);

        Assert.True(e.Value == 0f, "setting a target does not move the value");

        e.Update(0.25f);
        Assert.True(MathF.Abs(e.Value - 0.25f) < 0.0001f, "a quarter second covers a quarter of it");

        e.Update(0.25f);
        Assert.True(MathF.Abs(e.Value - 0.5f) < 0.0001f, "and the next quarter another quarter");

        e.Update(10f);
        Assert.True(e.Value == 1.0f, "a long step lands ON the target and does not overshoot");

        Assert.True(!e.ShouldUpdate(), "and a ramp that has arrived wants nothing more");
    }

    [Fact]
    public void ARampRunsDownwardsAtTheSameRate()
    {
        var e = new SoundController.Envelope();
        e.SetValue(1.0f);
        e.SetTarget(0f, 1.0f);

        e.Update(0.25f);

        Assert.True(MathF.Abs(e.Value - 0.75f) < 0.0001f,
            "a fade is the same arithmetic with the sign the other way");
    }

    [Fact]
    public void TheRateHasAFloorOfATenthAndAVerySmallRampArrivesEarly()
    {
        var e = new SoundController.Envelope();

        // 0.01 over ten seconds asks for 0.001 a second; the floor moves it at
        // 0.1, so it arrives in a tenth of a second rather than in ten.
        e.SetTarget(0.01f, 10.0f);
        e.Update(0.1f);

        Assert.True(e.Value == 0.01f,
            "stock's rate floor of 0.1 makes a very small ramp arrive early, and a "
            + "mod tuned against stock's timing is tuned against that");
    }

    [Fact]
    public void ArrivingForcesOneMoreUpdateAndThenStops()
    {
        var e = new SoundController.Envelope();
        e.SetTarget(0.5f, 0f);

        Assert.True(e.ShouldUpdate(),
            "an instant set forces ONE more update, which is the emit that tells "
            + "the engine the new value");

        Assert.True(!e.ShouldUpdate(),
            "and asking CONSUMES the flag -- a second ask in one frame would eat "
            + "the emit it existed to cause");
    }

    [Fact]
    public void SettingTheValueItAlreadyHasForcesNothing()
    {
        var e = new SoundController.Envelope();
        e.SetTarget(0.5f, 0f);
        e.ShouldUpdate();

        e.SetTarget(0.5f, 0f);

        Assert.True(!e.ShouldUpdate(),
            "the force flag is set when the TARGET moves, and setting a ramp to "
            + "where it already was moves nothing");
    }

    [Fact]
    public void ARunningRampWantsUpdatingWithoutTheForceFlag()
    {
        var e = new SoundController.Envelope();
        e.SetTarget(1.0f, 1.0f);
        e.ShouldUpdate();

        Assert.True(e.ShouldUpdate(),
            "a ramp that has not arrived wants updating every frame, which is not "
            + "the same question as the one-shot force flag");
    }

    // ---- the volume clamp --------------------------------------------------

    [Fact]
    public void AVolumeAboveOneIsClampedToOne()
    {
        var c = Playing(out _, out _);

        c.ChangeVolume(1, 4.0f, 0f);

        Assert.True(c.GetVolume(1) == 1.0f, "a patch cannot be asked for more than full volume");
    }

    [Fact]
    public void TheClampIsOnTheTargetAndNotOnTheCurrentValue()
    {
        var c = Playing(out var clock, out _);

        // Ramp toward a clamped 1 from 0, and stop halfway. If the clamp were
        // applied to the CURRENT value the answer would be 1 already.
        c.ChangeVolume(1, 0f, 0f);
        c.ChangeVolume(1, 4.0f, 1.0f);

        Frame(c, clock, 0.25f);

        Assert.True(c.GetVolume(1) < 0.5f,
            "the clamp bounds the target, so the ramp still takes its time to get "
            + "there -- clamping the value instead would snap it");
    }

    [Fact]
    public void PitchIsNotClamped()
    {
        var c = Playing(out _, out _);

        c.ChangePitch(1, 180f, 0f);

        Assert.True(c.GetPitch(1) == 180f,
            "the clamp is the VOLUME's; a pitch of 180 is a real value the engine takes");
    }

    // ---- the list ----------------------------------------------------------

    [Fact]
    public void ACreatedPatchIsKnownAndNotPlaying()
    {
        var c = New(out _, out _);
        c.Create(1);

        Assert.True(c.KnownCount == 1, "the patch is tracked from the moment it is built");
        Assert.True(c.PlayingCount == 0, "and is not in the playing list until it is played");
    }

    [Fact]
    public void PlayingAPatchListsItAndEmitsOnce()
    {
        var c = New(out _, out var engine);
        c.Create(1);
        c.Play(1, 0.8f, 100f, 0f);

        Assert.True(c.PlayingCount == 1, "a played patch is in the list");
        Assert.True(engine.CountOf("Sound.SoundPatchEmit") == 1,
            "and StartSound is one emit, not one per envelope");
    }

    [Fact]
    public void PlayingAnAlreadyPlayingPatchDoesNotStartItTwice()
    {
        var c = New(out _, out var engine);
        c.Create(1);
        c.Play(1, 0.8f, 100f, 0f);
        c.Play(1, 0.4f, 90f, 0f);

        Assert.True(c.PlayingCount == 1, "it is listed once");
        Assert.True(engine.CountOf("Sound.SoundPatchEmit") == 1,
            "and a second Play resets the ramps rather than restarting the sound");
        Assert.True(c.GetVolume(1) == 0.4f, "with the new volume in place");
    }

    [Fact]
    public void PlayingAnAlreadyPlayingPatchClearsItsQueue()
    {
        var c = New(out _, out _);
        c.Create(1);
        c.Play(1, 0.8f, 100f, 0f);
        c.CommandAdd(1, 5f, SoundController.CommandChangeVolume, 1f, 0.1f);

        Assert.True(c.QueuedCount == 1, "a ramp is queued");

        c.Play(1, 0.4f, 90f, 0f);

        Assert.True(c.QueuedCount == 0,
            "and a second Play drops it -- a patch told to play again is not still "
            + "heading wherever it was sent before");
    }

    [Fact]
    public void ShutdownStopsTheSoundAndUnlistsItWithoutForgettingIt()
    {
        var c = Playing(out _, out var engine);

        c.Shutdown(1);

        Assert.True(engine.CountOf("Sound.SoundPatchStop") == 1, "the sound is stopped");
        Assert.True(c.PlayingCount == 0, "and unlisted");
        Assert.True(c.KnownCount == 1,
            "but still tracked -- the native object exists until SoundDestroy");
    }

    [Fact]
    public void ShuttingDownTwiceStopsItOnce()
    {
        var c = Playing(out _, out var engine);

        c.Shutdown(1);
        c.Shutdown(1);

        Assert.True(engine.CountOf("Sound.SoundPatchStop") == 1,
            "stopping a sound that never started is a warning from the engine, so "
            + "the playing flag is what stops the second call happening");
    }

    [Fact]
    public void DestroyForgetsIt()
    {
        var c = Playing(out _, out _);

        c.Destroy(1);

        Assert.True(c.KnownCount == 0, "a destroyed patch is not tracked");
        Assert.True(c.PlayingCount == 0, "nor listed");
    }

    [Fact]
    public void AnUnknownHandleIsSilentlyNothing()
    {
        var c = New(out var clock, out var engine);

        c.Play(99, 1f, 100f, 0f);
        c.ChangeVolume(99, 0.5f, 1f);
        c.Shutdown(99);
        c.Destroy(99);
        Frame(c, clock, 0.1f);

        Assert.True(engine.CountOf("Sound.SoundPatchEmit") == 0,
            "a handle from a patch that has been freed names nothing, and naming "
            + "nothing is not an error -- handles are never reused, so a stale one "
            + "cannot land on a live patch");
    }

    // ---- the frame ---------------------------------------------------------

    [Fact]
    public void ARunningRampEmitsOnEveryFrameItMoves()
    {
        var c = New(out var clock, out var engine);
        c.Create(1);
        c.Play(1, 0f, 100f, 0f);

        int atStart = engine.CountOf("Sound.SoundPatchEmit");

        c.ChangeVolume(1, 1.0f, 1.0f);

        Frame(c, clock, 0.1f);
        Frame(c, clock, 0.1f);
        Frame(c, clock, 0.1f);

        Assert.True(engine.CountOf("Sound.SoundPatchEmit") == atStart + 3,
            "a ramp in flight is one emit a frame -- that IS the ramp, as far as "
            + "the engine is concerned");
    }

    [Fact]
    public void ASettledPatchStopsEmitting()
    {
        var c = New(out var clock, out var engine);
        c.Create(1);
        c.Play(1, 0.5f, 100f, 0f);

        // The first frame carries the force flag from Play's instant set.
        Frame(c, clock, 0.1f);
        int settled = engine.CountOf("Sound.SoundPatchEmit");

        Frame(c, clock, 0.1f);
        Frame(c, clock, 0.1f);

        Assert.True(engine.CountOf("Sound.SoundPatchEmit") == settled,
            "a sound sitting at a constant volume costs no traffic at all");
    }

    [Fact]
    public void EveryEmitCarriesTheVolumeChangeFlagWhateverMoved()
    {
        var c = New(out var clock, out var engine);
        c.Create(1);
        c.Play(1, 0.5f, 100f, 0f);
        Frame(c, clock, 0.1f);

        // A PITCH ramp only. Stock still sets SND_CHANGE_VOL, because the
        // envelope's volume is a multiplier on the script's and an emit without
        // the flag makes the engine use the script's number raw.
        c.ChangePitch(1, 120f, 1.0f);

        engine.Clear();
        Frame(c, clock, 0.1f);

        var call = engine.Last("Sound.SoundPatchEmit");
        Assert.True(call is not null, "the pitch ramp emitted");

        int flags = (int)call!.Value.Args[3]!;
        Assert.True((flags & SoundController.SndChangeVolume) != 0,
            "SND_CHANGE_VOL is on every emit whatever changed; without it the "
            + "engine uses the sound script's volume and the ramp vanishes");
        Assert.True((flags & SoundController.SndChangePitch) != 0,
            "and the pitch flag is there too, because the pitch is what moved");
    }

    [Fact]
    public void APatchWhoseEntityHasGoneLeavesTheList()
    {
        var c = New(out var clock, out var engine, entIndex: 7);
        c.Create(1);
        c.Play(1, 0.5f, 100f, 0f);

        Assert.True(c.PlayingCount == 1, "it starts listed");

        engine.Returns("Sound.SoundPatchState.entIndex", -1);
        Frame(c, clock, 0.1f);

        Assert.True(c.PlayingCount == 0,
            "a patch riding an entity that has been deleted has nothing to be "
            + "attached to and is dropped");
    }

    [Fact]
    public void APatchWithNoListenersIsKeptAndNotEmitted()
    {
        var c = New(out var clock, out var engine, filterActive: false);
        c.Create(1);
        c.Play(1, 0f, 100f, 0f);
        c.ChangeVolume(1, 1.0f, 1.0f);

        engine.Clear();
        Frame(c, clock, 0.1f);

        Assert.True(c.PlayingCount == 1,
            "nobody listening is NOT the same as having lost the entity; the patch "
            + "stays, because a player may still connect");
        Assert.True(engine.CountOf("Sound.SoundPatchEmit") == 0,
            "and nothing is sent, because there is nobody to send it to");
    }

    [Fact]
    public void AClockThatRunsBackwardsDoesNotRunTheRampsInReverse()
    {
        var c = New(out var clock, out _);
        c.Create(1);
        c.Play(1, 0f, 100f, 0f);
        c.ChangeVolume(1, 1.0f, 1.0f);

        Frame(c, clock, 0.5f);
        float halfway = c.GetVolume(1);

        // A map change moves curtime backwards.
        clock.Now -= 100f;
        ((IFrameLifecycle)c).OnPreClientUpdate(0.015f);

        Assert.True(c.GetVolume(1) >= halfway,
            "stock clamps a negative delta to zero, so a clock reset stalls the "
            + "ramps rather than unwinding them");
    }

    // ---- the queue ---------------------------------------------------------

    [Fact]
    public void AQueuedRampFiresWhenItsTimeComes()
    {
        var c = Playing(out var clock, out _, volume: 1.0f);

        c.CommandAdd(1, 1.0f, SoundController.CommandChangeVolume, 0f, 0.25f);

        Frame(c, clock, 0.5f);
        Assert.True(c.GetVolume(1) == 1.0f, "before its time, it has not fired");

        Frame(c, clock, 0.6f);
        Assert.True(c.GetVolume(1) == 0.25f, "and at its time it has");
    }

    [Fact]
    public void QueuedRampsFireInTimeOrderWhateverOrderTheyWereAddedIn()
    {
        var c = Playing(out var clock, out _);

        // Added late-first. The queue is sorted by TIME, not by arrival.
        c.CommandAdd(1, 2.0f, SoundController.CommandChangePitch, 0f, 50f);
        c.CommandAdd(1, 1.0f, SoundController.CommandChangePitch, 0f, 150f);

        Frame(c, clock, 1.1f);
        Assert.True(c.GetPitch(1) == 150f, "the earlier one runs first");

        Frame(c, clock, 1.0f);
        Assert.True(c.GetPitch(1) == 50f, "and the later one after it");
    }

    [Fact]
    public void ClearingAPatchsQueueLeavesAnotherPatchsAlone()
    {
        var c = New(out _, out _);
        c.Create(1);
        c.Create(2);

        c.CommandAdd(1, 1f, SoundController.CommandChangeVolume, 0f, 0.5f);
        c.CommandAdd(2, 1f, SoundController.CommandChangeVolume, 0f, 0.5f);

        c.CommandClear(1);

        Assert.True(c.QueuedCount == 1,
            "CommandClear takes one patch's ramps and not the queue");
    }

    [Fact]
    public void AQueuedStopStopsTheSound()
    {
        var c = Playing(out var clock, out var engine);

        c.CommandAdd(1, 0.5f, SoundController.CommandStop, 0f, 0f);
        Frame(c, clock, 0.6f);

        Assert.True(engine.CountOf("Sound.SoundPatchStop") == 1, "the queued stop fired");
    }

    [Fact]
    public void AQueuedDestroyFreesTheNativePatchToo()
    {
        var c = Playing(out var clock, out var engine);

        c.CommandAdd(1, 0.5f, SoundController.CommandDestroy, 0f, 0f);
        Frame(c, clock, 0.6f);

        Assert.True(c.KnownCount == 0, "the patch is forgotten here");
        Assert.True(engine.CountOf("Sound.SoundPatchFree") == 1,
            "and the shim is asked to free the native object -- the ONE direction "
            + "SoundDestroy does not cover, because the decision was managed");
    }

    // ---- fade out ----------------------------------------------------------

    [Fact]
    public void AFadeOutRampsTheVolumeToZero()
    {
        var c = Playing(out var clock, out _, volume: 1.0f);

        c.FadeOut(1, 1.0f, destroyOnFadeout: false);
        Frame(c, clock, 0.5f);

        Assert.True(c.GetVolume(1) < 1.0f && c.GetVolume(1) > 0f,
            "a fade is a volume ramp toward zero and is halfway through at halfway");
    }

    [Fact]
    public void AFadeOutStopsTheSoundWhenItsTimeIsUp()
    {
        var c = Playing(out var clock, out var engine, volume: 1.0f);

        c.FadeOut(1, 0.5f, destroyOnFadeout: false);

        Frame(c, clock, 0.6f);

        Assert.True(engine.CountOf("Sound.SoundPatchStop") == 1, "the fade ended in a stop");
        Assert.True(c.PlayingCount == 0, "and the patch left the list");
        Assert.True(c.KnownCount == 1, "but was NOT destroyed -- that is the other flag");
    }

    [Fact]
    public void AFadeOutThatDestroysQueuesADestroyRatherThanAShutdownTime()
    {
        var c = Playing(out var clock, out var engine, volume: 1.0f);

        c.FadeOut(1, 0.5f, destroyOnFadeout: true);

        Assert.True(c.QueuedCount == 1, "a DESTROY is queued for the end of the fade");

        Frame(c, clock, 0.6f);

        Assert.True(c.KnownCount == 0, "and at the end the patch is gone");
        Assert.True(engine.CountOf("Sound.SoundPatchFree") == 1, "native object freed");
    }

    [Fact]
    public void APatchThatLeftTheListOnAShutdownTimeCanBePlayedAgain()
    {
        var c = Playing(out var clock, out _, volume: 1.0f);

        c.FadeOut(1, 0.5f, destroyOnFadeout: false);
        Frame(c, clock, 0.6f);

        Assert.True(c.PlayingCount == 0, "it left");

        c.Play(1, 1.0f, 100f, 0f);
        Frame(c, clock, 0.1f);

        Assert.True(c.PlayingCount == 1,
            "and playing it again works -- the shutdown time is cleared when it "
            + "leaves the list, so the old fade's deadline does not kill it again");
    }

    // ---- envelope expansion ------------------------------------------------

    [Fact]
    public void AnEnvelopeQueuesOneRampPerPointAndAnswersTheTotal()
    {
        var c = New(out _, out var engine);
        engine.Returns("Host.RandomFloat", 0.5f);
        c.Create(1);

        // Three points, each amplitude 0..1 and duration 0..1; the stubbed draw
        // answers 0.5 to every one.
        ReadOnlySpan<float> points = stackalloc float[]
        {
            0f, 1f, 0f, 1f,
            0f, 1f, 0f, 1f,
            0f, 1f, 0f, 1f,
        };

        float total = c.PlayEnvelope(1, SoundController.CommandChangeVolume, points, 3);

        Assert.True(c.QueuedCount == 3, "one queued ramp per point");
        Assert.True(MathF.Abs(total - 1.5f) < 0.0001f,
            "and the answer is the SUM of the durations, which is what a caller "
            + "schedules the next thing against");
    }

    [Fact]
    public void AnEnvelopeClearsWhateverWasQueuedBefore()
    {
        var c = New(out _, out var engine);
        engine.Returns("Host.RandomFloat", 0.5f);
        c.Create(1);
        c.CommandAdd(1, 9f, SoundController.CommandChangeVolume, 0f, 0.1f);

        ReadOnlySpan<float> points = stackalloc float[] { 0f, 1f, 0f, 1f };
        c.PlayEnvelope(1, SoundController.CommandChangeVolume, points, 1);

        Assert.True(c.QueuedCount == 1,
            "an envelope replaces the patch's queue rather than adding to it");
    }

    [Fact]
    public void APointOfMinusOneKeepsThePreviousValue()
    {
        var c = New(out _, out var engine);
        engine.Returns("Host.RandomFloat", 0.25f);
        c.Create(1);

        // The second point keeps both. Its duration is therefore the first's,
        // and the total is two of them rather than one.
        ReadOnlySpan<float> points = stackalloc float[]
        {
            0f,    1f,   0f,    1f,
            -1f,  -1f,  -1f,   -1f,
        };

        float total = c.PlayEnvelope(1, SoundController.CommandChangeVolume, points, 2);

        Assert.True(MathF.Abs(total - 0.5f) < 0.0001f,
            "a point of -1 keeps the last duration, so two points at 0.25 total 0.5");
        Assert.True(engine.CountOf("Host.RandomFloat") == 2,
            "and no draw is made for the point that kept -- two draws for the "
            + "first point's two values, none for the second's");
    }

    [Fact]
    public void AFirstPointOfMinusOneIsComplainedAboutRatherThanGuessed()
    {
        var c = New(out _, out var engine);
        c.Create(1);

        ReadOnlySpan<float> points = stackalloc float[] { -1f, -1f, -1f, -1f };
        c.PlayEnvelope(1, SoundController.CommandChangeVolume, points, 1);

        Assert.True(engine.CountOf("Console.Msg") == 2,
            "there is no previous value to keep on the FIRST point, and stock says "
            + "so once for the amplitude and once for the duration");
    }

    [Fact]
    public void HalfAMinusOnePairIsARangeAndNotAKeep()
    {
        var c = New(out _, out var engine);
        engine.Returns("Host.RandomFloat", 2f);
        c.Create(1);

        // (-1, 5) is a RANGE, because stock's test is `min != -1 || max != -1`.
        ReadOnlySpan<float> points = stackalloc float[] { -1f, 5f, -1f, 5f };
        c.PlayEnvelope(1, SoundController.CommandChangeVolume, points, 1);

        Assert.True(engine.CountOf("Host.RandomFloat") == 2,
            "a point with one -1 is drawn from, not kept -- the test is on the "
            + "PAIR and a range starting at -1 is a legal range");
        Assert.True(engine.CountOf("Console.Msg") == 0, "and nothing is complained about");
    }

    // ---- the level ---------------------------------------------------------

    [Fact]
    public void ALevelShutdownStopsEveryPlayingPatchAndEmptiesBothLists()
    {
        var c = New(out _, out var engine);

        c.Create(1);
        c.Create(2);
        c.Play(1, 1f, 100f, 0f);
        c.Play(2, 1f, 100f, 0f);
        c.CommandAdd(1, 5f, SoundController.CommandChangeVolume, 0f, 0f);

        ((IGameLifecycle)c).OnLevelShutdownPreEntity();

        Assert.True(engine.CountOf("Sound.SoundPatchStop") == 2, "both playing patches stopped");
        Assert.True(c.PlayingCount == 0, "the sound list is empty");
        Assert.True(c.QueuedCount == 0, "and so is the command queue");
    }

    [Fact]
    public void ALevelShutdownDoesNotForgetThePatchesThemselves()
    {
        var c = New(out _, out _);
        c.Create(1);
        c.Play(1, 1f, 100f, 0f);

        ((IGameLifecycle)c).OnLevelShutdownPreEntity();

        Assert.True(c.KnownCount == 1,
            "the NATIVE objects still exist and the shim still holds them, so "
            + "forgetting them here would make a later SoundDestroy reach a "
            + "handle this side has never heard of");
    }

    // ---- the numbers this side shares with the headers ----------------------

    [Fact]
    public void TheCommandNumbersMatchSoundenvelopeH()
    {
        // soundcommands_t is an unannotated enum, so its members are 0,1,2,3 in
        // declaration order and the ORDER is the contract. Read out of the
        // header rather than restated, per docs/writing-tests.md's rule about a
        // fixture standing in for something native owns.
        string header = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoLayout.SdkTree, "src/game/shared/soundenvelope.h"));

        int at = header.IndexOf("enum soundcommands_t", StringComparison.Ordinal);
        Assert.True(at >= 0, "soundenvelope.h declares soundcommands_t");

        string body = header[at..header.IndexOf('}', at)];

        Assert.True(Position(body, "SOUNDCTRL_CHANGE_VOLUME") == SoundController.CommandChangeVolume,
            "SOUNDCTRL_CHANGE_VOLUME is member " + SoundController.CommandChangeVolume);
        Assert.True(Position(body, "SOUNDCTRL_CHANGE_PITCH") == SoundController.CommandChangePitch,
            "SOUNDCTRL_CHANGE_PITCH is member " + SoundController.CommandChangePitch);
        Assert.True(Position(body, "SOUNDCTRL_STOP") == SoundController.CommandStop,
            "SOUNDCTRL_STOP is member " + SoundController.CommandStop);
        Assert.True(Position(body, "SOUNDCTRL_DESTROY") == SoundController.CommandDestroy,
            "SOUNDCTRL_DESTROY is member " + SoundController.CommandDestroy);
    }

    [Fact]
    public void TheSoundFlagsMatchSoundflagsH()
    {
        string header = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoLayout.SdkTree, "src/public/soundflags.h"));

        // The BIT, read out of the header. This fact found the port's one real
        // bug: both constants were written from memory as 1<<6 and 1<<7, which
        // are SND_SPEAKER and SND_SHOULDPAUSE. Every ramp would have gone out
        // flagged as a microphone replay and WITHOUT the volume-change flag, so
        // the engine would have used the sound script's raw volume and the
        // envelope would have been silent. It compiled and nothing else noticed.
        Assert.Contains("SND_CHANGE_VOL\t\t= (1<<0)", header);
        Assert.Contains("SND_CHANGE_PITCH\t= (1<<1)", header);

        Assert.True(SoundController.SndChangeVolume == 1 << 0, "SND_CHANGE_VOL is bit 0");
        Assert.True(SoundController.SndChangePitch == 1 << 1, "SND_CHANGE_PITCH is bit 1");

        // AND THE TWO ARE NOT THE SAME BIT, which the two Contains above would
        // not notice if a copy-paste made both constants zero.
        Assert.True(SoundController.SndChangeVolume != SoundController.SndChangePitch,
            "the volume flag and the pitch flag are different bits");
    }

    /// <summary>Which member of the enum body a name is, counting from zero.</summary>
    private static int Position(string enumBody, string name)
    {
        string[] members = enumBody.Split(',');

        for (int i = 0; i < members.Length; i++)
        {
            if (members[i].Contains(name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
