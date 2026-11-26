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
                                                AiVector ownSpawn, AiVector enemySpawn, float reach = SentryRange * RangeTolerance, float best = BestRange,
                                                float exposure = ExposureWeight, bool ownSide = true)
        => Candidates(graph, sight, point, team, zones, ownSpawn, enemySpawn, out _, reach, best, exposure, ownSide);

    /// <summary><see cref="Candidates(NavGraph, ICellSight?, AiVector, int, ImmutableArray{TeamZone}, AiVector, AiVector, float, float)"/>, and what the finder saw (<see cref="TFSpotCensus"/>) for the harness.</summary>
    public static List<TFSentrySpot> Candidates(NavGraph graph, ICellSight? sight, AiVector point, int team, ImmutableArray<TeamZone> zones,
                                                AiVector ownSpawn, AiVector enemySpawn, out TFSpotCensus census, float reach = SentryRange * RangeTolerance,
                                                float best = BestRange, float exposure = ExposureWeight, bool ownSide = true)
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
            // A finder with no side (the Ninjaneer's, behind the enemy's lines) weighs both walks alike.
            spots.Add(new TFSentrySpot(c, at, Score(sight, c, at, point, ownSide ? ownWalk : 1f, ownSide ? enemyWalk : 1f, flat, best, exposure), YawTo(at, point)));
        }

        census = new TFSpotCensus(near.Count, spots.Count, sides.Count(x => x), pc.IsValid, own is not null, enemy is not null);
        if (anyOwnSide || !ownSide)
        {
            var kept = new List<TFSentrySpot>();
            for (int i = 0; i < spots.Count; i++)
            {
                if (sides[i] || !ownSide)
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
                              float best = BestRange, float exposureWeight = ExposureWeight)
    {
        float exposure = sight?.Exposure(cell) ?? 0f;
        float side = ownWalk + enemyWalk > 0f ? (enemyWalk - ownWalk) / (ownWalk + enemyWalk) : 0f;
        float height = Math.Clamp((at.Z - point.Z) / 128f, -1f, 1f);
        float range = 1f - MathF.Min(1f, MathF.Abs(flat - best) / best);
        return -exposureWeight * exposure + OwnSideWeight * side + HeightWeight * height + RangeWeight * range;
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

    /// <summary>
    /// Stock's entrance rules (tf_bot_engineer_build_teleport_entrance.cpp:18-19): no farther than this walk from his spawn
    /// (<c>tf_bot_max_teleport_entrance_travel</c>), on flat floor (<c>tf_bot_teleport_build_surface_normal_limit</c> 0.99: the
    /// nav keeps no normal, so the floor of the walk's cells round it within <see cref="FlatUnits"/>). Ours: this far past the
    /// respawn room's door, so the pad is not in the doorway, with floor this far to both sides, so a body passes it.
    /// </summary>
    public const float EntranceWalkUnits = 1500f, PastDoorUnits = 160f, PadRoomUnits = 72f, FlatUnits = 4f;

    /// <summary>
    /// Where the Engineer puts his teleporter entrance: on his team's way from its spawn toward <paramref name="toward"/> (his
    /// sentry's spot or his point), stock's "head towards the control point and build as soon as we can". Each walk from one
    /// of <paramref name="homes"/> (where he came into the world, and his team's spawn points by it) is followed to
    /// <paramref name="toward"/>; a cell of a walk is a place when it lies <see cref="PastDoorUnits"/> past the walk's last
    /// respawn room, on flat floor with room either side, not in a no-build area of his team, within
    /// <see cref="EntranceWalkUnits"/> of the walk. The place the most walks cross wins (his own walk's earliest on a tie):
    /// live on cp_granary his own walk left by one door and his team by another, and none of them ever saw his entrance.
    /// It faces on along the walk. Null when no walk finds one.
    /// </summary>
    public static TFSentrySpot? EntranceSpot(NavGraph graph, ReadOnlySpan<AiVector> homes, AiVector toward, int team, ImmutableArray<TeamZone> zones)
    {
        CellId to = PointCell(graph, toward);
        if (!to.IsValid || homes.Length == 0)
            return null;
        var field = new FlowField();
        field.Build(graph, to, ClearanceClass.Stand, new NavObstacleState(graph.ObstacleCount));
        // Each place's walks, and the place itself as the first walk to cross it saw it (in walk order, his own first).
        var counts = new Dictionary<int, int>();
        var order = new List<TFSentrySpot>();
        var walk = new List<int>();
        for (int h = 0; h < homes.Length; h++)
        {
            CellId from = PointCell(graph, homes[h]);
            if (!from.IsValid)
                continue;
            walk.Clear();
            for (CellId c = from; c.IsValid && walk.Count < 4096; c = field.NextFrom(c.Index))
            {
                walk.Add(c.Index);
                if (c.Index == to.Index)
                    break;
            }

            var seen = new HashSet<int>();
            float walked = 0f, doorAt = 0f;
            for (int i = 0; i < walk.Count; i++)
            {
                AiVector at = graph.Cell(walk[i]).Centre;
                if (i > 0)
                    walked += Flat(graph.Cell(walk[i - 1]).Centre, at);
                if (walked > EntranceWalkUnits)
                    break;
                if (InRespawnRoom(at, zones))
                {
                    doorAt = walked;
                    continue;
                }

                if (walked - doorAt < PastDoorUnits || InNoBuild(at, team, zones) || i == 0 || i + 2 >= walk.Count)
                    continue;
                AiVector ahead = graph.Cell(walk[i + 2]).Centre;
                float yaw = YawTo(at, ahead);
                if (Flat(at, ahead) < 1f || !Flat3(graph, walk[i - 1], walk[i], walk[i + 1]) || !Roomy(graph, at, yaw)
                    || FloorAt(graph, at - Forward(yaw) * PadStandUnits, at.Z) is null || !seen.Add(walk[i]))
                    continue;
                if (!counts.TryGetValue(walk[i], out int n))
                    order.Add(new TFSentrySpot(walk[i], at, 0f, yaw));
                counts[walk[i]] = n + 1;
            }
        }

        // The most crossed place; on a tie the first found (his own walk's earliest, then the next walk's).
        TFSentrySpot? best = null;
        int most = 0;
        foreach (TFSentrySpot spot in order)
        {
            if (counts[spot.Cell] > most)
                (most, best) = (counts[spot.Cell], spot);
        }

        return best;
    }

    /// <summary>A single walk's entrance (<see cref="EntranceSpot(NavGraph, ReadOnlySpan{AiVector}, AiVector, int, ImmutableArray{TeamZone})"/> from one home).</summary>
    public static TFSentrySpot? EntranceSpot(NavGraph graph, AiVector home, AiVector toward, int team, ImmutableArray<TeamZone> zones)
        => EntranceSpot(graph, [home], toward, team, zones);

    /// <summary>
    /// Stock drops the nest's exit right where he stands within 300 u of his sentry (tf_bot_engineer_building.cpp:459-463); here
    /// the first place behind the gun from the point (<paramref name="sentryYaw"/> faces it) at <see cref="ExitBehind"/>, then to
    /// its sides, on the gun's floor with room for a body by it and floor where he stands to put it down, clear of the nest's
    /// boxes; it faces the point, so his team
    /// comes out facing the front. Null for none.
    /// </summary>
    public static TFSentrySpot? NestExitSpot(NavGraph graph, AiVector sentry, float sentryYaw, ReadOnlySpan<AiVector> boxes, int team, ImmutableArray<TeamZone> zones)
    {
        AiVector fwd = Forward(sentryYaw), side = new(-fwd.Y, fwd.X, 0f);
        foreach (float back in ExitBehind)
        {
            foreach (float off in ExitAside)
            {
                AiVector at = sentry - fwd * back + side * off;
                CellId c = graph.FindCell(at + new AiVector(0f, 0f, 8f));
                if (!c.IsValid || MathF.Abs(graph.Cell(c).FloorZ - sentry.Z) > 24f)
                    continue;
                at = new AiVector(at.X, at.Y, graph.Cell(c).FloorZ);
                if (!PadClearOf(at, boxes) || InRespawnRoom(at, zones) || InNoBuild(at, team, zones) || !Roomy(graph, at, sentryYaw)
                    || FloorAt(graph, at - fwd * PadStandUnits, at.Z) is null)
                    continue;
                return new TFSentrySpot(c.Index, at, 0f, sentryYaw);
            }
        }

        return null;
    }

    /// <summary>
    /// Stock's offensive exit (tf_bot_engineer_move_to_build.cpp:363-420): at least this walk past the entrance
    /// (<c>tf_bot_min_teleport_travel</c>), within this walk of the point (<c>tf_bot_max_teleport_exit_travel_to_point</c>,
    /// here a straight line from it); ours: no nearer the point than <see cref="FrontExitNear"/>, best at
    /// <see cref="FrontExitBest"/>, where the fight at it does not stand on the pad.
    /// </summary>
    public const float MinTeleportTravel = 3000f, FrontExitFar = 2500f, FrontExitNear = 500f, FrontExitBest = 1000f;

    /// <summary>
    /// The Battle Engie's forward exit for the point at <paramref name="point"/>: a cell on his own side of it (his walk from his
    /// spawn shorter than the enemy's), <see cref="FrontExitNear"/> to <see cref="FrontExitFar"/> from it, at least
    /// <see cref="MinTeleportTravel"/> farther from his spawn by the walk than his <paramref name="entrance"/>, not in a respawn room
    /// or a no-build area of his team, on floor with room for a body by the pad; the least exposed (the sight table's share of
    /// places that see it: stock retreats to cover to build it), then the nearest <see cref="FrontExitBest"/>. It faces the point.
    /// Null when none is.
    /// </summary>
    public static TFSentrySpot? FrontExitSpot(NavGraph graph, ICellSight? sight, AiVector point, int team, ImmutableArray<TeamZone> zones,
                                              AiVector ownSpawn, AiVector enemySpawn, AiVector entrance)
    {
        FlowField? own = Field(graph, ownSpawn), enemy = Field(graph, enemySpawn);
        CellId e = PointCell(graph, entrance);
        float entranceWalk = own is not null && e.IsValid && float.IsFinite(own.CostAt(e.Index)) ? own.CostAt(e.Index) : entrance.DistanceTo(ownSpawn);
        var near = new List<int>();
        graph.CellsNear(point.X - FrontExitFar, point.Y - FrontExitFar, point.X + FrontExitFar, point.Y + FrontExitFar, near);
        TFSentrySpot? pick = null;
        float top = float.NegativeInfinity;
        foreach (int c in near)
        {
            NavCell cell = graph.Cell(c);
            if (!cell.Admits(ClearanceClass.Stand))
                continue;
            AiVector at = cell.Centre;
            float flat = Flat(at, point);
            if (flat > FrontExitFar || flat < FrontExitNear || InRespawnRoom(at, zones) || InNoBuild(at, team, zones))
                continue;
            // A cell with no walk to his spawn is no exit (the review's fix 5: a nav island near the point, least seen, would hold
            // him walking at it for the life); with no field at all the straight line stands in.
            float ownWalk = own?.CostAt(c) ?? at.DistanceTo(ownSpawn), enemyWalk = enemy?.CostAt(c) ?? float.PositiveInfinity;
            if (!float.IsFinite(ownWalk))
                continue;
            if (!float.IsFinite(enemyWalk))
                enemyWalk = at.DistanceTo(enemySpawn);
            if (ownWalk >= enemyWalk || ownWalk - entranceWalk < MinTeleportTravel)
                continue;
            float yaw = YawTo(at, point);
            if (!Roomy(graph, at, yaw) || FloorAt(graph, at - Forward(yaw) * PadStandUnits, at.Z) is null)
                continue;
            float score = -(sight?.Exposure(c) ?? 0f) - 0.5f * MathF.Abs(flat - FrontExitBest) / FrontExitBest;
            if (score > top)
                (top, pick) = (score, new TFSentrySpot(c, at, score, yaw));
        }

        return pick;
    }

    /// <summary>The nest exit's places behind the gun, and aside of that line, tried in order.</summary>
    public static ReadOnlySpan<float> ExitBehind => [160f, 128f, 224f];

    public static ReadOnlySpan<float> ExitAside => [0f, 72f, -72f, 144f, -144f];

    /// <summary>Where he stands to put a pad down: this far back from it (CalculatePlacementPos for the teleporter's 28 u box and his).</summary>
    public const float PadStandUnits = 78f;

    /// <summary>A pad (24 u a side, its build box 4 u more) clears a building's box (20 u) and a body's walk between them: 64 u on an axis.</summary>
    public const float PadClearUnits = 64f;

    private static bool PadClearOf(AiVector at, ReadOnlySpan<AiVector> boxes)
    {
        foreach (AiVector b in boxes)
        {
            if (MathF.Max(MathF.Abs(at.X - b.X), MathF.Abs(at.Y - b.Y)) < PadClearUnits)
                return false;
        }

        return true;
    }

    /// <summary>Three cells of a walk whose floors lie within <see cref="FlatUnits"/>: no slope or step under the pad.</summary>
    private static bool Flat3(NavGraph graph, int a, int b, int c)
    {
        float z = graph.Cell(b).FloorZ;
        return MathF.Abs(graph.Cell(a).FloorZ - z) <= FlatUnits && MathF.Abs(graph.Cell(c).FloorZ - z) <= FlatUnits;
    }

    /// <summary>Floor at the same height <see cref="PadRoomUnits"/> to both sides of <paramref name="yaw"/> at <paramref name="at"/>.</summary>
    private static bool Roomy(NavGraph graph, AiVector at, float yaw)
    {
        AiVector f = Forward(yaw), side = new(-f.Y, f.X, 0f);
        foreach (float s in (ReadOnlySpan<float>)[PadRoomUnits, -PadRoomUnits])
        {
            CellId c = graph.FindCell(at + side * s + new AiVector(0f, 0f, 8f));
            if (!c.IsValid || MathF.Abs(graph.Cell(c).FloorZ - at.Z) > 18f)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Where the Engineer stands to put his dispenser down beside his gun at <paramref name="sentry"/> (facing
    /// <paramref name="sentryYaw"/>): <paramref name="side"/> units or more to its <paramref name="sideSign"/> side, far enough
    /// that his box clears the gun's (<see cref="WorkClearUnits"/>), facing back, so the blueprint lands
    /// <paramref name="ahead"/> behind the gun; the other side next. Both his stand and the blueprint's spot must lie on floor
    /// at the gun's height: live on cp_granary's mid ledge a stand over the drop sent him to the floor below for 66 s.
    /// </summary>
    public static AiVector? DispenserStand(NavGraph graph, AiVector sentry, float sentryYaw, float sideSign, float side, float ahead)
    {
        AiVector fwd = Forward(sentryYaw), across = new AiVector(-fwd.Y, fwd.X, 0f) * sideSign;
        float off = MathF.Max(side, WorkClearUnits / MathF.Max(MathF.Abs(across.X), MathF.Abs(across.Y)));
        foreach (float sign in (ReadOnlySpan<float>)[1f, -1f])
        {
            AiVector at = sentry + across * (off * sign), lands = at - fwd * ahead;
            if (FloorAt(graph, at, sentry.Z) is { } z && FloorAt(graph, lands, sentry.Z) is not null)
                return new AiVector(at.X, at.Y, z);
        }

        return null;
    }

    /// <summary>The floor's height under <paramref name="at"/> when a cell there lies within 24 u of <paramref name="height"/>, else null.</summary>
    public static float? FloorAt(NavGraph graph, AiVector at, float height)
    {
        CellId c = graph.FindCell(new AiVector(at.X, at.Y, height + 8f));
        return c.IsValid && MathF.Abs(graph.Cell(c).FloorZ - height) <= 24f ? graph.Cell(c).FloorZ : null;
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
