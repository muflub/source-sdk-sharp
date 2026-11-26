//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>What the spot finder saw: cells near the point, candidates, of them on his side, and whether the point and both spawns stood on cells.</summary>
public readonly record struct TFSpotCensus(int Near, int Candidates, int OwnSide, bool PointCell, bool OwnField, bool EnemyField);

/// <summary>One place an Engineer may build his sentry: its nav cell, where he stands, its score and the yaw the gun faces.</summary>
public readonly record struct TFSentrySpot(int Cell, AiVector At, float Score, float Yaw);

/// <summary>
/// Where an Engineer builds his sentry (plan_bots_teams_tf2 C.3.2, the Engineer research's §4.3), on map knowledge only: the
/// nav graph, the static sight table and the objective he defends. Stock's candidates
/// (<c>CTFBotEngineerMoveToBuild::CollectBuildAreas</c>, tf_bot_engineer_move_to_build.cpp:135-186): the places that see
/// the point, not on it, not more than <see cref="TooFarBelow"/> under it, within <see cref="SentryRange"/> times
/// <see cref="RangeTolerance"/> of it, and not in a respawn room; nor in a no-build area that denies his team (stock's
/// blueprint refuses it there, so its nav areas are no build spot). Stock draws among them by surface area; here each is
/// scored by how exposed it is (<see cref="ICellSight.Exposure"/>: the open is a beginner's mistake), whether it lies on the
/// Engineer's own side of the point (his spawn nearer than the enemy's), its height over the point, and its range from it,
/// and the bot draws among the best <see cref="DrawFrom"/> by its own seeded stream, so two Engineers and two lives differ.
/// The gun faces the point (stock aims it where the enemy comes from, <c>m_needToAimSentry</c>).
/// </summary>
public static class TFEngineerSpots
{
    /// <summary><c>SENTRY_MAX_RANGE</c> (tf_obj_sentrygun.h), and stock's 1.1 tolerance on it (:175-178).</summary>
    public const float SentryRange = 1100f, RangeTolerance = 1.1f;

    /// <summary>Stock's "ignore areas below the point" (:168-170).</summary>
    public const float TooFarBelow = 150f;

    /// <summary>Not on the point: a cell within this of its centre is the capture area (stock skips <c>TF_NAV_CONTROL_POINT</c> areas).</summary>
    public const float OffPoint = 192f;

    /// <summary>The range from the point a sentry is best placed at: it reaches the point and the approach beyond it.</summary>
    public const float BestRange = 600f;

    /// <summary>The weights of a candidate's terms (research §4.3, step 2).</summary>
    public const float ExposureWeight = 0.75f, OwnSideWeight = 0.5f, HeightWeight = 0.3f, RangeWeight = 0.5f;

    /// <summary>The draw is among this many of the best, by <c>exp(score / Temperature)</c>.</summary>
    public const int DrawFrom = 8;

    public const float Temperature = 0.25f;

    /// <summary>
    /// Every candidate for defending <paramref name="point"/>, best first: the stock filters, then the score. A cell beyond
    /// the sight table's range is not known to see the point and is no candidate; with no sight table at all every cell in
    /// range is. A mini sentry's finder asks a shorter <paramref name="reach"/> and <paramref name="best"/> range.
    ///
    /// <para>The sides are travel, not distance (the research's step 0, stock's incursion distance): a field to each team's
    /// spawn gives each cell's walk to it (straight when none comes back); a cell nearer his spawn by that walk than the
    /// enemy's is his side, and when the point has candidates on his side
    /// only those are kept (stock keeps that rule as a hard one on KOTH alone; live on cp_granary a spot on the enemy's side
    /// of the middle, picked for its height, had him walk through the enemy for 70 s and never build).</para>
    /// </summary>
    public static List<TFSentrySpot> Candidates(NavGraph graph, ICellSight? sight, AiVector point, int team, ImmutableArray<TeamZone> zones,
                                                AiVector ownSpawn, AiVector enemySpawn, float reach = SentryRange * RangeTolerance, float best = BestRange)
        => Candidates(graph, sight, point, team, zones, ownSpawn, enemySpawn, out _, reach, best);

