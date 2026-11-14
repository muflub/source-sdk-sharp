using System.Globalization;
using System.Text;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// A fact that needs this tree's <c>.nav3d</c> and <c>.navhull</c> of a TF2 map, both built from the map
/// in this tree; it skips without them. <c>make ai-nav-tf MAP=map</c> builds the first (<c>make map
/// MAP=ss_ai_tf_jump</c> for that map, with its bsp) and <c>make ai-nav-tf MAP=map HULL=1</c> the second.
/// </summary>
public sealed class PerTreeHullNavFactAttribute : FactAttribute
{
    public PerTreeHullNavFactAttribute(string map)
    {
        if (TFHullNav.BspPath(map) is null || !File.Exists(TFHullNav.Nav3dPath(map)) || !File.Exists(TFHullNav.HullPath(map)))
        {
            Skip = $"this tree has no {TFHullNav.HullPath(map)} beside {TFHullNav.Nav3dPath(map)} and the map: "
                   + $"`make ai-nav-tf MAP={map}` and `make ai-nav-tf MAP={map} HULL=1` build them";
        }
    }
}

/// <summary>Where this tree keeps a TF2 map's two navigation files (the mod's own maps/) and its bsp (there, or the content the mod mounts).</summary>
internal static class TFHullNav
{
    private static string Maps => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps");

    public static string Nav3dPath(string map) => Path.Combine(Maps, map + ".nav3d");

    public static string HullPath(string map) => Path.Combine(Maps, map + ".navhull");

    public static string? BspPath(string map)
        => new[] { Path.Combine(Maps, map + ".bsp"), TF2Nav.BspPath(map) }.FirstOrDefault(File.Exists);
}

/// <summary>
/// How two derived graphs of one map differ, cell by place. A cell is matched by its place: its
/// column's corner and its floor (min x, min y, floor z, compared exactly; no two cells of one graph
/// share them). Indexes are not compared: the two files number their leaves in different orders.
/// A matched cell is the same when its far corner, classes, flags and cost are equal. An edge is
/// matched by its two ends' places, and is the same when its type and its obstacle's name are.
/// </summary>
internal sealed class NavGraphDiff
{
    public int CellsA { get; private init; }

    public int CellsB { get; private init; }

    public int EdgesA { get; private init; }

    public int EdgesB { get; private init; }

    /// <summary>The first graph's cells the second has none at the place of, and the second's the first has none at.</summary>
    public List<int> OnlyA { get; } = new();

    public List<int> OnlyB { get; } = new();

    /// <summary>Matched cells (the first graph's index) whose far corner, classes, flags or cost differ.</summary>
    public List<int> Changed { get; } = new();

    /// <summary>The first graph's edges the second lacks between the same places, or has with another type or obstacle; and the second's the first lacks.</summary>
    public List<NavEdge> EdgesOnlyA { get; } = new();

    public List<NavEdge> EdgesChanged { get; } = new();

    public List<NavEdge> EdgesOnlyB { get; } = new();

    public bool Same => OnlyA.Count + OnlyB.Count + Changed.Count + EdgesOnlyA.Count + EdgesChanged.Count + EdgesOnlyB.Count == 0;

    private static (float, float, float) Place(in NavCell c) => (c.MinX, c.MinY, c.FloorZ);

