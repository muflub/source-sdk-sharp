using System.Globalization;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// One bot's walk of one life (lane Groutes): from where it stood when it could first move (its spawn, or where the
/// round's start found it) towards the point in play for its team as it was then (<see cref="TFRouteSpread.GoalOf"/>),
/// one point a sample, until it came within <see cref="TFRouteSpread.ArriveUnits"/> of it, died, the goal changed or the
/// round stopped.
/// </summary>
public sealed class RouteWalk(int slot, int life, int team, int goalId, AiVector goal, int startTick, AiVector start)
{
    public int Slot { get; } = slot;

    public int Life { get; } = life;

    public int Team { get; } = team;

    /// <summary>The objective's id the walk went for.</summary>
    public int GoalId { get; } = goalId;

    /// <summary>Where that objective stood when the walk began.</summary>
    public AiVector Goal { get; } = goal;

    public int StartTick { get; } = startTick;

    /// <summary>The walk's samples, its start first.</summary>
    public List<AiVector> Points { get; } = [start];

    /// <summary>The tick it first came within <see cref="TFRouteSpread.FrontUnits"/> of the goal; -1 for never.</summary>
    public int FrontTick { get; set; } = -1;

    /// <summary>The tick it came within <see cref="TFRouteSpread.ArriveUnits"/> of the goal; -1 for never.</summary>
    public int ArriveTick { get; set; } = -1;

    /// <summary>Whether it still takes samples.</summary>
    public bool Open { get; set; } = true;

    public bool Reached => ArriveTick >= 0;

    /// <summary>Takes a sample at <paramref name="tick"/>: the front's and the arrival's ticks when it first comes so near; arriving ends the walk.</summary>
    public void Add(AiVector at, int tick)
    {
        Points.Add(at);
        float d = at.DistanceTo(Goal);
        if (FrontTick < 0 && d <= TFRouteSpread.FrontUnits)
            FrontTick = tick;
        if (d <= TFRouteSpread.ArriveUnits)
        {
            ArriveTick = tick;
            Open = false;
        }
    }
}

/// <summary>One band of a group's walks: how many walks crossed it, and the mean and standard deviation of their sideways offsets in it.</summary>
public readonly record struct RouteBand(int Index, int Walks, float Mean, float Sd);

/// <summary>
/// The walks of one team to one goal, measured (<see cref="TFRouteSpread.Measure"/>): the axis from the walks' mean start to
/// the goal, each band's sideways spread, the corridors the walks that arrived took (their sizes, largest first), and the
/// seconds each walk took to the front and to the point.
/// </summary>
public sealed record RouteGroup(int Team, int GoalId, AiVector Start, AiVector Goal, int Walks, int Reached, IReadOnlyList<RouteBand> Bands,
                                IReadOnlyList<int> Corridors, IReadOnlyList<float> FrontSeconds, IReadOnlyList<float> PointSeconds)
{
    /// <summary>The bands between the first and the goal's that at least <see cref="TFRouteSpread.MinBandWalks"/> walks crossed: the way, not the spawn room or the point.</summary>
    public IEnumerable<RouteBand> Middle
    {
        get
        {
            int last = (int)MathF.Floor(TFRouteSpread.Flat(Goal - Start).Length2D / TFRouteSpread.BandUnits);
            return Bands.Where(b => b.Index >= 1 && b.Index < last && b.Walks >= TFRouteSpread.MinBandWalks);
        }
    }
}

/// <summary>
/// How spread the bots' routes are (lane Groutes, the user 2026-11-15: "they always seem to take the exact same path"): per
/// team and point in play, walks from spawn are cut into bands of <see cref="BandUnits"/> along the line from their mean
/// start to the point, and in each band the walks' mean sideways offsets are compared: their standard deviation (lane Gobj
/// read 2-95 u at base on cp_granary, where the cells within 1.05 times the shortest walk are 1,400-2,300 u wide), and,
/// for the walks that arrived, the distinct corridors (walks whose offsets in every middle band shared stay within
/// <see cref="CorridorUnits"/> of the corridor's first walk's are one corridor).
/// </summary>
public static class TFRouteSpread
{
    /// <summary>A band's length along the axis: lane Gobj's.</summary>
    public const float BandUnits = 768f;

    /// <summary>
    /// How far apart two walks' offsets in a band may be and still be one corridor: wider than a doorway's or a hall's own
    /// spread (two bots of one corridor keep to its sides), narrower than the space between two halls.
    /// </summary>
    public const float CorridorUnits = 400f;

