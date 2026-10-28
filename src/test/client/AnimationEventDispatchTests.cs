using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Shared.AnimationEventClock;

namespace SourceSharp.Tests;

/// <summary>
/// The animation-event DISPATCHER: the half that makes an event happen, as
/// against <see cref="AnimationEventCheckTests"/>, which pins the half that
/// says what a model carries.
///
/// <para>
/// Phase 5 shipped the checker and nothing fired. <c>cs_animcheck</c> would
/// report <c>v_smg1  Primary  seq 3  ok  want AE_MUZZLEFLASH</c> while the
/// screen stayed exactly as dark on the frame of the shot as on the frame
/// before it, because there was no dispatcher at all. Everything below is
/// about the four ways the noticing can go wrong quietly.
/// </para>
/// </summary>
public class AnimationEventClockTests
{
    private static AnimationEventCursor After(AnimationEventCursor from, int seq, int starts, float cycle)
        => AnimationEventClock.Advance(from, seq, starts, cycle).Cursor;

    [Fact]
    public void AnAnimationThatHasJustStartedFiresOnlyItsCycleZeroEvents()
    {
        var step = AnimationEventClock.Advance(AnimationEventCursor.Fresh, 3, 1, 0.4f);

        Assert.True(step.Fires(0f), "an event stamped at cycle 0 fires on the frame the animation starts");
        Assert.True(!step.Fires(0.2f),
                    "an event at 0.2 does not fire on the start frame, however far the clock claims to have got");
        Assert.True(step.Cursor.PreviousCycle == 0f,
                    "a started animation leaves its cursor at zero, not at the cycle it was first seen at");
    }

    [Fact]
    public void AnOrdinaryFrameFiresWhatItCrossedAndNothingElse()
    {
        var cursor = new AnimationEventCursor(3, 1, 0.20f, true);
        var step = AnimationEventClock.Advance(cursor, 3, 1, 0.40f);

        Assert.True(!step.Fires(0.20f), "an event exactly at the previous cycle has already fired");
        Assert.True(step.Fires(0.25f), "an event inside the band fires");
        Assert.True(step.Fires(0.40f), "an event exactly at the new cycle fires, so a band is half-open at its top");
        Assert.True(!step.Fires(0.41f), "an event beyond the new cycle has not happened yet");
    }

    [Fact]
    public void AnEventIsNotFiredTwiceOnTwoConsecutiveFrames()
    {
        var cursor = new AnimationEventCursor(3, 1, 0.20f, true);

        var first = AnimationEventClock.Advance(cursor, 3, 1, 0.30f);
        var second = AnimationEventClock.Advance(first.Cursor, 3, 1, 0.40f);

        Assert.True(first.Fires(0.30f), "the event fires on the frame that crossed it");
        Assert.True(!second.Fires(0.30f), "and not again on the next frame");
    }

    [Fact]
    public void AStalledClockFiresNothingAndDoesNotMoveTheCursor()
    {
        var cursor = new AnimationEventCursor(3, 1, 0.40f, true);
        var step = AnimationEventClock.Advance(cursor, 3, 1, 0.40f);

        Assert.True(!step.Any, "a frame where the cycle did not move crossed nothing");
        Assert.True(step.Cursor == cursor, "and left the cursor exactly where it was");
    }

    [Fact]
    public void ACycleThatSlipsBackwardsALittleDoesNotReplayTheBandItFellThrough()
    {
        var cursor = new AnimationEventCursor(3, 1, 0.40f, true);

        // Interpolation catching up, or a frame out of order. Stock's rule and
        // the reason for it: replayed, every event between here and there
        // fires a second time.
        var slipped = AnimationEventClock.Advance(cursor, 3, 1, 0.30f);

        Assert.True(!slipped.Any, "a small backwards step crosses nothing");
        Assert.True(slipped.Cursor.PreviousCycle == 0.40f,
                    "and leaves the cursor ahead, so the band it fell through is not re-opened");

        var forward = AnimationEventClock.Advance(slipped.Cursor, 3, 1, 0.45f);

        Assert.True(!forward.Fires(0.35f),
                    "so an event at 0.35 that already fired does not fire again when the clock catches up");
    }

    [Fact]
    public void AWrapFiresTheEndOfTheLapBeforeTheStartOfTheNext()
    {
        var cursor = new AnimationEventCursor(3, 1, 0.90f, true);
        var step = AnimationEventClock.Advance(cursor, 3, 1, 0.05f);

        Assert.True(step.Tail.HasValue, "a wrap has a tail band");
        Assert.True(step.Tail!.Value.Contains(0.95f), "which carries the events at the end of the lap");
        Assert.True(step.Head.HasValue && step.Head!.Value.Contains(0.02f),
                    "and a head band carrying the events at the start of the next");
        Assert.True(!step.Tail!.Value.Contains(0.02f) && !step.Head!.Value.Contains(0.95f),
                    "the two bands do not overlap, so nothing fires twice on a wrap");
    }

    /// <summary>
    /// The documented divergence from stock, pinned so that it is a decision
    /// rather than a drift.
    /// </summary>
    [Fact]
    public void AnEventNearTheStartOfALoopingSequenceFiresOnEveryLap()
    {
        // Stock's DoAnimationEvents opens the post-wrap band at
        // `flEventCycle - 0.001f`, so an event at 0.02 is skipped on every lap
        // but the first. This shell opens it before the first frame.
        var cursor = new AnimationEventCursor(3, 1, 0.97f, true);
        var step = AnimationEventClock.Advance(cursor, 3, 1, 0.03f);

        Assert.True(step.Fires(0.02f),
                    "a footstep stamped near the start of a looping walk fires on the lap that reached it");
        Assert.True(step.Fires(0f),
                    "and so does one stamped at exactly zero");
    }

    [Fact]
    public void RefiringTheSameSequenceFiresItsEventsAgainEvenWhenTheCycleBarelyMoves()
    {
        // The case that makes the start COUNT necessary rather than nice. An
        // SMG re-fires every 0.1s against a 0.5s animation: the cycle falls
        // from about 0.2 to 0, which is too small a fall to be a wrap and too
        // large to be a stumble. Read by size alone, this fires nothing.
        var cursor = new AnimationEventCursor(3, 1, 0.20f, true);

        var bySize = AnimationEventClock.Advance(cursor, 3, /*starts unchanged*/ 1, 0.0f);
        Assert.True(!bySize.Any, "told apart by the size of the fall alone, a fast re-fire crosses nothing");

        var byCount = AnimationEventClock.Advance(cursor, 3, /*restarted*/ 2, 0.0f);
        Assert.True(byCount.Fires(0f), "the start counter says it restarted, so the cycle-0 muzzle flash fires");
    }

    [Fact]
    public void ChangingSequenceStartsTheNewOneRatherThanContinuingTheOld()
    {
        var cursor = new AnimationEventCursor(3, 1, 0.80f, true);
        var step = AnimationEventClock.Advance(cursor, 7, 2, 0.0f);

        Assert.True(step.Fires(0f), "the new sequence's cycle-0 events fire");
        Assert.True(step.Cursor.Sequence == 7, "and the cursor now belongs to the new sequence");
        Assert.True(!step.Fires(0.9f),
                    "nothing from the sequence that was abandoned fires as though it had finished");
    }

