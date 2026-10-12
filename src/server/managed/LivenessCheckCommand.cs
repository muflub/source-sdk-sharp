using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// One handle's liveness, as the mirror has it and as the ABI slot answers it.
/// </summary>
public readonly struct LivenessComparison
{
    public LivenessComparison(uint handle, string className, string mirror, string slot)
    {
        Handle = handle;
        ClassName = className;
        Mirror = mirror;
        Slot = slot;
    }

    public uint Handle { get; }
    public string ClassName { get; }
    public string Mirror { get; }
    public string Slot { get; }

    public override string ToString()
        => $"handle {Handle} ({ClassName}): mirror={Mirror} slot={Slot}";
}

/// <summary>
/// <c>cs_sv_liveness_check</c> — every entity's mirrored liveness, held against
/// the ABI slot it replaced.
///
/// <para>
/// <b>The divergence gate plan_crossings.md Phase X5 requires.</b> Push-on-event
/// makes staleness impossible by construction, so the failure it trades
/// staleness for is a different one: a lifetime path nobody pushed from. Such a
/// mirror does not go stale — it diverges, and then answers confidently
/// forever, with no log line and no crash. The write-site census in this
/// phase's commit is the static half of holding that shut. This is the live
/// half, and X2/X3's equivalent found a REAL bug that <c>make check</c> was
/// green over.
/// </para>
///
/// <para>
/// <b>It compares against <see cref="EngineSide.EntityIsValidBySlot"/>, which is
/// why the slot was kept.</b> That call resolves through the engine's own
/// handle table — the table the trace filters and the event queue resolve
/// through — so it is an INDEPENDENT oracle rather than a second look at what
/// managed already believes. A gate that compared the mirror against the
/// binding would go through the mirror as well and agree with itself whatever
/// the shells did, which is the shape of check this project has been fooled by
/// more than forty times.
/// </para>
///
/// <para>
/// <b>BOTH DIRECTIONS, and the second is the one that matters.</b> Walking the
/// world's live entities catches a creation push that never fired. It cannot
/// catch a REMOVAL push that never fired, because the entity is not there to be
/// walked — and that is the dangerous direction: the mirror would answer "still
/// there" for freed memory. So the second pass walks
/// <see cref="LivenessMirror.LiveHandles"/> and asks the slot about each one.
/// A gate with only the first pass would have passed over exactly the bug this
/// phase can introduce.
/// </para>
///
/// <para>
/// <b>It also MEASURES the two arms X0 left owed</b> rather than assuming them.
/// Every live entity the mirror does not claim is a fall-through — a
/// <c>pBase</c> entity or worldspawn — and the count is printed. X0 measured
/// the <c>pBase</c> arm at 0 in every clean window, but the arm still compiles;
/// this is how a reader confirms that, on a running map, rather than trusting
/// it. A non-zero count is NOT a failure: those handles are answered by the
/// slot, correctly, at the cost of a crossing.
/// </para>
///
/// <para>
/// <b>At a gate, never per frame.</b> Running this every frame would put back
/// more crossings than the phase removed, which is the joke that writes itself.
/// It is a command, run when someone asks.
/// </para>
/// </summary>
[ConCommand("cs_sv_liveness_check",
            Help = "Compares every entity's managed liveness mirror against the EntityIsValid ABI "
                 + "slot it replaced, in both directions, and reports how many live entities the "
                 + "mirror does not claim. The gate for plan_crossings X5.")]
public sealed class LivenessCheckCommand : IConCommand
{
    private readonly IEngineEntities _entities;
    private readonly IEngineConsole _console;
    private readonly LivenessMirror _mirror;

    /// <summary>On the server composition's mirror, from its container (plan_abi_hotreload D7).</summary>
    public LivenessCheckCommand(IEngineEntities entities, IEngineConsole console,
                                LivenessMirror mirror, LivenessSlotReference slot)
    {
        _entities = entities;
        _console = console;
        _mirror = mirror;
        _slot = slot;
    }

    // The independent oracle, over this side's own slot (plan_abi_hotreload D8).
    private readonly LivenessSlotReference _slot;

    /// <summary>
    /// One handle, compared. Public because it is the unit worth testing: a test
    /// drives this with a stubbed slot reader and asserts what it reports, with
    /// no map anywhere near it.
    /// </summary>
    /// <returns>True when the mirror claimed this handle and agreed with the slot,
    /// or did not claim it at all; false when it claimed it and disagreed.</returns>
    public static bool Compare(uint handle, string className, bool claimed, bool mirrorAlive,
                               bool slotAlive, System.Action<LivenessComparison> report)
    {
        if (!claimed)
            return true;

        if (mirrorAlive == slotAlive)
            return true;

        report(new LivenessComparison(handle, className,
                                      mirrorAlive ? "alive" : "gone",
                                      slotAlive ? "alive" : "gone"));
        return false;
    }

