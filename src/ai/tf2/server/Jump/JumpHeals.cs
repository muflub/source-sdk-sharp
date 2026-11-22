using System.Collections.Immutable;
using SourceSharp.Ai.Server.Nav;
using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// A place on a level that gives a player health (lane G9): a health pack of <paramref name="Kind"/> standing at
/// <paramref name="At"/>, or a resupply cabinet's zone (<paramref name="Cabinet"/>) from <paramref name="Mins"/> to
/// <paramref name="Maxs"/> of <paramref name="Team"/> (0: every team's), <paramref name="At"/> the middle of its floor.
/// </summary>
public readonly record struct JumpHealPlace(bool Cabinet, ItemKind Kind, int Team, AiVector At, AiVector Mins, AiVector Maxs);

/// <summary>A heal place (its index among the level's, and what it is) <paramref name="Walk"/> units on foot from a jump's landing.</summary>
public readonly record struct JumpHeal(int Place, bool Cabinet, ItemKind Kind, int Team, AiVector At, float Walk);

/// <summary>
/// A level's heal places and, for each, the cells from which it is no more than a walk (<see cref="JumpHealthRule.HealWalk"/>)
/// away along the graph (lane G9): the graph's own edges, every door open, as a standing body walks, hops and drops,
/// never a blast link. What a jump's landing has near it (<see cref="Near"/>) is read from that, with no search. Built
/// once a level, off the game's thread; immutable after.
/// </summary>
public sealed class JumpHeals
{
    private readonly NavGraph _graph;
    private readonly JumpHealPlace[] _places;
    private readonly Dictionary<int, float>[] _walks;

    private JumpHeals(NavGraph graph, JumpHealPlace[] places, Dictionary<int, float>[] walks)
    {
        _graph = graph;
        _places = places;
        _walks = walks;
    }

    /// <summary>The places.</summary>
    public IReadOnlyList<JumpHealPlace> Places => _places;

    /// <summary>The cells within the walk of place <paramref name="place"/>.</summary>
    public int CellsNear(int place) => _walks[place].Count;

    /// <summary>
    /// The heal places of a level's placed entities, in their order: each health pack by its size
    /// (<see cref="Knowledge.TFMapKnowledge.KindOf"/>), each resupply cabinet's zone (<c>func_regenerate</c>) with its team.
    /// </summary>
    public static JumpHealPlace[] PlacesOf(MapEntities entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var places = new List<JumpHealPlace>();
        foreach (MapEntity e in entities.All)
        {
            ItemKind kind = Knowledge.TFMapKnowledge.KindOf(e.ClassName);
            if (JumpHealthRule.IsPack(kind))
            {
                places.Add(new JumpHealPlace(false, kind, 0, e.Origin, e.Origin, e.Origin));
            }
            else if (e.ClassName == FuncRegenerate.MapClass && e.Mins is { } mins && e.Maxs is { } maxs)
            {
                var floor = new AiVector((mins.X + maxs.X) * 0.5f, (mins.Y + maxs.Y) * 0.5f, mins.Z);
                places.Add(new JumpHealPlace(true, ItemKind.Other, e.Team, floor, mins, maxs));
            }
        }

        return [.. places];
    }

    /// <summary>
    /// The places of <paramref name="places"/> over <paramref name="graph"/>, each with the cells it is within
    /// <paramref name="walk"/> of: flooded back from the place's own cells (a pack's: the cell it stands in; a cabinet's: the
    /// cells whose middle is in its zone) along the edges into them.
    /// </summary>
    public static JumpHeals Of(NavGraph graph, IReadOnlyList<JumpHealPlace> places, float walk = JumpHealthRule.HealWalk)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(places);
        var obstacles = new NavObstacleState(graph.ObstacleCount) { OpenDoors = true };
        var walks = new Dictionary<int, float>[places.Count];
        var open = new PriorityQueue<int, float>();
        for (int p = 0; p < places.Count; p++)
        {
            var near = new Dictionary<int, float>();
            open.Clear();
            foreach (int seed in SeedsOf(graph, places[p]))
            {
                if (near.TryAdd(seed, 0f))
                    open.Enqueue(seed, 0f);
            }

            while (open.TryDequeue(out int cell, out float cost))
            {
                if (cost > near[cell])
                    continue;
                foreach (NavEdge e in graph.EdgesTo(cell))
                {
                    if (!graph.Passable(e, ClearanceClass.Stand, obstacles))
                        continue;
                    float c = cost + e.Cost;
                    if (c > walk || (near.TryGetValue(e.From, out float had) && had <= c))
                        continue;
                    near[e.From] = c;
                    open.Enqueue(e.From, c);
                }
            }

            walks[p] = near;
        }