    [Fact]
    public void AnEntityWithNoAnimationCrossesNothing()
    {
        var step = AnimationEventClock.Advance(AnimationEventCursor.Fresh, -1, 0, 0f);

        Assert.True(!step.Any, "no sequence means nothing to fire");
        Assert.True(!step.Cursor.Established,
                    "and the cursor stays unestablished, so arriving at a sequence later reads as a start");
    }

    [Fact]
    public void ArrivingAtASequenceAfterHavingNoneReadsAsAStart()
    {
        var none = After(AnimationEventCursor.Fresh, -1, 0, 0f);
        var step = AnimationEventClock.Advance(none, 3, 1, 0.0f);

        Assert.True(step.Fires(0f), "an entity that gains a model fires that model's cycle-0 events");
    }
}

/// <summary>
/// The catalog, against the event list it mirrors.
///
/// <para>
/// This is the fixture-versus-native rule applied to a table that would
/// otherwise be a hand-copied list nobody could check: a wrong side flag here
/// makes the client act on the server's events, or silently drop its own, and
/// nothing anywhere reports either.
/// </para>
/// </summary>
public class AnimationEventCatalogTests
{

    [Fact]
    public void ANamedEventIsTheClientsWhenTheEventListSaysSoAndNotOtherwise()
    {
        Assert.True(AnimationEventCatalog.IsClientEvent(Named("AE_MUZZLEFLASH")),
                    "AE_MUZZLEFLASH is the client's");
        Assert.True(AnimationEventCatalog.IsClientEvent(Named("AE_CL_PLAYSOUND")),
                    "AE_CL_PLAYSOUND is the client's");
        Assert.True(!AnimationEventCatalog.IsClientEvent(Named("AE_NPC_LEFTFOOT")),
                    "AE_NPC_LEFTFOOT is the server's and the client does not act on it");
        Assert.True(!AnimationEventCatalog.IsClientEvent(Named("AE_SOMETHING_A_MOD_INVENTED")),
                    "an unregistered name is not treated as the client's — stock registers those with no side at all");
    }

    /// <summary>
    /// The check that could not otherwise be made: the raw <c>type</c> word a
    /// .mdl carries has no side bit in it, so a dispatcher testing it would
    /// fire nothing at all, forever.
    /// </summary>
    [Fact]
    public void ClientNessIsDecidedByNameRatherThanByTheTypeBitTheModelCarries()
    {
        // Exactly what SequenceEvents_Read hands back for a new-system event:
        // AE_TYPE_NEWEVENTSYSTEM and no side flags whatever, because
        // SetEventIndexForSequence — which is what ORs them in — is
        // deliberately not run.
        var asTheModelHasIt = new ModelAnimationEvent(0, 0f, 0,
                                                      AnimationEventTypes.NewEventSystem,
                                                      "AE_MUZZLEFLASH", "SMG1 muzzle");

        Assert.True((asTheModelHasIt.Type & (1 << 4)) == 0,
                    "the model's own type word carries no AE_TYPE_CLIENT bit");
        Assert.True(AnimationEventCatalog.IsClientEvent(asTheModelHasIt),
                    "and the event is still recognised as the client's, because the name is what decides");
    }

    [Fact]
    public void AnOldSystemEventIsTheClientsByNumber()
    {
        Assert.True(AnimationEventCatalog.IsClientEvent(Numbered(5001)),
                    "the RPG's bare 5001 is at or above EVENT_CLIENT and is the client's");
        Assert.True(AnimationEventCatalog.IsClientEvent(Numbered(6001)),
                    "so is the SMG's 6001 shell eject");
        Assert.True(!AnimationEventCatalog.IsClientEvent(Numbered(3005)),
                    "the grenade's throw is a bare 3005, below EVENT_CLIENT, and is the server's");
    }

    private static ModelAnimationEvent Named(string name)
        => new(0, 0f, 0, AnimationEventTypes.NewEventSystem, name, string.Empty);

    private static ModelAnimationEvent Numbered(int id)
        => new(0, 0f, id, 0, string.Empty, string.Empty);
}

/// <summary>
/// The decoding: what a fired event asks for, and where.
/// </summary>
public class AnimationEventActionTests
{
    [Fact]
    public void AMuzzleFlashNamesItsTypeAndThenItsAttachment()
    {
        var action = AnimationEventActions.Decide(
            new ModelAnimationEvent(0, 0f, 0, AnimationEventTypes.NewEventSystem,
                                    "AE_MUZZLEFLASH", "SMG1 muzzle"));

        Assert.True(action.Kind == AnimationEventKind.MuzzleFlash, "it is a muzzle flash");
        Assert.True(action.Payload == "SMG1", "the FIRST token is the flash type");
        Assert.True(action.AttachmentName == "muzzle", "the SECOND is the attachment name");
    }

    [Fact]
    public void AMuzzleFlashWithNoAttachmentIsRefusedRatherThanGuessed()
    {
        var action = AnimationEventActions.Decide(
            new ModelAnimationEvent(0, 0f, 0, AnimationEventTypes.NewEventSystem,
                                    "AE_MUZZLEFLASH", "SMG1"));

        Assert.True(action.Kind == AnimationEventKind.None,
                    "a flash at a guessed attachment comes out of the stock of the gun; stock refuses too");
    }

    [Fact]
    public void TheNumberedMuzzleFlashesDecodeToTheAttachmentTheirNamesClaim()
    {
        // 5001, 5011, 5021, 5031 — ten apart, counting attachments from ZERO,
        // against an attachment API that counts from ONE.
        Assert.True(Attachment(5001) == 1, "CL_EVENT_MUZZLEFLASH0 is attachment 0, which is index 1 here");
        Assert.True(Attachment(5011) == 2, "CL_EVENT_MUZZLEFLASH1 is attachment 1, which is index 2 here");
        Assert.True(Attachment(5021) == 3, "CL_EVENT_MUZZLEFLASH2 is index 3");
        Assert.True(Attachment(5031) == 4, "CL_EVENT_MUZZLEFLASH3 is index 4");
        Assert.True(Attachment(5003) == 1, "CL_EVENT_NPC_MUZZLEFLASH0 is index 1");
        Assert.True(Attachment(5013) == 2, "CL_EVENT_NPC_MUZZLEFLASH1 is index 2");
    }

    [Fact]
    public void ANumberInTheGapBetweenTheVariantsIsNotAMuzzleFlash()
    {
        // 5041 would be "attachment 4", which the family does not go up to,
        // and 5005 is in the gap between 5004 and 5011. Both must miss, or the
        // arithmetic would claim every fifth event id in the range.
        Assert.True(AnimationEventActions.Decide(Old(5041)).Kind == AnimationEventKind.None,
                    "the numbered muzzle flashes stop at attachment 3");
        Assert.True(AnimationEventActions.Decide(Old(5005)).Kind == AnimationEventKind.None,
                    "5005 is in the gap and is not one of the family");
    }

