using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_crossings.md Phases X2 and X3: the animation clock managed writes and
/// the dormancy the engine pushes, answered from a mirror instead of polled 102
/// times a client frame.
///
/// <para>
/// <b>What these facts are about is the RULES and the CLOSURE, not the
/// plumbing.</b> Push-on-change makes staleness impossible by construction, so
/// the two things that can go wrong are different: managed's copy of
/// <c>SetPlayingSequence</c>'s rules drifting from the shell's, and a write site
/// nobody pushed from. The first half of this file pins the rules against the
/// C++ line by line; the second half pins the write-site censuses AS FACTS, so
/// a fourth writer of <c>m_bDormant</c> is a red test rather than a divergence
/// somebody notices months later because an entity stopped animating.
/// </para>
///
/// <para>
/// <b>The side is the binding's own.</b> The thread-local side the reroute once
/// read is gone (plan_side_isolation P5): each side runs its own copy of the
/// Abi, so <c>OnTheClient</c>/<c>OnTheServer</c> only name the side a fact is
/// about.
/// </para>
/// </summary>
public class StandaloneMirrorTests
{
    private static EntityHandle H(uint value) => new(value);

    //-------------------------------------------------------------------------
    // SetPlayingSequence's rules, restated in managed code. Each of these is a
    // line of shell_standalone_client.cpp:4201-4241.
    //-------------------------------------------------------------------------

    [Fact]
    public void AFreshRecordIsWhatTheShellConstructorWrites()
    {
        SequenceRecord fresh = SequenceRecord.Fresh;

        Assert.True(fresh.Sequence == 0 && fresh.PlaybackRate == 1f && fresh.StartTime == 0f
                    && fresh.Duration == 0f && !fresh.Looping && fresh.Starts == 0,
            "an entity nothing has played a sequence on reads as the C_StandaloneEntity "
            + "constructor left it, so the mirror and the shell agree with no crossing");
    }

    [Fact]
    public void ARateOfZeroBecomesOne()
    {
        SequenceRecord played = SequenceRecord.Fresh.Play(sequence: 3, playbackRate: 0f,
                                                          restart: false, now: 10f);

        Assert.True(played.PlaybackRate == 1f,
            "a playback rate of zero means 'default speed' and becomes 1, as the shell does — "
            + "kept at zero it would freeze the cycle on its first frame forever");
    }

    [Fact]
    public void ANegativeRateBecomesOne()
    {
        SequenceRecord played = SequenceRecord.Fresh.Play(sequence: 3, playbackRate: -2f,
                                                          restart: false, now: 10f);

        Assert.True(played.PlaybackRate == 1f, "a negative playback rate becomes 1, as the shell does");
    }

    [Fact]
    public void ANegativeSequenceBecomesZero()
    {
        SequenceRecord played = SequenceRecord.Fresh.Play(sequence: -4, playbackRate: 1f,
                                                          restart: true, now: 10f);

        Assert.True(played.Sequence == 0,
            "a negative sequence is clamped to 0 rather than refused, so a lookup that missed "
            + "plays the reference pose — which is what the shell does");
    }

    [Fact]
    public void TheSameSequenceUnrestartedKeepsItsClock()
    {
        SequenceRecord first = SequenceRecord.Fresh.Play(7, 1f, restart: false, now: 10f);
        SequenceRecord again = first.Play(7, 2f, restart: false, now: 40f);

        Assert.True(again.StartTime == 10f,
            "re-sending the sequence already playing does NOT restart its clock — a system that "
            + "re-sends what it is playing every frame would never get past its own first frame");
    }

    [Fact]
    public void TheSameSequenceUnrestartedKeepsItsStartCount()
    {
        SequenceRecord first = SequenceRecord.Fresh.Play(7, 1f, restart: false, now: 10f);
        SequenceRecord again = first.Play(7, 2f, restart: false, now: 40f);

        Assert.True(again.Starts == first.Starts,
            "the restart counter is bumped with the clock and nowhere else, so the two cannot "
            + "disagree — the shell's own comment on m_nSequenceStarts");
    }

    [Fact]
    public void TheSameSequenceUnrestartedStillMovesTheRate()
    {
        SequenceRecord first = SequenceRecord.Fresh.Play(7, 1f, restart: false, now: 10f);
        SequenceRecord again = first.Play(7, 2.5f, restart: false, now: 40f);

        Assert.True(again.PlaybackRate == 2.5f,
            "the early return moves the rate and only the rate, which is the one thing that "
            + "path exists to do");
    }

    [Fact]
    public void TheSameSequenceUnrestartedKeepsTheModelsTwoNumbers()
    {
        SequenceRecord playing = SequenceRecord.Fresh.Play(7, 1f, restart: true, now: 10f)
                                                     .WithInfo(duration: 2f, looping: true);

        SequenceRecord again = playing.Play(7, 3f, restart: false, now: 40f);

        Assert.True(again.Duration == 2f && again.Looping,
            "the early return keeps the clock, so it keeps the duration that clock is measured "
            + "against — zeroing them here would make a playing animation answer 'no animation'");
    }