    public static NavGraphDiff Compare(DerivedNav a, DerivedNav b)
    {
        NavGraph ga = a.Graph, gb = b.Graph;
        var d = new NavGraphDiff { CellsA = ga.CellCount, CellsB = gb.CellCount, EdgesA = ga.EdgeCount, EdgesB = gb.EdgeCount };
        var placeB = new Dictionary<(float, float, float), int>(gb.CellCount);
        for (int c = 0; c < gb.CellCount; c++)
        {
            if (!placeB.TryAdd(Place(gb.Cell(c)), c))
                throw new InvalidDataException($"two cells of the second graph share the place {Place(gb.Cell(c))}");
        }

        var toB = new int[ga.CellCount];
        var toA = new int[gb.CellCount];
        Array.Fill(toA, -1);
        var seenA = new HashSet<(float, float, float)>();
        for (int c = 0; c < ga.CellCount; c++)
        {
            NavCell ca = ga.Cell(c);
            if (!seenA.Add(Place(ca)))
                throw new InvalidDataException($"two cells of the first graph share the place {Place(ca)}");
            toB[c] = placeB.GetValueOrDefault(Place(ca), -1);
            if (toB[c] < 0)
            {
                d.OnlyA.Add(c);
                continue;
            }

            toA[toB[c]] = c;
            NavCell cb = gb.Cell(toB[c]);
            if (ca.MaxX != cb.MaxX || ca.MaxY != cb.MaxY || ca.Classes != cb.Classes || ca.Flags != cb.Flags || ca.CostMultiplier != cb.CostMultiplier)
                d.Changed.Add(c);
        }

        for (int c = 0; c < gb.CellCount; c++)
        {
            if (toA[c] < 0)
                d.OnlyB.Add(c);
        }

        for (int c = 0; c < ga.CellCount; c++)
        {
            foreach (NavEdge e in ga.EdgesFrom(c))
            {
                if (toB[e.From] < 0 || toB[e.To] < 0 || !gb.TryEdge(toB[e.From], toB[e.To], out NavEdge other))
                {
                    d.EdgesOnlyA.Add(e);
                    continue;
                }

                string? na = e.Obstacle < 0 ? null : a.Obstacles[e.Obstacle].Name, nb = other.Obstacle < 0 ? null : b.Obstacles[other.Obstacle].Name;
                if (e.Type != other.Type || na != nb)
                    d.EdgesChanged.Add(e);
            }
        }

        for (int c = 0; c < gb.CellCount; c++)
        {
            foreach (NavEdge e in gb.EdgesFrom(c))
            {
                if (toA[e.From] < 0 || toA[e.To] < 0 || !ga.TryEdge(toA[e.From], toA[e.To], out _))
                    d.EdgesOnlyB.Add(e);
            }
        }

        return d;
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture,
            $"cells {CellsA} and {CellsB}: {OnlyA.Count} only the first has, {OnlyB.Count} only the second, {Changed.Count} that differ; "
            + $"edges {EdgesA} and {EdgesB}: {EdgesOnlyA.Count} only the first has ({ByType(EdgesOnlyA)}), {EdgesOnlyB.Count} only the second ({ByType(EdgesOnlyB)}), "
            + $"{EdgesChanged.Count} of another type or obstacle");

    private static string ByType(List<NavEdge> edges)
        => string.Join(", ", edges.GroupBy(e => e.Type).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));
}

/// <summary>
/// The differential that makes the hull navigation trustworthy (plan_bots_teams_tf2_part_g G.7):
/// on a map this tree has both files of, built from the same map, the graph derived through
/// <see cref="NavHullNavSource"/> is the graph derived through <see cref="Nav3dNavSource"/>, by
/// TF2's numbers (<see cref="NavGraphDiff"/> says what "the same" is), and a point is free in both
/// files or in neither.
/// <list type="bullet">
/// <item><b>ss_ai_tf_jump</b> (every brush axis-aligned): the same graph, cell for cell and edge
/// for edge (15,679 cells and 119,523 edges at map-tools 3551fa8).</item>
/// <item><b>ctf_2fort</b>: the same but at leaves whose clearance record lists a sloped brush
/// (a ramp, a roof, a displacement's hull). There the <c>.nav3d</c> reader tests the body's box
/// against the brush at the height asked, and the hull file keeps one number, the head room over
/// the voxel's bottom: the two agree at a voxel's bottom (32,185,712 asks, none differ) and part
/// where the derivation asks at the floor under the voxel (the hull stands a body in 16,788 of
/// 892,152 asks where the <c>.nav3d</c> does not) or a step over it, the wide hull's stair rule
/// (the <c>.nav3d</c> fits one in 7,290 of 673,060 asks where the hull does not). In the graphs:
/// 2,043 of the <c>.nav3d</c>'s 162,712 cells are missing from the hull's and 59 of the hull's
/// 160,728 from the <c>.nav3d</c>'s, 37 differ in class; 11,646 of 1,158,283 edges are only the
/// <c>.nav3d</c>'s (every one at such a cell but one) and 5,586 of 1,152,223 only the hull's (308
/// at such a cell, the rest a two-column move over a column whose crossing the two answer apart),
/// and 181 are of another type or carry another obstacle. The facts hold the shape of that, not
/// the counts: every cell that differs is on such a leaf, every edge that differs touches one, and
/// both are under two in a hundred. It is not a harmless two in a hundred: the cells the hull lacks
/// are rows of the map's stairs, and its graph cuts each spawn from the far flag.</item>
/// </list>
/// </summary>
public sealed class NavHullDifferentialTests
{
    private const string Jump = "ss_ai_tf_jump";
    private const string Fort = "ctf_2fort";

    private sealed record Pair(byte[] Bsp, Nav3dReader Nav3d, NavHullReader Hull, NavHullNavSource HullSource, DerivedNav FromNav3d, DerivedNav FromHull, NavGraphDiff Diff);

