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

    /// <summary>
    /// The link the route of the bot in <paramref name="slot"/> takes next, as the plan the program flies, when its
    /// follower last said, before <paramref name="tick"/>, that the bot stands at the link's start on a route to
    /// <paramref name="goal"/> (lane G5c-2b); null otherwise, or for a link of a set the level no longer has.
    /// </summary>
    JumpPlan? RouteLink(int slot, Tick tick, AiVector goal) => null;

    /// <summary>
    /// What the follower of the bot in <paramref name="slot"/> last said, before <paramref name="tick"/>, of its route to
    /// <paramref name="goal"/> (lane G7a): the newest such report (a link next, or no way and what it needs), or null when
    /// none of the reports kept is of a route to that goal. A report of a route to another goal (the driver's "as near as it
    /// can get") does not hide one before it.
    /// </summary>
    NavLinkStep? RouteSaid(int slot, Tick tick, AiVector goal) => null;

    /// <summary>A place the bot in <paramref name="slot"/> is sent to by hand (<c>ai_tf_errand</c>, for proving jumps), or false.</summary>
    bool Errand(int slot, out AiVector goal)
    {
        goal = default;
        return false;
    }

    /// <summary>
    /// The tree did not begin a link this decide, for <paramref name="why"/>: counted only when the bot's route has one it stands at
    /// the start of for this decide (what <see cref="RouteLink"/> would answer), so a count is a wait at a takeoff and its reason.
    /// </summary>
    void LinkRefused(int slot, Tick tick, AiVector goal, JumpLinkRefusal why)
    {
    }

    /// <summary>
    /// The level's links that are the quicker way to a heal place by their landing (lane G9b, <see cref="JumpPlan.JumpWays"/>),
    /// as the plans the program flies: what a bot looks through for a jump to a pack near it. None by default.
    /// </summary>
    IReadOnlyList<JumpPlan> HealLinks => [];

    /// <summary>
    /// Whether a bot standing at <paramref name="landing"/> is within a walk of the heal place at <paramref name="place"/>
    /// (lane G9b): a jump that came down by its place, or one that fizzled, was called off or fell short of it. Null when the
    /// level's places are not known. Null by default.
    /// </summary>
    bool? LandedBy(AiVector landing, AiVector place) => null;
}

/// <summary>Why the tree did not begin the link its bot stands at the start of (the first of its rule's parts that failed).</summary>
public enum JumpLinkRefusal : byte
{
    None = 0,

    /// <summary>Another weapon in hand.</summary>
    Launcher,

    /// <summary>Under <c>RocketJumpRockets</c> loaded.</summary>
    Clip,

    /// <summary>The launcher being reloaded.</summary>
    Reloading,

    /// <summary>Not on the ground.</summary>
    Airborne,

    /// <summary>An enemy in sight.</summary>
    Sighted,

    /// <summary>Carrying the flag.</summary>
    Carrying,

    /// <summary>Its health, or its health after the link's blast, under the floor.</summary>
    Health,

    /// <summary>No goal, or not following a way to it.</summary>
    NoGoal,

    /// <summary>In the middle of a glance.</summary>
    Glance,

    /// <summary>The jumps' spacing: another jump was begun less than <c>RocketJumpEverySeconds</c> ago.</summary>
    Spacing,
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

/// <summary>
/// What the book has done, for the counters: <paramref name="Calls"/> every <see cref="JumpBook.Advise"/> (lane G9b), of which
/// <paramref name="Advised"/> answered a plan, <paramref name="Asked"/> queued the stretch, <paramref name="Dropped"/> found the
/// queue full and <paramref name="Covered"/> found it waiting, under way or answered none that still stands (the rest wait for
/// a plan ahead).
/// </summary>
public readonly record struct JumpBookCounts(long Advised, long Asked, long Dropped, long Planned, long Refused, int Plans, int Waiting, long Calls = 0, long Covered = 0);

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
    private long _advised, _asked, _dropped, _planned, _calls, _covered;

    public JumpBookSettings Settings => settings;

    /// <summary>Called when a stretch is queued, from the asking thread: a worker's cue. Set once, before any ask.</summary>
    public Action? Queued { get; set; }

