using System.Globalization;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The texture on every face a room library writes (Phase 0 review, point 1):
/// each face at its surface's scale, projected on the world's axes, with its
/// texture starting at the room's cell, so rooms line up wherever they stand.
/// </summary>
public sealed class DescentRoomTextureTests
{
    private static readonly RoomTheme Theme = new(
        Floor: new("t/floor", 1f), Wall: new("t/wall", 0.5f), Ceiling: "t/ceiling", Block: "t/block", Plug: "t/plug", Trigger: "t/trigger");

    private static Room Hall(string name) => new RoomBuilder(name, RoomKit.Normal, Theme).WithHeight(512)
        .WithDoors(CellFace.East, CellFace.North).WithBrush(Brush.Wedge(new(300, 300, 32), new(556, 428, 96), Compass.East, "t/ramp")).Build();

    private static VmfMap Library() => new RoomLibrary(RoomKit.Normal).Add(Hall("a")).Add(Hall("b")).Build();

    private static (Point Axis, float Offset, float Scale) Parse(string axis)
    {
        string[] parts = axis.Replace("[", "").Replace("]", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float F(int i) => float.Parse(parts[i], CultureInfo.InvariantCulture);
        return (new(F(0), F(1), F(2)), F(3), F(4));
    }

    private static float Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    [Fact]
    public void Every_face_is_written_at_its_surfaces_scale()
    {
        var sides = Library().WorldSolids.SelectMany(s => s.Sides).ToArray();

        Assert.All(sides.Where(s => s.Material == "t/floor"), s => Assert.Equal(1f, Parse(s.UAxis).Scale));
        Assert.All(sides.Where(s => s.Material == "t/wall"), s => Assert.Equal(0.5f, Parse(s.VAxis).Scale));
        Assert.Contains(sides, s => s.Material == "t/wall");
    }

    [Fact]
    public void A_rooms_texture_coordinates_are_the_same_in_whichever_cell_it_stands()
    {
        var solids = Library().WorldSolids;
        int half = solids.Count / 2;

        for (int i = 0; i < half; i++)
            for (int k = 0; k < solids[i].Sides.Count; k++)
            {
                VmfSide a = solids[i].Sides[k], b = solids[half + i].Sides[k];
                foreach (Func<VmfSide, string> axis in (Func<VmfSide, string>[])[s => s.UAxis, s => s.VAxis])
                {
                    var (ua, oa, sa) = Parse(axis(a));
                    var (ub, ob, sb) = Parse(axis(b));
                    Assert.Equal(Dot(a.P0, ua) / sa + oa, Dot(b.P0, ub) / sb + ob, 3);
                }
            }
    }

    [Fact]
    public void Every_faces_texture_axes_are_world_axes_lying_in_its_plane()
    {
        foreach (VmfSide side in Library().WorldSolids.SelectMany(s => s.Sides))
        {
            Point a = side.P0 - side.P1, b = side.P2 - side.P1;
            Point n = new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            float ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
            int dominant = az >= ax && az >= ay ? 2 : ax >= ay ? 0 : 1;
            foreach (Point axis in (Point[])[Parse(side.UAxis).Axis, Parse(side.VAxis).Axis])
            {
                float[] c = [axis.X, axis.Y, axis.Z];
                Assert.Equal(1f, c.Sum(Math.Abs));
                Assert.Equal(0f, c[dominant]);
            }
        }
    }

    [Fact]
    public void The_second_cells_textures_are_offset_back_to_its_corner()
    {
        // The positive arm of the fact above: without the offset the two
        // rooms' coordinates would differ by the cell spacing.
        var solids = Library().WorldSolids;
        VmfSide side = solids[solids.Count / 2].Sides[0];
        var (axis, offset, scale) = Parse(side.UAxis);

        Assert.Equal(-(RoomKit.Normal.Cell + RoomLibrary.DefaultGap) * axis.X / scale, offset);
        Assert.NotEqual(0f, offset);
    }
}