    [Fact]
    public void BothWaysOfAskingForASoundDecodeToTheSoundscript()
    {
        var named = AnimationEventActions.Decide(
            new ModelAnimationEvent(0, 0.25f, 0, AnimationEventTypes.NewEventSystem,
                                    "AE_CL_PLAYSOUND", "Weapon_357.OpenLoader"));

        Assert.True(named.Kind == AnimationEventKind.Sound && named.Payload == "Weapon_357.OpenLoader",
                    "AE_CL_PLAYSOUND carries its soundscript in the options");

        var numbered = AnimationEventActions.Decide(
            new ModelAnimationEvent(0, 0.25f, 5004, 0, string.Empty, "Weapon_SMG1.Reload"));

        Assert.True(numbered.Kind == AnimationEventKind.Sound && numbered.Payload == "Weapon_SMG1.Reload",
                    "and CL_EVENT_SOUND is the same request written as a number");
    }

    [Fact]
    public void TheBrassEjectDecodesToTheAttachmentStockUsesForIt()
    {
        var action = AnimationEventActions.Decide(Old(6001));

        Assert.True(action.Kind == AnimationEventKind.EjectBrass, "6001 is the shell eject");
        Assert.True(action.AttachmentIndex == 2,
                    "from attachment 1, which stock's own handler spells GetAttachment( 2, ... )");
    }

    [Fact]
    public void A_view_model_flash_is_first_person_and_its_third_person_twin_is_not()
    {
        var named = new ModelAnimationEvent(0, 0f, 0, AnimationEventTypes.NewEventSystem, "AE_MUZZLEFLASH", "SMG1 muzzle");
        var npc = new ModelAnimationEvent(0, 0f, 0, AnimationEventTypes.NewEventSystem, "AE_NPC_MUZZLEFLASH", "SMG1 muzzle");
        Assert.True(AnimationEventActions.Decide(named).FirstPerson, "AE_MUZZLEFLASH: DispatchMuzzleEffect( options, true )");
        Assert.False(AnimationEventActions.Decide(npc).FirstPerson, "AE_NPC_MUZZLEFLASH: DispatchMuzzleEffect( options, false )");
        Assert.True(AnimationEventActions.Decide(Old(5011)).FirstPerson, "CL_EVENT_MUZZLEFLASH1 is first person");
        Assert.False(AnimationEventActions.Decide(Old(5013)).FirstPerson, "CL_EVENT_NPC_MUZZLEFLASH1 is not");
    }

    [Fact]
    public void The_flash_type_tokens_are_DispatchMuzzleEffects()
    {
        Assert.Equal([5, 2, 4, 1, 6, 7, 0, 2],
                     new[] { "COMBINE", "SMG1", "PISTOL", "SHOTGUN", "357", "RPG", "AR2", "smg1" }.Select(AnimationEventActions.MuzzleFlashType));
    }

    [Fact]
    public void A_numbered_flash_reads_its_type_as_atoi_does()
    {
        Assert.Equal([2, 0, -3, 12, 0], new[] { "2", "", " -3x", "12 4", "x2" }.Select(AnimationEventActions.Atoi));
    }

    private static ModelAnimationEvent Old(int id)
        => new(0, 0f, id, 0, string.Empty, string.Empty);

    private static int Attachment(int id) => AnimationEventActions.Decide(Old(id)).AttachmentIndex;
}

/// <summary>
/// ONCE. The word in step 6d's gate, and the one an animation-event test
/// usually cannot see.
///
/// <para>
/// <b>"It fired" is not the assertion.</b> The classic animation-event bug is
/// an event that fires on every frame the cycle is past its threshold — sixty
/// muzzle flashes a second out of one shot — and a test that asserts a sink
/// received something passes on it. So does a test that steps two frames: the
/// half-open band gets that case right and still gets a pinned one-shot wrong.
/// Every fact below COUNTS, over a sweep long enough for the failure to show:
/// three laps of a looping sequence, or ten times the length of a one-shot.
/// </para>
///
/// <para>
/// <b>The cycle is <see cref="SequenceRecord.Cycle"/>'s and not this file's.</b>
/// It is the shell's own arithmetic — the wrap, the 0.999 pin, the zero-duration
/// refusal — and a sweep driven by a cycle this test computed for itself would
/// be two rules agreeing with each other. What is under test is the pair.
/// </para>
/// </summary>
public class AnimationEventOnceTests
{
    private const float Frame = 1f / 60f;

    /// <summary>
    /// How many times each of <paramref name="eventCycles"/> fired over a sweep
    /// of <paramref name="frames"/> frames.
    ///
    /// <para>
    /// The clock is stepped by frame INDEX rather than by accumulating a float,
    /// because three laps at a sixtieth of a second is a hundred and eighty
    /// additions and the drift in the last of them is larger than the gap
    /// between two adjacent event stamps.
    /// </para>
    /// </summary>
    private static int[] FireCounts(SequenceRecord record, int frames, float step,
                                    params float[] eventCycles)
    {
        var counts = new int[eventCycles.Length];
        var cursor = AnimationEventCursor.Fresh;

        for (int i = 0; i < frames; i++)
        {
            float now = record.StartTime + i * step;

            var advanced = AnimationEventClock.Advance(cursor, record.Sequence,
                                                       record.Starts, record.Cycle(now));
            cursor = advanced.Cursor;

            for (int e = 0; e < eventCycles.Length; e++)
            {
                if (advanced.Fires(eventCycles[e]))
                    counts[e]++;
            }
        }

        return counts;
    }

    private static SequenceRecord Looping(float duration)
        => new SequenceRecord(3, 1f, 0f, duration, true, 1);

    private static SequenceRecord OneShot(float duration)
        => new SequenceRecord(3, 1f, 0f, duration, false, 1);

    //-------------------------------------------------------------------------
    // A looping sequence: once per lap, and exactly once.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnEventMidLapFiresExactlyOncePerLap()
    {
        // A one-second sequence, sixty frames a second, three laps less one
        // frame — so the third lap has reached 0.98 and not 1.0.
        int[] counts = FireCounts(Looping(1f), frames: 180, step: Frame, 0.5f);

        Assert.Equal(3, counts[0]);
    }

    [Fact]
    public void AnEventAtCycleZeroFiresExactlyOncePerLap()
    {
        // The one that is easiest to fire twice: it is in the head band of
        // every wrap AND at the lower edge of the start band.
        int[] counts = FireCounts(Looping(1f), frames: 180, step: Frame, 0f);

        Assert.Equal(3, counts[0]);
    }

    [Fact]
    public void AnEventAtTheVeryEndOfTheLapFiresExactlyOncePerLap()
    {
        // The one that is easiest to fire ZERO times: at a sixtieth of a second
        // the last cycle before a wrap is 0.98333, so an event at 0.99 is never
        // in an ordinary band. It is only ever in the TAIL band, which exists
        // on the one frame the wrap is noticed — and a dispatcher without a
        // tail band drops it silently, forever, on every model in the game.
        //
        // ONE FRAME PAST the third wrap, deliberately: 180 frames stop at
        // cycle 0.98333 of the third lap and the honest answer there is TWO.
        // The fixture is sized to the fact rather than the fact rounded to the
        // fixture, which is the thing that made this fail first time.
        int[] counts = FireCounts(Looping(1f), frames: 181, step: Frame, 0.99f);

        Assert.Equal(3, counts[0]);
    }

