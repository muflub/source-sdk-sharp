using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Rules;
using TF2.Shared.Rules;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// <c>/ai/routes/spread</c> (lane Groutes, measurement only: no bot reads it): every <see cref="SampleTicks"/> ticks of a
/// running round, each living bot's place, kept as one walk a life from where it could first move towards its team's
/// point in play (<see cref="RouteWalk"/>); the GET measures them (<see cref="TFRouteSpread"/>): per team and goal the
/// walks' sideways spread per band, the corridors of those that arrived, and each walk's seconds to the front and to the
/// point; and per round, the seconds from its start to each team's first bot on its point; and each bot's life now, the
/// corridors its goal has and the one it drew (<see cref="TFRouteVariation"/>). <c>?walks=1</c> adds every
/// walk's points, <c>?reset=1</c> forgets every walk (a measured run's start). A level's tick count going back (a new
/// level) forgets them too.
/// </summary>
[SideService(Side.Server)]
public sealed class TFRouteSpreadRoute(AiBots bots, IEnginePlayers players, VisionPass vision, TFObjectiveFeed feed, ITFRoundFacts round, IEngineFrame frame,
                                       IEngineEnvironment environment, IRenderedTeams teams, TFRouteVariation? variation = null)
    : IFrameLifecycle, IDebugRouteProvider, IOrderedHook
{
    public const string Route = "/ai/routes/spread";

    /// <summary>Two samples a second at TF2's 66 ticks.</summary>
    public const int SampleTicks = 33;

    /// <summary>The last sample's tick before any: a level's first tick (0) is due (an int's least, the first wiring's, wrapped the difference, and no sample was ever taken).</summary>
    public const int NoSample = -SampleTicks;

    /// <summary>Whether a sample is due at <paramref name="tick"/>, the last having been at <paramref name="last"/>.</summary>
    public static bool Due(int tick, int last) => tick - last >= SampleTicks;

    /// <summary>A walk that has not arrived after this many seconds stops taking samples.</summary>
    public const float MaxWalkSeconds = 150f;

    private readonly List<RouteWalk> _walks = new();
    private readonly RouteWalk?[] _open = new RouteWalk?[VisionPass.Slots];
    private readonly bool[] _alive = new bool[VisionPass.Slots];
    private readonly bool[] _walked = new bool[VisionPass.Slots];
    private readonly int[] _lives = new int[VisionPass.Slots];
    private readonly List<(int StartTick, int[] FirstOn)> _rounds = new();
    private int _lastTick = -1, _lastSample = NoSample;
    private bool _running;

    /// <summary>After the bots' commands (First + 500): their places as this tick moved them.</summary>
    public int FrameOrder => ModStages.First + 517;

    /// <summary>Among the dev API's route providers: after <see cref="TFPointsRoute"/>.</summary>
    public int HookOrder => ModStages.First + 932;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "the bots' routes from spawn to the point in play (lane Groutes): per team and goal, walks from where each life could first move, cut into 768-u bands along the line from their mean start to the point; each band's sideways mean and standard deviation, the middle bands' median and largest deviation, the distinct corridors of the walks that arrived (within 400 u of each other in every middle band shared) and the largest one's share, the seconds to the front (1,000 u) and to the point (300 u); per round the seconds from its start to each team's first bot on its point. ?walks=1 adds each walk's points; ?reset=1 forgets every walk"),
    ];

    public void OnThink(float frameTime)
    {
        int tick = frame.TickCount();
        if (tick < _lastTick)
            Reset();
        _lastTick = tick;
        if (!Due(tick, _lastSample))
            return;
        _lastSample = tick;
        bool running = round.State == RoundState.RoundRunning;
        if (running && !_running)
        {
            // A round's start finds everyone at a spawn: each life walks from there, as from a respawn.
            _rounds.Add((tick, new int[4]));
            Array.Clear(_walked);
        }
        _running = running;
        ObjectiveKnowledge objectives = feed.Current;
        foreach (AiBot b in bots.Bots)
        {
            int slot = b.Id.Slot;
            if ((uint)slot >= VisionPass.Slots)
                continue;
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            bool alive = p != EntityHandle.None && players.IsAlive(p) && players.GetObserverMode(p) == ObserverMode.None;
            if (alive && !_alive[slot])
            {
                _lives[slot]++;
                _walked[slot] = false;
            }

            _alive[slot] = alive;
            if (!alive || !running || vision.ReadOf(slot) is not { } read)
            {
                Close(slot);
                continue;
            }

            // The bot's team as clients render it, as its snapshot has it (its HUD's own read carries none).
            int team = teams.TeamOf(p);
            if (team == 0)
                continue;
            (int goalId, AiVector goal) = TFRouteSpread.GoalOf(objectives, team, read.Origin);
            NoteFirstOn(team, goalId, goal, read.Origin, tick);
            if (_open[slot] is { } w)
            {
                // A walk ends when its goal is no longer one for its team (taken, or locked), not when another is nearer.
                if (!TFRouteSpread.IsGoal(objectives, team, w.GoalId) || (tick - w.StartTick) * TickSeconds > MaxWalkSeconds)
                    Close(slot);
                else
                    w.Add(read.Origin, tick);
                if (!w.Open)
                    _open[slot] = null;
            }
            else if (!_walked[slot] && goalId >= 0)
            {
                // A life walks once: from where it could first move.
                _walked[slot] = true;
                var walk = new RouteWalk(slot, _lives[slot], team, goalId, goal, tick, read.Origin);
                _walks.Add(walk);
                _open[slot] = walk;
            }
        }
    }

    private float TickSeconds => environment.Globals is { IsAvailable: true } g ? g.IntervalPerTick : 0.015f;

    private void NoteFirstOn(int team, int goalId, AiVector goal, AiVector at, int tick)
    {
        if (_rounds.Count == 0 || goalId < 0 || (uint)team >= 4u || at.DistanceTo(goal) > TFRouteSpread.ArriveUnits)
            return;
        (int start, int[] firstOn) = _rounds[^1];
        if (firstOn[team] == 0)
            firstOn[team] = tick - start;
    }

    private void Close(int slot)
    {
        if (_open[slot] is { } w)
            w.Open = false;
        _open[slot] = null;
    }

    private void Reset()
    {
        _walks.Clear();
        _rounds.Clear();
        Array.Clear(_open);
        Array.Clear(_alive);
        Array.Clear(_walked);
        _running = false;
        _lastSample = NoSample;
    }

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        if (query.GetValueOrDefault("reset") == "1")
        {
            Reset();
            return new DebugRouteReply(200, AiJson.Write(new { reset = true }));
        }

        float seconds = TickSeconds;
        IReadOnlyList<RouteGroup> groups = TFRouteSpread.Measure(_walks, seconds);
        return new DebugRouteReply(200, AiJson.Write(new
        {
            tick = frame.TickCount(), round = round.State.ToString(), walks = _walks.Count, groups = groups.Select(TFRouteSpread.Row),
            objectives = feed.Current.All.Select(o => new
            {
                id = o.Id, kind = o.Kind.ToString(), owner = o.OwnerTeam, may = o.MayAct, state = o.State.ToString(),
                at = new[] { Math.Round(o.Position.X), Math.Round(o.Position.Y), Math.Round(o.Position.Z) },
            }),
            rounds = _rounds.Select(r => new
            {
                start_tick = r.StartTick,
                first_on = Enumerable.Range(2, 2).Select(t => new { team = t, seconds = r.FirstOn[t] == 0 ? -1 : Math.Round(r.FirstOn[t] * seconds, 1) }),
            }),
            walk_points = query.GetValueOrDefault("walks") == "1" ? _walks.Select(TFRouteSpread.WalkRow) : null,
            lives = variation?.Lives().Select(l => new { slot = l.Slot, life = l.Life, goal = l.Goal, corridors = l.Ratios.Select(r => Math.Round(r, 3)), choice = l.Choice }),
            composes = variation?.Composes ?? 0,
            draws = variation?.Draws().Select(d => new { slot = d.Slot, team = d.Team, life = d.Life, goal = d.Goal, choice = d.Choice, of = d.Of, at = Math.Round(d.At, 1) }),
            curtime = environment.Globals is { IsAvailable: true } gl ? Math.Round(gl.CurTime, 1) : -1,
            corridor_sets = variation?.Sets().Select(c => new
            {
                team = c.Team, goal = c.Goal, start = new[] { Math.Round(c.Start.X), Math.Round(c.Start.Y), Math.Round(c.Start.Z) }, ready = c.Ready,
                corridors = c.Ratios.Select(r => Math.Round(r, 3)),
                tries = c.Tries.Select(t => new[] { Math.Round(t.Ratio, 3), Math.Round(t.Own, 2) }),
            }),
        }));
    }
}
