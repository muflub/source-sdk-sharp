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

    /// <summary>
    /// The walk on foot from cell <paramref name="cell"/> to place <paramref name="place"/>, when it is no more than
    /// <paramref name="most"/>; null when it is more, or there is none (lane G9b): a flood back from the place's cells as
    /// <see cref="Of"/> floods, bounded by <paramref name="most"/>.
    /// </summary>
    public float? WalkTo(int place, int cell, float most)
    {
        if ((uint)place >= (uint)_places.Length || (uint)cell >= (uint)_graph.CellCount)
            return null;
        if (_walks[place].TryGetValue(cell, out float near))
            return near <= most ? near : null;
        var obstacles = new NavObstacleState(_graph.ObstacleCount) { OpenDoors = true };
        var cost = new Dictionary<int, float>();
        var open = new PriorityQueue<int, float>();
        foreach (int seed in SeedsOf(_graph, _places[place]))
        {
            if (cost.TryAdd(seed, 0f))
                open.Enqueue(seed, 0f);
        }

        while (open.TryDequeue(out int at, out float c))
        {
            if (c > cost[at])
                continue;
            if (at == cell)
                return c;
            foreach (NavEdge e in _graph.EdgesTo(at))
            {
                if (!_graph.Passable(e, ClearanceClass.Stand, obstacles))
                    continue;
                float next = c + e.Cost;
                if (next > most || (cost.TryGetValue(e.From, out float had) && had <= next))
                    continue;
                cost[e.From] = next;
                open.Enqueue(e.From, next);
            }
        }

        return null;
    }

    /// <summary>
    /// Of the places by <paramref name="plan"/>'s landing (<see cref="JumpPlan.Heals"/>), those the jump is the quicker way to
    /// from the cell <paramref name="start"/> it starts in (lane G9b): no walk of the jump's seconds at
    /// <paramref name="runSpeed"/> and the place's walk from the landing gets there.
    /// </summary>
    public ImmutableArray<JumpHeal> JumpWays(JumpPlan plan, int start, float runSpeed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ImmutableArray<JumpHeal>.Builder? ways = null;
        foreach (JumpHeal h in plan.Heals)
        {
            if (WalkTo(h.Place, start, (plan.Seconds * runSpeed) + h.Walk) is null)
                (ways ??= ImmutableArray.CreateBuilder<JumpHeal>()).Add(h);
        }

        return ways is null ? [] : ways.ToImmutable();
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

    /// <summary>
    /// How many times its walk a detour from a landing to a heal place and back to the way costs, at most: there and back
    /// again (a way that goes on past the place costs less).
    /// </summary>
    public const float DetourShare = 2f;

    /// <summary>
    /// The health each of <paramref name="set"/>'s links is weighed at by a route search (lane G9b): what its blast costs a
    /// player of <paramref name="maxHealth"/> once a place by its landing that any player may use has given health back, the
    /// place taken next. The place gives back up to what it pays (<see cref="JumpHealthRule.Pays"/>), so the blast and the
    /// worst fall less that, never under 0; the walk there and back (<see cref="DetourShare"/> times the place's walk, at
    /// <paramref name="costs"/>' run speed) is weighed as the health those seconds are worth
    /// (<see cref="NavLinkCosts.HealthSeconds"/>). The least of that and the blast's whole cost: a place too far for its pay is
    /// not worth the walk, and the link weighs what it did. A link by no such place weighs its cost.
    /// <para>
    /// Whatever the player's health, taking the place leaves it no worse than the blast less the pay: from full it lands at
    /// full less the blast and the pack makes it up to what it pays; under the floor the same. So one weight serves every bot.
    /// </para>
    /// </summary>
    public float[] Weights(JumpLinkSet set, IReadOnlyList<JumpPlan> plans, int maxHealth, NavLinkCosts costs)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(costs);
        var weights = new float[set.Links.Count];
        for (int i = 0; i < weights.Length; i++)
        {
            float cost = set.JumpOf(i).Health, weight = cost;
            ImmutableArray<JumpHeal> near = NearCell(set.Links[i].To);
            if (!near.IsEmpty && costs.HealthSeconds > 0f && costs.RunSpeed > 0f)
            {
                float fall = JumpHealthRule.Fall(plans[i], maxHealth);
                foreach (JumpHeal h in near)
                {
                    if (h.Cabinet && h.Team != 0)
                        continue;
                    float back = MathF.Max(0f, cost + fall - JumpHealthRule.Pays(h.Cabinet, h.Kind, maxHealth));
                    float walk = DetourShare * h.Walk / costs.RunSpeed / costs.HealthSeconds;
                    weight = MathF.Min(weight, back + walk);
                }
            }

            weights[i] = weight;
        }

        return weights;
    }
}
