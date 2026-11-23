using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// Each bot's own way to the point in play (lane Groutes; the user 2026-11-15: "they always seem to take the exact same
/// path"; ruled 2026-11-16: "a per-bot, per-life cost within 1.1 times the shortest", raised 2026-11-20: "Raise to 1.15";
/// and 2026-11-20: "Bots should take different ways to the same objective if arriving at the same time"). A person picks one of the near-equal
/// ways (this hall, not that one) and keeps to it for that life; the next life it may take another. So for each team and
/// goal the corridors from its spawn are found once, on a worker (<see cref="TFRouteCorridors"/>: the shortest, and each
/// distinct way no dearer than 1.15 times it), and each life of each bot takes one (<see cref="Choose"/>); its searches then
/// mind that corridor's field, which makes every step off the corridor 1.15 times as dear and none on it dearer.
/// <list type="bullet">
/// <item><b>The cap.</b> Every step a field weighs costs from what it would without the field to (1 + <see cref="MaxExtra"/>)
/// times that (<see cref="Compose"/>; links and a cell's own extra are not weighed). So any route costs from its own cost to
/// 1.15 times it, and the route a bot takes, to any goal, costs it no more than 1.15 times the route it would take without
/// the field: the route it takes is no dearer under the field than that one, which the field makes at most 1.15 times
/// dearer. A corridor dearer than 1.15 times the shortest is never offered (none would be kept to).</item>
/// <item><b>A life holds its way.</b> The corridor is taken once a life and goal, and every search of the life minds the same
/// composed field (the same object), so a replan finds the same way. A goal's corridors are found when a bot first asks for
/// them (a level's first bots ask while the round's start holds them); until they are, the bot minds no field.</item>
/// <item><b>Together, apart.</b> Teammates who would reach the same goal at about the same time (their expected arrivals,
/// the moment each took its corridor plus that corridor's walk at <see cref="RunSpeed"/>, within <see cref="TogetherSeconds"/>
/// of each other) take different corridors: a bot takes one no such teammate is on, the least taken when every corridor
/// has one. Five seconds is a respawn wave's bots leaving together, and at a Soldier's run 1,200 u: further apart than
/// that, two bots on one corridor are not seen as together. A bot with no such teammate draws as a lone bot does.</item>
/// <item><b>From personality: Teamwork.</b> Among the corridors open to it (all, alone; those no teammate arriving with it
/// is on, together), a lone wolf strays to a longer one more often and a player glued to the team keeps to the shortest
/// open: a corridor's chance falls with its extra cost (<see cref="Steep"/>), three times as fast at Teamwork 10 as at 0.
/// Teamwork does not keep a bot on a teammate's corridor: the ruling spreads a wave whatever its players are.</item>
/// <item><b>Repeatable.</b> The draw is seeded by the match, the bot's seed index (the roster's, kept over a map change), its
/// life's number and the goal, so a replay with the same lives draws the same ways.</item>
/// <item><b>The switch.</b> <c>ai_tf_feel_routes</c> 0 and a bot added afterwards has no field (<see cref="TFFeelSwitches.Routes"/>).</item>
/// </list>
/// The bots' lives are counted by <see cref="TFRouteLives"/>, before their commands run (each turn from dead to alive is a
/// life; where it stands at its life's first ask is its start), which the navigation profile, built before the bots, cannot
/// hold; the navigation profile composes the field with the team's hazard when a bot's obstacles are asked for
/// (<see cref="TFNavProfile.HazardFor(EntityHandle, int, NavGraph, NavObstacleState)"/>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFRouteVariation(TFObjectiveFeed objectives)
{
    /// <summary>The most a step costs over its own under a field: the ruling's 1.15 times (raised from 1.1, 2026-11-20).</summary>
    public const float MaxExtra = 0.15f;

    /// <summary>How fast a bot is taken to walk a corridor, for when it arrives: a Soldier's run, as the links are weighed.</summary>
    public const float RunSpeed = 240f;

    /// <summary>Teammates whose expected arrivals at one goal are this near arrive together.</summary>
    public const float TogetherSeconds = 5f;

    /// <summary>How fast a corridor's chance falls with its extra cost for a lone wolf: e^(-10 · 0.05), 0.61 of the shortest's, for one 5 % dearer.</summary>
    public const float Steep = 10f;

    /// <summary>How much faster it falls at Teamwork 10: three times as fast.</summary>
    public const float TeamworkSteep = 2f;

    /// <summary>A bot whose life starts this near the start a goal's corridors were found from uses them; further, they are found again from its start.</summary>
    public const float RoomUnits = 1500f;

    /// <summary>The stream the routes' seeds are drawn from, beside the bots' other streams.</summary>
    private const ulong Stream = 0x524F_5554_4553_0001UL;

    /// <summary>A slot's bot: its seed index, its Teamwork's share of the cap, and whether it has a field; an empty slot is not <paramref name="Present"/>.</summary>
    private readonly record struct Bot(bool Present, int SeedIndex, float Teamwork, bool On);

    /// <summary>A team's corridors to one goal from one start, found on a worker.</summary>
    private sealed record CorridorSet(int Team, int GoalId, AiVector Start, ulong GraphHash, Task<IReadOnlyList<RouteCorridor>> Work, List<(float Ratio, float Own)> Tries)
    {
        /// <summary>Who took which corridor: the slot, its life, the corridor and when it was expected to arrive.</summary>
        public List<(int Slot, int Life, int Corridor, double Arrival)> Taken { get; } = new();

        public bool Ready => Work.IsCompleted;

        public IReadOnlyList<RouteCorridor> Corridors => Work.IsCompletedSuccessfully ? Work.Result : [];
    }

    /// <summary>One life of a slot's bot: where it started, the goal it was last asked for, its corridors and draw, and the field composed.</summary>
    private sealed class Life(int number)
    {
        public int Number { get; } = number;

        /// <summary>Where the bot stood when its searches were first asked for in this life: its spawn.</summary>
        public AiVector? Start { get; set; }

        public int GoalId { get; set; } = -1;

        public CorridorSet? Set { get; set; }

        public int Choice { get; set; } = -1;

        public NavCellHazard? Composed { get; set; }

        public NavCellHazard? Team { get; set; }

        public float TeamWeight { get; set; }
    }

    private volatile Bot[] _bySlot = [];
    private readonly Life?[] _lives = new Life?[VisionPass.Slots];
    private readonly int[] _numbers = new int[VisionPass.Slots];
    private readonly bool[] _alive = new bool[VisionPass.Slots];
    private readonly List<CorridorSet> _sets = new();
    private CancellationTokenSource _cancel = new();
    private ulong _graphHash;
    private double _now;
    private readonly List<(int Slot, int Team, int Life, int Goal, int Choice, int Of, double At)> _draws = new();

    /// <summary>The most takings kept for <see cref="Draws"/>: a long game's first are let go.</summary>
    public const int DrawsKept = 2000;
    private readonly Lock _gate = new();

    /// <summary>How many fields have been composed: one a bot a life and goal (and one when the team's hazard is republished).</summary>
    public int Composes { get; private set; }

    /// <summary>
    /// Seats <paramref name="all"/>, the bots now: a bot already in its slot keeps whether it has a field (the switch,
    /// <paramref name="on"/>, is read for bots added afterwards); a new one starts its lives again.
    /// </summary>
    public void Seat(IEnumerable<AiBot> all, bool on)
    {
        ArgumentNullException.ThrowIfNull(all);
        Bot[] old = _bySlot;
        var table = new Bot[VisionPass.Slots];
        foreach (AiBot b in all)
        {
            int slot = b.Id.Slot;
            if ((uint)slot >= (uint)table.Length)
                continue;
            bool same = slot < old.Length && old[slot].Present && old[slot].SeedIndex == b.SeedIndex;
            table[slot] = same ? old[slot] : new Bot(true, b.SeedIndex, TeamworkOf(b.Skills), on);
            if (!same)
            {
                lock (_gate)
                {
                    _numbers[slot] = 0;
                    _lives[slot] = null;
                    _alive[slot] = false;
                }
            }
        }

        _bySlot = table;
    }

    /// <summary>Notes whether <paramref name="slot"/>'s bot is <paramref name="alive"/>: each turn from dead to alive is a new life, and a death ends the life's hold on its corridor.</summary>
    public void Note(int slot, bool alive)
    {
        if ((uint)slot >= (uint)_alive.Length)
            return;
        if (alive && !_alive[slot])
        {
            lock (_gate)
                _lives[slot] = new Life(++_numbers[slot]);
        }
        else if (!alive && _alive[slot])
        {
            // A dead bot walks no corridor: a teammate leaving now may take the one it held.
            lock (_gate)
                _lives[slot] = null;
        }

        _alive[slot] = alive;
    }

    /// <summary>The life <paramref name="slot"/>'s bot is in: 0 before its first, then one more each time it comes back.</summary>
    public int LifeOf(int slot) => (uint)slot < (uint)_numbers.Length ? Volatile.Read(ref _numbers[slot]) : 0;

    /// <summary>
    /// What <paramref name="slot"/>'s searches over <paramref name="graph"/> mind, on <paramref name="team"/>: the field of the
    /// corridor its life drew to its team's point in play (<see cref="TFRouteSpread.GoalOf"/> from where the life started: where
    /// it stood, <paramref name="at"/>, at the life's first ask),
    /// composed with its team's <paramref name="minded"/> (<see cref="Compose"/>), at weight 1, the same object for every ask
    /// of the life and goal; <paramref name="minded"/> as it is for a player that is no bot of ours, a bot with the switch
    /// off, no goal, a goal with one way only, or corridors still being found (from <paramref name="obstacles"/>, the bot's).
    /// </summary>
    public NavHazardAllowance For(int slot, NavGraph? graph, int team, NavHazardAllowance minded, NavObstacleState? obstacles, AiVector at)
    {
        Bot[] table = _bySlot;
        if (graph is null || obstacles is null || (uint)slot >= (uint)table.Length || !table[slot].Present || !table[slot].On)
            return minded;
        lock (_gate)
        {
            if (_lives[slot] is not { } life)
                return minded;
            if (graph.Hash != _graphHash)
            {
                // A new level: what was found for the last is no use, and its work stops.
                _cancel.Cancel();
                _cancel.Dispose();
                _cancel = new CancellationTokenSource();
                _sets.Clear();
                _graphHash = graph.Hash;
            }

            AiVector start = life.Start ??= at;
            (int goalId, AiVector goalAt) = TFRouteSpread.GoalOf(objectives.Current, team, start);
            if (goalId < 0)
                return minded;
            if (life.GoalId != goalId || (life.Set is { } old && old.GraphHash != graph.Hash))
                (life.GoalId, life.Set, life.Choice, life.Composed) = (goalId, null, -1, null);
            life.Set ??= SetFor(team, goalId, goalAt, start, graph, obstacles);
            if (!life.Set.Ready || life.Set.Corridors is not { Count: >= 2 } corridors)
                return minded;
            if (life.Choice < 0)
            {
                int[] taken = TakenBy(life.Set, slot, corridors);
                life.Choice = Choose(corridors, AiRandom.SeedFor(SeedOf(table[slot].SeedIndex, life.Number), goalId), table[slot].Teamwork, taken);
                life.Set.Taken.Add((slot, life.Number, life.Choice, _now + corridors[life.Choice].Seconds));
                if (_draws.Count >= DrawsKept)
                    _draws.RemoveAt(0);
                _draws.Add((slot, team, life.Number, goalId, life.Choice, corridors.Count, _now));
            }
            NavCellHazard? hazard = minded.Hazard is { } h && minded.Weight > 0f && h.IsOver(graph) ? h : null;
            float weight = hazard is null ? 0f : minded.Weight;
            if (life.Composed is null || !ReferenceEquals(life.Team, hazard) || life.TeamWeight != weight)
            {
                (life.Composed, life.Team, life.TeamWeight) = (Compose(graph, corridors[life.Choice].Extra, hazard, weight), hazard, weight);
                Composes++;
            }

            return new NavHazardAllowance(life.Composed, 1f);
        }
    }

    /// <summary>Tells the variation the game's time, in seconds: arrivals are reckoned from it.</summary>
    public void Clock(double seconds)
    {
        lock (_gate)
            _now = seconds;
    }

    /// <summary>
    /// How many teammates on each of <paramref name="corridors"/> would arrive with <paramref name="slot"/>'s bot were it to
    /// take that corridor now: those in its life still (a teammate since dead or gone is not), expected within
    /// <see cref="TogetherSeconds"/> of its own arrival on it. Takings long past are let go.
    /// </summary>
    private int[] TakenBy(CorridorSet set, int slot, IReadOnlyList<RouteCorridor> corridors)
    {
        set.Taken.RemoveAll(t => t.Arrival < _now - TogetherSeconds || _lives[t.Slot]?.Number != t.Life);
        var taken = new int[corridors.Count];
        foreach ((int other, int _, int corridor, double arrival) in set.Taken)
        {
            if (other != slot && corridor < taken.Length && Math.Abs(arrival - (_now + corridors[corridor].Seconds)) <= TogetherSeconds)
                taken[corridor]++;
        }

        return taken;
    }

    /// <summary>The corridors of <paramref name="team"/> to its goal from near <paramref name="start"/>: found already, or set to be found now on a worker.</summary>
    private CorridorSet SetFor(int team, int goalId, AiVector goalAt, AiVector start, NavGraph graph, NavObstacleState obstacles)
    {
        foreach (CorridorSet s in _sets)
        {
            if (s.Team == team && s.GoalId == goalId && s.GraphHash == graph.Hash && s.Start.DistanceTo(start) <= RoomUnits)
                return s;
        }

        CellId from = NavPoints.CellNear(graph, start, ClearanceClass.Stand), to = NavPoints.CellNear(graph, goalAt, ClearanceClass.Stand);
        // The bot's obstacles as they stand now (its team's cells, the doors), copied here for the worker.
        NavObstacleState walker = TFRouteCorridors.Walker(obstacles);
        CancellationToken cancel = _cancel.Token;
        var tries = new List<(float Ratio, float Own)>();
        Task<IReadOnlyList<RouteCorridor>> work = from.IsValid && to.IsValid
            ? Task.Run(() => TFRouteCorridors.Find(graph, from, to, walker, cancel, tries), cancel)
            : Task.FromResult<IReadOnlyList<RouteCorridor>>([]);
        var set = new CorridorSet(team, goalId, start, graph.Hash, work, tries);
        _sets.Add(set);
        return set;
    }

    /// <summary>Every goal's corridors being found now, finished (well or not): for a fact that reads them after.</summary>
    public Task Settled()
    {
        Task[] work;
        lock (_gate)
            work = [.. _sets.Select(s => (Task)s.Work)];
        return Task.WhenAll(work).ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Each bot's life now: its number, the goal it was last asked for, the corridors' costs against the shortest and the one drawn (for <c>/ai/routes/spread</c>).</summary>
    public IReadOnlyList<(int Slot, int Life, int Goal, float[] Ratios, int Choice)> Lives()
    {
        var rows = new List<(int, int, int, float[], int)>();
        lock (_gate)
        {
            for (int slot = 0; slot < _lives.Length; slot++)
            {
                if (_lives[slot] is { } life)
                    rows.Add((slot, life.Number, life.GoalId, life.Set is { Ready: true } s ? [.. s.Corridors.Select(c => c.Ratio)] : [], life.Choice));
            }
        }

        return rows;
    }

    /// <summary>The corridor sets found or being found: each one's team, goal, start, whether it is ready, its corridors' costs over the shortest and each search's way (cost over the shortest, share its own) (for <c>/ai/routes/spread</c>).</summary>
    public IReadOnlyList<(int Team, int Goal, AiVector Start, bool Ready, float[] Ratios, (float Ratio, float Own)[] Tries)> Sets()
    {
        lock (_gate)
        {
            return [.. _sets.Select(s => (s.Team, s.GoalId, s.Start, s.Ready, s.Ready ? s.Corridors.Select(c => c.Ratio).ToArray() : [],
                                          s.Ready ? s.Tries.ToArray() : []))];
        }
    }

    /// <summary>Every corridor a life took (for <c>/ai/routes/spread</c>): its slot, team, life, goal, the corridor and how many the goal had, and the game's time.</summary>
    public IReadOnlyList<(int Slot, int Team, int Life, int Goal, int Choice, int Of, double At)> Draws()
    {
        lock (_gate)
            return [.. _draws];
    }

    /// <summary>The seed of the bot of <paramref name="seedIndex"/> in its life <paramref name="life"/>.</summary>
    public static ulong SeedOf(int seedIndex, int life) => AiRandom.SeedFor(AiRandom.SeedFor(AiBots.MatchSeed ^ Stream, seedIndex), life);

    /// <summary>A profile's Teamwork over the cap (a profile without the skill reads its own mean level, as the teams do).</summary>
    public static float TeamworkOf(SkillProfile skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        return Math.Clamp(StandardSkills.TeamworkOf(skills), 0f, StandardSkills.Cap) / StandardSkills.Cap;
    }

    /// <summary>
    /// The corridor of <paramref name="corridors"/> a life of <paramref name="seed"/> takes, for a bot of
    /// <paramref name="teamwork"/> (0 to 1), with <paramref name="taken"/> teammates arriving with it on each (none: alone):
    /// it takes one of the least taken (with any free, a free one), each one's chance e^(-k · (its cost over the shortest's
    /// − 1)), k being <see cref="Steep"/> times (1 + <see cref="TeamworkSteep"/> · teamwork). Alone, the draw is the lone
    /// bot's whatever the counts' absence.
    /// </summary>
    public static int Choose(IReadOnlyList<RouteCorridor> corridors, ulong seed, float teamwork, IReadOnlyList<int>? taken = null)
    {
        ArgumentNullException.ThrowIfNull(corridors);
        if (corridors.Count == 0)
            throw new ArgumentException("no corridor to choose", nameof(corridors));
        if (taken is not null && taken.Count != corridors.Count)
            throw new ArgumentException($"{taken.Count} counts for {corridors.Count} corridors", nameof(taken));
        float k = Steep * (1f + (TeamworkSteep * Math.Clamp(teamwork, 0f, 1f)));
        int least = taken is null ? 0 : taken.Min();
        float[] weights = [.. corridors.Select((c, i) => taken is not null && taken[i] > least ? 0f : MathF.Exp(-k * (c.Ratio - 1f)))];
        float draw = new AiRandom(seed).NextFloat() * weights.Sum();
        for (int i = 0; i < weights.Length; i++)
        {
            draw -= weights[i];
            if (draw < 0f)
                return i;
        }

        return weights.Length - 1;
    }

    /// <summary>
    /// The hazard a search of weight 1 minds for a field <paramref name="extra"/> over a team's <paramref name="team"/> at
    /// <paramref name="teamWeight"/> (none: null): a step into a cell costs its own times (1 + the team's share, at most
    /// <see cref="NavCellHazard.MaxScale"/>) times (1 + the field's, at most <see cref="MaxExtra"/>), and never more than the
    /// hazard's own most. So each step costs from what it would without the field to (1 + <see cref="MaxExtra"/>) times that.
    /// </summary>
    public static NavCellHazard Compose(NavGraph graph, float[] extra, NavCellHazard? team, float teamWeight)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(extra);
        if (extra.Length != graph.CellCount)
            throw new ArgumentException($"{extra.Length} values for {graph.CellCount} cells", nameof(extra));
        var seconds = new float[extra.Length];
        for (int c = 0; c < seconds.Length; c++)
        {
            float own = team is null ? 0f : team.Scale(c, teamWeight);
            seconds[c] = MathF.Min(NavCellHazard.MaxScale, ((1f + own) * (1f + Math.Clamp(extra[c], 0f, MaxExtra))) - 1f);
        }

        return new NavCellHazard(graph, seconds);
    }

    /// <summary>The team's hazard <paramref name="composed"/> was composed over (null for none, or for a hazard this did not compose).</summary>
    public NavCellHazard? TeamOf(NavCellHazard? composed)
    {
        if (composed is null)
            return null;
        lock (_gate)
        {
            foreach (Life? life in _lives)
            {
                if (life is not null && ReferenceEquals(life.Composed, composed))
                    return life.Team;
            }
        }

        return null;
    }
}

