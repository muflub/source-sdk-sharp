//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>A place a Sniper holds (its cell's middle on the floor), what it watches from there, and its score.</summary>
public readonly record struct TFSniperSpot(AiVector At, int Cell, AiVector Watch, float Score);

/// <summary>
/// Where a Sniper stands to hold a sight line onto what its team fights over (plan_bots_teams_tf2 C.3.2, the Sniper's
/// §4.3 "Spots"), from what the bot knows of the level: its nav cells, the cell-to-cell sight table and the level's
/// choke points. The watched place is the objective in play (a point, a flag); the watched set is it and the choke points
/// near it. A cell is a vantage when, standing, it sees one of the watched set from as far as the table answers, on the
/// team's own side of the objective (stock keeps an attacker's vantage 750 u back from the point and lets a defender's
/// reach 750 u past it, tf_bot.cpp:2390-2402, on the route's length: here the straight distance to the team's spawn).
/// A vantage scores by what it sees (the objective twice a choke), its range (further is better, out to the table's),
/// its height over the objective, less how many places see it back (its exposure). The table reaches
/// <see cref="ICellSight.Range"/> (1,024 u at this tip): stock wants 1,000 u or more (tf_bot.cpp:52), so the best spots are
/// at the table's edge, and a longer line waits on the long-line pass (C.3.2).
/// </summary>
public static class TFSniperSpots
{
    /// <summary>How near the watched place a vantage may stand, and the band's far end when the level has no sight table.</summary>
    public const float NearestUnits = 500f, FarthestUnits = 1100f;

    /// <summary>An attacker's vantage is this much nearer its own spawn than the objective; a defender's at most this much further (stock's 750 u, ours shortened for the attacker: the straight line is not the route).</summary>
    public const float AttackBackUnits = 250f, DefendPastUnits = 750f;

    /// <summary>How far round the watched place a choke point is part of the watched set.</summary>
    public const float ChokeReach = 1200f;

    /// <summary>The best spots kept, each at least <see cref="Separation"/> from the others.</summary>
    public const int Kept = 6;

    public const float Separation = 192f;

    /// <summary>
    /// The best spots onto <paramref name="watch"/> for a team whose spawn is at <paramref name="ownSpawn"/>, attacking or
    /// defending, best first; none when the level has no nav. <paramref name="refused"/> says a place a vantage may not be
    /// (an enemy respawn room). With <paramref name="from"/>, only cells a player walks to from there.
    /// </summary>
    public static ImmutableArray<TFSniperSpot> Find(NavGraph? graph, ICellSight? sight, ChokeMap? chokes, AiVector watch, AiVector ownSpawn, bool attack,
                                                   Func<AiVector, bool>? refused = null, AiVector? from = null)
    {
        if (graph is null || graph.CellCount == 0)
            return [];
        float range = sight is null ? FarthestUnits : MathF.Min(sight.Range, FarthestUnits);
        CellId watchCell = NearestCell(graph, watch);
        var watched = new List<(int Cell, AiVector At, float Weight)>();
        if (watchCell.Index >= 0)
            watched.Add((watchCell.Index, watch, 2f));
        if (chokes is not null)
        {
            foreach (ChokePoint c in chokes.Top)
            {
                if (c.Cell.Index >= 0 && c.Cell.Index < graph.CellCount && c.Position.DistanceTo(watch) <= ChokeReach && c.Cell.Index != watchCell.Index)
                    watched.Add((c.Cell.Index, c.Position, 1f));
            }
        }

        // Only a place the bot can walk to from where it stands, by the edges a player without a blast jump or a double jump
        // takes (walks, jumps, drops, ladders, doors): a roof is out, and so is a ledge only a drop leaves or a link reaches.
        bool[]? reach = from is { } at0 && graph.FindCell(at0) is { Index: >= 0 } fromCell ? Walkable(graph, fromCell.Index) : null;
        float objectiveFromSpawn = Flat(watch, ownSpawn);
        var near = new List<int>();
        graph.CellsNear(watch.X - range, watch.Y - range, watch.X + range, watch.Y + range, near);
        var scored = new List<TFSniperSpot>();
        foreach (int cell in near)
        {
            NavCell c = graph.Cell(cell);
            if (!c.Admits(ClearanceClass.Stand) || (reach is not null && !reach[cell]))
                continue;
            AiVector at = c.Centre;
            float d = at.DistanceTo(watch);
            if (d < NearestUnits || d > range)
                continue;
            float fromSpawn = Flat(at, ownSpawn);
            if (attack ? fromSpawn > objectiveFromSpawn - AttackBackUnits : fromSpawn > objectiveFromSpawn + DefendPastUnits)
                continue;
            if (refused?.Invoke(at) == true)
                continue;
            float seen = 0f, best = -1f;
            AiVector looks = watch;
            foreach ((int w, AiVector wAt, float weight) in watched)
            {
                if (sight is not null && !sight.Visible(cell, w))
                    continue;
                seen += weight;
                float far = at.DistanceTo(wAt);
                if (far > best)
                    (best, looks) = (far, wAt);
            }

            if (sight is not null && seen <= 0f)
                continue;
            if (sight is null)
                seen = 1f;
            float height = Math.Clamp((at.Z - watch.Z) / 256f, 0f, 1f);
            float score = seen + 0.6f * (d / range) + 0.5f * height - (sight?.Exposure(cell) ?? 0f);
            scored.Add(new TFSniperSpot(at, cell, looks, score));
        }

        // Best first; the cell index breaks a tie, so the order is the same on every run.
        scored.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Cell.CompareTo(b.Cell));
        var kept = ImmutableArray.CreateBuilder<TFSniperSpot>();
        foreach (TFSniperSpot s in scored)
        {
            if (kept.Count >= Kept)
                break;
            bool apart = true;
            foreach (TFSniperSpot k in kept)
            {
                if (k.At.DistanceTo(s.At) < Separation)
                {
                    apart = false;
                    break;
                }
            }

            if (apart)
                kept.Add(s);
        }

        return kept.ToImmutable();
    }

    /// <summary>The cells a player reaches from <paramref name="start"/> by the graph's edges, but a double jump's (a Scout's alone).</summary>
    public static bool[] Walkable(NavGraph graph, int start)
    {
        var seen = new bool[graph.CellCount];
        var queue = new Queue<int>();
        seen[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            foreach (NavEdge e in graph.EdgesFrom(c))
            {
                if (e.Type is NavEdgeType.DoubleJump or NavEdgeType.Link || seen[e.To])
                    continue;
                seen[e.To] = true;
                queue.Enqueue(e.To);
            }
        }

        return seen;
    }

    /// <summary>The cell under <paramref name="p"/>, else the nearest whose middle is within 128 u (an objective's origin floats over its floor).</summary>
    private static CellId NearestCell(NavGraph graph, AiVector p)
    {
        CellId under = graph.FindCell(p);
        if (under.Index >= 0)
            return under;
        var near = new List<int>();
        graph.CellsNear(p.X - 128f, p.Y - 128f, p.X + 128f, p.Y + 128f, near);
        int best = -1;
        float bestD = 128f * 1.8f;
        foreach (int i in near)
        {
            float d = graph.Cell(i).Centre.DistanceTo(p);
            if (d < bestD)
                (best, bestD) = (i, d);
        }

        return best >= 0 ? new CellId(best) : CellId.None;
    }

    private static float Flat(AiVector a, AiVector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