    [Fact]
    public void ASweepThatStopsBeforeAStampDoesNotFireIt()
    {
        // The control for the fact above, and it is what makes the 181 a
        // measurement rather than a number chosen until the test went green: at
        // 180 frames the third lap has not reached 0.99, and the answer is two.
        int[] counts = FireCounts(Looping(1f), frames: 180, step: Frame, 0.99f);

        Assert.Equal(2, counts[0]);
    }

    [Fact]
    public void EveryEventOfALapFiresTheSameNumberOfTimesAsEveryOther()
    {
        // Four stamps across the lap at once, which is the thing a per-event
        // test cannot see: a band arithmetic that is right at one cycle and
        // wrong at another produces four different numbers here and one number
        // in four separate facts.
        int[] counts = FireCounts(Looping(1f), frames: 180, step: Frame,
                                  0f, 0.25f, 0.5f, 0.75f);

        Assert.Equal(new[] { 3, 3, 3, 3 }, counts);
    }

    [Fact]
    public void TheCountDoesNotDependOnTheFrameRate()
    {
        // Three laps at three frame rates. A rule that happened to be right at
        // sixty frames a second because an event stamp fell on a frame boundary
        // is wrong at 30 and at 144, and this is the only fact here that can
        // tell those apart.
        int[] slow = FireCounts(Looping(1f), frames: 90, step: 1f / 30f, 0f, 0.25f, 0.5f, 0.75f);
        int[] fast = FireCounts(Looping(1f), frames: 432, step: 1f / 144f, 0f, 0.25f, 0.5f, 0.75f);

        Assert.Equal(new[] { 3, 3, 3, 3 }, slow);
        Assert.Equal(new[] { 3, 3, 3, 3 }, fast);
    }

    [Fact]
    public void TheCountSurvivesAJitteredFrameStep()
    {
        // A REAL frame clock is not uniform, and a uniform one is exactly the
        // input under which an off-by-one band can land on its feet every lap.
        // The steps below run from a third of a frame to a frame and a half,
        // from a fixed seed so the failure is the same one every time.
        var counts = new int[4];
        float[] cycles = { 0f, 0.25f, 0.5f, 0.75f };

        var record = Looping(1f);
        var cursor = AnimationEventCursor.Fresh;

        uint seed = 0x6D_A1_17u;
        float now = 0f;

        while (now < 3f)
        {
            var advanced = AnimationEventClock.Advance(cursor, record.Sequence,
                                                       record.Starts, record.Cycle(now));
            cursor = advanced.Cursor;

            for (int e = 0; e < cycles.Length; e++)
            {
                if (advanced.Fires(cycles[e]))
                    counts[e]++;
            }

            seed = seed * 1664525u + 1013904223u;
            now += 0.005f + (seed >> 16) % 1000 * 0.00002f;
        }

        Assert.Equal(new[] { 3, 3, 3, 3 }, counts);
    }

    //-------------------------------------------------------------------------
    // A one-shot: once, and then never again however long it is looked at.
    //-------------------------------------------------------------------------

    [Fact]
    public void AOneShotsEventFiresOnceAndNotOnEveryFrameThatFollowsIt()
    {
        // THE BUG THIS WHOLE FILE IS ABOUT. A half-second animation watched for
        // five seconds: the cycle reaches its 0.999 pin after thirty frames and
        // sits there for two hundred and seventy more. A dispatcher that asked
        // "is the cycle past the stamp" rather than "did this frame cross it"
        // answers 271 here and looks entirely healthy in every two-frame test.
        int[] counts = FireCounts(OneShot(0.5f), frames: 300, step: Frame, 0.9f);

        Assert.Equal(1, counts[0]);
    }

    [Fact]
    public void AnEventAtTheOneShotsPinFiresOnceToo()
    {
        // 0.999 is where a finished one-shot HOLDS, so an event stamped at or
        // just below it is the one a "past the stamp" dispatcher fires most.
        int[] counts = FireCounts(OneShot(0.5f), frames: 300, step: Frame, 0.999f);

        Assert.Equal(1, counts[0]);
    }

    [Fact]
    public void AOneShotFiresEveryStampExactlyOnceAcrossItsWholeLife()
    {
        int[] counts = FireCounts(OneShot(0.5f), frames: 300, step: Frame,
                                  0f, 0.25f, 0.5f, 0.75f, 0.999f);

        Assert.Equal(new[] { 1, 1, 1, 1, 1 }, counts);
    }

    [Fact]
    public void RestartingTheOneShotIsTheOnlyThingThatFiresItAgain()
    {
        // The counterpart, and it is what makes the fact above a statement
        // about crossing rather than about the dispatcher being deaf: the same
        // sweep, with the start counter bumped a third of the way through,
        // fires everything a second time.
        var record = OneShot(0.5f);
        var cursor = AnimationEventCursor.Fresh;
        int fired = 0;

        for (int i = 0; i < 300; i++)
        {
            // A second Play() of the same sequence at t = 1.0, which is what
            // SequenceRecord.Play does with restart: true.
            SequenceRecord playing = i < 60
                ? record
                : record.Play(record.Sequence, 1f, restart: true, now: 1f).WithInfo(0.5f, false);

            float now = i * Frame;

            var advanced = AnimationEventClock.Advance(cursor, playing.Sequence,
                                                       playing.Starts, playing.Cycle(now));
            cursor = advanced.Cursor;

            if (advanced.Fires(0.9f))
                fired++;
        }

        Assert.Equal(2, fired);
    }

    //-------------------------------------------------------------------------
    // Nothing at all, which is the other way to be wrong.
    //-------------------------------------------------------------------------

    [Fact]
    public void AStalledClockFiresNothingHoweverManyFramesItStalls()
    {
        // Every frame reads the same cycle: a paused game, or an entity whose
        // clock the server has stopped updating. The cursor must not move
        // backwards and nothing must re-open.
        var cursor = AnimationEventClock.Advance(AnimationEventCursor.Fresh, 3, 1, 0f).Cursor;
        cursor = AnimationEventClock.Advance(cursor, 3, 1, 0.6f).Cursor;

        int fired = 0;

        for (int i = 0; i < 300; i++)
        {
            var advanced = AnimationEventClock.Advance(cursor, 3, 1, 0.6f);
            cursor = advanced.Cursor;

            if (advanced.Fires(0.5f) || advanced.Fires(0.6f))
                fired++;
        }

        Assert.Equal(0, fired);
    }