    [Fact]
    public void ARestartOfTheSameSequenceBumpsTheStartCount()
    {
        SequenceRecord first = SequenceRecord.Fresh.Play(7, 1f, restart: true, now: 10f);
        SequenceRecord again = first.Play(7, 1f, restart: true, now: 40f);

        Assert.True(again.Starts == first.Starts + 1 && again.StartTime == 40f,
            "a restart is the caller saying the same sequence fired AGAIN, so the clock and the "
            + "counter both move — which is the only unambiguous 'this is playing again'");
    }

    [Fact]
    public void ADifferentSequenceBumpsTheStartCount()
    {
        SequenceRecord first = SequenceRecord.Fresh.Play(7, 1f, restart: false, now: 10f);
        SequenceRecord other = first.Play(8, 1f, restart: false, now: 40f);

        Assert.True(other.Starts == first.Starts + 1 && other.StartTime == 40f && other.Sequence == 8,
            "a real change restarts the clock and bumps the counter");
    }

    [Fact]
    public void ARealChangeClearsTheModelsTwoNumbersUntilTheyAreReRead()
    {
        SequenceRecord playing = SequenceRecord.Fresh.Play(7, 1f, restart: true, now: 10f)
                                                     .WithInfo(duration: 2f, looping: true);

        SequenceRecord other = playing.Play(9, 1f, restart: false, now: 40f);

        Assert.True(other.Duration == 0f && !other.Looping,
            "the shell zeroes the duration and the loop flag before it consults the model, and so "
            + "does this: a caller that cannot reach the model leaves 'no animation', not the "
            + "PREVIOUS sequence's length, which would run the new one at the old one's speed");
    }

    //-------------------------------------------------------------------------
    // SequenceCycle's arithmetic (shell_standalone_client.cpp:4381-4397).
    //-------------------------------------------------------------------------

    [Fact]
    public void ADurationOfZeroIsNoAnimationRatherThanADivision()
    {
        Assert.True(SequenceRecord.Fresh.Cycle(now: 100f) == 0f,
            "a duration of zero is the honest way to say this model has no animation, and it is "
            + "NOT the same fact as a cycle of zero");
    }

    [Fact]
    public void AClockThatHasNotStartedAnswersZeroRatherThanANegativeCycle()
    {
        SequenceRecord record = new(1, 1f, startTime: 50f, duration: 2f, looping: false, starts: 1);

        Assert.True(record.Cycle(now: 10f) == 0f,
            "a negative elapsed time answers 0, as the shell does — a negative cycle would index "
            + "backwards through an animation's events");
    }

    [Fact]
    public void ALoopingSequenceKeepsOnlyTheFraction()
    {
        SequenceRecord record = new(1, 1f, startTime: 0f, duration: 2f, looping: true, starts: 1);

        Assert.True(Math.Abs(record.Cycle(now: 5f) - 0.5f) < 0.0001f,
            "two and a half laps of a two-second looping sequence is half way through the third");
    }

    [Fact]
    public void AFinishedOneShotHoldsItsLastFrame()
    {
        SequenceRecord record = new(1, 1f, startTime: 0f, duration: 2f, looping: false, starts: 1);

        Assert.True(record.Cycle(now: 100f) == 0.999f,
            "a one-shot clamps rather than wrapping, exactly as the shell does — a fire animation "
            + "that returned to the ready pose on its own would look like a shot that never happened");
    }

    [Fact]
    public void ThePlaybackRateMultipliesTheCycle()
    {
        SequenceRecord record = new(1, playbackRate: 2f, startTime: 0f, duration: 4f,
                                    looping: true, starts: 1);

        Assert.True(Math.Abs(record.Cycle(now: 1f) - 0.5f) < 0.0001f,
            "a sequence played at double rate is half way through a four-second animation after "
            + "one second");
    }

    //-------------------------------------------------------------------------
    // The store, and the lifetime rule that keeps recycled handles honest.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnEntityNothingHasPlayedAnythingOnReadsAsFresh()
    {
        var mirror = new StandaloneMirror();

        Assert.True(mirror.Sequence(H(12)).Duration == 0f && mirror.Sequence(H(12)).Starts == 0,
            "most of this hierarchy is static props nothing ever animates, and they must read as "
            + "the shell's constructor left them without a record ever being made");
    }

    [Fact]
    public void TheModelsTwoNumbersArriveAfterTheWrite()
    {
        var mirror = new StandaloneMirror();

        mirror.NoteSequence(H(12), 4, 1f, restart: false, now: 10f);
        mirror.NoteSequenceInfo(H(12), duration: 1.5f, looping: true);

        Assert.True(mirror.Sequence(H(12)).Duration == 1.5f && mirror.Sequence(H(12)).Looping,
            "the duration and loop flag come from the model's studio header, which managed has no "
            + "copy of, so they are read back once per sequence change");
    }