    private static Pair Load(string map)
    {
        byte[] bsp = File.ReadAllBytes(TFHullNav.BspPath(map)!);
        Nav3dReader nav3d = Nav3dReader.Open(File.ReadAllBytes(TFHullNav.Nav3dPath(map)));
        NavHullReader hull = NavHullReader.Open(File.ReadAllBytes(TFHullNav.HullPath(map)));
        MapEntities entities = MapEntities.Read(bsp);
        NavHullNavSource source = NavHullNavSource.Over(hull);
        DerivedNav a = NavGraphFromNav3d.Derive(new Nav3dNavSource(nav3d), TFNavProfile.ModNumbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs);
        DerivedNav b = NavGraphFromNav3d.Derive(source, TFNavProfile.ModNumbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs);
        return new Pair(bsp, nav3d, hull, source, a, b, NavGraphDiff.Compare(a, b));
    }

    private static readonly Lazy<Pair> JumpMap = new(() => Load(Jump));
    private static readonly Lazy<Pair> FortMap = new(() => Load(Fort));

    [PerTreeHullNavFact(Jump)]
    public void TheJumpMapsHullIsBoundToItsBsp() => Assert.True(JumpMap.Value.Hull.MatchesBsp(JumpMap.Value.Bsp));

    [PerTreeHullNavFact(Jump)]
    public void OnAMapOfAxisAlignedBrushesTheTwoGraphsAreTheSame()
    {
        Pair p = JumpMap.Value;
        Assert.True(p.Diff.CellsA > 10_000 && p.Diff.EdgesA > 100_000, p.Diff.ToString());
        Assert.True(p.Diff.Same, p.Diff.ToString());
    }

    [PerTreeHullNavFact(Jump)]
    public void OnTheJumpMapAPointIsFreeInBothFilesOrInNeither() => PointsAgree(JumpMap.Value);

    [PerTreeHullNavFact(Fort)]
    public void TwoFortsHullIsBoundToItsBsp() => Assert.True(FortMap.Value.Hull.MatchesBsp(FortMap.Value.Bsp));

    [PerTreeHullNavFact(Fort)]
    public void OnTwoFortAPointIsFreeInBothFilesOrInNeither() => PointsAgree(FortMap.Value);

    /// <summary>100,000 seeded points over the hull's grid and an eye over every cell of the <c>.nav3d</c>'s graph: free in both or in neither, with some of each.</summary>
    private static void PointsAgree(Pair p)
    {
        var nav3d = new Nav3dVoxelSpace(p.Nav3d);
        var hull = new NavHullVoxelSpace(p.Hull);
        NavHullReader h = p.Hull;
        float side = h.CellVoxels * h.VoxelSize;
        var rng = new AiRandom(7);
        int free = 0, solid = 0;
        var apart = new List<AiVector>();
        void Ask(AiVector at)
        {
            bool a = nav3d.Free(at), b = hull.Free(at);
            if (a != b)
                apart.Add(at);
            else if (a)
                free++;
            else
                solid++;
        }

        for (int i = 0; i < 100_000; i++)
            Ask(new AiVector(h.Origin.X + rng.NextFloat() * h.Columns * side, h.Origin.Y + rng.NextFloat() * h.Rows * side, h.Origin.Z + rng.NextFloat() * h.ColumnVoxels * h.VoxelSize));
        for (int c = 0; c < p.FromNav3d.Graph.CellCount; c++)
            Ask(p.FromNav3d.Graph.Cell(c).Centre + new AiVector(0, 0, NavSight.EyeHeight));
        Assert.True(free > 5_000 && solid > 5_000, $"{free} free and {solid} solid points: the sample missed one of them");
        Assert.True(apart.Count == 0, $"{apart.Count} points are free in one file only, the first at {(apart.Count > 0 ? apart[0] : default)}");
    }

    /// <summary>Whether the <c>.nav3d</c> leaf holding the bottom voxel of <paramref name="leaf"/>'s run lists a sloped brush in its clearance record.</summary>
    private static bool OnASlopedBrush(Pair p, in NavLeaf leaf)
    {
        float half = p.Nav3d.VoxelSize * 0.5f;
        int at = p.Nav3d.FindLeaf(new SourceSharp.MapFormats.Geometry.Vec3(leaf.MinX + half, leaf.MinY + half, leaf.BottomZ + half));
        return at >= 0 && Nav3dClearance.BrushCount(p.Nav3d.ClearanceRecord(at, Nav3dClipClass.Player)) > 0;
    }

    [PerTreeHullNavFact(Fort)]
    public void OnTwoFortEveryCellThatDiffersIsOnALeafWithASlopedBrush()
    {
        Pair p = FortMap.Value;
        var nav3d = new Nav3dNavSource(p.Nav3d);
        int plain = p.Diff.OnlyA.Concat(p.Diff.Changed).Count(c => !OnASlopedBrush(p, nav3d.Leaf(p.FromNav3d.LeafOfCell[c])))
                    + p.Diff.OnlyB.Count(c => !OnASlopedBrush(p, p.HullSource.Leaf(p.FromHull.LeafOfCell[c])));
        Assert.True(plain == 0, $"{plain} cells differ on leaves with no sloped brush; {p.Diff}");
    }

