using System.Collections.Concurrent;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>What the desk's worker has done: the stretches planned, the jumps flown again in the simulator, the flights and floods that took, and its thread's CPU time.</summary>
public readonly record struct JumpDeskCounts(long Planned, long Predicted, long Flights, long Floods, double WorkMicros, long Faults);

/// <summary>
/// Where the jumps of a level are worked out (plan_bots_teams_tf2_part_g G.5 step 6): the book the
/// brains read, the tally of what the jumps did, and the level's planner, with the one piece of
/// work that joins them. <see cref="Work"/> is the worker's: it plans every stretch the book has
/// waiting and flies again every jump a bot made, one at a time, and it is the only thing that
/// touches the planner, so a search never runs in a decide or on the game's thread. Whoever owns
/// the desk says when (<see cref="Cue"/>): a task in the game, the fact itself in a fact.
/// </summary>
public sealed class JumpDesk(JumpBook book, JumpTally tally, Func<double>? cpu = null)
{
    private readonly ConcurrentQueue<(int Serial, JumpFlown Flown, IJumpPlanner For)> _flown = new();
    private IJumpPlanner? _planner;
    private int _working;
    private long _planned, _predicted, _faults, _flights, _floods;
    private double _micros;

    public JumpBook Book => book;

    public JumpTally Tally => tally;

    /// <summary>There is work: called from whichever thread queued it. Set once, before any ask.</summary>
    public Action? Cue
    {
        get => book.Queued;
        set => book.Queued = value;
    }

    /// <summary>A level's planner: what was known of the last level goes.</summary>
    public void Open(IJumpPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(planner);
        book.Clear();
        Volatile.Write(ref _planner, planner);
    }

    /// <summary>No level: nothing is planned until the next <see cref="Open"/>.</summary>
    public void Close()
    {
        Volatile.Write(ref _planner, null);
        book.Clear();
    }

    /// <summary>Whether a level's planner is in.</summary>
    public bool IsOpen => Volatile.Read(ref _planner) is not null;

    /// <summary>A jump a bot made: tallied, and queued to be flown again in the simulator from the same launch.</summary>
    public void Flew(JumpFlown flown)
    {
        int serial = tally.Ended(flown);
        if (flown.Launched && Volatile.Read(ref _planner) is { } planner)
        {
            _flown.Enqueue((serial, flown, planner));
            Cue?.Invoke();
        }
    }

    /// <summary>
    /// Does everything waiting, on the calling thread; answers how many pieces it did. One caller
    /// at a time works: a second finds the first at it and leaves, and the first looks again
    /// before it goes, so nothing queued meanwhile is left behind. A stretch the planner throws on
    /// is answered none (<see cref="JumpRefusal.Faulted"/>) and counted; the work goes on.
    /// </summary>
    public int Work()
    {
        int done = 0;
        while (Interlocked.CompareExchange(ref _working, 1, 0) == 0)
        {
            double start = cpu?.Invoke() ?? 0d;
            try
            {
                done += Drain();
            }
            finally
            {
                if (cpu is not null)
                    _micros += cpu() - start;
                Volatile.Write(ref _working, 0);
            }

            if (!((Volatile.Read(ref _planner) is not null && book.Asks > 0) || !_flown.IsEmpty))
                break;
        }

        return done;
    }

    private int Drain()
    {
        int done = 0;
        while (true)
        {
            IJumpPlanner? planner = Volatile.Read(ref _planner);
            if (planner is not null && book.TryTake(out JumpStretchAsk ask, out int generation))
            {
                JumpPlanned planned;
                try
                {
                    planned = planner.Plan(ask);
                }
                catch (Exception e)
                {
                    planned = new JumpPlanned(null, JumpRefusal.Faulted, 0);
                    Fault(e);
                }

                book.Answer(ask, generation, planned);
                Interlocked.Increment(ref _planned);
            }
            else if (_flown.TryDequeue(out (int Serial, JumpFlown Flown, IJumpPlanner For) flown))
            {
                planner = flown.For;
                try
                {
                    tally.Predicted(flown.Serial, planner.Predict(flown.Flown.Recipe, flown.Flown.Launch));
                    Interlocked.Increment(ref _predicted);
                }
                catch (Exception e)
                {
                    Fault(e);
                }
            }
            else
            {
                return done;
            }

            done++;
            Interlocked.Exchange(ref _flights, planner.Flights);
            Interlocked.Exchange(ref _floods, planner.Floods);
        }
    }

    private void Fault(Exception e)
    {
        Interlocked.Increment(ref _faults);
        LastFault = $"{e.GetType().Name}: {e.Message}";
    }

    /// <summary>The last fault's message, or null.</summary>
    public string? LastFault { get; private set; }

    public JumpDeskCounts Counts => new(Interlocked.Read(ref _planned), Interlocked.Read(ref _predicted), Interlocked.Read(ref _flights), Interlocked.Read(ref _floods),
                                        _micros, Interlocked.Read(ref _faults));
}