    [Fact]
    public void TheModelsTwoNumbersDoNotInventARecord()
    {
        var mirror = new StandaloneMirror();

        mirror.NoteSequenceInfo(H(12), duration: 1.5f, looping: true);

        Assert.True(mirror.SequenceCount == 0,
            "a record is what a WRITE creates; a read-back for an entity nothing wrote to would "
            + "be a record with a duration and no clock, which reads as an animation at cycle 0");
    }

    [Fact]
    public void ARemovedEntityLosesItsSequenceRecord()
    {
        var mirror = new StandaloneMirror();

        mirror.NoteSequence(H(12), 4, 1f, restart: true, now: 10f);
        mirror.Forget(H(12));

        Assert.True(mirror.Sequence(H(12)).Starts == 0 && mirror.SequenceCount == 0,
            "handles are recycled, so a record left behind is not stale — it is INHERITED by "
            + "whatever entity next wears the number");
    }

    [Fact]
    public void ARemovedEntityLosesItsDormancyToo()
    {
        var mirror = new StandaloneMirror();

        mirror.NoteDormancy(H(12), dormant: true);
        mirror.Forget(H(12));

        Assert.True(!mirror.IsDormant(H(12)) && mirror.DormancyCount == 0,
            "an inherited dormancy of true would make the next entity to wear this handle "
            + "invisible to every managed reader while the engine went on drawing it");
    }

    [Fact]
    public void AnEntityNobodyHasPushedForIsNotDormant()
    {
        var mirror = new StandaloneMirror();

        Assert.True(!mirror.IsDormant(H(12)),
            "false is the shell constructor's value and the slot's answer for a handle that "
            + "resolves to nothing, so the default cannot disagree with either");
    }

    [Fact]
    public void ADormancyPushForAHandleTheMirrorHasNotSeenIsKept()
    {
        var mirror = new StandaloneMirror();

        mirror.NoteDormancy(H(12), dormant: true);

        Assert.True(mirror.IsDormant(H(12)),
            "NotifyShouldTransmit can reach an entity before its first data update names a "
            + "classname, so the store is keyed by handle and does not require a prior creation");
    }

    [Fact]
    public void ALevelChangeEmptiesBoth()
    {
        var mirror = new StandaloneMirror();

        mirror.NoteSequence(H(12), 4, 1f, restart: true, now: 10f);
        mirror.NoteDormancy(H(13), dormant: true);
        mirror.Clear();

        Assert.True(mirror.SequenceCount == 0 && mirror.DormancyCount == 0,
            "every handle in the mirror belongs to the level that is going");
    }

    //-------------------------------------------------------------------------
    // The reroute itself: the binding answers from the mirror, on the client,
    // when it is switched on, and from the slot otherwise.
    //
    // In a test process Engine is uninitialised, so both slots answer their
    // "no" — false and false. That is what makes these facts able to tell the
    // two paths apart at all: a mirror saying TRUE and a slot saying FALSE.
    //-------------------------------------------------------------------------

    // No thread marks a side since plan_side_isolation P5: each binding's
    // engine is its own side's, so these name the side a fact is about and
    // switch nothing.
    private static void OnTheClient(Action body) => body();

    private static void OnTheServer(Action body) => body();