    /// <summary><see cref="Candidates(NavGraph, ICellSight?, AiVector, int, ImmutableArray{TeamZone}, AiVector, AiVector, float, float)"/>, and what the finder saw (<see cref="TFSpotCensus"/>) for the harness.</summary>
    public static List<TFSentrySpot> Candidates(NavGraph graph, ICellSight? sight, AiVector point, int team, ImmutableArray<TeamZone> zones,
                                                AiVector ownSpawn, AiVector enemySpawn, out TFSpotCensus census, float reach = SentryRange * RangeTolerance,
                                                float best = BestRange)
    {
        var spots = new List<TFSentrySpot>();
        CellId pc = PointCell(graph, point);
        if (sight is not null)
            reach = MathF.Min(reach, sight.Range);
        FlowField? own = Field(graph, ownSpawn), enemy = Field(graph, enemySpawn);
        var near = new List<int>();
        graph.CellsNear(point.X - reach, point.Y - reach, point.X + reach, point.Y + reach, near);
        bool anyOwnSide = false;
        var sides = new List<bool>();
        foreach (int c in near)
        {
            NavCell cell = graph.Cell(c);
            if (!cell.Admits(ClearanceClass.Stand))
                continue;
            AiVector at = cell.Centre;
            float flat = MathF.Sqrt((at.X - point.X) * (at.X - point.X) + (at.Y - point.Y) * (at.Y - point.Y));
            if (flat > reach || flat < OffPoint || at.Z < point.Z - TooFarBelow || InRespawnRoom(at, zones) || InNoBuild(at, team, zones))
                continue;
            if (sight is not null && (!pc.IsValid || !sight.Visible(c, pc.Index)))
                continue;
            // A field to a spawn gives each cell's walk to it, the reverse of the walk from it (the review's C3): a cell with no
            // way back to the spawn is measured as the crow flies, not dropped. Whether he can walk to a spot is asked when it is
            // drawn (Reachable).
            float ownWalk = own?.CostAt(c) ?? float.PositiveInfinity, enemyWalk = enemy?.CostAt(c) ?? float.PositiveInfinity;
            if (!float.IsFinite(ownWalk))
                ownWalk = at.DistanceTo(ownSpawn);
            if (!float.IsFinite(enemyWalk))
                enemyWalk = at.DistanceTo(enemySpawn);
            bool mine = ownWalk < enemyWalk;
            anyOwnSide |= mine;
            sides.Add(mine);
            spots.Add(new TFSentrySpot(c, at, Score(sight, c, at, point, ownWalk, enemyWalk, flat, best), YawTo(at, point)));
        }

        census = new TFSpotCensus(near.Count, spots.Count, sides.Count(x => x), pc.IsValid, own is not null, enemy is not null);
        if (anyOwnSide)
        {
            var kept = new List<TFSentrySpot>();
            for (int i = 0; i < spots.Count; i++)
            {
                if (sides[i])
                    kept.Add(spots[i]);
            }

            spots = kept;
        }

        spots.Sort((a, b) => b.Score.CompareTo(a.Score));
        return spots;
    }

    /// <summary>Whether a walker at <paramref name="from"/> can walk to <paramref name="spot"/>'s cell: a field to the spot reaches his cell (a ledge he could drop from but not climb to is no spot).</summary>
    public static bool Reachable(NavGraph graph, AiVector from, in TFSentrySpot spot)
    {
        CellId at = PointCell(graph, from);
        if (!at.IsValid)
            return true;
        var field = new FlowField();
        field.Build(graph, new CellId(spot.Cell), ClearanceClass.Stand, new NavObstacleState(graph.ObstacleCount));
        return float.IsFinite(field.CostAt(at.Index));
    }

    /// <summary>A field to <paramref name="spawn"/>'s cell, each cell's walk to it; null when the spawn stands on no cell.</summary>
    private static FlowField? Field(NavGraph graph, AiVector spawn)
    {
        CellId cell = PointCell(graph, spawn);
        if (!cell.IsValid)
            return null;
        var field = new FlowField();
        field.Build(graph, cell, ClearanceClass.Stand, new NavObstacleState(graph.ObstacleCount));
        return field;
    }