        return new JumpHeals(graph, [.. places], walks);
    }

    /// <summary>A place's own cells: the cell a pack stands in, or every standing cell whose middle is in a cabinet's zone (the nearest to its floor's middle when none is).</summary>
    private static IEnumerable<int> SeedsOf(NavGraph graph, JumpHealPlace place)
    {
        CellId at = NavPoints.CellNear(graph, place.At, ClearanceClass.Stand);
        if (!place.Cabinet)
        {
            if (at.IsValid)
                yield return at.Index;
            yield break;
        }

        bool any = false;
        for (int i = 0; i < graph.CellCount; i++)
        {
            NavCell c = graph.Cell(i);
            AiVector m = c.Centre;
            if (c.Admits(ClearanceClass.Stand) && m.X >= place.Mins.X && m.X <= place.Maxs.X && m.Y >= place.Mins.Y && m.Y <= place.Maxs.Y
                && c.FloorZ >= place.Mins.Z - 18f && c.FloorZ <= place.Maxs.Z)
            {
                any = true;
                yield return i;
            }
        }

        if (!any && at.IsValid)
            yield return at.Index;
    }

    /// <summary>The places within the walk of the cell <paramref name="landing"/> stands in, nearest first; none off the graph.</summary>
    public ImmutableArray<JumpHeal> Near(AiVector landing)
    {
        CellId cell = _graph.FindCell(landing + new AiVector(0f, 0f, 1f));
        if (!cell.IsValid)
            cell = NavPoints.CellNear(_graph, landing, ClearanceClass.Stand);
        return cell.IsValid ? NearCell(cell.Index) : [];
    }

    /// <summary>The places within the walk of cell <paramref name="cell"/>, nearest first.</summary>
    public ImmutableArray<JumpHeal> NearCell(int cell)
    {
        ImmutableArray<JumpHeal>.Builder? found = null;
        for (int p = 0; p < _places.Length; p++)
        {
            if (!_walks[p].TryGetValue(cell, out float walk))
                continue;
            JumpHealPlace place = _places[p];
            (found ??= ImmutableArray.CreateBuilder<JumpHeal>()).Add(new JumpHeal(p, place.Cabinet, place.Kind, place.Team, place.At, walk));
        }

        if (found is null)
            return [];
        found.Sort(static (a, b) => a.Walk.CompareTo(b.Walk));
        return found.ToImmutable();
    }

    /// <summary>
    /// What a route search spends on each of <paramref name="set"/>'s links (<see cref="JumpHealthRule.Spend"/>), for a
    /// player of <paramref name="maxHealth"/> against <paramref name="floor"/>: for a link whose landing cell has a place any
    /// player may use (a pack, a cabinet of every team: one set serves both teams), the least spend of them; else its cost.
    /// The fall is each link's plan's (<paramref name="plans"/>, index for index).
    /// </summary>
    public float[] Spends(JumpLinkSet set, IReadOnlyList<JumpPlan> plans, int maxHealth, float floor)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(plans);
        var spends = new float[set.Links.Count];
        for (int i = 0; i < spends.Length; i++)
        {
            float cost = set.JumpOf(i).Health, spend = cost;
            ImmutableArray<JumpHeal> near = NearCell(set.Links[i].To);
            if (!near.IsEmpty)
            {
                float fall = JumpHealthRule.Fall(plans[i], maxHealth);
                foreach (JumpHeal h in near)
                {
                    if (!h.Cabinet || h.Team == 0)
                        spend = MathF.Min(spend, JumpHealthRule.Spend(cost, fall, JumpHealthRule.Pays(h.Cabinet, h.Kind, maxHealth), floor));
                }
            }

            spends[i] = spend;
        }

        return spends;
    }
}
