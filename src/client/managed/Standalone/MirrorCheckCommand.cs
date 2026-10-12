using System;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// One entity's mirror, held against the ABI slot that is still there to
/// answer the same questions.
/// </summary>
public readonly struct MirrorComparison
{
    public MirrorComparison(int index, string className, string field,
                            string mirror, string slot)
    {
        Index = index;
        ClassName = className;
        Field = field;
        Mirror = mirror;
        Slot = slot;
    }

    public int Index { get; }
    public string ClassName { get; }
    public string Field { get; }
    public string Mirror { get; }
    public string Slot { get; }

    public override string ToString()
        => $"#{Index} {ClassName}: {Field} mirror={Mirror} slot={Slot}";
}

/// <summary>
/// <c>cs_cl_mirror_check</c> — every live client entity's mirror, compared
/// against the ABI slot it replaced.
///
/// <para>
/// <b>The gate plan_crossings.md Part C2 requires, and the reason it exists is
/// specific.</b> Push-on-change makes staleness impossible by construction —
/// the mirror is written synchronously with the write — so the failure mode it
/// trades staleness for is a DIFFERENT one: a write site nobody pushed from.
/// That mirror does not go stale; it diverges, and then answers confidently
/// forever, with no log line and no crash. The write-site census (in the
/// phase's commit) is the static half of holding that shut. This is the live
/// half.
/// </para>
///
/// <para>
/// <b>It compares against the slots, which is why the slots were kept.</b>
/// <c>StandaloneSequenceState</c> and <c>StandaloneIsDormant</c> read the
/// shell's own members — the ones the engine and the native render path
/// actually use — so they are an independent oracle rather than a second look
/// at the same answer. That is the difference between this and the shape of
/// check this project has been fooled by: a report of what managed pushed
/// would agree with itself whatever the shell did.
/// </para>
///
/// <para>
/// <b>At a gate, never per frame.</b> Running this every frame would put back
/// exactly the 102 crossings a frame the two phases removed, which is the
/// joke that writes itself. It is a command, run when someone asks.
/// </para>
///
/// <para>
/// The cycle is compared with a tolerance and the rest exactly. Both sides
/// compute the cycle from <c>curtime</c> — the shell from <c>gpGlobals</c>, the
/// mirror from the <see cref="Globals"/> LOAD of the same memory (Phase X1) —
/// so they agree to the bit within one frame; the tolerance is there so a
/// future change that puts a frame between them is reported as a number rather
/// than as a wall of failures.
/// </para>
/// </summary>
[ConCommand("cs_cl_mirror_check",
            Help = "cs_cl_mirror_check [any argument for the per-entity clock]. Compares every live " +
                   "client entity's managed mirror (dormancy, animation clock) against the ABI " +
                   "slots it replaced. The gate for plan_crossings X2/X3.")]
