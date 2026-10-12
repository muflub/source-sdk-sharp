//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// What an animation has crossed since the last time anybody looked.
///
/// <para>
/// A sequence's events are stamped at a CYCLE — <c>AE_MUZZLEFLASH</c> at
/// 0.000, <c>AE_CL_PLAYSOUND</c> at 0.255 — and firing one means noticing that
/// the cycle went past it between two frames. That sounds like a comparison
/// and is not: the cycle wraps when the sequence loops, it is pinned at 0.999
/// when the sequence does not, it jumps backwards when the same animation is
/// fired again, and it does not move at all on a frame where nothing happened.
/// Four of those five look identical to a naive <c>previous &lt; c &lt;= now</c>.
/// </para>
///
/// <para>
/// So the rule lives here, on its own, with no engine in it: given where the
/// cursor was and where the clock is now, which half-open windows of cycle did
/// this frame cross. Everything else — reading the clock, reading the event
/// table, deciding what a fired event DOES — is somewhere else, and none of it
/// can be silently wrong in this particular way.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 5, a shared declaration. It is arithmetic over a model's
/// own numbers and holds nothing about which side is asking. Only the client
/// runs it today, because the cycle it is fed is client-local and deliberately
/// off the wire; that is a fact about the caller, not about this rule.
/// </para>
/// </summary>
public static class AnimationEventClock
{
    /// <summary>
    /// Where the cursor is placed when an animation STARTS, so that an event
    /// stamped at cycle 0 fires.
    ///
    /// <para>
    /// Stock's number and stock's reason: the first window has to be open at
    /// its lower end or the muzzle flash that every HL2 view model stamps at
    /// exactly 0.000 would never fire once. Every window is half-open —
    /// <c>(after, upTo]</c> — which is what stops an event firing twice on two
    /// consecutive frames.
    /// </para>
    /// </summary>
    public const float BeforeTheFirstFrame = -0.01f;

    /// <summary>
    /// How far the cycle must fall for the fall to be a WRAP rather than a
    /// stumble.
    ///
    /// <para>
    /// Stock's threshold. A cycle that moves backwards by a little is
    /// interpolation catching up or a frame arriving out of order, and
    /// replaying the band between here and there would fire those events
    /// twice. A cycle that moves backwards by a lot went round the end.
    /// </para>
    ///
    /// <para>
    /// This threshold does NOT have to separate a wrap from a restart, and
    /// that is what <see cref="AnimationEventCursor.Starts"/> is for. An
    /// automatic weapon re-fires faster than its own animation runs, so a
    /// restart there is a fall of about 0.2 — too small to read as a wrap and
    /// too large to be a stumble. Told apart by size alone, the muzzle flash
    /// fires once for a whole burst.
    /// </para>
    /// </summary>
    public const float WrapFall = 0.5f;

    /// <summary>
    /// Where the last look left off. Held by the caller, one per entity.
    /// </summary>
    /// <param name="Sequence">
    /// Which sequence the cursor belongs to. A different sequence is a new
    /// animation, whatever the cycles say.
    /// </param>
    /// <param name="Starts">
    /// The clock's start count when the cursor was taken. A change is a
    /// restart, which is the same event as a sequence change.
    /// </param>
    /// <param name="PreviousCycle">The cycle this cursor last stood at.</param>
    /// <param name="Established">
    /// False until an animation has been looked at once. Distinct from a
    /// sequence of 0 at cycle 0, which is a real animation that a great many
    /// entities really are playing.
    /// </param>
    public readonly record struct AnimationEventCursor(int Sequence, int Starts,
                                                       float PreviousCycle, bool Established)
    {
        /// <summary>Nobody has looked at this entity yet.</summary>
        public static AnimationEventCursor Fresh => new(-1, 0, 0f, false);
    }

    /// <summary>
    /// A half-open band of cycle, <c>(After, UpTo]</c>.
    /// </summary>
    public readonly record struct AnimationEventWindow(float After, float UpTo)
    {
        /// <summary>Whether an event stamped here falls inside.</summary>
        public bool Contains(float eventCycle) => eventCycle > After && eventCycle <= UpTo;
    }