    /// <summary>How near the goal is the front: a walk's seconds to it are its time to the front.</summary>
    public const float FrontUnits = 1000f;

    /// <summary>How near the goal a walk has arrived: on a control point, or at a flag.</summary>
    public const float ArriveUnits = 300f;

    /// <summary>A band with fewer walks says nothing of their spread.</summary>
    public const int MinBandWalks = 3;

    /// <summary>The bands two walks must share before they are compared as corridors.</summary>
    public const int MinSharedBands = 2;

    /// <summary>
    /// The point in play for <paramref name="team"/>: the control point it may capture now nearest <paramref name="from"/>, else
    /// the other team's flag (where it is now), else none (id -1).
    /// </summary>
    public static (int Id, AiVector At) GoalOf(ObjectiveKnowledge objectives, int team, AiVector from)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        (int Id, AiVector At) best = (-1, default);
        float bestDistance = float.PositiveInfinity;
        foreach (Objective o in objectives.All)
        {
            bool goal = o.Kind == ObjectiveKind.Capture ? (o.MayAct & (1 << team)) != 0 && o.State != ObjectiveState.Locked
                : o.Kind == ObjectiveKind.Carry && o.OwnerTeam != team;
            float d = o.Position.DistanceTo(from);
            if (goal && (best.Id < 0 || (o.Kind == ObjectiveKind.Capture && d < bestDistance)))
                (best, bestDistance) = ((o.Id, o.Position), d);
        }

