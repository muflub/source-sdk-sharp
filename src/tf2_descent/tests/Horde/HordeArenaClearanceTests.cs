using SourceSharp.Ai;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Server.Horde.Nav;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A boss arena's corner on a graph of the level's own 16-unit cells, every cell admitting every
/// robot class: a pillar (obstacle 0) and the rubble that lands where it stood (obstacle 1), each
/// box open in the grid. As the derivation gives a cell the first obstacle that reaches it, the
/// ring round the rubble's box is the pillar's where the pillar's box reaches it (on the built
/// level 10 seed 1, rubble 1's south and east sides were pillar 1's). Robots of each class walk
/// round the box from its west to its east.
/// </summary>
internal static class ClearanceArena
{
    public const int Width = 48, Height = 32;
    public const float Cell = 16f, FloorZ = 16f;

    /// <summary>The pillar's box (fallen in these facts) and the rubble's, which reaches past it to the west and north.</summary>
    public static readonly AiVector PillarMins = new(352f, 160f, FloorZ), PillarMaxs = new(512f, 352f, FloorZ + 1248f);

    public static readonly AiVector RubbleMins = new(288f, 176f, FloorZ), RubbleMaxs = new(448f, 400f, FloorZ + 64f);

    /// <summary>How far round its box an obstacle's ring reaches (the game's own body's half-width).</summary>
    private const float Reach = 24f;

    private static readonly ClearanceClasses Every = ClearanceClasses.Stand | ClearanceClasses.Crouch | ClearanceClasses.Hull2 | ClearanceClasses.Hull3
                                                     | ClearanceClasses.Hull4 | ClearanceClasses.Hull5;

    public static HordeNavData Data()
    {
        var b = new NavGraphBuilder();
        var holder = new int[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                b.AddCell(new NavCell(x * Cell, y * Cell, (x + 1) * Cell, (y + 1) * Cell, FloorZ, Every, 0, 1f));
                var c = new AiVector(x * Cell + Cell * 0.5f, y * Cell + Cell * 0.5f, FloorZ);
                holder[y * Width + x] = Within(c, PillarMins, PillarMaxs) ? 0 : Within(c, RubbleMins, RubbleMaxs) ? 1 : -1;
            }
        }

        NavObstacleInfo[] obstacles =
        [
            new(ArenaStage.PillarName(1), NavObstacleKind.Toggle, "func_brush", PillarMins, PillarMaxs),
            new(ArenaStage.RubbleName(1), NavObstacleKind.Toggle, "func_brush", RubbleMins, RubbleMaxs),
        ];
        foreach (var _ in obstacles)
            b.AddObstacle();

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                        continue;
                    b.AddEdge(new CellId(y * Width + x), new CellId(ny * Width + nx), NavEdgeType.Walk, 0f, holder[ny * Width + nx]);
                }
            }
        }

        var nav = new DerivedNav(b.Build(), [], [], obstacles, 0);
        var columns = VoxelColumns.Of(0f, 0f, Cell, Width, Height, (_, _) => [new FreeSpan(FloorZ, 4096f)]);
        return HordeNavData.Of(nav, columns);
    }

    /// <summary>Whether <paramref name="p"/> is within <see cref="Reach"/> of the box on the floor plane.</summary>
    private static bool Within(AiVector p, AiVector mins, AiVector maxs) =>
        p.X > mins.X - Reach && p.X < maxs.X + Reach && p.Y > mins.Y - Reach && p.Y < maxs.Y + Reach;

    public static HordeLevel Level => HordeLevel.Create(1, 10,
        [new HordeRoom(0, new AiVector(0f, 0f, 0f), new AiVector(Width * Cell, Height * Cell, 512f), [])], []);
}

/// <summary>
/// A body's box against an arena brush's: robots' paths keep each class's half-width off a box
/// that blocks (a standing pillar, landed rubble, the exit's cover), as the class's own hull
/// meets it. The step's voxels hold no arena brush, so a path that runs a hull into one has the
/// body stand pressed on its face, its wish into it (live on level 10 seed 1, 2026-11-16: the
/// boss 52 s at the corner of rubble 1, a giant pyro 101 s at its west face).
/// </summary>
public sealed class HordeArenaClearanceTests
{
    /// <summary>A hull of half-width <paramref name="half"/> at a point of the walk is inside <paramref name="mins"/>..<paramref name="maxs"/> by how much (positive: overlapping), worst along the walk, sampled every unit.</summary>
    internal static float WorstOverlap(AiVector from, ReadOnlySpan<AiVector> corners, AiVector mins, AiVector maxs, float half)
    {
        float worst = float.MinValue;
        var at = from;
        foreach (var c in corners)
        {
            int n = Math.Max(1, (int)(c - at).Length2D);
            for (int i = 0; i <= n; i++)
            {
                var p = at + (c - at) * ((float)i / n);
                float dx = MathF.Max(mins.X - p.X, p.X - maxs.X), dy = MathF.Max(mins.Y - p.Y, p.Y - maxs.Y);
                worst = MathF.Max(worst, half - MathF.Max(dx, dy));
            }

            at = c;
        }

        return worst;
    }

