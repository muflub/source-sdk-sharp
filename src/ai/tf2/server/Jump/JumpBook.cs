namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// What a Soldier's tree asks about a jump (plan_bots_teams_tf2_part_g G.5 step 6). The answer is
/// read, never worked out: a decide costs a look through what is already known.
/// </summary>
public interface IJumpAdvisor
{
    /// <summary>
    /// The jump to begin now on <paramref name="ask"/>'s stretch, or null: none is known for it
    /// (it is then asked about, to be known the next time), or the simulator found none.
    /// </summary>
    JumpPlan? Advise(in JumpStretchAsk ask);
}

/// <summary>The book's numbers.</summary>
public sealed record JumpBookSettings
{
    /// <summary>Two goals this near are one: a plan toward the one serves a bot going to the other.</summary>
    public float GoalSame { get; init; } = 96f;

    /// <summary>
    /// A plan is begun from this far before its takeoff at most. And no later than where a bot whose view is late
    /// would stop for it (two of the plan's hold runs before its slide's end: <see cref="RocketJumpProgram"/>): from
    /// further on there is neither the room to stop and run at the jump nor the time for any hand to be round. A
    /// plan that allows no wait is begun no later than this share of its slide past its takeoff.
    /// </summary>
    public float ApproachMax { get; init; } = 384f;

    public float LateShare { get; init; } = 0.25f;

    /// <summary>How far to a side of a plan's line a bot may be, and how near its way the bot's heading (the cosine).</summary>
    public float AsideMax { get; init; } = 32f;

    public float HeadingDot { get; init; } = 0.94f;

    /// <summary>A bot this far before a plan's takeoff, on its line and not yet past where the plan may be begun, waits for it rather than asking for another.</summary>
    public float WaitFor { get; init; } = 640f;

    /// <summary>An answer of none covers asks from this near where it was asked, the same way and to the same goal.</summary>
    public float RefusalReach { get; init; } = 128f;

    /// <summary>How long an answer of none stands, in ticks: the map's own (no straight, no recipe) and the moment's (turning, no route).</summary>
    public int RefusalTicks { get; init; } = 4000;

    public int PassingTicks { get; init; } = 66;

    /// <summary>The plans, the answers of none and the asks kept; the oldest go.</summary>
    public int Plans { get; init; } = 96;

    public int Refusals { get; init; } = 128;

    public int Asks { get; init; } = 4;
}

/// <summary>What the book has done, for the counters.</summary>
public readonly record struct JumpBookCounts(long Advised, long Asked, long Dropped, long Planned, long Refused, int Plans, int Waiting);

/// <summary>
/// The jumps known on a level (plan_bots_teams_tf2_part_g G.5 step 6): the plans a planner has
/// answered, the stretches it answered none for, and the stretches waiting to be asked. The brains
/// read it from their own threads (<see cref="Advise"/>) and a worker fills it
/// (<see cref="TryTake"/>, <see cref="Answer"/>), so everything is under one lock and nothing in a
/// read flies or searches: it compares the bot's place with the plans it holds.
///
/// <para>
/// A plan serves a bot going to the plan's goal that is on the plan's line, heading along it, and
/// between <see cref="JumpBookSettings.ApproachMax"/> before the takeoff and the last place it can
/// still be made from (<see cref="Latest"/>). A
/// stretch nothing is known of is queued once: an ask near one already waiting, near an answer of
/// none that still stands, or on the line of a plan it will soon be in reach of, is not queued again.
/// </para>
/// </summary>
public sealed class JumpBook(JumpBookSettings settings) : IJumpAdvisor
{
    private readonly object _gate = new();
    private readonly List<JumpPlan> _plans = [];
    private readonly List<(JumpStretchAsk Ask, JumpRefusal Why, int Until)> _refused = [];
    private readonly List<JumpStretchAsk> _asks = [];
    private readonly long[] _refusals = new long[Enum.GetValues<JumpRefusal>().Length];
    private JumpStretchAsk? _working;
    private int _nextId = 1, _generation;
    private long _advised, _asked, _dropped, _planned;

    public JumpBookSettings Settings => settings;

    /// <summary>Called when a stretch is queued, from the asking thread: a worker's cue. Set once, before any ask.</summary>
    public Action? Queued { get; set; }

