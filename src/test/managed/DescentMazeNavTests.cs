using System.Globalization;
using System.Text.RegularExpressions;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Level 5's maze as its built <c>.nav3d</c> holds it (brief p18): from the way in, the largest robot hull the nav
/// derives (x1.90, 92 wide and 156 high) walks to every maze cell and to every pack spawn in the maze, so every
/// corridor a pack stands in is one its giants leave by. The switched brushes block as the server blocks them
/// (all but the exit cover, as <c>tools/descent-breakables reach</c>). Reads this tree's build of level 5 at seeds 1
/// to 3; each fact skips where its own level is not built.
/// </summary>
public sealed partial class DescentMazeNavTests
{
    /// <summary>The largest robot's hull (Sir Nukesalot's x1.90 band, the server's extra hull).</summary>
    public const float GiantWidth = 92, GiantHeight = 156;

    [BuiltDescentLevelFact(5, 1)] public void Depth_5_seed_1s_giant_walks_from_the_way_in_to_every_maze_cell_and_pack() => Walks(5, 1);
    [BuiltDescentLevelFact(5, 2)] public void Depth_5_seed_2s_giant_walks_from_the_way_in_to_every_maze_cell_and_pack() => Walks(5, 2);
    [BuiltDescentLevelFact(5, 3)] public void Depth_5_seed_3s_giant_walks_from_the_way_in_to_every_maze_cell_and_pack() => Walks(5, 3);

    [GeneratedRegex("^(stub|drift|bend|tee|cross)_maze[a-z0-9]*-")]
    private static partial Regex MazeRoom();

    private static void Walks(int depth, int seed)
    {
        Nav3dReader nav = Nav3dReader.Open(File.ReadAllBytes(BuiltDescentLevelFactAttribute.Nav3d(depth, seed)));
        var reached = Reached(nav, nav.Poi(nav.UpArrivalPoi).Position, GiantWidth, GiantHeight);

        var cells = Enumerable.Range(0, nav.Columns * nav.Rows).Where(c => nav.Cell(c).Room is { } room && MazeRoom().IsMatch(room)).ToArray();
        var packs = Enumerable.Range(0, nav.PoiCount).Select(nav.Poi).Where(p => p.Type == "descent_spawn_pack" && cells.Contains(CellOf(nav, p.Position))).ToArray();
        var missed = cells.Select(c => Centre(nav, c)).Where(p => !reached.Contains(nav.FindLeaf(p))).Select(p => $"cell centre {p}")
            .Concat(packs.Where(p => !reached.Contains(nav.FindLeaf(new Vec3(p.Position.X, p.Position.Y, p.Position.Z + 1)))).Select(p => $"pack at {p.Position}")).ToArray();

        Assert.InRange(cells.Length, 80, 100);
        Assert.NotEmpty(packs);
        Assert.True(missed.Length == 0, $"{missed.Length} not reached: {string.Join("; ", missed.Take(10))}");
    }

    [BuiltDescentLevelFact(5, 1)]
    public void A_hull_wider_than_the_doorways_stays_in_the_maze_cell_it_starts_in()
    {
        // The control: a 200-wide hull stands in a cell's 256 corridor but passes no doorway (192 between the frame's
        // posts), so the walk is held by the maze's real widths.
        Nav3dReader nav = Nav3dReader.Open(File.ReadAllBytes(BuiltDescentLevelFactAttribute.Nav3d(5, 1)));
        var cells = Enumerable.Range(0, nav.Columns * nav.Rows).Where(c => nav.Cell(c).Room is { } room && MazeRoom().IsMatch(room)).ToArray();
        var reached = Reached(nav, Centre(nav, cells[0]), 200, GiantHeight);

        Assert.Contains(nav.FindLeaf(Centre(nav, cells[0])), reached);
        Assert.DoesNotContain(cells.Skip(1), c => reached.Contains(nav.FindLeaf(Centre(nav, c))));
    }

    private static int CellOf(Nav3dReader nav, Vec3 p) =>
        (int)((p.Y - nav.Origin.Y) / nav.CellSize) * nav.Columns + (int)((p.X - nav.Origin.X) / nav.CellSize);

    private static Vec3 Centre(Nav3dReader nav, int cell) =>
        new(nav.Origin.X + (cell % nav.Columns + 0.5f) * nav.CellSize, nav.Origin.Y + (cell / nav.Columns + 0.5f) * nav.CellSize, nav.Origin.Z + 17);

    /// <summary>The leaves a hull walks to from <paramref name="origin"/> on steps of 18, the named brushes but the exit cover blocking.</summary>
    internal static HashSet<int> Reached(Nav3dReader nav, Vec3 origin, float width, float height)
    {
        var blocking = new bool[nav.ObstacleCount];
        for (int i = 0; i < blocking.Length; i++)
            blocking[i] = nav.Obstacle(i).Name is { } n && n != "descent_boss_exit";
        var seen = new HashSet<int>();
        int start = nav.FindLeaf(new Vec3(origin.X, origin.Y, origin.Z + 1));
        if (start < 0 || !nav.StandableAtCentre(start, width, height, Nav3dClipClass.Npc, blocking))
            return seen;
        var queue = new Queue<int>([start]);
        seen.Add(start);
        while (queue.Count > 0)
        {
            int l = queue.Dequeue();
            foreach (Nav3dNeighbour nb in nav.Neighbours(l))
                if (!seen.Contains(nb.Leaf) && nav.StandableAtCentre(nb.Leaf, width, height, Nav3dClipClass.Npc, blocking)
                    && Math.Abs(nav.Leaf(nb.Leaf).FloorZ(Nav3dClipClass.Npc) - nav.Leaf(l).FloorZ(Nav3dClipClass.Npc)) <= 18)
                {
                    seen.Add(nb.Leaf);
                    queue.Enqueue(nb.Leaf);
                }
        }

        return seen;
    }
}