    internal static float HalfWidth(ClearanceClass cls) => cls == ClearanceClass.Stand ? HordeHulls.StandWidth * 0.5f : HordeHulls.Width((int)cls - 2) * 0.5f;

    private static (PathAnswer Answer, AiVector[] Corners) Find(HordePaths paths, AiVector from, AiVector to, ClearanceClass cls)
    {
        var corners = new AiVector[HordeRoute.MaxCorners];
        var arrive = new NavEdgeType[HordeRoute.MaxCorners];
        var answer = paths.Find(from, to, cls, HordeRoute.SearchBudget, corners, arrive, out int n, out _);
        return (answer, corners[..n]);
    }

    private static HordePaths Landed(HordeNavData nav)
    {
        var paths = new HordePaths();
        var changes = new Dictionary<string, bool> { [ArenaStage.PillarName(1)] = false, [ArenaStage.RubbleName(1)] = true };
        paths.Ready(nav, ClearanceArena.Level, HordeArenaObstacles.State(nav.Nav, nav.Open, changes, out _));
        return paths;
    }

    /// <summary>South-west of the rubble, and east of it: the way between rounds its south-east corner, the pillar's side.</summary>
    private static readonly AiVector West = new(200f, 100f, ClearanceArena.FloorZ), East = new(500f, 300f, ClearanceArena.FloorZ);

    [Fact]
    public void A_stand_bodys_path_round_landed_rubble_keeps_its_half_width_off_the_side_its_fallen_pillar_held()
    {
        var nav = ClearanceArena.Data();

        var (answer, corners) = Find(Landed(nav), West, East, ClearanceClass.Stand);

        Assert.Equal(PathAnswer.Found, answer);
        float over = WorstOverlap(West, corners, ClearanceArena.RubbleMins, ClearanceArena.RubbleMaxs, HalfWidth(ClearanceClass.Stand));
        Assert.True(over <= 0f, $"a stand body on the path {string.Join(" ", corners)} is {over} units inside the landed rubble");
    }

    [Theory]
    [InlineData(ClearanceClass.Hull2)]
    [InlineData(ClearanceClass.Hull3)]
    [InlineData(ClearanceClass.Hull4)]
    [InlineData(ClearanceClass.Hull5)]
    public void A_giants_path_round_landed_rubble_keeps_its_classs_half_width_off_it(ClearanceClass cls)
    {
        var nav = ClearanceArena.Data();

        var (answer, corners) = Find(Landed(nav), West, East, cls);

        Assert.Equal(PathAnswer.Found, answer);
        float over = WorstOverlap(West, corners, ClearanceArena.RubbleMins, ClearanceArena.RubbleMaxs, HalfWidth(cls));
        Assert.True(over <= 0f, $"a {HordeHulls.NameOf(cls)} hull on the path {string.Join(" ", corners)} is {over} units inside the landed rubble");
    }

    [Fact]
    public void A_giants_path_round_a_standing_pillar_keeps_its_half_width_off_it()
    {
        var nav = ClearanceArena.Data();
        var paths = new HordePaths();
        paths.Ready(nav, ClearanceArena.Level, HordeArenaObstacles.State(nav.Nav, nav.Open, new Dictionary<string, bool>(), out _));
        var from = new AiVector(300f, 100f, ClearanceArena.FloorZ);
        var to = new AiVector(580f, 440f, ClearanceArena.FloorZ);
        var cls = ClearanceClass.Hull5;

        var (answer, corners) = Find(paths, from, to, cls);

        Assert.Equal(PathAnswer.Found, answer);
        float over = WorstOverlap(from, corners, ClearanceArena.PillarMins, ClearanceArena.PillarMaxs, HalfWidth(cls));
        Assert.True(over <= 0f, $"a {HordeHulls.NameOf(cls)} hull on the path {string.Join(" ", corners)} is {over} units inside the standing pillar");
    }

    [Fact]
    public void With_no_arena_brush_blocking_a_giant_plans_as_a_stand_body_does()
    {
        var nav = ClearanceArena.Data();
        var paths = new HordePaths();
        paths.Ready(nav, ClearanceArena.Level);

        var stand = Find(paths, West, East, ClearanceClass.Stand);
        var giant = Find(paths, West, East, ClearanceClass.Hull5);

        Assert.Equal(PathAnswer.Found, giant.Answer);
        Assert.Equal(stand.Corners, giant.Corners);
    }
}