    public JumpPlan? Advise(in JumpStretchAsk ask)
    {
        bool queued = false;
        lock (_gate)
        {
            bool wait = false;
            foreach (JumpPlan plan in _plans)
            {
                if (plan.Goal.DistanceTo(ask.Goal) > settings.GoalSame || plan.Way.Dot(ask.Heading) < settings.HeadingDot)
                    continue;
                (float along, float aside) = plan.Before(ask.Origin);
                if (aside > settings.AsideMax)
                    continue;
                if (along <= settings.ApproachMax && -along <= Latest(plan))
                {
                    _advised++;
                    return plan;
                }

                // Still to come within reach of it: a bot already past where it may be begun asks about the stretch ahead instead.
                wait |= -along <= Latest(plan) && along <= settings.WaitFor;
            }

            if (!wait && !Covered(ask))
            {
                if (_asks.Count >= settings.Asks)
                {
                    _dropped++;
                }
                else
                {
                    _asks.Add(ask);
                    _asked++;
                    queued = true;
                }
            }
        }

        if (queued)
            Queued?.Invoke();
        return null;
    }

    /// <summary>How far past its takeoff <paramref name="plan"/> may still be begun: see <see cref="JumpBookSettings.ApproachMax"/>.</summary>
    public float Latest(JumpPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.HoldRun > 0f ? plan.Slide - 2f * plan.HoldRun : plan.Slide * settings.LateShare;
    }

    /// <summary>Whether an ask like <paramref name="ask"/> waits, is being worked on, or was answered none and that still stands.</summary>
    private bool Covered(in JumpStretchAsk ask)
    {
        if (_working is { } working && Near(working, ask))
            return true;
        foreach (JumpStretchAsk waiting in _asks)
        {
            if (Near(waiting, ask))
                return true;
        }

        for (int i = _refused.Count - 1; i >= 0; i--)
        {
            if (ask.At.Value >= _refused[i].Until)
                _refused.RemoveAt(i);
            else if (Near(_refused[i].Ask, ask))
                return true;
        }

        return false;
    }

    private bool Near(in JumpStretchAsk a, in JumpStretchAsk b)
        => a.Goal.DistanceTo(b.Goal) <= settings.GoalSame && a.Origin.DistanceTo(b.Origin) <= settings.RefusalReach && a.Heading.Dot(b.Heading) >= settings.HeadingDot;

    /// <summary>The plan numbered <paramref name="id"/>, or null: none the book holds (another level's, or one long gone).</summary>
    public JumpPlan? Plan(int id)
    {
        lock (_gate)
        {
            foreach (JumpPlan plan in _plans)
            {
                if (plan.Id == id)
                    return plan;
            }
        }

        return null;
    }

    /// <summary>The next stretch to work on, and the level it is of, or false with none waiting. The worker's.</summary>
    public bool TryTake(out JumpStretchAsk ask, out int generation)
    {
        lock (_gate)
        {
            generation = _generation;
            if (_asks.Count == 0)
            {
                ask = default;
                return false;
            }

            ask = _asks[0];
            _asks.RemoveAt(0);
            _working = ask;
            return true;
        }
    }

    /// <summary>
    /// What the planner answered for <paramref name="ask"/>, taken at <paramref name="generation"/>:
    /// a plan is kept and numbered, an answer of none stands for a while. An answer for a level since
    /// left is dropped. Answers the plan as kept, or null.
    /// </summary>
    public JumpPlan? Answer(in JumpStretchAsk ask, int generation, in JumpPlanned planned)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return null;
            _working = null;
            if (planned.Plan is not { } plan)
            {
                _refusals[(int)planned.Refusal]++;
                int ticks = planned.Refusal is JumpRefusal.Turning or JumpRefusal.NoRoute ? settings.PassingTicks : settings.RefusalTicks;
                if (_refused.Count >= settings.Refusals)
                    _refused.RemoveAt(0);
                _refused.Add((ask, planned.Refusal, ask.At.Value + ticks));
                return null;
            }

            if (_plans.Count >= settings.Plans)
                _plans.RemoveAt(0);
            JumpPlan kept = plan with { Id = _nextId++ };
            if (_nextId > AbilityCall.MaxArgument)
                _nextId = 1;
            _plans.Add(kept);
            _planned++;
            return kept;
        }
    }

    /// <summary>Another level: everything known goes, and what a worker still answers for the old one is dropped.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _generation++;
            _plans.Clear();
            _refused.Clear();
            _asks.Clear();
            _working = null;
        }
    }

    public JumpBookCounts Counts
    {
        get
        {
            lock (_gate)
                return new JumpBookCounts(_advised, _asked, _dropped, _planned, _refusals.Sum(), _plans.Count, _asks.Count + (_working is null ? 0 : 1));
        }
    }

    /// <summary>The stretches queued and not yet taken.</summary>
    public int Asks
    {
        get
        {
            lock (_gate)
                return _asks.Count;
        }
    }

    /// <summary>How many stretches were answered none for <paramref name="why"/>.</summary>
    public long Refused(JumpRefusal why)
    {
        lock (_gate)
            return _refusals[(int)why];
    }

    /// <summary>The plans held, oldest first: a copy.</summary>
    public JumpPlan[] Plans()
    {
        lock (_gate)
            return [.. _plans];
    }
}
