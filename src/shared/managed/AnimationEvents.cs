using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Event type flags, mirrored from <c>eventlist.h</c>.
///
/// <para>
/// A shared declaration in Part 4b's fifth bucket: a fact both halves compile
/// against, spelled once. The tell that it belongs here rather than inline is
/// the one the taxonomy names — a comment narrating the other side's number
/// ("native uses 0x400") would mean the number is in the wrong place.
/// </para>
/// </summary>
public static class AnimationEventTypes
{
    /// <summary>
    /// <c>AE_TYPE_NEWEVENTSYSTEM</c>. Set when the event carries a NAME
    /// (<c>AE_MUZZLEFLASH</c>) rather than only a number.
    ///
    /// <para>
    /// Both kinds are live in the models this mod runs against, which is why
    /// the distinction has to survive the trip: <c>v_smg1</c>'s fire fires a
    /// named <c>AE_MUZZLEFLASH</c> AND an old numeric 6001, and
    /// <c>v_rpg</c>'s fire is a bare 5001 with no name at all.
    /// </para>
    /// </summary>
    public const int NewEventSystem = 1 << 10;
}

/// <summary>
/// One animation event, as the MODEL carries it.
///
/// <para>
/// Read out of the studio header, never constructed from an expectation — the
/// whole value of this type is that it says what is really there. An unnamed
/// event is not a broken one: it is an old-system event and is compared by
/// <see cref="EventId"/>.
/// </para>
/// </summary>
public readonly record struct ModelAnimationEvent(
    int Index,
    float Cycle,
    int EventId,
    int Type,
    string Name,
    string Options)
{
    /// <summary>Whether this event carries a name rather than only a number.</summary>
    public bool IsNamed => (Type & AnimationEventTypes.NewEventSystem) != 0 && Name.Length > 0;

    /// <summary>How the report prints it.</summary>
    public string Describe()
        => (IsNamed ? $"{Name}" : $"#{EventId}") +
           $" @ {Cycle:0.000}" +
           (Options.Length > 0 ? $" '{Options}'" : string.Empty);
}

/// <summary>
/// What managed code EXPECTS a model to carry, so the expectation can be
/// checked instead of believed.
///
/// <para>
/// This exists because of a specific bug. Weapon animations used to be
/// resolved by sequence NAME — <c>fire</c>, <c>attack1</c>, <c>shoot</c> — and
/// no HL2 v_ model uses any of those: the SMG's is <c>fire01</c>, the pulse
/// rifle's is <c>IR_fire</c>, the crowbar has only <c>hitcenter1</c>. Every
/// lookup answered -1, <c>SendAnim</c> returned silently, and for months
/// nothing anywhere said so. Resolving by ACTIVITY fixed that instance; what
/// it did not fix is the SHAPE of the mistake, which is managed code believing
/// something about a model file it never reads.
/// </para>
/// </summary>
/// <param name="Anim">Which animation kind the event should be on.</param>
/// <param name="EventName">
/// The named event to look for, or empty to look for <paramref name="EventId"/>.
/// </param>
/// <param name="EventId">The old-system numeric event, when the name is empty.</param>
public readonly record struct AnimationEventExpectation(WeaponAnim Anim, string EventName, int EventId)
{
    /// <summary>Expect a named (new-system) event, such as AE_MUZZLEFLASH.</summary>
    public static AnimationEventExpectation Named(WeaponAnim anim, string name)
        => new(anim, name, 0);

    /// <summary>
    /// Expect an old-system numeric event. Not a legacy path to be tidied
    /// away: the RPG's fire and every one of the grenade's events are this
    /// kind, and an expectation that could only be written as a name could not
    /// describe them at all.
    /// </summary>
    public static AnimationEventExpectation Numbered(WeaponAnim anim, int eventId)
        => new(anim, string.Empty, eventId);

    /// <summary>How the report names what it was looking for.</summary>
    public string Describe() => EventName.Length > 0 ? EventName : $"#{EventId}";
}

/// <summary>
/// The check: what a model actually carries, against what managed code said it
/// would, said out loud when they disagree.
///
/// <para>
/// The oracle discipline this project has learned twice over applies here
/// directly. <c>cs_cl_viewmodel_report</c> once said "drawing" while the screen
/// was empty; a pose-parameter setter once reported success against a
/// byte-identical screenshot. Both were reports agreeing with themselves. This
/// one cannot: every line it prints comes from the studio header through the
/// ABI, and the expectations it compares against are declared somewhere else
/// entirely.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 5 throughout. The event table is a fact of the model file
/// and both sides load the same file, so both sides can run this and agreeing
/// is the requirement rather than a coincidence. ACTING on an event — spawning
/// the flash, playing the sound — is bucket 3, the client's, and is not what
/// this is. Nothing here feeds prediction: the resolved sequence index does,
/// and this READS that index through the same first-match activity lookup the
/// machine fires with rather than computing one of its own.
/// </para>
/// </summary>
public static class ModelAnimationCheck
{
    /// <summary>The report's verdict for one animation kind.</summary>
    public enum Verdict
    {
        /// <summary>Every expectation for this animation was met.</summary>
        Ok,