    public void Execute(string args)
    {
        var divergences = new List<LivenessComparison>();
        int walked = 0;
        int claimed = 0;
        int unclaimed = 0;

        // Pass one: the world's live entities. Catches a creation push that
        // never fired, and counts the pBase/world fall-through.
        foreach (EntityHandle entity in _entities.FindAllByClassname("*"))
        {
            walked++;

            bool isClaimed = _mirror.TryLookup(entity, out bool mirrorAlive);

            if (isClaimed)
                claimed++;
            else
                unclaimed++;

            // The SLOT, not the binding: the binding goes through the mirror.
            bool slotAlive = _slot.IsValid(entity);

            Compare(entity.Value, _entities.GetClassName(entity), isClaimed, mirrorAlive,
                    slotAlive, divergences.Add);
        }

        // Pass two: everything the mirror claims is alive. Catches a removal
        // push that never fired — the direction pass one structurally cannot
        // see. Materialised first: the slot call crosses the boundary and
        // nothing may mutate the dictionary under an open enumerator.
        var live = new List<uint>(_mirror.LiveHandles());
        int orphaned = 0;

        foreach (uint value in live)
        {
            var entity = new EntityHandle(value);

            if (_slot.IsValid(entity))
                continue;

            orphaned++;
            divergences.Add(new LivenessComparison(value, "(freed)", "alive", "gone"));
        }

        _console.Msg($"[liveness] walked={walked} claimed={claimed} unclaimed={unclaimed} "
                     + $"mirrorLive={live.Count} records={_mirror.RecordCount} "
                     + $"evicted={_mirror.EvictedCount} rerouted={_mirror.Rerouted}\n");

        _console.Msg($"[liveness] unclaimed={unclaimed} is the pBase + worldspawn fall-through, "
                     + "answered by the slot at a crossing each\n");

        foreach (var divergence in divergences)
            _console.Msg($"[liveness] DIVERGENCE {divergence}\n");

        _console.Msg(divergences.Count == 0
            ? $"[liveness] OK — no divergence over {walked} live entities and {live.Count} claims\n"
            : $"[liveness] {divergences.Count} DIVERGENCES ({orphaned} claimed-but-freed)\n");
    }
}

/// <summary>
/// <c>cs_sv_liveness_reroute</c> — whether <c>EntityIsValid</c> is answered from
/// the mirror or from the ABI slot.
///
/// <para>
/// <b>The census control, and it is why the phase's two arms are one process
/// rather than two builds.</b> Phase X1 measured its reroute by building two
/// variants of one commit and the measurement was confounded: the two runs had
/// different entity populations, so the boundary total moved for a reason X1
/// had not caused. Flipping this at runtime holds the map, the population, the
/// viewpoint and the frame rate fixed and moves only the thing under test.
/// </para>
///
/// <para>
/// <b>It is a READING control.</b> The pushes do not consult it, so the mirror
/// stays correct while it is switched off and is not stale when it comes back.
/// Phase X2/X3's divergence gate found the bug that makes this worth saying out
/// loud: a write gated on the same flag as its readers corrupted the very
/// mirror the census was measuring.
/// </para>
/// </summary>
[ConCommand("cs_sv_liveness_reroute",
            Help = "cs_sv_liveness_reroute [0|1]. Whether EntityIsValid answers from the managed "
                 + "liveness mirror (1) or from the ABI slot (0). The census control for X5.")]
public sealed class LivenessRerouteCommand : IConCommand
{
    private readonly IEngineConsole _console;
    private readonly LivenessMirror _mirror;

    /// <summary>On the server composition's mirror, from its container (plan_abi_hotreload D7).</summary>
    public LivenessRerouteCommand(IEngineConsole console, LivenessMirror mirror)
    {
        _console = console;
        _mirror = mirror;
    }

    public void Execute(string args)
    {
        string trimmed = args.Trim();

        if (trimmed.Length > 0)
            _mirror.Rerouted = trimmed != "0";

        _console.Msg($"[liveness] rerouted={_mirror.Rerouted} records={_mirror.RecordCount} "
                     + $"live={_mirror.LiveCount}\n");

        _console.Msg($"[liveness] lookups answered={_mirror.AnsweredLookups} "
                     + $"sentinel={_mirror.SentinelLookups} zero={_mirror.ZeroLookups} "
                     + $"unknown={_mirror.UnknownLookups}\n");

        if (_mirror.UnknownSamples.Count > 0)
            _console.Msg($"[liveness] unknown handles: {string.Join(" ", _mirror.UnknownSamples)}\n");

        _mirror.ResetCounters();
    }
}
