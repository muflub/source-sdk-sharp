using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// What the legacy temp entity pool has actually done.
///
/// <para>
/// WHY IT EXISTS, and it is a sharper reason than <c>cs_cl_te_report</c>'s.
/// <b>The pool has four live callers that are not temp entities at all</b> —
/// <c>fx_shelleject.cpp</c> and <c>c_baseanimating.cpp</c> eject brass,
/// <c>fx.cpp</c> and <c>c_baseanimating.cpp</c> flash muzzles, and
/// <c>cdll_client_int.cpp</c> and <c>view.cpp</c> drive the frame and the level.
/// So a pool that stopped allocating would take every shell casing and every
/// gib in the game with it, <b>and no temp-entity fire command would show
/// it</b>: `cs_cl_te_report`'s counters would be exactly as green as before.
/// </para>
///
/// <para>
/// These four numbers are what tells those apart from the console, with no
/// screenshot and no eye. <c>allocated</c> climbing while a weapon fires is the
/// whole of gate 3 for Phase C4; <c>live</c> settling back to a small number
/// afterwards is the expiry working; and <c>refused</c> above zero is a pool
/// that filled, which in a firefight is real and in an empty map is a leak.
/// </para>
///
/// <para>
/// THE CAPACITY IS PRINTED BECAUSE IT IS NATIVE'S ANSWER. A zero there is not
/// "no tempents yet" — it is the <c>TempEntPoolCapacity</c> slot not being
/// wired, which is a pool that can never allocate anything, and it looks
/// identical to an idle one in every other number.
/// </para>
/// </summary>
[ConCommand("cs_cl_te_pool",
    Help = "The legacy temp entity pool: how many gibs, shells and sprites are live, how many "
         + "have been allocated, how many refused, and how many low-priority ones were evicted")]
public sealed class TempEntPoolReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientTempEntPool _pool;

    public TempEntPoolReportCommand(ILog log, ClientTempEntPool pool)
    {

        _log = log;
        _pool = pool;
    }

    public void Execute(string args)
    {
        _log.Msg($"[SourceSharp/Client] tempent pool: {_pool.Count} live of "
              + $"{_pool.Capacity} (native's MAX_TEMP_ENTITIES), "
              + $"{_pool.Allocated} allocated, {_pool.Refused} refused, "
              + $"{_pool.Evicted} evicted\n");

        if (_pool.Capacity == 0)
        {
            _log.Warning("[SourceSharp/Client] the capacity is ZERO, which is the "
                      + "TempEntPoolCapacity slot not being wired rather than an idle pool. "
                      + "Nothing can allocate: no gib, no shell casing, no sprite.\n");
        }
    }
}