        /// <summary>
        /// The animation does not resolve to a sequence at all. THE bug this
        /// whole file exists because of, and it outranks a missing event:
        /// there is no sequence to carry one.
        /// </summary>
        MissingSequence,

        /// <summary>The sequence is there and does not carry what was expected.</summary>
        MissingEvent,
    }

    /// <summary>Everything a sequence fires, in the order the model lists it.</summary>
    public static IReadOnlyList<ModelAnimationEvent> Read(IEngineEntityRender render,
                                                          EntityHandle entity,
                                                          int sequence)
    {
        var events = new List<ModelAnimationEvent>();

        if (sequence < 0)
            return events;

        int count = render.SequenceEventCount(entity, sequence);

        for (int i = 0; i < count; i++)
        {
            if (render.SequenceEvent(entity, sequence, i,
                                     out float cycle, out int eventId, out int type,
                                     out string name, out string options))
                events.Add(new ModelAnimationEvent(i, cycle, eventId, type, name, options));
        }

        return events;
    }

    /// <summary>
    /// Whether what the model carries satisfies one expectation.
    ///
    /// <para>
    /// Named expectations match on the NAME, case-insensitively, and only
    /// against events that actually carry one — an old-system event whose
    /// number happened to stringify the same way is a different thing. A
    /// numbered expectation matches on the id, and deliberately does not care
    /// whether the event is old- or new-system, because a model author may
    /// upgrade one without meaning to change what it does.
    /// </para>
    /// </summary>
    public static bool Satisfies(IReadOnlyList<ModelAnimationEvent> events,
                                 AnimationEventExpectation expected)
    {
        foreach (var carried in events)
        {
            if (expected.EventName.Length > 0)
            {
                if (carried.IsNamed &&
                    string.Equals(carried.Name, expected.EventName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (carried.EventId == expected.EventId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The whole check for one model, as lines a person reads.
    ///
    /// <para>
    /// Pure but for the two engine reads, so a test can hold it still. It
    /// resolves each animation through <see cref="WeaponAnimNames.Resolve"/> —
    /// the same call the weapon machine fires with, not a second copy of the
    /// rule — so "the fire animation never plays" reads here as
    /// <c>MISSING SEQUENCE</c> rather than as a check that agrees with itself.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Verify(IEngineEntityRender render,
                                               EntityHandle entity,
                                               string label,
                                               IReadOnlyList<AnimationEventExpectation> expectations)
    {
        var lines = new List<string>();

        if (expectations.Count == 0)
        {
            // An EMPTY expectation set is a real answer: somebody dumped this
            // model and it carries nothing. It is not the same as nobody
            // having looked, which is the caller's to say — see
            // WeaponAnimEvents.For, where null and empty are different.
            lines.Add($"{label}  declared as carrying no events");
            return lines;
        }

        int failures = 0;

        foreach (var anim in expectations.Select(e => e.Anim).Distinct())
        {
            int sequence = WeaponAnimNames.Resolve(render, entity, anim);
            var wanted = expectations.Where(e => e.Anim == anim).ToList();

            if (sequence < 0)
            {
                failures += wanted.Count;
                lines.Add($"{label}  {anim}  MISSING SEQUENCE  " +
                          $"(nothing on this model answers to {string.Join("/", WeaponAnimNames.ActivitiesFor(anim))})");
                continue;
            }

            var carried = Read(render, entity, sequence);
            string present = carried.Count > 0
                ? string.Join(", ", carried.Select(e => e.Describe()))
                : "no events";

            foreach (var expectation in wanted)
            {
                bool met = Satisfies(carried, expectation);

                if (!met)
                    failures++;

                lines.Add($"{label}  {anim}  seq {sequence}  " +
                          $"{(met ? "ok" : "MISSING EVENT")}  " +
                          $"want {expectation.Describe()}  carries: {present}");
            }
        }

        lines.Add($"{label}  {failures} unmet expectation(s) of {expectations.Count}");

        return lines;
    }

    /// <summary>Whether a set of report lines contains any failure.</summary>
    public static bool AnyUnmet(IReadOnlyList<string> lines)
        => lines.Any(l => l.Contains("MISSING SEQUENCE") || l.Contains("MISSING EVENT"));
}
