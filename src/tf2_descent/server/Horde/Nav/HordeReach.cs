using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Ai.Server.Nav;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Server.Level;

namespace TF2.Descent.Server.Horde.Nav;

/// <summary>
/// How a walk along the brains' planner ended (<see cref="HordeReach.Walk"/>): the last search's
/// answer, whether the goal was reached, the legs searched, the length walked along the corners,
/// and where the walk stopped.
/// </summary>
public readonly record struct ReachWalk(PathAnswer Answer, bool Reached, int Legs, float Length, AiVector End)
{
    /// <summary>The walk as the console prints one: <c>REACHED in 3 legs, 2140 u</c> or <c>Nearest after 1 leg at (x,y,z)</c>.</summary>
    public string Describe(AiVector goal) => Reached
        ? string.Create(CultureInfo.InvariantCulture, $"REACHED in {Legs} leg{(Legs == 1 ? "" : "s")}, {Length:0} u")
        : string.Create(CultureInfo.InvariantCulture,
            $"{Answer} after {Legs} leg{(Legs == 1 ? "" : "s")}, {Length:0} u, stopped at ({End.X:0},{End.Y:0},{End.Z:0}) {(goal - End).Length2D:0} u short");
}

/// <summary>
/// A path from a point to a goal the way a robot's brain gets one (<see cref="HordeRoute"/>): a
/// search with the brains' budget, and, while the answer is a truncated path to the goal or to the nearest place of the floor
/// to it (a goal beyond the planner's horizon of doors, or more corners than fit), another search from its last corner.
/// The walk reaches the goal when a search answers <see cref="PathAnswer.Found"/> untruncated; it
/// stops on any other answer, on a leg that gets nowhere, or after <paramref name="maxLegs"/>.
/// Pure: the paths in, the walk out.
/// </summary>
public static class HordeReach
{
    /// <summary>The legs a walk searches at most: a maze level's far corner is a few dozen doors away, two doors a leg.</summary>
    public const int MaxLegs = 64;

    /// <summary>A leg whose last corner is nearer than this to where it started got nowhere.</summary>
    public const float NoProgressUnits = 8f;

    public static ReachWalk Walk(IHordePaths paths, AiVector from, AiVector to, ClearanceClass cls, int budget = HordeRoute.SearchBudget, int maxLegs = MaxLegs)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Span<AiVector> corners = stackalloc AiVector[HordeRoute.MaxCorners];
        Span<NavEdgeType> arrive = stackalloc NavEdgeType[HordeRoute.MaxCorners];
        AiVector at = from;
        float length = 0f;
        for (int leg = 1; leg <= maxLegs; leg++)
        {
            PathAnswer answer = paths.Find(at, to, cls, budget, corners, arrive, out int count, out bool truncated);
            AiVector start = at;
            for (int i = 0; i < count; i++)
            {
                length += (corners[i] - at).Length;
                at = corners[i];
            }

            if (answer == PathAnswer.Found && !truncated)
                return new ReachWalk(answer, true, leg, length, at);
            if (answer is not (PathAnswer.Found or PathAnswer.Nearest) || !truncated || count == 0 || (at - start).Length < NoProgressUnits)
                return new ReachWalk(answer, false, leg, length, at);
        }

        return new ReachWalk(PathAnswer.OutOfBudget, false, maxLegs, length, at);
    }

    /// <summary>Every class a robot plans by, smallest first: the player's hull, then each scaled band.</summary>
    public static IEnumerable<ClearanceClass> Classes()
    {
        yield return ClearanceClass.Stand;
        for (int i = 0; i < HordeHulls.Scales.Length; i++)
            yield return (ClearanceClass)(2 + i);
    }
}