    [Fact]
    public void ASequenceWithNoDurationFiresNothingAtAll()
    {
        // A static prop. Its sequence 0 may carry events and its cycle is
        // always 0; a duration of zero is the honest way the shell says "this
        // model has no clock", and it is what the pump turns into "no sequence".
        var record = new SequenceRecord(0, 1f, 0f, 0f, true, 1);

        Assert.Equal(0f, record.Cycle(1.234f));

        int[] counts = FireCounts(record, frames: 300, step: Frame, 0f);

        // The clock alone still reports the START of sequence 0 once -- it is
        // told a sequence and cannot see a duration. What refuses it is the
        // PUMP, which is asserted next door in
        // AnEntityWithNoClockFiresNothingHoweverManyEventsItsSequenceCarries.
        Assert.Equal(1, counts[0]);
    }

    //-------------------------------------------------------------------------
    // A LIMIT, recorded rather than asserted away.
    //-------------------------------------------------------------------------

    [Fact]
    public void AFrameLongerThanALapFiresEachStampOnceAndNotOncePerLapItSkipped()
    {
        // Stock's behaviour and this shell's: the window is (previous, now],
        // and a frame that spanned two and a half laps has no way to say so.
        // A quarter-second frame over a tenth-second sequence crosses each
        // stamp two or three times and reports it once.
        //
        // Named rather than glossed: it is why a footstep event on a sprint
        // animation drops steps in a stall, and it is not a thing this step
        // fixes -- fixing it means the clock returning a LAP COUNT as well as a
        // cycle, which is a shell change and not a rule change.
        //
        // 0.23 rather than 0.25 on purpose: at a quarter second the cycle falls
        // by exactly 0.5 every frame, which is the wrap THRESHOLD and not past
        // it, so every one of those frames is read as a stumble and the sweep
        // fires once in total. That is a second, different limit and it belongs
        // to a different fact; measuring it here by accident would have made
        // this one look far worse than it is.
        int[] counts = FireCounts(Looping(0.1f), frames: 12, step: 0.23f, 0.5f);

        // Twelve frames of 0.23s over a 0.1s sequence is 25.3 laps.
        Assert.True(counts[0] < 25,
                    "a stamp cannot fire once per lap when a frame spanned several");
        Assert.Equal(3, counts[0]);
    }

    [Fact]
    public void ACycleThatFallsByExactlyTheWrapThresholdIsReadAsAStumble()
    {
        // The threshold is a strict comparison: `previous - cycle <= WrapFall`
        // is a stumble, so a fall of exactly 0.5 does not wrap. A 0.1s sequence
        // sampled every quarter second alternates 0.0, 0.5, 0.0, 0.5 — every
        // frame a fall of exactly 0.5 — and the whole sweep crosses its stamp
        // ONCE, on the one frame that moved forwards.
        //
        // Recorded rather than fixed. Moving the comparison would make a cycle
        // that slipped back by half a lap replay half a lap of events, which is
        // the failure WrapFall exists to prevent; what is wrong here is the
        // frame time, not the threshold.
        int[] counts = FireCounts(Looping(0.1f), frames: 12, step: 0.25f, 0.5f);

        Assert.Equal(1, counts[0]);
    }
}

/// <summary>
/// The pump, against a model table and a clock this test writes.
///
/// <para>
/// What these can and cannot prove, said plainly: they pin the WALK — dormancy,
/// the no-clock case, the ordering, the side filter — against a table this file
/// supplied. They prove nothing about any .mdl on disk, which is
/// <c>cs_animcheck</c>'s job, and nothing about whether a photon reached the
/// screen, which is a screenshot's.
/// </para>
/// </summary>
public class AnimationEventPumpTests
{
    private static readonly EntityHandle Weapon = new(0x0100_0041u);

    [Fact]
    public void TheMuzzleFlashOfAStartingAnimationReachesTheSink()
    {
        var model = new Clock().Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 1)
                               .Fires(3, "AE_MUZZLEFLASH", cycle: 0f, options: "SMG1 muzzle");
        var sink = new Recorder();

        new AnimationEventPump(model, model, sink).Advance(Weapon, "predicted_viewmodel");