public sealed class MirrorCheckCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary>How far the two cycles may be apart before it is a divergence.</summary>
    public const float CycleTolerance = 0.001f;

    private readonly IClientStandalones _standalones;
    private readonly StandaloneMirror _mirror;
    private readonly IGameClock _clock;

    public MirrorCheckCommand(ILog log, IClientStandalones standalones, StandaloneMirror mirror,
                              IGameClock clock, StandaloneSlotReference slot,
                              IEngineEntities entities)
    {

        _log = log;
        _standalones = standalones;
        _mirror = mirror;
        _clock = clock;
        _slot = slot;
        _entities = entities;
    }

    // This side's entity area, for an entity's index (plan_abi_hotreload D8:
    // it was EntityHandle.Index, over the static Engine).
    private readonly IEngineEntities _entities;

    // The independent oracle, over this side's own slots (plan_abi_hotreload D8).
    private readonly StandaloneSlotReference _slot;

    /// <summary>
    /// One entity, compared field by field. Public because it is the unit worth
    /// testing: a test drives this with a stubbed slot reader and asserts what
    /// it reports, with no map anywhere near it.
    /// </summary>
    public static void Compare(int index, string className,
                               SequenceRecord mirror, bool mirrorDormant, float now,
                               bool slotAlive, int slotSequence, float slotCycle,
                               float slotDuration, bool slotLooping, int slotStarts,
                               bool slotDormant,
                               Action<MirrorComparison> report)
    {
        if (mirrorDormant != slotDormant)
            report(new MirrorComparison(index, className, "dormant",
                                        Say(mirrorDormant), Say(slotDormant)));

        // A handle the slot cannot resolve is not a divergence to compare
        // fields over — it is an entity that went away between the listing and
        // the read, and every field would report.
        if (!slotAlive)
            return;

        if (mirror.Sequence != slotSequence)
            report(new MirrorComparison(index, className, "sequence",
                                        Say(mirror.Sequence), Say(slotSequence)));

        if (mirror.Duration != slotDuration)
            report(new MirrorComparison(index, className, "duration",
                                        Say(mirror.Duration), Say(slotDuration)));

        if (mirror.Looping != slotLooping)
            report(new MirrorComparison(index, className, "looping",
                                        Say(mirror.Looping), Say(slotLooping)));

        if (mirror.Starts != slotStarts)
            report(new MirrorComparison(index, className, "starts",
                                        Say(mirror.Starts), Say(slotStarts)));

        float cycle = mirror.Cycle(now);

        if (MathF.Abs(cycle - slotCycle) > CycleTolerance)
            report(new MirrorComparison(index, className, "cycle",
                                        Say(cycle), Say(slotCycle)));
    }

    private static string Say(bool value) => value ? "true" : "false";
    private static string Say(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Say(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    public void Execute(string args)
    {
        int walked = 0;
        int diverged = 0;
        int dormant = 0;
        float now = _clock.Now;
        bool verbose = args.Trim().Length > 0;

        foreach (var entity in _standalones.Live())
        {
            walked++;

            // The SLOTS, read directly rather than through IEngineEntityRender:
            // the binding answers from the mirror now, so asking it would
            // compare the mirror against itself.
            bool alive = _slot.SequenceState(entity.Handle,
                                                              out int sequence, out float cycle,
                                                              out float duration, out bool looping,
                                                              out int starts);
            bool slotDormant = _slot.IsDormant(entity.Handle);

            if (slotDormant)
                dormant++;

            // WITH AN ARGUMENT, THE CLOCK ITSELF, and it is here rather than in
            // a second command because "the two agree" and "the two are both
            // MOVING" are different questions and a gate needs both. A mirror
            // frozen at the values it was handed once agrees with a shell that
            // is also frozen; two reads of this a second apart are what say the
            // animation is running.
            if (verbose && (duration > 0f || _mirror.Sequence(entity.Handle).Duration > 0f))
            {
                SequenceRecord r = _mirror.Sequence(entity.Handle);

                _log.Msg($"[mirror] #{_entities.GetIndex(entity.Handle)} {entity.ClassName}: "
                        + $"seq {Say(r.Sequence)}/{Say(sequence)} "
                        + $"cycle {Say(r.Cycle(now))}/{Say(cycle)} "
                        + $"dur {Say(r.Duration)}/{Say(duration)} "
                        + $"loop {Say(r.Looping)}/{Say(looping)} "
                        + $"starts {Say(r.Starts)}/{Say(starts)} "
                        + $"dormant {Say(_mirror.IsDormant(entity.Handle))}/{Say(slotDormant)}"
                        + "  (mirror/slot)\n");
            }

            Compare(_entities.GetIndex(entity.Handle), entity.ClassName,
                    _mirror.Sequence(entity.Handle), _mirror.IsDormant(entity.Handle), now,
                    alive, sequence, cycle, duration, looping, starts, slotDormant,
                    row =>
                    {
                        diverged++;

                        if (diverged <= 20)
                            _log.Msg($"[mirror] DIVERGED {row}\n");
                    });
        }

        _log.Msg($"[mirror] rerouted={_mirror.Rerouted} walked={walked} "
                + $"sequenceRecords={_mirror.SequenceCount} dormancyRecords={_mirror.DormancyCount} "
                + $"dormantNow={dormant} curtime={Say(now)}\n");
        _log.Msg(diverged == 0
                    ? "[mirror] AGREE — every live entity's mirror equals its slot\n"
                    : $"[mirror] {diverged} DIVERGENCE(S)\n");
    }
}

/// <summary>
/// <c>cs_cl_mirror_reroute</c> — whether the mirror answers, or the ABI slots
/// do.
///
/// <para>
/// <b>The census control, and it is the phase's measurement instrument rather
/// than a feature.</b> Phase X1 measured its reroute by building two variants
/// of one commit and running each; the client half of that came out confounded,
/// because the two runs had different renderable populations and the client's
/// boundary total moved 22 crossings a frame for a reason X1 had not caused.
/// One commit, two arms, ONE PROCESS holds the population, the map, the
/// viewpoint and the frame rate fixed and moves only the thing under test.
/// </para>
///
/// <para>
/// The mirror is written either way, so switching it off does not make it
/// stale and switching it back on does not need it refilled. That is a property
/// of push-on-change and it is what makes this control honest: the "before" arm
/// is the same binary doing the same work, taking a different branch in three
/// methods.
/// </para>
/// </summary>
[ConCommand("cs_cl_mirror_reroute",
            Help = "cs_cl_mirror_reroute [0|1]. Whether IsDormant/SequenceState answer from " +
                   "the managed mirror (1, shipped) or fall through to the ABI slots (0).")]
public sealed class MirrorRerouteCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly StandaloneMirror _mirror;

    public MirrorRerouteCommand(ILog log, StandaloneMirror mirror) 

    {

        _log = log;

        _mirror = mirror;

    }

    public void Execute(string args)
    {
        string wanted = args.Trim();

        if (wanted.Length > 0)
            _mirror.Rerouted = wanted != "0";

        _log.Msg($"[mirror] rerouted={_mirror.Rerouted} — "
                + (_mirror.Rerouted
                       ? "IsDormant and SequenceState answer from the managed mirror\n"
                       : "IsDormant and SequenceState fall through to the ABI slots\n"));
    }
}