    [Fact]
    public void DormancyIsAnsweredFromTheMirror()
    {
        var mirror = new StandaloneMirror();
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Client), mirror);

        mirror.NoteDormancy(H(12), dormant: true);

        OnTheClient(() => Assert.True(render.IsDormant(H(12)),
            "IsDormant reads the mirror the engine pushes into — this is the 51.000 crossings a "
            + "client frame Phase X3 removes, and no port could ever have removed"));
    }

    [Fact]
    public void DormancyFallsBackToTheSlotWhenTheRerouteIsSwitchedOff()
    {
        var mirror = new StandaloneMirror { Rerouted = false };
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Client), mirror);

        mirror.NoteDormancy(H(12), dormant: true);

        OnTheClient(() => Assert.True(!render.IsDormant(H(12)),
            "with the reroute off the read goes to the ABI slot, which in a process with no "
            + "engine answers false — this is the census's 'before' arm and it must really differ"));
    }

    [Fact]
    public void TheServerIsNotAnsweredFromTheMirror()
    {
        var mirror = new StandaloneMirror();
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Server), mirror);

        mirror.NoteDormancy(H(12), dormant: true);

        OnTheServer(() => Assert.True(!render.IsDormant(H(12)),
            "StandaloneIsDormant is a CLIENT-ONLY slot — dormancy is a client concept — so the "
            + "server keeps reading the slot, which answers false there and always did"));
    }

    [Fact]
    public void TheAnimationClockIsAnsweredFromTheMirror()
    {
        var mirror = new StandaloneMirror();
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Client), mirror);

        mirror.NoteSequence(H(12), 6, 1f, restart: true, now: 0f);
        mirror.NoteSequenceInfo(H(12), duration: 2f, looping: true);

        OnTheClient(() =>
        {
            bool alive = render.SequenceState(H(12), out int sequence, out _,
                                              out float duration, out bool looping, out int starts);

            Assert.True(alive && sequence == 6 && duration == 2f && looping && starts == 1,
                "SequenceState answers from managed's own record — this is the other 51.000 "
                + "crossings a client frame, of state managed writes 0.051 times a frame");
        });
    }

    [Fact]
    public void TheAnimationClockFallsBackToTheSlotWhenTheRerouteIsSwitchedOff()
    {
        var mirror = new StandaloneMirror { Rerouted = false };
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Client), mirror);

        mirror.NoteSequence(H(12), 6, 1f, restart: true, now: 0f);
        mirror.NoteSequenceInfo(H(12), duration: 2f, looping: true);

        OnTheClient(() =>
        {
            bool alive = render.SequenceState(H(12), out _, out _, out float duration, out _, out _);

            Assert.True(!alive && duration == 0f,
                "with the reroute off the read goes to the ABI slot, which answers false with "
                + "every out parameter already written — the census's 'before' arm");
        });
    }

    [Fact]
    public void TheServersAnimationClockIsNotAnsweredFromTheMirror()
    {
        var mirror = new StandaloneMirror();
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Server), mirror);

        mirror.NoteSequence(H(12), 6, 1f, restart: true, now: 0f);

        OnTheServer(() => Assert.True(
            !render.SequenceState(H(12), out _, out _, out _, out _, out _),
            "StandaloneSequenceState is client-only — the cycle is a clock the two sides "
            + "legitimately disagree about — so the server keeps reading the slot"));
    }

    [Fact]
    public void PlayingASequenceWritesTheRecordThrough()
    {
        var mirror = new StandaloneMirror();
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Client), mirror);

        OnTheClient(() =>
        {
            render.PlaySequence(H(12), 6, 1f, restart: true);

            Assert.True(mirror.Sequence(H(12)).Sequence == 6 && mirror.Sequence(H(12)).Starts == 1,
                "PlaySequence is the only in-tree path to the shell's SetPlayingSequence, so it is "
                + "where the second copy is fed — one writer, two copies, one call");
        });
    }

    /// <summary>
    /// THE REGRESSION FACT FOR THE ONE DIVERGENCE THE LIVE GATE ACTUALLY FOUND.
    ///
    /// <para>
    /// The write was briefly gated on <c>Rerouted</c> as well as on the side,
    /// so taking the census's "before" arm stopped feeding the mirror.
    /// <c>cs_cl_mirror_check</c> reported <c>ss_viewmodel: starts mirror=167
    /// slot=185</c> — eighteen behind, growing by one per sequence change and
    /// never recovering, because nothing in a push mirror ever resynchronises.
    /// That is the failure mode Part C2 names, produced by the measurement
    /// instrument itself, and caught by the gate the plan required.
    /// </para>
    /// </summary>
    [Fact]
    public void AWriteIsRecordedEvenWithTheRerouteSwitchedOff()
    {
        var mirror = new StandaloneMirror { Rerouted = false };
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Client), mirror);

        OnTheClient(() =>
        {
            render.PlaySequence(H(12), 6, 1f, restart: true);

            Assert.True(mirror.Sequence(H(12)).Starts == 1,
                "the reroute is a READING control — a write it could switch off would make the "
                + "mirror permanently wrong by however many writes happened while it was off, "
                + "which is exactly what the live gate caught");
        });
    }

    [Fact]
    public void AServerSideWriteDoesNotEnterTheMirror()
    {
        var mirror = new StandaloneMirror();
        var render = SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Server), mirror);

        OnTheServer(() =>
        {
            render.PlaySequence(H(12), 6, 1f, restart: true);

            Assert.True(mirror.SequenceCount == 0,
                "StandaloneSetSequence is wired on BOTH sides, so a server write reaching the "
                + "mirror would stamp a record with the SERVER's curtime and answer it back to a "
                + "reader expecting the client's — a plausible cycle rather than an error");
        });
    }

    //-------------------------------------------------------------------------
    // The divergence gate: what cs_cl_mirror_check reports, and what it does not.
    //-------------------------------------------------------------------------

    private static List<MirrorComparison> Compare(SequenceRecord mirror, bool mirrorDormant,
                                                  bool slotAlive, int slotSequence, float slotCycle,
                                                  float slotDuration, bool slotLooping,
                                                  int slotStarts, bool slotDormant, float now = 0f)
    {
        var rows = new List<MirrorComparison>();

        MirrorCheckCommand.Compare(3, "ss_prop", mirror, mirrorDormant, now,
                                   slotAlive, slotSequence, slotCycle, slotDuration,
                                   slotLooping, slotStarts, slotDormant, rows.Add);

        return rows;
    }

    [Fact]
    public void AgreementReportsNothing()
    {
        SequenceRecord record = new(6, 1f, startTime: 0f, duration: 2f, looping: true, starts: 3);

        var rows = Compare(record, mirrorDormant: false,
                           slotAlive: true, slotSequence: 6, slotCycle: 0.5f, slotDuration: 2f,
                           slotLooping: true, slotStarts: 3, slotDormant: false, now: 1f);

        Assert.True(rows.Count == 0, "a mirror that equals its slot reports nothing at all");
    }

    [Fact]
    public void AMissedDormancyPushIsReported()
    {
        var rows = Compare(SequenceRecord.Fresh, mirrorDormant: false,
                           slotAlive: true, slotSequence: 0, slotCycle: 0f, slotDuration: 0f,
                           slotLooping: false, slotStarts: 0, slotDormant: true);

        Assert.True(rows.Count == 1 && rows[0].Field == "dormant",
            "a write site that did not push is the failure mode X3 introduces, and this is the "
            + "shape it takes: the engine says dormant and the mirror says live");
    }

    [Fact]
    public void AMissedSequenceWriteIsReported()
    {
        SequenceRecord record = new(6, 1f, 0f, 2f, true, 3);

        var rows = Compare(record, mirrorDormant: false,
                           slotAlive: true, slotSequence: 9, slotCycle: 0.5f, slotDuration: 2f,
                           slotLooping: true, slotStarts: 3, slotDormant: false, now: 1f);

        Assert.True(rows.Count == 1 && rows[0].Field == "sequence"
                    && rows[0].Mirror == "6" && rows[0].Slot == "9",
            "the mirror playing one sequence while the shell plays another is the visible half of "
            + "a divergence — the wrong animation on screen");
    }

    [Fact]
    public void AMissedRestartIsReported()
    {
        SequenceRecord record = new(6, 1f, 0f, 2f, true, 3);

        var rows = Compare(record, mirrorDormant: false,
                           slotAlive: true, slotSequence: 6, slotCycle: 0.5f, slotDuration: 2f,
                           slotLooping: true, slotStarts: 4, slotDormant: false, now: 1f);

        Assert.True(rows.Count == 1 && rows[0].Field == "starts",
            "the restart counter is the one field a same-sequence write moves, so it is where a "
            + "write site that reached the shell and not the mirror shows up first");
    }

    [Fact]
    public void ADisagreeingDurationIsReported()
    {
        SequenceRecord record = new(6, 1f, 0f, 2f, true, 3);

        var rows = Compare(record, mirrorDormant: false,
                           slotAlive: true, slotSequence: 6, slotCycle: 0.5f, slotDuration: 3f,
                           slotLooping: true, slotStarts: 3, slotDormant: false, now: 1f);

        Assert.True(rows.Count >= 1 && rows[0].Field == "duration",
            "the duration is the one number that crosses the boundary per sequence change, so a "
            + "read-back that did not happen is caught here");
    }

    [Fact]
    public void ACycleWithinToleranceIsNotADivergence()
    {
        SequenceRecord record = new(6, 1f, 0f, 2f, true, 3);

        var rows = Compare(record, mirrorDormant: false,
                           slotAlive: true, slotSequence: 6,
                           slotCycle: 0.5f + (MirrorCheckCommand.CycleTolerance / 2f),
                           slotDuration: 2f, slotLooping: true, slotStarts: 3,
                           slotDormant: false, now: 1f);

        Assert.True(rows.Count == 0,
            "the two cycles are computed from the same curtime a few instructions apart, so the "
            + "tolerance is there to report a FRAME of drift, not float noise");
    }

    [Fact]
    public void ACycleBeyondToleranceIsADivergence()
    {
        SequenceRecord record = new(6, 1f, 0f, 2f, true, 3);

        var rows = Compare(record, mirrorDormant: false,
                           slotAlive: true, slotSequence: 6, slotCycle: 0.9f,
                           slotDuration: 2f, slotLooping: true, slotStarts: 3,
                           slotDormant: false, now: 1f);

        Assert.True(rows.Count == 1 && rows[0].Field == "cycle",
            "a cycle that really is different is what a clock reading the wrong side's curtime "
            + "would look like");
    }

    [Fact]
    public void AHandleTheSlotCannotResolveStillHasItsDormancyChecked()
    {
        var rows = Compare(new SequenceRecord(6, 1f, 0f, 2f, true, 3), mirrorDormant: true,
                           slotAlive: false, slotSequence: 0, slotCycle: 0f, slotDuration: 0f,
                           slotLooping: false, slotStarts: 0, slotDormant: false, now: 1f);

        Assert.True(rows.Count == 1 && rows[0].Field == "dormant",
            "an entity that went away between the listing and the read would report every field, "
            + "which would drown the report — the dormancy comparison is still taken because the "
            + "slot answers it for an unresolvable handle rather than declining");
    }

    //-------------------------------------------------------------------------
    // THE WRITE-SITE CENSUSES, AS FACTS.
    //
    // This is the half of the mitigation that a grep in a commit message cannot
    // do. A push mirror is exactly as correct as the closure of its writer set,
    // and the writer set is in C++ where no managed compiler is watching it. A
    // fourth writer of m_bDormant added next month is a red test here; without
    // these it is an entity that stops animating in six months' time and a
    // fortnight of looking for the reason.
    //-------------------------------------------------------------------------

    /// <summary>The client shell's source, with comments and strings removed.</summary>
    private static string? ClientShellCode()
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("game", "client", "shell_standalone_client.cpp"));

        return path is null ? null : StripComments(File.ReadAllText(path));
    }

    private static string StripComments(string code)
    {
        code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        code = Regex.Replace(code, @"//[^\n]*", " ");
        return Regex.Replace(code, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");
    }

    /// <summary>
    /// Every line that ASSIGNS to one of these members: <c>x = </c>, <c>x += </c>,
    /// <c>x++</c> and the constructor's <c>x( ... )</c> initialiser form. A read
    /// (<c>if ( x )</c>, <c>return x</c>, <c>a == x</c>) is not one.
    /// </summary>
    private static List<string> WritesTo(string code, string member)
    {
        var writes = new List<string>();

        foreach (Match m in Regex.Matches(
                     code,
                     @"\b" + Regex.Escape(member) + @"\s*(?:(?<op>[-+*/]?=)(?!=)|(?<op>\+\+|--)|(?<op>\()\s*[^)]*\)\s*,)"))
        {
            // `==` is filtered by the (?!=) above; `x(` only counts in the
            // member-initialiser list, which is `x( value ),`.
            writes.Add(m.Value.Trim());
        }

        return writes;
    }

    // THE AUTHOR MOVED (plan physics_bones_native_zero 1.2, client half). The
    // clock is the managed skeleton's: ClientSkeletons.SetSequence decides it
    // (ClientBoneDriver.SetPlayingSequence's rule) and STORES it into native
    // through StandaloneStoreSequence, so the one native writer is
    // StoreSequence, whose only caller is that slot. The start count left
    // native with the decision (BoneEntityState.SequenceStarts).
    [ClientShellFact]
    public void TheSixSequenceMembersAreWrittenNowhereButTheConstructorAndSetPlayingSequence()
    {
        string code = ClientShellCode()!;

        // Everything except the constructor and StoreSequence's body must
        // contain no write at all. The name is historical.
        int ctorEnd = code.IndexOf("C_StandaloneEntity::~C_StandaloneEntity", StringComparison.Ordinal);
        int storeStart = code.IndexOf("void C_StandaloneEntity::StoreSequence", StringComparison.Ordinal);
        int storeEnd = storeStart < 0 ? -1 : code.IndexOf("\n}\n", storeStart, StringComparison.Ordinal);

        Assert.True(ctorEnd > 0 && storeStart > ctorEnd && storeEnd > storeStart,
            "the census needs the constructor and StoreSequence to be where it expects them");

        string elsewhere = code[ctorEnd..storeStart] + code[(storeEnd + 3)..];

        var stray = new List<string>();

        // The three block fields are needled by their BLOCK FIELD NAME ALONE
        // (step 5c's renames): the constructor reaches the tail through a local
        // reference, and the bare name catches a write made that way too.
        foreach (string member in new[]
                 {
                     "Sequence", "PlaybackRate", "AnimTime",
                     "m_nSequenceStarts", "m_flSeqDuration", "m_bSeqLooping",
                 })
        {
            foreach (string write in WritesTo(elsewhere, member))
                stray.Add($"{member}: {write}");
        }

        Assert.True(stray.Count == 0,
            "the clock members are written ONLY in the constructor and StoreSequence, the store of "
            + "what the managed skeleton chose -- a writer anywhere else is a second author whose "
            + "clock the mirror and the skeleton never see. Found: " + string.Join("; ", stray));

        Assert.True(code.IndexOf("void C_StandaloneEntity::SetPlayingSequence", StringComparison.Ordinal) < 0,
            "a native SetPlayingSequence is back, deciding a clock beside the managed skeleton");
    }

    [ClientShellFact]
    public void TheAnimationMirrorsWritesReallyAreInSetPlayingSequence()
    {
        string code = ClientShellCode()!;

        int storeStart = code.IndexOf("void C_StandaloneEntity::StoreSequence", StringComparison.Ordinal);
        int storeEnd = storeStart < 0 ? -1 : code.IndexOf("\n}\n", storeStart, StringComparison.Ordinal);

        Assert.True(storeStart > 0 && storeEnd > storeStart, "StoreSequence is gone");

        string body = code[storeStart..storeEnd];

        // The store of all five the cycle is computed from; the start count is
        // managed's alone now and must not be here.
        Assert.True(WritesTo(body, "Sequence").Count > 0
                    && WritesTo(body, "PlaybackRate").Count > 0
                    && WritesTo(body, "AnimTime").Count > 0
                    && WritesTo(body, "m_flSeqDuration").Count > 0
                    && WritesTo(body, "m_bSeqLooping").Count > 0
                    && WritesTo(body, "m_nSequenceStarts").Count == 0,
            "the census above proves nothing if the writes are not really there -- this is the "
            + "half that fails when a rename moves them somewhere the other fact cannot see");
    }

    [ClientShellFact]
    public void DormancyIsWrittenNowhereButSetDormant()
    {
        string code = ClientShellCode()!;

        // THE SUBJECT MOVED IN STEP 6b AND THIS FACT MOVED WITH IT. m_bDormant
        // is gone: dormancy is a render-policy INPUT, so it lives in the engine
        // block as Client.Dormant where the managed policy can read it. Left
        // naming the deleted member, this census would have found zero strays
        // for the rest of the port and reported it as health — a check that
        // cannot fail, about a member that no longer exists.
        int setStart = code.IndexOf("void C_StandaloneEntity::SetDormant", StringComparison.Ordinal);
        int setEnd = code.IndexOf("void C_StandaloneEntity::PushDormancy", StringComparison.Ordinal);

        Assert.True(setStart > 0 && setEnd > setStart,
            "the census needs SetDormant and PushDormancy to be where it expects them");

        string elsewhere = code[..setStart] + code[setEnd..];

        var stray = WritesTo(elsewhere, "Client.Dormant");

        Assert.True(stray.Count == 0,
            "Client.Dormant is written ONLY in SetDormant, which the two arms of "
            + "NotifyShouldTransmit call — an IClientNetworkable method the ENGINE calls, which is "
            + "why no port can remove this crossing and only a push can. Found: "
            + string.Join("; ", stray));
    }

    [ClientShellFact]
    public void SetDormantIsTheOnlyPlaceThatWritesTheBlockAndItPushes()
    {
        string code = ClientShellCode()!;

        int setStart = code.IndexOf("void C_StandaloneEntity::SetDormant", StringComparison.Ordinal);
        int setEnd = code.IndexOf("void C_StandaloneEntity::PushDormancy", StringComparison.Ordinal);

        string body = code[setStart..setEnd];

        Assert.True(Regex.Matches(body, @"\bPushDormancy\s*\(").Count == 1
                    && WritesTo(body, "Client.Dormant").Count == 1,
            "ONE write and ONE push, in one function — the failure this guards against used to be "
            + "one ARM of NotifyShouldTransmit pushing and the other not, which would look like an "
            + "entity that goes away and never comes back. Step 6b made the two arms one call, so "
            + "the shape that could go wrong is now the pair inside it.");
    }

    [ClientShellFact]
    public void BothArmsOfNotifyShouldTransmitReachTheOnePlaceThatWrites()
    {
        string code = ClientShellCode()!;

        int notifyStart = code.IndexOf("void C_StandaloneEntity::NotifyShouldTransmit", StringComparison.Ordinal);
        int notifyEnd = code.IndexOf("void C_StandaloneEntity::SetDormant", StringComparison.Ordinal);

        Assert.True(notifyStart > 0 && notifyEnd > notifyStart,
            "the census needs NotifyShouldTransmit and SetDormant to be where it expects them");

        string body = code[notifyStart..notifyEnd];

        Assert.True(Regex.Matches(body, @"\bSetDormant\s*\(\s*true\s*\)").Count == 1
                    && Regex.Matches(body, @"\bSetDormant\s*\(\s*false\s*\)").Count == 1,
            "the SHOULDTRANSMIT_END arm sets it and the SHOULDTRANSMIT_START arm clears it — an "
            + "arm that reached neither would be an entity whose dormancy never changed, which is "
            + "the same symptom by a different route");
    }

    [ClientShellFact]
    public void TheCreationPathPushesTheInitialDormancy()
    {
        string code = ClientShellCode()!;

        int created = code.IndexOf("OnStandaloneCreate", StringComparison.Ordinal);
        int spawn = code.IndexOf("OnStandaloneSpawn", StringComparison.Ordinal);

        Assert.True(created > 0 && spawn > created
                    && code[created..spawn].Contains("PushDormancy", StringComparison.Ordinal),
            "an entity can be told it left the PVS before its first update names a classname, so "
            + "the creation push carries whatever the flag SAYS rather than a default — a mirror "
            + "that guessed its initial value would be wrong for exactly those entities");
    }

    [ClientShellFact]
    public void TheRemovalPushAndTheDormancyPushAreGatedTheSameWay()
    {
        string code = ClientShellCode()!;

        int push = code.IndexOf("void C_StandaloneEntity::PushDormancy", StringComparison.Ordinal);

        Assert.True(push > 0 && code[push..].Contains("m_bHasManaged", StringComparison.Ordinal),
            "the mirror's lifetime rule is that nothing enters it that will not get a removal, and "
            + "OnStandaloneRemoved is gated on m_bHasManaged — an ungated push would leave entries "
            + "nothing clears, on handles the engine recycles");
    }

    //-------------------------------------------------------------------------
    // The appended ABI slots (the engine API one is gone with its struct).
    //-------------------------------------------------------------------------

    // TheSequenceInfoSlotWasAppendedAfterEverySlotThatPrecededIt went with the
    // engine API struct (plan_client_vtables CC-last 2c): it held the slot's
    // position in the struct's field order, and there is no struct. The slot
    // is a method of IShimStandaloneClient; tools/vtable-layout holds each
    // area's order. AbiParityTests.TheEngineApiStructIsGone holds the absence.

    // TheSequenceInfoSlotIsDeclaredClientOnlyForTheSideAudit went with the
    // slot (plan physics_bones_native_zero 1.2, client half): the duration and
    // loop flag are the managed skeleton's (ClientSkeletons.SequenceInfo,
    // ClientSkeletonsTests), and no slot answers them on either side.
    [Fact]
    public void TheSequenceInfoSlotIsRetired()
    {
        string tsv = File.ReadAllText(RepoLayout.Repo("docs", "engineapi-areas.tsv"));

        Assert.Contains("StandaloneStoreSequence\t", tsv);
        Assert.DoesNotContain("StandaloneSequenceInfo\t", tsv);
        Assert.DoesNotContain("StandaloneSequenceState\t", tsv);
    }
    //=========================================================================
    // plan_abi_hotreload D7: the mirror is a composition's now, so a hot
    // reload carries its contents to the next one as bytes.
    //=========================================================================

    [Fact]
    public void ASavedMirrorRestoresEveryRecordIntoAnEmptyOne()
    {
        var a = new EntityHandle(0x0001_0005u);
        var b = new EntityHandle(0x0002_0006u);
        var saved = new StandaloneMirror();
        saved.NoteSequence(a, 4, 2f, restart: false, now: 3f);
        saved.NoteSequenceInfo(a, 0.75f, looping: false);
        saved.NoteDormancy(a, false);
        saved.NoteDormancy(b, true);

        var restored = new StandaloneMirror();
        int adopted = restored.Restore(saved.Save());

        Assert.Equal(3, adopted);
        Assert.Equal((4, 2f, 3f, 0.75f, false, 1),
                     (restored.Sequence(a).Sequence, restored.Sequence(a).PlaybackRate,
                      restored.Sequence(a).StartTime, restored.Sequence(a).Duration,
                      restored.Sequence(a).Looping, restored.Sequence(a).Starts));
        Assert.True(restored.IsDormant(b), "a dormant entity stays dormant");
        Assert.Equal((1, 2), (restored.SequenceCount, restored.DormancyCount));
    }

    [Fact]
    public void TheRerouteSettingTravelsWithTheRecords()
    {
        var saved = new StandaloneMirror { Rerouted = false };
        var restored = new StandaloneMirror();

        Assert.True(restored.Rerouted, "a fresh mirror is rerouted (the setup took effect below)");
        restored.Restore(saved.Save());

        Assert.False(restored.Rerouted, "an operator's cs_cl_mirror_reroute 0 survives the reload");
    }

    [Fact]
    public void ARecordAlreadyInTheNewMirrorWinsOverTheCarriedOne()
    {
        var entity = new EntityHandle(0x0001_0005u);
        var saved = new StandaloneMirror();
        saved.NoteDormancy(entity, true);

        var restored = new StandaloneMirror();
        restored.NoteDormancy(entity, false);

        Assert.Equal(0, restored.Restore(saved.Save()));
        Assert.False(restored.IsDormant(entity), "the newer push is kept");
    }

    [Fact]
    public void ATruncatedSaveIsRefusedAndAdoptsNothing()
    {
        var saved = new StandaloneMirror();
        saved.NoteDormancy(new EntityHandle(0x0001_0005u), true);
        saved.NoteSequence(new EntityHandle(0x0002_0006u), 1, 1f, restart: false, now: 0f);
        byte[] bytes = saved.Save();

        var restored = new StandaloneMirror();

        Assert.Equal(2, new StandaloneMirror().Restore(bytes));
        Assert.Equal(-1, restored.Restore(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.Equal((0, 0), (restored.SequenceCount, restored.DormancyCount));
    }

    [Fact]
    public void BytesThatAreNotAMirrorSaveAreRefused()
    {
        byte[] bytes = new StandaloneMirror().Save();
        bytes[0] ^= 0xFF;

        Assert.Equal(-1, new StandaloneMirror().Restore(bytes));
    }
}