    /// <summary>
    /// A candidate's score: less exposed, on the own side (by the walks from each spawn, <paramref name="ownWalk"/> and
    /// <paramref name="enemyWalk"/>), higher than the point, near <paramref name="best"/> from it.
    /// </summary>
    public static float Score(ICellSight? sight, int cell, AiVector at, AiVector point, float ownWalk, float enemyWalk, float flat,
                              float best = BestRange)
    {
        float exposure = sight?.Exposure(cell) ?? 0f;
        float side = ownWalk + enemyWalk > 0f ? (enemyWalk - ownWalk) / (ownWalk + enemyWalk) : 0f;
        float height = Math.Clamp((at.Z - point.Z) / 128f, -1f, 1f);
        float range = 1f - MathF.Min(1f, MathF.Abs(flat - best) / best);
        return -ExposureWeight * exposure + OwnSideWeight * side + HeightWeight * height + RangeWeight * range;
    }

    /// <summary>
    /// The spot the bot takes: drawn by <paramref name="rng"/> among the best <see cref="DrawFrom"/> of
    /// <paramref name="spots"/> not in <paramref name="failed"/>, by <c>exp(score / Temperature)</c>; null when none is left.
    /// </summary>
    public static TFSentrySpot? Pick(List<TFSentrySpot> spots, ISet<int> failed, AiRandom rng)
    {
        Span<int> best = stackalloc int[DrawFrom];
        int n = 0;
        for (int i = 0; i < spots.Count && n < DrawFrom; i++)
        {
            if (!failed.Contains(spots[i].Cell))
                best[n++] = i;
        }

        if (n == 0)
            return null;
        float top = spots[best[0]].Score, total = 0f;
        Span<float> w = stackalloc float[DrawFrom];
        for (int i = 0; i < n; i++)
            total += w[i] = MathF.Exp((spots[best[i]].Score - top) / Temperature);
        float at = rng.NextFloat() * total;
        for (int i = 0; i < n; i++)
        {
            at -= w[i];
            if (at < 0f)
                return spots[best[i]];
        }

        return spots[best[n - 1]];
    }

    /// <summary>
    /// His box (24 u a side from his middle) and a building's (20 u, the sentry's and dispenser's hull, TFObjectModels) clear by
    /// this, each axis apart (the boxes are axis-aligned): 44 touches, the rest is room for a body that sways. A stand at 45 u
    /// along a diagonal (round 1's) put his box 13 u into the gun's.
    /// </summary>
    public const float WorkClearUnits = 56f;

    /// <summary>
    /// The work stand lies within this of the building's middle: the wrench's swing (48 u from the eye, an 18 u hull,
    /// stock's melee trace) reaches the box's face (20 u off the middle) from there. Sampled on a grid this fine.
    /// </summary>
    public const float WorkReachUnits = 80f, WorkGridUnits = 8f;

    /// <summary>Within this of his dispenser's middle he stands in its reach (TFDispenser.DispenserRadius 64 u, to his box).</summary>
    public const float NestFeedUnits = 72f;

    /// <summary>
    /// Where the Engineer stands to swing at the building at <paramref name="target"/>: within <see cref="WorkReachUnits"/> of it,
    /// his box clear of every building's in <paramref name="boxes"/> by <see cref="WorkClearUnits"/> on an axis, on floor at the
    /// building's height, within <see cref="NestFeedUnits"/> of <paramref name="feeder"/> (his dispenser) when there is one and
    /// some place allows it; the most behind the building along <paramref name="back"/> (away from the point it covers), then
    /// the nearest him.
    /// </summary>
    public static AiVector WorkStand(NavGraph? graph, AiVector target, AiVector? feeder, ReadOnlySpan<AiVector> boxes, AiVector back, AiVector self)
    {
        back = new AiVector(back.X, back.Y, 0f);
        AiVector behind = back.Length < 1f ? new AiVector(1f, 0f, 0f) : back.Normalised();
        int steps = (int)(WorkReachUnits / WorkGridUnits);
        for (int pass = 0; pass < 2; pass++)
        {
            bool fed = pass == 0 && feeder is not null;
            float best = float.PositiveInfinity;
            AiVector pick = default;
            for (int i = -steps; i <= steps; i++)
            {
                for (int j = -steps; j <= steps; j++)
                {
                    AiVector off = new(i * WorkGridUnits, j * WorkGridUnits, 0f), at = target + off;
                    float r = off.Length;
                    if (r > WorkReachUnits || !ClearOf(at, boxes) || (fed && Flat(at, feeder!.Value) > NestFeedUnits))
                        continue;
                    if (graph is not null)
                    {
                        CellId c = graph.FindCell(at + new AiVector(0f, 0f, 8f));
                        if (!c.IsValid || MathF.Abs(graph.Cell(c).FloorZ - target.Z) > 24f)
                            continue;
                        at = new AiVector(at.X, at.Y, graph.Cell(c).FloorZ);
                    }

                    float score = Flat(at, self) * 0.25f - (off.X * behind.X + off.Y * behind.Y) / r * 64f;
                    if (score < best)
                        (best, pick) = (score, at);
                }
            }

            if (best < float.PositiveInfinity)
                return pick;
            if (feeder is null)
                break;
        }

        return target + behind * WorkClearUnits;
    }

