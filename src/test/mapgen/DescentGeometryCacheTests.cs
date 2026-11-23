using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dresser's geometry answers as it did before it was made fast (PR #43):
/// a face's plane is worked out when its polygon is set, a giant's flood reads
/// one mask per walk, and a ray skips the brushes whose box it passes beside.
/// </summary>
public sealed class DescentGeometryCacheTests
{
    private static readonly RoomTheme Theme = ActOne.Collar;
    private static readonly RoomKit Kit = RoomKit.Normal;

    private static Room Drift(params Brush[] boxes)
    {
        RoomBuilder b = new RoomBuilder("d_a", Kit, Theme).WithHeight(256).WithWidth(256).WithDoors(CellFace.West, CellFace.East);
        foreach (Brush box in boxes)
            b = b.WithBox(box.Mins, box.Maxs);
        return b.Build();
    }

    private static Brush Box(float x0, float y0, float z0, float x1, float y1, float z1) =>
        Brush.Box(new Point(x0, y0, z0), new Point(x1, y1, z1), Theme.Block);

    [Fact]
    public void A_face_copied_with_a_new_polygon_takes_that_polygons_plane()
    {
        BrushFace top = Box(0, 0, 0, 64, 64, 32).Faces.Single(f => f.Normal.Z > 0.5f);

        BrushFace flipped = top with { Polygon = [.. top.Polygon.Reverse()] };
        BrushFace raised = top with { Polygon = [.. top.Polygon.Select(p => p with { Z = p.Z + 10 })] };

        Assert.Equal(new Point(0, 0, 1), top.Normal);
        Assert.Equal(32f, top.Distance);
        Assert.Equal(new Point(0, 0, -1), flipped.Normal);
        Assert.Equal(42f, raised.Distance);
    }

    /// <summary>The flood <see cref="FloorPlan.GiantReached"/> stands for, column by column through <see cref="FloorPlan.GiantFits"/>.</summary>
    private static HashSet<(int I, int J)> FloodByFits(FloorPlan plan, Room room, ISet<(int, int)> blocked)
    {
        var reached = new HashSet<(int, int)>();
        foreach (var (face, offset) in room.Sockets.Select(s => (s.Face, s.Offset)))
        {
            var start = plan.DoorColumn(face, offset);
            if (!plan.GiantFits(start.I, start.J, blocked))
                continue;
            var queue = new Queue<(int, int)>([start]);
            var seen = new HashSet<(int, int)> { start };
            while (queue.Count > 0)
            {
                var (i, j) = queue.Dequeue();
                foreach (var next in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) })
                    if (!seen.Contains(next) && plan.GiantFits(next.Item1, next.Item2, blocked))
                    {
                        seen.Add(next);
                        queue.Enqueue(next);
                    }
            }

            reached.UnionWith(seen);
        }

        return reached;
    }

    [Fact]
    public void A_giants_flood_is_the_columns_its_fit_reaches_from_the_doors_whatever_is_blocked()
    {
        Room room = Drift(Box(400, 500, Kit.Sill, 440, 540, Kit.Sill + 200));
        var plan = new FloorPlan(room);
        var random = new Random(43);
        var blockings = new List<HashSet<(int, int)>> { new() };
        for (int n = 0; n < 12; n++)
            blockings.Add([.. Enumerable.Range(0, 1 + n * 4).Select(_ => (random.Next(plan.Size), random.Next(plan.Size)))]);
        // A wall of blocked columns across the drift, which parts its doors.
        blockings.Add([.. Enumerable.Range(0, plan.Size).Select(j => (plan.Size / 2, j))]);

        var floods = blockings.Select(b => (Mask: plan.GiantReached(b), Fits: FloodByFits(plan, room, b))).ToArray();

        Assert.Contains(floods, f => f.Fits.Count > 0);
        Assert.Contains(floods, f => f.Fits.Count < floods[0].Fits.Count);
        Assert.All(floods, f => Assert.True(f.Mask.SetEquals(f.Fits), $"{f.Mask.Count} columns by the mask, {f.Fits.Count} by the fit"));
    }

    [Fact]
    public void A_ray_meets_the_box_ahead_of_it_and_passes_one_beside_it()
    {
        // A pillar in the middle of a 256-wide drift; rays along +x at the pillar's height.
        Room room = Drift();
        var pillar = Box(500, 500, Kit.Sill, 540, 540, Kit.Sill + 100);
        var solids = new RoomSolids(room, [pillar]);
        var bare = new RoomSolids(room);
        float z = Kit.Sill + 50;

        float? toPillar = solids.Ray(new Point(300, 520, z), 1, 0);
        float? grazing = solids.Ray(new Point(300, 539.5f, z), 1, 0);
        float? beside = solids.Ray(new Point(300, 540.5f, z), 1, 0);
        float? besideBare = bare.Ray(new Point(300, 540.5f, z), 1, 0);
        float? behind = solids.Ray(new Point(600, 520, z), 1, 0);
        float? behindBare = bare.Ray(new Point(600, 520, z), 1, 0);
        float? inside = solids.Ray(new Point(520, 520, z), 1, 0);

        Assert.Equal(200f, toPillar);
        Assert.Equal(200f, grazing);
        Assert.Equal(besideBare, beside);
        Assert.Equal(behindBare, behind);
        Assert.Null(inside);
    }
}
