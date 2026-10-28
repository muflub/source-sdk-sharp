using System;
using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Finds every <see cref="IFrameLifecycle"/> the container knows about and calls
/// them in sequence, at each of the frame's four points.
///
/// <para>
/// The per-frame twin of <see cref="LifecycleDriver"/>, and resolved the same
/// way and for the same reason: once, at construction, so the sequence cannot
/// depend on what happened to have been lazily built by the time the first
/// frame ran.
/// </para>
///
/// <para>
/// <b>A throwing participant is reported ONCE, not once a frame.</b> This is the
/// difference that matters between this and LifecycleDriver. A level event that
/// throws writes one line per map; a frame event that throws writes one line per
/// tick, which at 66 ticks a second buries the first and only useful copy of the
/// message under thousands of repeats and turns a bug in one service into an
/// unusable console for every other. So the first throw from a given participant
/// at a given point is logged in full and the rest are counted, with the count
/// reported when the level ends.
/// </para>
///
/// <para>
/// <b>It keeps being called after it throws.</b> Dropping a participant from the
/// sequence on its first fault would make a transient error — one bad entity
/// handle on one frame — permanently disable a service for the rest of the map,
/// which is a much worse failure than the one being reported.
/// </para>
/// </summary>
public sealed class FrameDriver
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IReadOnlyList<IFrameLifecycle> _participants;

    /// <summary>Throw counts, keyed by participant index and which of the points.</summary>
    private readonly int[] _faults;

    /// <summary>How many dispatch points a participant can fault at.</summary>
    private const int Points = 4;

    public FrameDriver(ILog log, ServiceContainer services)
    {

        _log = log;
        var found = services.ResolveAll<IFrameLifecycle>();

        // Ties are impossible, not resolved -- LifecycleDriver's reason, and
        // it bites harder here: a frame sequence's order is observable 66 times
        // a second and never printed once.
        StageUniqueness.Require(found, p => p.FrameOrder, "FrameOrder", "FrameStage");

        _participants = found.OrderBy(p => p.FrameOrder).ToArray();

        _faults = new int[_participants.Count * Points];
    }

    /// <summary>What was found, in the order they will be called.</summary>
    public IReadOnlyList<IFrameLifecycle> Participants => _participants;

    public void PreThink(float frameTime) => Dispatch(0, "OnPreThink", frameTime);

    public void Think(float frameTime) => Dispatch(1, "OnThink", frameTime);

    /// <summary>
    /// The think point for the participants up to and including
    /// <paramref name="lastOrder"/>: the entities' frame, which stock runs
    /// before the physics step (Physics_RunThinkFunctions).
    /// <see cref="ThinkRest"/> runs the others after it.
    /// </summary>
    public void ThinkThrough(int lastOrder, float frameTime)
    {
        _thoughtThrough = lastOrder;
        Dispatch(1, "OnThink", frameTime, int.MinValue, lastOrder);
    }

    /// <summary>Whether this frame's <see cref="ThinkThrough"/> has run and its <see cref="ThinkRest"/> has not.</summary>
    public bool EntitiesThought => _thoughtThrough is not null;

    /// <summary>
    /// The think point for what follows the physics step: the participants
    /// after the ones this frame's <see cref="ThinkThrough"/> ran, or all of
    /// them when it did not run (a host that drives the frame in one call).
    /// </summary>
    public void ThinkRest(float frameTime)
    {
        int from = _thoughtThrough is int last ? last + 1 : int.MinValue;
        _thoughtThrough = null;
        Dispatch(1, "OnThink", frameTime, from, int.MaxValue);
    }

    // The order this frame's ThinkThrough ran up to, until its ThinkRest.
    private int? _thoughtThrough;

    public void PostThink(float frameTime) => Dispatch(2, "OnPostThink", frameTime);

    /// <summary>
    /// Stock's <c>PreClientUpdate</c>. No participant in this build — see
    /// <see cref="IFrameLifecycle.OnPreClientUpdate"/> for why the empty pass
    /// is here.
    /// </summary>
    public void PreClientUpdate(float frameTime) => Dispatch(3, "OnPreClientUpdate", frameTime);

    /// <summary>
    /// Report and clear what threw during the level. Called from level shutdown,
    /// which is the only moment a per-frame count is worth printing: during the
    /// level the whole point is to stay quiet.
    /// </summary>
    public void ReportFaults()
    {
        for (int i = 0; i < _participants.Count; i++)
        {
            for (int point = 0; point < Points; point++)
            {
                int n = _faults[(i * Points) + point];
                if (n <= 1)
                {
                    // 0 is silence and 1 was already logged in full when it
                    // happened -- repeating it here would be the flood this
                    // class exists to prevent, just deferred.
                    _faults[(i * Points) + point] = 0;
                    continue;
                }

                _log.Warning($"[SourceSharp/Server] {_participants[i].GetType().Name}."
                    + $"{PointName(point)} threw on {n} frames this level\n");

                _faults[(i * Points) + point] = 0;
            }
        }
    }

    private static string PointName(int point) => point switch
    {
        0 => "OnPreThink",
        1 => "OnThink",
        2 => "OnPostThink",
        _ => "OnPreClientUpdate",
    };

    private void Dispatch(int point, string what, float frameTime, int fromOrder = int.MinValue, int toOrder = int.MaxValue)
    {
        for (int i = 0; i < _participants.Count; i++)
        {
            var p = _participants[i];
            if (p.FrameOrder < fromOrder || p.FrameOrder > toOrder)
                continue;

            try
            {
                switch (point)
                {
                    case 0: p.OnPreThink(frameTime); break;
                    case 1: p.OnThink(frameTime); break;
                    case 2: p.OnPostThink(frameTime); break;
                    default: p.OnPreClientUpdate(frameTime); break;
                }
            }
            catch (Exception e)
            {
                int slot = (i * Points) + point;

                // Only the first one says anything. See the class comment: the
                // alternative is this line 66 times a second.
                if (_faults[slot] == 0)
                {
                    _log.Warning($"[SourceSharp/Server] {p.GetType().Name}.{what} threw: "
                        + $"{e.Message} (further throws this level are counted, not logged)\n");
                }

                if (_faults[slot] < int.MaxValue)
                    _faults[slot]++;
            }
        }
    }
}