    /// <summary>A stand's height tolerance: a step and a crouch, no floor above or below.</summary>
    public const float StandHeightUnits = 40f;

    /// <summary>Whether a body at <paramref name="self"/> stands at <paramref name="at"/>: within <paramref name="arrive"/> flat and <see cref="StandHeightUnits"/> up or down.</summary>
    public static bool Arrived(AiVector self, AiVector at, float arrive) => Flat(self, at) <= arrive && MathF.Abs(self.Z - at.Z) <= StandHeightUnits;

    /// <summary>Whether a body standing at <paramref name="at"/> keeps <see cref="WorkClearUnits"/> on an axis from each building's middle.</summary>
    public static bool ClearOf(AiVector at, ReadOnlySpan<AiVector> boxes)
    {
        foreach (AiVector b in boxes)
        {
            if (MathF.Max(MathF.Abs(at.X - b.X), MathF.Abs(at.Y - b.Y)) < WorkClearUnits)
                return false;
        }

        return true;
    }

    private static float Flat(AiVector a, AiVector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>How far round the point's place its cell is looked for when no floor lies right under it (a point's origin sits on its model).</summary>
    public const float PointCellReach = 160f;

    /// <summary>
    /// The nav cell the point stands on: the one under its place, else under it from a little above (an origin on the floor's
    /// edge), else the standing cell whose centre is nearest it within <see cref="PointCellReach"/>; None for none.
    /// </summary>
    public static CellId PointCell(NavGraph graph, AiVector point)
    {
        CellId under = graph.FindCell(point);
        if (under.IsValid)
            return under;
        under = graph.FindCell(point + new AiVector(0f, 0f, 48f));
        if (under.IsValid)
            return under;
        var near = new List<int>();
        graph.CellsNear(point.X - PointCellReach, point.Y - PointCellReach, point.X + PointCellReach, point.Y + PointCellReach, near);
        int best = -1;
        float bd = PointCellReach;
        foreach (int c in near)
        {
            NavCell cell = graph.Cell(c);
            float d = cell.Centre.DistanceTo(point);
            if (cell.Admits(ClearanceClass.Stand) && d < bd)
                (bd, best) = (d, c);
        }

        return best >= 0 ? new CellId(best) : CellId.None;
    }

    /// <summary>The yaw, in degrees, from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float YawTo(AiVector from, AiVector to) => MathF.Atan2(to.Y - from.Y, to.X - from.X) * (180f / MathF.PI);

    /// <summary>The flat unit vector of <paramref name="yaw"/> degrees.</summary>
    public static AiVector Forward(float yaw)
    {
        float r = yaw * (MathF.PI / 180f);
        return new AiVector(MathF.Cos(r), MathF.Sin(r), 0f);
    }

    private static bool InRespawnRoom(AiVector at, ImmutableArray<TeamZone> zones)
    {
        foreach (TeamZone z in zones)
        {
            if (z.Kind == ZoneKind.RespawnRoom && z.Contains(at + new AiVector(0f, 0f, 16f)))
                return true;
        }

        return false;
    }

    /// <summary>Whether <paramref name="at"/> lies in a no-build area that denies <paramref name="team"/> (its own team's, or every team's).</summary>
    public static bool InNoBuild(AiVector at, int team, ImmutableArray<TeamZone> zones)
    {
        foreach (TeamZone z in zones)
        {
            if (z.Kind == ZoneKind.NoBuild && (z.Team == 0 || z.Team == team) && z.Contains(at + new AiVector(0f, 0f, 16f)))
                return true;
        }

        return false;
    }
}