        Assert.True(sink.Fired.Count == 1, "the animation started and its cycle-0 muzzle flash fired");
        Assert.True(sink.Fired[0].Carried.Name == "AE_MUZZLEFLASH", "and it is the event the model carries");
    }

    [Fact]
    public void ADormantEntityIsNotAskedAnything()
    {
        var model = new Clock { Dormant = true }
                        .Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 1)
                        .Fires(3, "AE_MUZZLEFLASH", cycle: 0f, options: "SMG1 muzzle");
        var sink = new Recorder();

        new AnimationEventPump(model, model, sink).Advance(Weapon, "predicted_viewmodel");

        Assert.True(sink.Fired.Count == 0, "a dormant entity fires nothing");
        Assert.True(model.StateAsks == 0,
                    "and is not even asked for its clock — its start time ticks against a live curtime");
    }

    [Fact]
    public void AnEntityWithNoClockFiresNothingHoweverManyEventsItsSequenceCarries()
    {
        // A static prop: sequence 0, duration 0. Its sequence 0 may well carry
        // events, and none of them can ever have happened, because nothing is
        // playing.
        var model = new Clock().Playing(sequence: 0, cycle: 0f, duration: 0f, starts: 0)
                               .Fires(0, "AE_MUZZLEFLASH", cycle: 0f, options: "SMG1 muzzle");
        var sink = new Recorder();

        new AnimationEventPump(model, model, sink).Advance(Weapon, "prop_physics");

        Assert.True(sink.Fired.Count == 0, "an entity with no animation clock fires nothing");
    }

    [Fact]
    public void TheServersEventsAreNotFiredHere()
    {
        var model = new Clock().Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 1)
                               .Fires(3, "AE_NPC_LEFTFOOT", cycle: 0f)
                               .FiresNumbered(3, 3005, cycle: 0f);
        var sink = new Recorder();

        new AnimationEventPump(model, model, sink).Advance(Weapon, "predicted_viewmodel");

        Assert.True(sink.Fired.Count == 0,
                    "a server-side named event and an old-system event below 5000 both stay on the server");
    }

    [Fact]
    public void RefiringTheSameAnimationFiresItsMuzzleFlashAgain()
    {
        var model = new Clock().Playing(sequence: 3, cycle: 0.0f, duration: 0.5f, starts: 1)
                               .Fires(3, "AE_MUZZLEFLASH", cycle: 0f, options: "SMG1 muzzle");
        var sink = new Recorder();
        var pump = new AnimationEventPump(model, model, sink);

        pump.Advance(Weapon, "predicted_viewmodel");
        model.Playing(sequence: 3, cycle: 0.2f, duration: 0.5f, starts: 1);
        pump.Advance(Weapon, "predicted_viewmodel");

        Assert.True(sink.Fired.Count == 1, "the flash fired once for the first shot and not again as it played out");

        // The second shot, 0.1s later: the cycle falls from 0.2 to 0, which is
        // not a wrap and is not a stall.
        model.Playing(sequence: 3, cycle: 0.0f, duration: 0.5f, starts: 2);
        pump.Advance(Weapon, "predicted_viewmodel");

        Assert.True(sink.Fired.Count == 2, "and again for the second shot of the burst");
    }

    [Fact]
    public void OnAWrapTheEndOfTheLapIsFiredBeforeTheStartOfTheNext()
    {
        var model = new Clock().Playing(sequence: 3, cycle: 0.5f, duration: 1.0f, starts: 1)
                               .Fires(3, "AE_CL_PLAYSOUND", cycle: 0.95f, options: "Late")
                               .Fires(3, "AE_CL_PLAYSOUND", cycle: 0.02f, options: "Early");
        var sink = new Recorder();
        var pump = new AnimationEventPump(model, model, sink);

        pump.Advance(Weapon, "ss_npc");            // establishes the cursor at 0
        model.Playing(sequence: 3, cycle: 0.90f, duration: 1.0f, starts: 1);
        pump.Advance(Weapon, "ss_npc");            // crosses nothing but 0..0.90
        sink.Fired.Clear();

        model.Playing(sequence: 3, cycle: 0.05f, duration: 1.0f, starts: 1);
        pump.Advance(Weapon, "ss_npc");

        Assert.True(sink.Fired.Count == 2, "both sides of the wrap fired");
        Assert.True(sink.Fired[0].Carried.Options == "Late",
                    "the end of the lap that finished fired first, because it happened first");
        Assert.True(sink.Fired[1].Carried.Options == "Early",
                    "and the start of the lap that began fired second");
    }

    //-------------------------------------------------------------------------
    // THE EVENT-TABLE MEMO — plan_crossings.md Phase X5.
    //
    // EntitySequenceEventCount was 2.000 crossings a client frame and A12 named
    // it a re-ask: the table comes out of a CStudioHdr in the engine's model
    // cache and is immutable per (model, sequence). These facts measure the
    // memo by COUNTING THE ASKS on the double, never by reading the pump's own
    // report of itself.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A clock that crosses one event every time it is advanced, so each
    /// Advance would read the table if nothing memoised it.
    /// </summary>
    private static Clock FiringClock()
        => new Clock().Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 1)
                      .Fires(3, "AE_MUZZLEFLASH", cycle: 0f, options: "SMG1 muzzle");

    //-------------------------------------------------------------------------
    // ONCE, through the WHOLE pump — step 6d's gate, end to end.
    //
    // The facts in AnimationEventOnceTests pin the band arithmetic on its own.
    // These drive the same sweeps through the dispatcher that reads a clock,
    // reads a table, filters by side and calls a sink, because every one of
    // those four is a place a second fire could be introduced without the
    // arithmetic moving at all.
    //-------------------------------------------------------------------------

    [Fact]
    public void ALoopingSequencesEventReachesTheSinkExactlyOncePerLap()
    {
        var model = new Clock().Fires(3, "AE_CL_PLAYSOUND", cycle: 0.5f, options: "Weapon_SMG1.Reload");
        var sink = new Recorder();
        var pump = new AnimationEventPump(model, model, sink);

        // The shell's own cycle rule, fed a real frame clock. Three laps of a
        // one-second sequence at a sixtieth of a second.
        var record = new SequenceRecord(3, 1f, 0f, 1f, looping: true, 1);

        for (int i = 0; i < 180; i++)
        {
            model.Playing(sequence: 3, cycle: record.Cycle(i / 60f), duration: 1f, starts: 1);
            pump.Advance(Weapon, "predicted_viewmodel");
        }

        Assert.True(sink.Fired.Count == 3,
                    $"three laps, three sounds — got {sink.Fired.Count}");
        Assert.True(pump.FiredCount == 3, "and the pump's own counter agrees");
    }

    [Fact]
    public void AOneShotsEventReachesTheSinkOnceAndNotOnEveryFrameOfItsPinnedTail()
    {
        var model = new Clock { Looping = false }
                        .Fires(3, "AE_CL_PLAYSOUND", cycle: 0.9f, options: "Weapon_SMG1.Empty");
        var sink = new Recorder();
        var pump = new AnimationEventPump(model, model, sink);

        // Half a second of animation watched for five: two hundred and seventy
        // of these three hundred frames read the same pinned 0.999.
        var record = new SequenceRecord(3, 1f, 0f, 0.5f, looping: false, 1);

        for (int i = 0; i < 300; i++)
        {
            model.Playing(sequence: 3, cycle: record.Cycle(i / 60f), duration: 0.5f, starts: 1);
            pump.Advance(Weapon, "predicted_viewmodel");
        }

        Assert.True(sink.Fired.Count == 1,
                    $"one animation, one sound — got {sink.Fired.Count}");
    }

    [Fact]
    public void ThreeLapsOfEventsCostOneTableRead()
    {
        // The memo, measured over the sweep rather than over two frames: a memo
        // that had quietly stopped memoising shows here as this number tracking
        // the lap count instead of settling at one.
        var model = new Clock().Fires(3, "AE_CL_PLAYSOUND", cycle: 0.5f, options: "Weapon_SMG1.Reload");
        var pump = new AnimationEventPump(model, model, new Recorder());

        var record = new SequenceRecord(3, 1f, 0f, 1f, looping: true, 1);

        for (int i = 0; i < 180; i++)
        {
            model.Playing(sequence: 3, cycle: record.Cycle(i / 60f), duration: 1f, starts: 1);
            pump.Advance(Weapon, "predicted_viewmodel");
        }

        Assert.True(model.EventCountAsks == 1,
                    $"one table read for three laps — got {model.EventCountAsks}");
    }

    [Fact]
    public void TheCountIsKeptPerEntityAndNotOnlyForTheWholeClient()
    {
        // The client total moves for every viewmodel in the map, so a gate that
        // watched it for growth would be watching noise. What "fires once"
        // means is a statement about ONE entity.
        var quiet = new EntityHandle(0x0100_0055u);

        var model = new Clock().Fires(3, "AE_CL_PLAYSOUND", cycle: 0f, options: "Weapon_SMG1.Reload");
        var pump = new AnimationEventPump(model, model, new Recorder());

        model.Playing(sequence: 3, cycle: 0f, duration: 0.5f, starts: 1);
        pump.Advance(Weapon, "predicted_viewmodel");

        Assert.True(pump.FiredOn(Weapon) == 1, "the entity that started an animation fired one");
        Assert.True(pump.FiredOn(quiet) == 0, "and one nothing has happened to fired none");
        Assert.True(pump.FiredCount == 1, "with the client total agreeing");
    }

    [Fact]
    public void ThePerEntityCountOfALoopingSequenceIsOncePerLap()
    {
        var model = new Clock().Fires(3, "AE_CL_PLAYSOUND", cycle: 0.5f, options: "Weapon_SMG1.Reload");
        var pump = new AnimationEventPump(model, model, new Recorder());

        var record = new SequenceRecord(3, 1f, 0f, 1f, looping: true, 1);

        for (int i = 0; i < 180; i++)
        {
            model.Playing(sequence: 3, cycle: record.Cycle(i / 60f), duration: 1f, starts: 1);
            pump.Advance(Weapon, "predicted_viewmodel");
        }

        Assert.True(pump.FiredOn(Weapon) == 3,
                    $"three laps, three fires on this entity — got {pump.FiredOn(Weapon)}");
    }

    [Fact]
    public void AnEntityGoingAwayTakesItsPerEntityCountWithIt()
    {
        // HANDLES ARE RECYCLED. A count left behind is not stale, it is
        // inherited: the next entity to wear this number would be reported as
        // having already fired, and a gate reading the delta would see none.
        var model = new Clock().Fires(3, "AE_CL_PLAYSOUND", cycle: 0f, options: "Weapon_SMG1.Reload");
        var pump = new AnimationEventPump(model, model, new Recorder());

        model.Playing(sequence: 3, cycle: 0f, duration: 0.5f, starts: 1);
        pump.Advance(Weapon, "predicted_viewmodel");
        Assert.True(pump.FiredOn(Weapon) == 1, "it fired once");

        pump.Forget(Weapon);

        Assert.True(pump.FiredOn(Weapon) == 0,
                    "and the entity that inherits the handle starts from zero");
    }

    [Fact]
    public void ALevelChangeClearsThePerEntityCountsToo()
    {
        var model = new Clock().Fires(3, "AE_CL_PLAYSOUND", cycle: 0f, options: "Weapon_SMG1.Reload");
        var pump = new AnimationEventPump(model, model, new Recorder());

        model.Playing(sequence: 3, cycle: 0f, duration: 0.5f, starts: 1);
        pump.Advance(Weapon, "predicted_viewmodel");

        pump.Reset();

        Assert.True(pump.FiredOn(Weapon) == 0, "the per-entity count goes with the level");
        Assert.True(pump.FiredCount == 0, "and so does the total");
    }

    [Fact]
    public void TheEventTableIsReadOnceAndThenMemoised()
    {
        var model = FiringClock();
        var pump = new AnimationEventPump(model, model, new Recorder());

        pump.Advance(Weapon, "predicted_viewmodel");
        int afterFirst = model.EventCountAsks;

        for (int i = 0; i < 8; i++)
        {
            model.Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: i + 2);
            pump.Advance(Weapon, "predicted_viewmodel");
        }

        Assert.True(afterFirst == 1, "the first look reads the table out of the model");
        Assert.True(model.EventCountAsks == 1,
            "and the eight after it read nothing — this is the 2.000 crossings a client frame "
            + "A12 called a re-ask of an immutable studio header");
    }

    [Fact]
    public void AnEmptyEventTableIsMemoisedToo()
    {
        // A sequence that carries nothing. Most do, so "nothing here" is the
        // answer worth keeping MOST: not keeping it would leave the crossing in
        // place for exactly the entities that ask most often.
        var model = new Clock().Playing(sequence: 7, cycle: 0.4f, duration: 0.5f, starts: 1);
        var pump = new AnimationEventPump(model, model, new Recorder());

        pump.Advance(Weapon, "ss_prop");
        model.Playing(sequence: 7, cycle: 0.4f, duration: 0.5f, starts: 2);
        pump.Advance(Weapon, "ss_prop");

        Assert.True(model.EventCountAsks == 1,
            "an empty table is a memoised answer like any other, not a miss that is retried "
            + "every frame forever");
    }

    [Fact]
    public void ANetworkUpdateDropsTheMemoBecauseTheModelMayHaveMoved()
    {
        var model = FiringClock();
        var pump = new AnimationEventPump(model, model, new Recorder());

        pump.Advance(Weapon, "predicted_viewmodel");

        pump.NoteDataChanged(Weapon);

        model.Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 2);
        pump.Advance(Weapon, "predicted_viewmodel");

        Assert.True(model.EventCountAsks == 2,
            "the model index is networked into the shell's own member and never reaches managed, "
            + "so the memo is dropped on the UPDATE — a model cannot change without one, which is "
            + "what makes this a closure rather than a hope");
    }

    [Fact]
    public void ANetworkUpdateDoesNotResetTheAnimationClock()
    {
        var model = FiringClock();
        var sink = new Recorder();
        var pump = new AnimationEventPump(model, model, sink);

        pump.Advance(Weapon, "predicted_viewmodel");
        sink.Fired.Clear();

        pump.NoteDataChanged(Weapon);
        pump.Advance(Weapon, "predicted_viewmodel");

        Assert.True(sink.Fired.Count == 0,
            "dropping the CURSOR on a net update would re-establish it as Fresh, which claims "
            + "sequence 0 and would fire a whole event list on the next frame — the memo goes, "
            + "the clock stays");
    }

    [Fact]
    public void AnEntityGoingAwayTakesItsMemoWithIt()
    {
        var model = FiringClock();
        var pump = new AnimationEventPump(model, model, new Recorder());

        pump.Advance(Weapon, "predicted_viewmodel");
        pump.Forget(Weapon);

        model.Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 2);
        pump.Advance(Weapon, "predicted_viewmodel");

        Assert.True(model.EventCountAsks == 2,
            "handles are recycled, so a table left behind would be handed to whatever entity the "
            + "number came back as — a different model's events on the wrong entity");
    }

    [Fact]
    public void ALevelChangeEmptiesTheMemo()
    {
        var model = FiringClock();
        var pump = new AnimationEventPump(model, model, new Recorder());

        pump.Advance(Weapon, "predicted_viewmodel");
        pump.Reset();

        Assert.True(pump.MemoisedCount == 0,
            "every model index in the memo belongs to the level that is going");

        model.Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 2);
        pump.Advance(Weapon, "predicted_viewmodel");

        Assert.True(model.EventCountAsks == 2, "and the next level reads its own tables");
    }

    [Fact]
    public void TwoSequencesOnOneEntityAreMemoisedSeparately()
    {
        var model = new Clock().Fires(3, "AE_MUZZLEFLASH", cycle: 0f)
                               .Fires(4, "AE_CL_PLAYSOUND", cycle: 0f);
        var pump = new AnimationEventPump(model, model, new Recorder());

        model.Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 1);
        pump.Advance(Weapon, "predicted_viewmodel");

        model.Playing(sequence: 4, cycle: 0.4f, duration: 0.5f, starts: 2);
        pump.Advance(Weapon, "predicted_viewmodel");

        model.Playing(sequence: 3, cycle: 0.4f, duration: 0.5f, starts: 3);
        pump.Advance(Weapon, "predicted_viewmodel");

        Assert.True(model.EventCountAsks == 2,
            "the memo is per (entity, sequence), so going back to a sequence already seen reads "
            + "nothing — an entity that alternates two animations reads two tables, not one a frame");
    }

    /// <summary>A table and a clock, both written by this test.</summary>
    private sealed class Clock : IEngineEntityRender, IClientStandalones
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public string? GetModelKeyValueText(string modelName) => null;

        public readonly Dictionary<int, List<ModelAnimationEvent>> Events = new();

        public bool Dormant;
        public int StateAsks;

        /// <summary>
        /// Whether the sequence wraps. TRUE by default, which is what this
        /// double always answered before step 6d needed a one-shot: a pinned
        /// cycle and a wrapping one are the same number on every frame but the
        /// one where the lap ends.
        /// </summary>
        public bool Looping = true;

        /// <summary>
        /// How many times the event TABLE was read out of the model. The memo
        /// plan_crossings.md Phase X5 adds is measured by this and by nothing
        /// else: a memo asserted by its own report would agree with itself.
        /// </summary>
        public int EventCountAsks;

        private int _sequence = -1;
        private float _cycle;
        private float _duration;
        private int _starts;

        public Clock Playing(int sequence, float cycle, float duration, int starts)
        {
            _sequence = sequence;
            _cycle = cycle;
            _duration = duration;
            _starts = starts;
            return this;
        }

        public Clock Fires(int sequence, string name, float cycle, string options = "")
            => Add(sequence, new ModelAnimationEvent(0, cycle, 0,
                                                     AnimationEventTypes.NewEventSystem, name, options));

        public Clock FiresNumbered(int sequence, int eventId, float cycle)
            => Add(sequence, new ModelAnimationEvent(0, cycle, eventId, 0, string.Empty, string.Empty));

        private Clock Add(int sequence, ModelAnimationEvent ev)
        {
            if (!Events.TryGetValue(sequence, out var list))
                Events[sequence] = list = new List<ModelAnimationEvent>();

            list.Add(ev with { Index = list.Count });
            return this;
        }

        public bool IsDormant(EntityHandle entity) => Dormant;

        public bool SequenceState(EntityHandle entity, out int sequence, out float cycle,
                                  out float duration, out bool looping, out int starts)
        {
            StateAsks++;
            sequence = _sequence;
            cycle = _cycle;
            duration = _duration;
            looping = Looping;
            starts = _starts;
            return _sequence >= 0;
        }

        public int SequenceEventCount(EntityHandle entity, int sequence)
        {
            EventCountAsks++;
            return Events.TryGetValue(sequence, out var list) ? list.Count : 0;
        }

        public bool SequenceEvent(EntityHandle entity, int sequence, int index,
                                  out float cycle, out int eventId, out int type,
                                  out string name, out string options)
        {
            cycle = 0f; eventId = 0; type = 0; name = string.Empty; options = string.Empty;

            if (!Events.TryGetValue(sequence, out var list) || index < 0 || index >= list.Count)
                return false;

            var ev = list[index];
            cycle = ev.Cycle; eventId = ev.EventId; type = ev.Type;
            name = ev.Name; options = ev.Options;
            return true;
        }

        // The registry half. The pump's Frame() walks it; these tests drive
        // Advance directly, so an empty world is the honest answer.
        public IReadOnlyList<ClientStandaloneEntity> Live() => Array.Empty<ClientStandaloneEntity>();

        // Row C4's index lookup. These doubles are about animation and the HUD,
        // not about what a gib hit, so they answer "not one of ours" for every
        // index -- which is the honest answer for a table nothing was put in.
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;

        private static Exception Refuse(string member)
            => new InvalidOperationException($"the pump read {member}, which it has no business reading");

        public string GetModelName(EntityHandle entity) => throw Refuse(nameof(GetModelName));
        public int GetTextureFrame(EntityHandle entity) => throw Refuse(nameof(GetTextureFrame));
        public int LookupSequence(EntityHandle entity, string name) => throw Refuse(nameof(LookupSequence));
        public int LookupSequenceByActivity(EntityHandle entity, string activityName) => throw Refuse(nameof(LookupSequenceByActivity));
        public float SequenceDuration(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceDuration));
        public float SequenceGroundSpeed(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceGroundSpeed));
        public int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains) => throw Refuse(nameof(ProceduralBones));
        public void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order, bool restart) => throw Refuse(nameof(SetAnimLayer));
        public int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order, out float cycle) => throw Refuse(nameof(AnimLayer));
        public int LookupAttachment(EntityHandle entity, string name) => throw Refuse(nameof(LookupAttachment));
        public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles) => throw Refuse(nameof(GetAttachment));
        public int AttachmentCount(EntityHandle entity) => throw Refuse(nameof(AttachmentCount));
        public string AttachmentName(EntityHandle entity, int attachment) => throw Refuse(nameof(AttachmentName));
        public int BoneCount(EntityHandle entity, out int cached) => throw Refuse(nameof(BoneCount));
        public StandaloneBones? BoneMatrices(EntityHandle entity) => throw Refuse(nameof(BoneMatrices));
        public bool IsPoseable(EntityHandle entity) => throw Refuse(nameof(IsPoseable));
        public string StudioName(EntityHandle entity) => throw Refuse(nameof(StudioName));
        public int PoseParameterCount(EntityHandle entity) => throw Refuse(nameof(PoseParameterCount));
        public int LookupPoseParameter(EntityHandle entity, string name) => throw Refuse(nameof(LookupPoseParameter));
        public string PoseParameterName(EntityHandle entity, int parameter) => throw Refuse(nameof(PoseParameterName));
        public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end) => throw Refuse(nameof(PoseParameterRange));
        public float SetPoseParameter(EntityHandle entity, int parameter, float value) => throw Refuse(nameof(SetPoseParameter));
        public float GetPoseParameter(EntityHandle entity, int parameter) => throw Refuse(nameof(GetPoseParameter));
        public void SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs) => throw Refuse(nameof(SetCustomDraw));
        public void SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha) => throw Refuse(nameof(SetBlending));
        public void SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha = 0f) => throw Refuse(nameof(SetFade));
        public void SetModel(EntityHandle entity, string model) => throw Refuse(nameof(SetModel));
        // Added when the shadow lane extended IEngineEntityRender; these doubles
        // predate it and none of their tests reach a shadow.
        public ShadowCast ShadowCast(EntityHandle entity, out ShadowCastChoice asked) => throw Refuse(nameof(ShadowCast));
        public void SetOverlayModel(EntityHandle entity, int modelIndex) => throw Refuse(nameof(SetOverlayModel));
        public void SetModelScale(EntityHandle entity, float scale) => throw Refuse(nameof(SetModelScale));
        public void SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex) => throw Refuse(nameof(SetBoneMergeSource));
        public void SetSequence(EntityHandle entity, int sequence) => throw Refuse(nameof(SetSequence));
        public void SetViewModel(EntityHandle entity, bool enable) => throw Refuse(nameof(SetViewModel));
        public void PlaySequence(EntityHandle entity, int sequence, float playbackRate, bool restart) => throw Refuse(nameof(PlaySequence));
        public void SetCycle(EntityHandle entity, float cycle) => throw Refuse(nameof(SetCycle));
        public void SetSkin(EntityHandle entity, int skin) => throw Refuse(nameof(SetSkin));
        public void SetTextureFrame(EntityHandle entity, int frame) => throw Refuse(nameof(SetTextureFrame));
        public void SetTransmitAlways(EntityHandle entity, bool always) => throw Refuse(nameof(SetTransmitAlways));
        public bool DetectInSkybox(EntityHandle entity) => throw Refuse(nameof(DetectInSkybox));
        public void SetVisible(EntityHandle entity, bool visible) => throw Refuse(nameof(SetVisible));
    }

    private sealed class Recorder : IAnimationEventSink
    {
        public readonly List<AnimationEventFired> Fired = new();

        public void Fire(in AnimationEventFired fired) => Fired.Add(fired);
    }
}