    [PerTreeHullNavFact(Fort)]
    public void OnTwoFortTheCellsThatDifferAreUnderTwoInAHundred()
    {
        NavGraphDiff d = FortMap.Value.Diff;
        Assert.True(d.CellsA > 100_000, d.ToString());
        Assert.True(d.OnlyA.Count + d.OnlyB.Count + d.Changed.Count < d.CellsA / 50, d.ToString());
    }

    [PerTreeHullNavFact(Fort)]
    public void OnTwoFortEveryEdgeThatDiffersTouchesALeafWithASlopedBrush()
    {
        Pair p = FortMap.Value;
        NavGraphDiff d = p.Diff;
        var nav3d = new Nav3dNavSource(p.Nav3d);
        int plain = d.EdgesOnlyA.Concat(d.EdgesChanged).Count(e => !Touches(p, p.FromNav3d.Graph, e, c => nav3d.Leaf(p.FromNav3d.LeafOfCell[c])))
                    + d.EdgesOnlyB.Count(e => !Touches(p, p.FromHull.Graph, e, c => p.HullSource.Leaf(p.FromHull.LeafOfCell[c])));
        Assert.True(plain == 0, $"{plain} edges differ with no sloped brush at either end or in a column between; {d}");
    }

    /// <summary>Whether an edge has an end on a leaf with a sloped brush, or passes over a column that has one at the higher floor (a move two columns along, a jump link across a gap).</summary>
    private static bool Touches(Pair p, NavGraph g, in NavEdge e, Func<int, NavLeaf> leafOf)
    {
        NavLeaf from = leafOf(e.From), to = leafOf(e.To);
        if (OnASlopedBrush(p, from) || OnASlopedBrush(p, to))
            return true;
        float side = from.MaxX - from.MinX, high = MathF.Max(g.Cell(e.From).FloorZ, g.Cell(e.To).FloorZ);
        int steps = (int)MathF.Round(MathF.Max(MathF.Abs(to.MinX - from.MinX), MathF.Abs(to.MinY - from.MinY)) / side);
        for (int i = 1; i < steps; i++)
        {
            float x = from.MinX + (to.MinX - from.MinX) * i / steps, y = from.MinY + (to.MinY - from.MinY) * i / steps;
            // The column between, from the higher floor's voxel up through a crouched body.
            for (float z = high; z < high + 62f + side; z += side)
            {
                if (OnASlopedBrush(p, new NavLeaf(x, y, x + side, y + side, MathF.Floor(z / side) * side, 0f, 0f, false, false, false, 1f)))
                    return true;
            }
        }

        return false;
    }

    [PerTreeHullNavFact(Fort)]
    public void OnTwoFortTheEdgesThatDifferAreUnderTwoInAHundred()
    {
        NavGraphDiff d = FortMap.Value.Diff;
        Assert.True(d.EdgesA > 1_000_000, d.ToString());
        Assert.True(d.EdgesOnlyA.Count + d.EdgesOnlyB.Count + d.EdgesChanged.Count < d.EdgesA / 50, d.ToString());
    }

    /// <summary>
    /// What the difference costs, and why the loader takes a map's <c>.nav3d</c> before its hull
    /// navigation (<see cref="NavLoader"/>): the cells the hull's graph lacks are rows of 2fort's
    /// stairs, and without them neither team's spawn has a way to the other base's flag, where the
    /// <c>.nav3d</c>'s graph joins all four (TF2NavTests). A tripwire: when this fails, map-tools'
    /// hull file answers a sloped brush as its <c>.nav3d</c> does, and the loader's order can turn.
    /// </summary>
    [PerTreeHullNavFact(Fort)]
    public void TheHullsGraphOfTwoFortCutsEachSpawnFromTheFarFlagWhereTheNav3dsJoinsThem()
    {
        Pair p = FortMap.Value;
        Assert.Equal([true, true, true, true], Ways(p.FromNav3d.Graph));
        // Flag by flag, spawn by spawn: each flag is joined to its own base's spawn only.
        Assert.Equal([true, false, false, true], Ways(p.FromHull.Graph));
    }

    private static bool[] Ways(NavGraph g)
    {
        AiVector[] flags = [new(489f, -3348.5f, -131f), new(-488.7f, 3348.5f, -131f)];
        AiVector[] spawns = [new(-1888f, -1280f, 257f), new(1888f, 1368f, 257f)];
        var open = new NavObstacleState(g.ObstacleCount) { OpenDoors = true };
        var ways = new List<bool>();
        foreach (AiVector flag in flags)
        {
            var field = new FlowField();
            field.Build(g, g.FindCell(flag), ClearanceClass.Crouch, open);
            foreach (AiVector spawn in spawns)
                ways.Add(float.IsFinite(field.CostAt(g.FindCell(spawn).Index)));
        }

        return [.. ways];
    }
}