/// <summary>
/// What <see cref="TFRouteVariation"/> is told of the bots (lane Groutes), before their commands run: the game's time (for
/// when bots arrive); whenever the bots change,
/// who they are and whether their routes vary (<c>ai_tf_feel_routes</c> as it stands, for bots added since); each tick, which
/// are alive (each turn from dead to alive is a life). Its own service, as <see cref="TFRouteWeights"/>' feeder is: the
/// variation is the navigation profile's, which the navigation is built with, so it cannot hold the bots, which hold the
/// navigation.
/// </summary>
[SideService(Side.Server)]
public sealed class TFRouteLives(AiBots bots, IEnginePlayers players, TFRouteVariation variation, TFFeelSwitchConVars feel, IEngineEnvironment environment)
    : IFrameLifecycle
{
    private int _botsVersion = -1;

    /// <summary>Before the bots' commands (First + 500), which ask for each bot's obstacles; after the route weights (First + 495).</summary>
    public int FrameOrder => ModStages.First + 496;

    public void OnPreThink(float frameTime)
    {
        if (environment.Globals is { IsAvailable: true } g)
            variation.Clock(g.CurTime);
        if (bots.Version != _botsVersion)
        {
            _botsVersion = bots.Version;
            variation.Seat(bots.Bots, feel.Read().Routes);
        }

        foreach (AiBot b in bots.Bots)
        {
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            variation.Note(b.Id.Slot, p != EntityHandle.None && players.IsAlive(p));
        }
    }
}