    public JumpPlan? Advise(in JumpStretchAsk ask)
    {
        bool queued = false;
        lock (_gate)
        {
            _calls++;
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

            bool covered = !wait && Covered(ask);
            _covered += covered ? 1 : 0;
            if (!wait && !covered)
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

    /// <summary>The plan numbered <paramref name="id"/>, or null: none the book holds (another level's, or one long gone). A link's number answers the link's plan.</summary>
    public JumpPlan? Plan(int id)
    {
        lock (_gate)
        {
            if (NavLinks.LinkOf(id) is >= 0 and int link)
                return _linkPlans is { } plans && link < plans.Length ? plans[link] : null;
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
            JumpPlan kept = plan with { Id = _nextId++, Heals = _heals is { } heals ? heals.Near(new AiVector(plan.Touch.X, plan.Touch.Y, plan.Touch.Z)) : [] };
            // A plan's number stays under the links' (NavLinks.ArgumentBase), so a one-shot names one or the other.
            if (_nextId >= NavLinks.ArgumentBase)
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
            _links = null;
            _linkPlans = null;
            _healLinks = [];
            _heals = null;
            Array.Clear(_routes);
        }
    }

    private JumpHeals? _heals;

    /// <summary>
    /// The level's heal places (lane G9), or null: a stretch's plan answered after they are set carries those near its touch
    /// (<see cref="JumpPlan.Heals"/>). Set when the level's links' task has them; another level clears them.
    /// </summary>
    public JumpHeals? Heals
    {
        get
        {
            lock (_gate)
                return _heals;
        }

        set
        {
            lock (_gate)
                _heals = value;
        }
    }

    public bool? LandedBy(AiVector landing, AiVector place)
    {
        JumpHeals? heals = Heals;
        if (heals is null)
            return null;
        foreach (JumpHeal h in heals.Near(landing))
        {
            if (h.At.DistanceTo(place) <= 1f)
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ the level's links (lane G5c-2b)

    private NavLinks? _links;
    private JumpPlan[]? _linkPlans;
    private JumpPlan[] _healLinks = [];

    public IReadOnlyList<JumpPlan> HealLinks
    {
        get
        {
            lock (_gate)
                return _healLinks;
        }
    }
    private readonly RouteReports[] _routes = new RouteReports[RouteSlots];
    private readonly AiVector?[] _errands = new AiVector?[RouteSlots];
    private long _routeNotes, _routeAdvised;

    /// <summary>The bot slots a book keeps the routes' reports and the errands of.</summary>
    public const int RouteSlots = 256;

    /// <summary>How many of a bot's last changes of report are kept: a decide a few ticks behind still finds the one of its tick.</summary>
    public const int RouteKept = 8;

    /// <summary>The level's links, and the plan the program flies for each (index for index), or none.</summary>
    public void SetLinks(NavLinks? links, IReadOnlyList<JumpPlan>? plans)
    {
        if (links is not null && (plans is null || plans.Count != links.Count))
            throw new ArgumentException($"{plans?.Count ?? 0} plans for {links.Count} links", nameof(plans));
        lock (_gate)
        {
            _links = links;
            _linkPlans = links is null ? null : [.. plans!];
            _healLinks = _linkPlans is null ? [] : [.. _linkPlans.Where(static p => !p.JumpWays.IsEmpty)];
        }
    }

    /// <summary>
    /// What the follower of the bot in <paramref name="slot"/> said in the drive step handed on <paramref name="stamp"/>
    /// (the main thread's, as it takes the step back): kept when it differs from the last.
    /// </summary>
    public void NoteRoute(int slot, Tick stamp, in NavLinkStep step)
    {
        if ((uint)slot >= RouteSlots)
            return;
        lock (_gate)
        {
            RouteReports r = _routes[slot] ??= new RouteReports();
            if (r.Count > 0 && r.Newest.Step == step)
                return;
            r.Add(stamp, step);
            _routeNotes++;
        }
    }

    public JumpPlan? RouteLink(int slot, Tick tick, AiVector goal)
    {
        lock (_gate)
        {
            if (RouteLinkLocked(slot, tick, goal) is not { } plan)
                return null;
            _routeAdvised++;
            return plan;
        }
    }

    public void LinkRefused(int slot, Tick tick, AiVector goal, JumpLinkRefusal why)
    {
        lock (_gate)
        {
            if (RouteLinkLocked(slot, tick, goal) is not null)
                _linkRefusals[(int)why]++;
        }
    }

    public NavLinkStep? RouteSaid(int slot, Tick tick, AiVector goal)
    {
        if ((uint)slot >= RouteSlots)
            return null;
        lock (_gate)
            return _routes[slot]?.Before(tick, goal, settings.GoalSame);
    }

    private JumpPlan? RouteLinkLocked(int slot, Tick tick, AiVector goal)
    {
        if ((uint)slot >= RouteSlots || _routes[slot] is not { } r || _links is not { } links || _linkPlans is not { } plans || r.Before(tick) is not { } step)
            return null;
        if (!step.IsLink || !step.AtStart || step.Links != links.Hash || (uint)step.Link >= (uint)plans.Length
            || step.Objective.DistanceTo(goal) > settings.GoalSame)
            return null;
        return plans[step.Link];
    }

    private readonly long[] _linkRefusals = new long[Enum.GetValues<JumpLinkRefusal>().Length];

    /// <summary>Decides at a link's start that did not begin it, for <paramref name="why"/>.</summary>
    public long LinkRefusals(JumpLinkRefusal why)
    {
        lock (_gate)
            return _linkRefusals[(int)why];
    }

    /// <summary>Sends the bot in <paramref name="slot"/> to <paramref name="goal"/> by hand, or (null) calls it off.</summary>
    public void SetErrand(int slot, AiVector? goal)
    {
        if ((uint)slot >= RouteSlots)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"slots run 0..{RouteSlots - 1}");
        lock (_gate)
            _errands[slot] = goal;
    }

    public bool Errand(int slot, out AiVector goal)
    {
        goal = default;
        if ((uint)slot >= RouteSlots)
            return false;
        lock (_gate)
        {
            if (_errands[slot] is not { } g)
                return false;
            goal = g;
            return true;
        }
    }

    /// <summary>Reports kept (changes of what a follower said) and link plans answered to a decide.</summary>
    public (long Notes, long Advised) RouteCounts
    {
        get
        {
            lock (_gate)
                return (_routeNotes, _routeAdvised);
        }
    }

    /// <summary>The level's link plans, index for index, or none: a copy.</summary>
    public JumpPlan[] LinkPlans()
    {
        lock (_gate)
            return _linkPlans is null ? [] : [.. _linkPlans];
    }

    /// <summary>One bot's last few changes of report, each with the tick of the drive step that said it, oldest first.</summary>
    private sealed class RouteReports
    {
        private readonly (Tick Stamp, NavLinkStep Step)[] _ring = new (Tick, NavLinkStep)[RouteKept];
        private int _next;

        public int Count { get; private set; }

        public (Tick Stamp, NavLinkStep Step) Newest => _ring[(_next + RouteKept - 1) % RouteKept];

        public void Add(Tick stamp, in NavLinkStep step)
        {
            _ring[_next] = (stamp, step);
            _next = (_next + 1) % RouteKept;
            Count = Math.Min(Count + 1, RouteKept);
        }

        /// <summary>The newest report stamped before <paramref name="tick"/> of a route to within <paramref name="same"/> of <paramref name="goal"/>, or null.</summary>
        public NavLinkStep? Before(Tick tick, AiVector goal, float same)
        {
            for (int i = 1; i <= Count; i++)
            {
                (Tick stamp, NavLinkStep step) = _ring[(_next + RouteKept - i) % RouteKept];
                if (stamp.Value < tick.Value && (step.IsLink || step.NoWay) && step.Objective.DistanceTo(goal) <= same)
                    return step;
            }

            return null;
        }

        /// <summary>The newest report stamped before <paramref name="tick"/>, or null.</summary>
        public NavLinkStep? Before(Tick tick)
        {
            for (int i = 1; i <= Count; i++)
            {
                (Tick stamp, NavLinkStep step) = _ring[(_next + RouteKept - i) % RouteKept];
                if (stamp.Value < tick.Value)
                    return step;
            }

            return null;
        }
    }

    public JumpBookCounts Counts
    {
        get
        {
            lock (_gate)
                return new JumpBookCounts(_advised, _asked, _dropped, _planned, _refusals.Sum(), _plans.Count, _asks.Count + (_working is null ? 0 : 1), _calls, _covered);
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