        return best;
    }

    /// <summary>The vector in the ground plane.</summary>
    public static AiVector Flat(AiVector v) => v with { Z = 0f };

    /// <summary>Every group of <paramref name="walks"/> (by team, then goal), measured.</summary>
    public static IReadOnlyList<RouteGroup> Measure(IEnumerable<RouteWalk> walks, float secondsPerTick)
    {
        ArgumentNullException.ThrowIfNull(walks);
        return [.. walks.GroupBy(w => (w.Team, w.GoalId)).OrderBy(g => g.Key.Team).ThenBy(g => g.Key.GoalId)
                        .Select(g => MeasureGroup([.. g.OrderBy(w => w.StartTick).ThenBy(w => w.Slot)], secondsPerTick))];
    }

    /// <summary>One team's walks to one goal, measured.</summary>
    public static RouteGroup MeasureGroup(IReadOnlyList<RouteWalk> walks, float secondsPerTick)
    {
        ArgumentNullException.ThrowIfNull(walks);
        if (walks.Count == 0)
            throw new ArgumentException("no walks to measure", nameof(walks));
        AiVector start = new(walks.Average(w => w.Points[0].X), walks.Average(w => w.Points[0].Y), walks.Average(w => w.Points[0].Z));
        AiVector goal = walks[0].Goal;
        Dictionary<int, float>[] sides = [.. walks.Select(w => BandSides(w.Points, start, goal))];
        var bands = new List<RouteBand>();
        foreach (int band in sides.SelectMany(s => s.Keys).Distinct().Order())
        {
            float[] v = [.. sides.Where(s => s.ContainsKey(band)).Select(s => s[band])];
            float mean = v.Average();
            float sd = MathF.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Length);
            bands.Add(new RouteBand(band, v.Length, mean, sd));
        }

        int last = (int)MathF.Floor(Flat(goal - start).Length2D / BandUnits);
        var reached = new List<Dictionary<int, float>>();
        for (int i = 0; i < walks.Count; i++)
        {
            if (walks[i].Reached)
                reached.Add(sides[i].Where(kv => kv.Key >= 1 && kv.Key < last).ToDictionary());
        }

        return new RouteGroup(walks[0].Team, walks[0].GoalId, start, goal, walks.Count, reached.Count, bands, Corridors(reached),
                              [.. walks.Where(w => w.FrontTick >= 0).Select(w => (w.FrontTick - w.StartTick) * secondsPerTick)],
                              [.. walks.Where(w => w.Reached).Select(w => (w.ArriveTick - w.StartTick) * secondsPerTick)]);
    }

    /// <summary>
    /// Each band's mean sideways offset of <paramref name="points"/> from the line from <paramref name="start"/> to
    /// <paramref name="goal"/>, in the ground plane (left of the line is plus); band <c>k</c> holds the points from
    /// <c>k</c> to <c>k + 1</c> bands along it.
    /// </summary>
    public static Dictionary<int, float> BandSides(IReadOnlyList<AiVector> points, AiVector start, AiVector goal)
    {
        ArgumentNullException.ThrowIfNull(points);
        AiVector axis = Flat(goal - start);
        float length = axis.Length2D;
        var sums = new Dictionary<int, (float Sum, int N)>();
        if (length <= 0f)
            return [];
        float ux = axis.X / length, uy = axis.Y / length;
        foreach (AiVector p in points)
        {
            float dx = p.X - start.X, dy = p.Y - start.Y;
            int band = (int)MathF.Floor(((dx * ux) + (dy * uy)) / BandUnits);
            float side = (ux * dy) - (uy * dx);
            (float s, int n) = sums.GetValueOrDefault(band);
            sums[band] = (s + side, n + 1);
        }

        return sums.ToDictionary(kv => kv.Key, kv => kv.Value.Sum / kv.Value.N);
    }

    /// <summary>
    /// The corridors of <paramref name="walks"/> (each a walk's band offsets), largest first: a walk joins the first corridor
    /// whose first walk shares at least <see cref="MinSharedBands"/> bands with it, every one within <see cref="CorridorUnits"/>;
    /// else it starts its own.
    /// </summary>
    public static IReadOnlyList<int> Corridors(IReadOnlyList<Dictionary<int, float>> walks)
    {
        ArgumentNullException.ThrowIfNull(walks);
        var firsts = new List<Dictionary<int, float>>();
        var sizes = new List<int>();
        foreach (Dictionary<int, float> w in walks)
        {
            int joined = -1;
            for (int c = 0; c < firsts.Count && joined < 0; c++)
            {
                int[] shared = [.. w.Keys.Where(firsts[c].ContainsKey)];
                if (shared.Length >= MinSharedBands && shared.All(b => MathF.Abs(w[b] - firsts[c][b]) <= CorridorUnits))
                    joined = c;
            }

            if (joined < 0)
            {
                firsts.Add(w);
                sizes.Add(1);
            }
            else
            {
                sizes[joined]++;
            }
        }

        return [.. sizes.OrderDescending()];
    }

    /// <summary>The median of <paramref name="values"/>; NaN for none.</summary>
    public static float Median(IEnumerable<float> values)
    {
        float[] v = [.. values.Order()];
        return v.Length == 0 ? float.NaN : v.Length % 2 == 1 ? v[v.Length / 2] : (v[(v.Length / 2) - 1] + v[v.Length / 2]) / 2f;
    }

    /// <summary>A group as the route serves it.</summary>
    public static object Row(RouteGroup g)
    {
        ArgumentNullException.ThrowIfNull(g);
        RouteBand[] middle = [.. g.Middle];
        static double R(float v) => float.IsFinite(v) ? Math.Round(v, 1) : -1;
        return new
        {
            team = g.Team, goal = g.GoalId, walks = g.Walks, reached = g.Reached,
            start = new[] { Math.Round(g.Start.X), Math.Round(g.Start.Y), Math.Round(g.Start.Z) },
            goal_at = new[] { Math.Round(g.Goal.X), Math.Round(g.Goal.Y), Math.Round(g.Goal.Z) },
            middle_bands = middle.Length,
            middle_sd_median = R(Median(middle.Select(b => b.Sd))),
            middle_sd_max = middle.Length == 0 ? -1 : R(middle.Max(b => b.Sd)),
            corridors = g.Corridors,
            largest_share = g.Reached == 0 ? -1 : Math.Round(g.Corridors[0] / (double)g.Reached, 3),
            front_s_median = R(Median(g.FrontSeconds)), point_s_median = R(Median(g.PointSeconds)),
            front_s = g.FrontSeconds.Select(R), point_s = g.PointSeconds.Select(R),
            bands = g.Bands.Select(b => new { band = b.Index, walks = b.Walks, mean = R(b.Mean), sd = R(b.Sd) }),
        };
    }

    /// <summary>A walk's points as the route serves them (<c>?walks=1</c>), rounded to the unit.</summary>
    public static object WalkRow(RouteWalk w)
    {
        ArgumentNullException.ThrowIfNull(w);
        return new
        {
            slot = w.Slot, life = w.Life, team = w.Team, goal = w.GoalId, start_tick = w.StartTick, front_tick = w.FrontTick, arrive_tick = w.ArriveTick,
            points = string.Join(' ', w.Points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:F0},{p.Y:F0}"))),
        };
    }
}
