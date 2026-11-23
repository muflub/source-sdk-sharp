using SourceSharp.MapGen;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The convex brushes the Descent rooms are built from (plan task A4) and
/// the displacement writer (A3): each shape is closed, convex and wound
/// outward as vbsp reads a face, and goes where it says.
/// </summary>
public sealed class DescentBrushShapeTests
{
    private const string M = "t/m";

    /// <summary>The mean of the brush's corners, which a convex brush holds strictly inside.</summary>
    private static Point Centre(Brush b)
    {
        var corners = b.Faces.SelectMany(f => f.Polygon).Distinct().ToArray();
        return new(corners.Average(p => p.X), corners.Average(p => p.Y), corners.Average(p => p.Z));
    }

    /// <summary>vbsp's normal for a side: (p0 - p1) x (p2 - p1).</summary>
    private static Point VbspNormal(VmfSide s)
    {
        Point a = s.P0 - s.P1, b = s.P2 - s.P1;
        return new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }

    private static float Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static TheoryData<string> Shapes => ["box", "wedge-east", "wedge-west", "wedge-north", "wedge-south", "prism", "ring", "stairs"];

    private static IReadOnlyList<Brush> Shape(string name) => name switch
    {
        "box" => [Brush.Box(new(0, 0, 0), new(64, 32, 16), M)],
        "wedge-east" => [Brush.Wedge(new(0, 0, 0), new(128, 64, 32), Compass.East, M)],
        "wedge-west" => [Brush.Wedge(new(0, 0, 0), new(128, 64, 32), Compass.West, M)],
        "wedge-north" => [Brush.Wedge(new(0, 0, 0), new(64, 128, 32), Compass.North, M)],
        "wedge-south" => [Brush.Wedge(new(0, 0, 0), new(64, 128, 32), Compass.South, M)],
        "prism" => [Brush.Prism(100, 100, 48, 12, 0, 200, M)],
        "ring" => Brush.RingWall(512, 512, 320, 352, 16, 32, 512, M),
        _ => Brush.Stairs(new(0, 0, 0), new(64, 192, 96), Compass.North, 6, M),
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_face_vbsp_reads_points_out_of_its_brush(string shape)
    {
        foreach (Brush brush in Shape(shape))
        {
            Point centre = Centre(brush);
            foreach (VmfSide side in brush.ToVmf().Sides)
                Assert.True(Dot(VbspNormal(side), side.P1 - centre) > 0, $"{shape}: a side at ({side.P1}) faces inward");
        }
    }

    [Fact]
    public void A_box_brush_writes_the_planes_and_axes_of_VmfMap_Box()
    {
        Point mins = new(-16, 32, 0), maxs = new(48, 96, 128);
        var mine = Brush.Box(mins, maxs, M).ToVmf();
        var theirs = VmfMap.Box(mins, maxs, M);

        // The same planes (by their vbsp normals and a point) and the same texture axes, side for side.
        Assert.Equal(theirs.Sides.Select(s => (Norm(VbspNormal(s)), s.UAxis, s.VAxis)),
                     mine.Sides.Select(s => (Norm(VbspNormal(s)), s.UAxis, s.VAxis)));

        static Point Norm(Point n)
        {
            float l = MathF.Sqrt(Dot(n, n));
            return new(MathF.Round(n.X / l, 4), MathF.Round(n.Y / l, 4), MathF.Round(n.Z / l, 4));
        }
    }

    [Theory]
    [InlineData(Compass.East, 4, 32, 124, 32)]
    [InlineData(Compass.West, 124, 32, 4, 32)]
    [InlineData(Compass.North, 32, 4, 32, 124)]
    [InlineData(Compass.South, 32, 124, 32, 4)]
    public void A_wedge_is_low_at_one_end_and_high_toward_where_it_rises(Compass rises, float lowX, float lowY, float highX, float highY)
    {
        Brush wedge = Brush.Wedge(new(0, 0, 0), new(128, 128, 64), rises, M);

        Assert.InRange(wedge.Column(lowX, lowY)!.Value.High, 0, 4);
        Assert.InRange(wedge.Column(highX, highY)!.Value.High, 60, 64);
    }

    [Fact]
    public void Stairs_climb_in_equal_whole_rises_to_the_boxs_top()
    {
        var flight = Brush.Stairs(new(0, 0, 32), new(64, 192, 128), Compass.North, 6, M);

        Assert.Equal(6, flight.Count);
        Assert.Equal([48f, 64f, 80f, 96f, 112f, 128f], flight.Select(s => s.Maxs.Z));
        Assert.Equal(128f, flight[^1].Column(32, 190)!.Value.High);
        Assert.Equal(48f, flight[0].Column(32, 2)!.Value.High);
    }

    [Fact]
    public void Stairs_whose_rise_is_not_a_whole_unit_are_refused()
    {
        Assert.Throws<ArgumentException>(() => Brush.Stairs(new(0, 0, 0), new(64, 70, 100), Compass.North, 7, M));
    }

    [Fact]
    public void A_prisms_corners_are_whole_units_on_its_circle()
    {
        Brush pillar = Brush.Prism(200, 300, 64, 10, 32, 160, M);

        Assert.Equal(12, pillar.Faces.Count);
        var corners = pillar.Faces.SelectMany(f => f.Polygon).ToArray();
        Assert.All(corners, p => Assert.Equal((MathF.Round(p.X), MathF.Round(p.Y)), (p.X, p.Y)));
        Assert.All(corners, p => Assert.InRange(MathF.Sqrt((p.X - 200) * (p.X - 200) + (p.Y - 300) * (p.Y - 300)), 63, 65));
    }

    [Fact]
    public void A_ring_walls_segments_share_their_corners_so_the_ring_is_closed()
    {
        var ring = Brush.RingWall(512, 512, 300, 332, 12, 32, 400, M);

        Assert.Equal(12, ring.Count);
        for (int i = 0; i < ring.Count; i++)
        {
            var mine = ring[i].Faces.SelectMany(f => f.Polygon).Select(p => (p.X, p.Y)).ToHashSet();
            var next = ring[(i + 1) % ring.Count].Faces.SelectMany(f => f.Polygon).Select(p => (p.X, p.Y)).ToHashSet();
            Assert.Equal(2, mine.Intersect(next).Count());
        }
    }

    [Fact]
    public void A_ring_wall_leaves_out_the_segments_its_filter_refuses()
    {
        var ring = Brush.RingWall(512, 512, 300, 332, 16, 32, 400, M, keep: i => i % 4 != 0);

        Assert.Equal(12, ring.Count);
    }

    [Fact]
    public void A_face_wound_inward_is_refused()
    {
        Brush box = Brush.Box(new(0, 0, 0), new(32, 32, 32), M);
        var faces = box.Faces.ToArray();
        faces[0] = faces[0] with { Polygon = [.. faces[0].Polygon.Reverse()] };

        Assert.Throws<ArgumentException>(() => new Brush(faces, M));
    }

    [Fact]
    public void A_column_through_a_box_is_its_height_and_one_beside_it_misses()
    {
        Brush box = Brush.Box(new(0, 0, 16), new(32, 32, 48), M);

        Assert.Equal((16f, 48f), box.Column(10, 10));
        Assert.Null(box.Column(40, 10));
    }

    [Fact]
    public void Touching_boxes_do_not_overlap_and_crossing_ones_do()
    {
        Brush box = Brush.Box(new(0, 0, 0), new(32, 32, 32), M);

        Assert.False(box.Overlaps(new(32, 0, 0), new(64, 32, 32)));
        Assert.True(box.Overlaps(new(31, 0, 0), new(64, 32, 32)));
    }

    [Fact]
    public void A_power_four_displacement_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Brush.DisplacedBox(new(0, 0, 0), new(256, 256, 32), 4, (u, v) => 0, M));
    }

    [Fact]
    public void A_displaced_floor_writes_one_row_of_heights_per_grid_row_from_its_function()
    {
        Brush patch = Brush.DisplacedBox(new(0, 0, 0), new(256, 256, 32), 2, (u, v) => 100 * u + 10 * v, M);
        string text = Write(patch);

        Assert.Contains("\"power\" \"2\"", text);
        Assert.Contains("\"startposition\" \"[0 0 32]\"", text);
        Assert.Contains("\"row0\" \"0 25 50 75 100\"", text);
        Assert.Contains("\"row4\" \"10 35 60 85 110\"", text);
    }

    [Fact]
    public void A_displaced_wall_pushes_out_along_its_faces_normal()
    {
        Brush wall = Brush.DisplacedWall(new(0, 0, 0), new(32, 256, 256), Compass.East, 3, (u, v) => 8, M);
        string text = Write(wall);

        Assert.Contains("\"power\" \"3\"", text);
        Assert.Contains("\"row0\" \"1 0 0 1 0 0", text);
    }

    private static string Write(Brush b)
    {
        var map = new VmfMap();
        map.WorldSolids.Add(b.ToVmf());
        return map.Write();
    }
}