    /// <summary>
    /// What one frame crossed, and where the cursor now stands.
    /// </summary>
    /// <param name="Cursor">What to keep for the next frame.</param>
    /// <param name="Tail">
    /// The band running up to the end of the sequence, present only on a wrap.
    /// Fired FIRST, so that an event at 0.95 is dispatched before an event at
    /// 0.02 of the next lap — they happened in that order.
    /// </param>
    /// <param name="Head">The ordinary band, or none when nothing moved.</param>
    public readonly record struct AnimationEventStep(AnimationEventCursor Cursor,
                                                     AnimationEventWindow? Tail,
                                                     AnimationEventWindow? Head)
    {
        /// <summary>Whether an event stamped at this cycle fired this frame.</summary>
        public bool Fires(float eventCycle)
            => (Tail.HasValue && Tail.Value.Contains(eventCycle)) ||
               (Head.HasValue && Head.Value.Contains(eventCycle));

        /// <summary>Whether this frame crossed anything at all.</summary>
        public bool Any => Tail.HasValue || Head.HasValue;
    }

    /// <summary>
    /// Advance the cursor to the clock's current reading.
    /// </summary>
    /// <param name="cursor">Where the last look left off.</param>
    /// <param name="sequence">The sequence playing now, or a negative for none.</param>
    /// <param name="starts">The clock's start count now.</param>
    /// <param name="cycle">Where the clock stands now, 0..1.</param>
    public static AnimationEventStep Advance(AnimationEventCursor cursor,
                                             int sequence, int starts, float cycle)
    {
        // No animation. The cursor still records the sequence, so that arriving
        // AT one later reads as a start rather than as a continuation of
        // whatever was playing before the model was taken away.
        if (sequence < 0)
            return new AnimationEventStep(new AnimationEventCursor(sequence, starts, 0f, false),
                                          null, null);

        bool started = !cursor.Established ||
                       cursor.Sequence != sequence ||
                       cursor.Starts != starts;

        if (started)
        {
            // Stock's rule, and it looks wrong until the alternative is
            // written out. The window is (-0.01, 0] rather than (-0.01, cycle]
            // — an animation that has just begun has, by definition, not got
            // anywhere yet, and firing everything up to the cycle this frame
            // happens to report would dispatch a whole animation's worth of
            // events in a single frame whenever a restart was noticed a frame
            // late.
            return new AnimationEventStep(new AnimationEventCursor(sequence, starts, 0f, true),
                                          null,
                                          new AnimationEventWindow(BeforeTheFirstFrame, 0f));
        }

        float previous = cursor.PreviousCycle;

        if (cycle > previous)
            return new AnimationEventStep(cursor with { PreviousCycle = cycle },
                                          null,
                                          new AnimationEventWindow(previous, cycle));

        // Backwards, or not at all.
        if (previous - cycle <= WrapFall)
        {
            // A stall, or a stumble. Nothing fires and — this is the part that
            // matters — the cursor does NOT move. Advancing it to a cycle
            // behind where it stood would re-open the band between them, and
            // the next forward frame would fire everything in that band a
            // second time.
            return new AnimationEventStep(cursor, null, null);
        }

        // A wrap: the tail of the lap that ended, then the head of the one that
        // began.
        //
        // DELIBERATELY NOT STOCK, and the divergence is one line.
        // C_BaseAnimating::DoAnimationEvents opens the second band at
        // `flEventCycle - 0.001f`, a hair below the cycle it has just arrived
        // at — so every event between the start of the sequence and there is
        // skipped on every lap but the first. A footstep stamped at 0.02 on a
        // looping walk fires once and then never again. Opened at
        // BeforeTheFirstFrame it fires every lap, which is what a looping
        // animation with an event near its start means.
        return new AnimationEventStep(cursor with { PreviousCycle = cycle },
                                      new AnimationEventWindow(previous, 1.0f),
                                      new AnimationEventWindow(BeforeTheFirstFrame, cycle));
    }
}