/// <summary>
/// <c>tf2_descent_horde_reach [packs | between x1 y1 z1 x2 y2 z2 [budget]]</c>: whether the brains' planner (<see cref="HordeReach"/>)
/// gets from the level's first arrival point to each place a fight needs, for each robot class:
/// the boss's spot, each arena mouth (by leg) and the way down. With <c>packs</c>, from each
/// pack's post to the arrival point by the class of its biggest robot, the walks that fail named. With
/// <c>between</c>, two points: their cells by class and the walk between them with the budget given.
/// </summary>
[ConCommand("tf2_descent_horde_reach", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_reach [packs | between x1 y1 z1 x2 y2 z2 [budget]]: can each robot class walk the planner from the arrival to the boss, its mouths and the exit; packs: each pack's post to the arrival; between: two points, their cells and the walk")]
public sealed class HordeReachCommand(HordeNav nav, HordeService horde, ILevelData levels, ILog log)
{
    public const string Tag = "[TF2Descent/Horde] reach:";

    public void Execute(string what = "", float x1 = 0, float y1 = 0, float z1 = 0, float x2 = 0, float y2 = 0, float z2 = 0, int budget = HordeRoute.SearchBudget)
    {
        if (levels.Current is not { } level || level.Arrivals.Length == 0)
        {
            log.Msg($"{Tag} no level or no arrival point\n");
            return;
        }

        if (nav.State != HordeNavState.Ready)
        {
            log.Msg($"{Tag} the nav is {nav.State}\n");
            return;
        }

        AiVector arrival = HordeVectors.ToAi(level.Arrivals[0].Position);
        if (what == "packs")
        {
            Packs(arrival);
            return;
        }

        if (what == "between")
        {
            Between(new AiVector(x1, y1, z1), new AiVector(x2, y2, z2), budget);
            return;
        }

        var goals = new List<(string Name, AiVector At)>();
        if (level.Arena is { } arena)
        {
            goals.Add(("boss spot", HordeVectors.ToAi(arena.Spot)));
            for (int m = 0; m < arena.Mouths.Length; m++)
                goals.Add((string.Create(CultureInfo.InvariantCulture, $"mouth {m} leg {arena.Mouths[m].Leg}"), HordeVectors.ToAi(arena.Mouths[m].Position)));
        }

        if (level.Exit is { } exit)
            goals.Add(("exit", HordeVectors.ToAi(exit.Position)));
        foreach (var (name, at) in goals)
        {
            foreach (ClearanceClass cls in HordeReach.Classes())
            {
                var walk = HordeReach.Walk(nav.Paths, arrival, at, cls);
                log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"{Tag} arrival ({arrival.X:0},{arrival.Y:0},{arrival.Z:0}) -> {name} ({at.X:0},{at.Y:0},{at.Z:0}) as {HordeHulls.NameOf(cls)}: {walk.Describe(at)}\n"));
            }
        }
    }

    /// <summary>From one point to another for each class: each end's cell (on the class's main floor or not), whether the level's components join them, and the walk with the budget asked.</summary>
    private void Between(AiVector from, AiVector to, int budget)
    {
        if (nav.Data is not { } data)
            return;
        foreach (ClearanceClass cls in HordeReach.Classes())
        {
            CellId a = data.CellAt(from, cls), b = data.CellAt(to, cls);
            string End(CellId c) => c.IsValid
                ? string.Create(CultureInfo.InvariantCulture, $"cell {c.Index} at ({data.Graph.Cell(c.Index).Centre.X:0},{data.Graph.Cell(c.Index).Centre.Y:0},{data.Graph.Cell(c.Index).Centre.Z:0}){(data.OnMainFloor(c, cls) ? "" : " OFF the main floor")}")
                : "no cell";
            var walk = HordeReach.Walk(nav.Paths, from, to, cls, budget);
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"{Tag} ({from.X:0},{from.Y:0},{from.Z:0}) {End(a)} -> ({to.X:0},{to.Y:0},{to.Z:0}) {End(b)} as {HordeHulls.NameOf(cls)}: joined with the arena open {(a.IsValid && b.IsValid && data.Joined(a, b, cls))}, as it stands {(a.IsValid && b.IsValid && nav.Paths.Joined(a, b, cls))}; budget {budget}: {walk.Describe(to)}\n"));
        }
    }

    private void Packs(AiVector arrival)
    {
        if (horde.Current is not { Begun: true, Roster: { } roster })
        {
            log.Msg($"{Tag} no horde this level\n");
            return;
        }

        int reached = 0, failed = 0;
        var byClass = new SortedDictionary<string, (int Reached, int Failed)>(StringComparer.Ordinal);
        for (int p = 0; p < roster.PackCount; p++)
        {
            int first = roster.PackFirst(p), size = roster.PackSize(p);
            if (size <= 0)
                continue;
            double scale = 0;
            int big = first;
            for (int i = first; i < first + size; i++)
            {
                if (roster.Scale(i) > scale)
                    (scale, big) = (roster.Scale(i), i);
            }

            var cls = HordeHulls.ClassOf((float)scale);
            AiVector post = roster.Post(big);
            var walk = HordeReach.Walk(nav.Paths, post, arrival, cls);
            string name = HordeHulls.NameOf(cls);
            var tally = byClass.GetValueOrDefault(name);
            if (walk.Reached)
            {
                reached++;
                byClass[name] = (tally.Reached + 1, tally.Failed);
            }
            else
            {
                failed++;
                byClass[name] = (tally.Reached, tally.Failed + 1);
            }

            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"{Tag} pack {p} room {roster.PackRoom(p)} ({size} robots, rows {first}-{first + size - 1}, biggest {roster.Template(big)} x{scale:0.00} as {name}) post ({post.X:0},{post.Y:0},{post.Z:0}) -> arrival: {walk.Describe(arrival)}\n"));
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"{Tag} packs: {reached} reached the arrival, {failed} did not; by class: {string.Join(", ", byClass.Select(kv => $"{kv.Key} {kv.Value.Reached}/{kv.Value.Reached + kv.Value.Failed}"))}\n"));
    }
}
