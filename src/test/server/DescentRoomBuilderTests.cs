using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// One Descent room as <see cref="RoomBuilder"/> writes it, room-local: the
/// sealed shell, its doors and caps, its height, and a transition room's
/// pieces.
/// </summary>
public sealed class DescentRoomBuilderTests
{
    /// <summary>A kit of whole 32s (every coordinate the flood fill below walks is one): the plan's first door and wall.</summary>
    private static readonly RoomKit Kit = new("wide", 1024, 256, 192, 32);
    // Every material distinct, so a fact can tell a ceiling from a wall by it.
    private static readonly RoomTheme Theme = new("t/floor", "t/wall", "t/ceiling", "t/block", "t/plug", "t/trigger");

    private static RoomBuilder New(string name = "r") => new(name, Kit, Theme);

    private static Room Drift(int height = 256) => New("drift").WithHeight(height).WithWidth(256)
        .WithDoors(CellFace.East, CellFace.West).Build();

    private static IEnumerable<Brush> Plugs(Room room) => room.World.Where(b => b.Material == Theme.Plug.Material);

    [Fact]
    public void A_door_gets_a_plug_brush_exactly_the_kits_opening_on_its_wall()
    {
        Room room = New().WithDoors(CellFace.North).Build();
        var plug = Kit.PlugBox(CellFace.North);

        Brush only = Assert.Single(Plugs(room));
        Assert.Equal((plug.Mins, plug.Maxs), (only.Mins, only.Maxs));
    }

    [Fact]
    public void A_wall_without_a_door_is_capped_with_no_plug()
    {
        Room room = New().WithDoors(CellFace.East).Build();
        float c = Kit.Cell, t = Kit.WallDepth;

        Assert.Single(Plugs(room));
        Assert.Contains(room.World, b => b.Material == Theme.Wall.Material && b.Mins.X == 0 && b.Maxs.X == t
                                         && b.Mins.Y <= t && b.Maxs.Y >= c - t && b.Mins.Z == t && b.Maxs.Z == c - t);
    }

    [Fact]
    public void A_corridors_door_wall_has_no_jamb_when_it_is_the_doors_width()
    {
        Room drift = Drift();
        float c = Kit.Cell, t = Kit.WallDepth;

        // On the east face's slab, between the corridor's side walls, only the plug.
        Assert.DoesNotContain(drift.World, b => b.Mins.X == c - t && b.Material == Theme.Wall.Material
                                               && b.Mins.Y >= Kit.DoorLow && b.Maxs.Y <= Kit.DoorHigh);
    }

    [Fact]
    public void A_full_width_rooms_door_wall_has_a_jamb_each_side_of_the_plug()
    {
        Room room = New().WithDoors(CellFace.East).Build();
        float c = Kit.Cell, t = Kit.WallDepth;

        var slab = room.World.Where(b => b.Mins.X == c - t && b.Maxs.X == c && b.Material == Theme.Wall.Material).ToArray();
        Assert.Contains(slab, b => b.Maxs.Y == Kit.DoorLow);
        Assert.Contains(slab, b => b.Mins.Y == Kit.DoorHigh);
    }

    [Fact]
    public void A_low_rooms_ceiling_slab_is_at_its_own_height()
    {
        Room drift = Drift(256);

        Assert.Contains(drift.World, b => b.Mins.Z == 224 && b.Maxs.Z == 256 && b.Material == Theme.Ceiling.Material);
        Assert.All(drift.World, b => Assert.True(b.Maxs.Z <= 256));
    }

    [Fact]
    public void A_room_taller_than_its_door_gets_a_lintel_from_the_doors_head_to_its_ceiling()
    {
        Room hall = New().WithHeight(1024).WithDoors(CellFace.West).Build();

        Assert.Contains(hall.World, b => b.Mins.X == 0 && b.Maxs.X == Kit.WallDepth && b.Mins.Y == Kit.DoorLow
                                         && b.Maxs.Y == Kit.DoorHigh && b.Mins.Z == Kit.Head && b.Maxs.Z == 1024 - Kit.WallDepth);
    }

    [Fact]
    public void A_room_exactly_the_doors_height_has_no_lintel()
    {
        Room drift = Drift(Kit.LeastHeight);

        Assert.DoesNotContain(drift.World, b => b.Mins.Z == Kit.Head && b.Material == Theme.Wall.Material);
    }

    [Fact]
    public void A_room_lower_than_the_door_kit_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => New().WithHeight(Kit.LeastHeight - 16).Build());
    }

    [Fact]
    public void A_height_that_is_not_whole_navigation_voxels_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => New().WithHeight(520).Build());
    }

    [Fact]
    public void A_corridor_narrower_than_the_door_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => New().WithWidth(Kit.DoorWidth - 32).Build());
    }

    [Fact]
    public void Two_doors_on_one_wall_are_refused()
    {
        Assert.Throws<ArgumentException>(() => New().WithDoors(CellFace.East, CellFace.East));
    }

    [Fact]
    public void A_box_outside_the_cell_is_refused()
    {
        Assert.Throws<ArgumentException>(() => New().WithBox(new Point(1000, 0, 32), new Point(1040, 32, 64)).Build());
    }

    [Fact]
    public void A_corridors_walls_hug_it_and_leave_the_rest_of_the_cell_empty()
    {
        Room drift = Drift();
        float a = (Kit.Cell - 256) / 2f - Kit.WallDepth, b = (Kit.Cell + 256) / 2f + Kit.WallDepth;

        var walls = drift.World.Where(x => x.Material != Theme.Floor.Material && x.Material != Theme.Ceiling.Material).ToArray();
        Assert.NotEmpty(walls);
        Assert.All(walls, w => Assert.True(w.Mins.Y >= a && w.Maxs.Y <= b, $"({w.Mins})-({w.Maxs}) is outside the corridor"));
    }

    [Fact]
    public void A_drift_is_its_slabs_walls_and_plugs_and_the_floor_round_its_two_doorway_dips()
    {
        // The ceiling, two side walls and two plugs; the floor's base and the five pieces of its top round the two dips.
        Assert.Equal(11, Drift().World.Count);
    }

    public static TheoryData<string> Shapes => ["drift", "bend", "tee", "cross", "stub", "hall", "chamber"];

    private static Room Shape(string shape) => shape switch
    {
        "drift" => Drift(),
        "bend" => New().WithHeight(256).WithWidth(256).WithDoors(CellFace.East, CellFace.North).Build(),
        "tee" => New().WithHeight(256).WithWidth(384).WithDoors(CellFace.East, CellFace.West, CellFace.South).Build(),
        "cross" => New().WithHeight(192 + 64).WithWidth(256)
            .WithDoors(CellFace.East, CellFace.West, CellFace.North, CellFace.South).Build(),
        "stub" => New().WithHeight(256).WithWidth(256).WithDoors(CellFace.South).Build(),
        "hall" => New().WithHeight(1024).WithDoors(CellFace.East, CellFace.West).Build(),
        _ => New().WithHeight(512).WithDoors(CellFace.North, CellFace.South, CellFace.West).Build(),
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_shape_is_sealed_with_its_plugs_in(string shape)
    {
        Room room = Shape(shape);

        var fill = Flood(room.World, Kit.Cell, room.Height);
        Assert.False(fill.Leaked, $"{shape} leaks");
        Assert.True(fill.Reached > 0);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void No_two_brushes_of_a_shell_overlap(string shape)
    {
        var boxes = Shape(shape).World;

        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(Overlap(boxes[i], boxes[j]), $"{shape}: ({boxes[i].Mins})-({boxes[i].Maxs}) overlaps ({boxes[j].Mins})-({boxes[j].Maxs})");
    }

    [Fact]
    public void A_room_with_a_plug_taken_out_leaks()
    {
        // The seal fact's negative control: the same fill sees the opening.
        Room drift = Drift();
        var open = drift.World.Where(b => b != Plugs(drift).First()).ToList();

        Assert.True(Flood(open, Kit.Cell, drift.Height).Leaked);
    }

    [Fact]
    public void An_up_room_has_a_named_transition_volume_a_button_that_fires_it_and_an_arrival()
    {
        Room up = New().WithDoors(CellFace.East).WithRole(RoomRole.Up, new Point(256, 512, 32), 90).Build();

        RoomEntity volume = Assert.Single(up.Entities, e => e.ClassName == "trigger_room_transition");
        Assert.Equal(RoomBuilder.VolumeName, volume.Key("targetname"));
        RoomEntity button = Assert.Single(up.Entities, e => e.ClassName == "func_button");
        Assert.Contains(button.Wires, w => w.Output == "OnPressed" && w.Target == RoomBuilder.VolumeName && w.Input == "Transition");
        RoomEntity arrival = Assert.Single(up.Entities, e => e.Key("poi_type") == "arrival");
        Assert.Equal((new Point(256, 512, 32), "0 90 0"), (arrival.Origin, arrival.Key("angles")));
    }

    [Fact]
    public void A_down_room_fires_its_transition_from_a_hallway_trigger()
    {
        Room down = New().WithDoors(CellFace.East).WithRole(RoomRole.Down, new Point(256, 512, 32), 0).Build();

        RoomEntity hall = Assert.Single(down.Entities, e => e.ClassName == "trigger_once");
        Assert.Contains(hall.Wires, w => w.Output == "OnStartTouch" && w.Target == RoomBuilder.VolumeName && w.Input == "Transition");
        Assert.DoesNotContain(down.Entities, e => e.ClassName == "func_button");
    }

    [Fact]
    public void A_room_without_a_role_has_no_transition_pieces()
    {
        Room room = New().WithDoors(CellFace.East).Build();

        Assert.Equal(RoomRole.None, room.Role);
        Assert.All(room.Entities, e => Assert.Contains(e.ClassName, (string[])["func_brush", "prop_static"]));
    }

    [Fact]
    public void An_arrival_outside_the_room_is_refused()
    {
        Assert.Throws<ArgumentException>(() => New().WithHeight(256).WithRole(RoomRole.Up, new Point(512, 512, 200), 0).Build());
    }

    [Fact]
    public void A_light_carries_its_brightness_as_the_fourth_number_of_light()
    {
        Room room = New().WithLight(new Point(512, 512, 900), 450).Build();

        Assert.Equal("255 214 160 450", Assert.Single(room.Entities).Key("_light"));
    }

    private static bool Overlap(Brush a, Brush b)
        => a.Mins.X < b.Maxs.X && b.Mins.X < a.Maxs.X && a.Mins.Y < b.Maxs.Y && b.Mins.Y < a.Maxs.Y
           && a.Mins.Z < b.Maxs.Z && b.Mins.Z < a.Maxs.Z;

    /// <summary>
    /// A flood fill over the boxes' own lattice (the cells between every
    /// coordinate any box has, so a 2-unit slit is as visible as a room) from
    /// the cell's centre just above the floor: a room leaks when the fill
    /// reaches a cell outside its box.
    /// </summary>
    private static (bool Leaked, int Reached) Flood(IReadOnlyList<Brush> boxes, int cell, int height)
    {
        float[] Axis(Func<Brush, float> lo, Func<Brush, float> hi, int max) =>
            [.. boxes.SelectMany(b => new[] { lo(b), hi(b) }).Append(0).Append(max).Where(v => v >= 0 && v <= max).Distinct().Order()];
        float[] xs = Axis(b => b.Mins.X, b => b.Maxs.X, cell), ys = Axis(b => b.Mins.Y, b => b.Maxs.Y, cell), zs = Axis(b => b.Mins.Z, b => b.Maxs.Z, height);
        int nx = xs.Length - 1, ny = ys.Length - 1, nz = zs.Length - 1;
        bool Solid(int x, int y, int z)
        {
            float px = (xs[x] + xs[x + 1]) / 2, py = (ys[y] + ys[y + 1]) / 2, pz = (zs[z] + zs[z + 1]) / 2;
            return boxes.Any(b => px > b.Mins.X && px < b.Maxs.X && py > b.Mins.Y && py < b.Maxs.Y && pz > b.Mins.Z && pz < b.Maxs.Z);
        }

        int Index(float[] axis, float v) => Array.FindLastIndex(axis, a => a <= v);
        var start = (Index(xs, cell / 2f), Index(ys, cell / 2f), Index(zs, zs[1] + 1));
        var seen = new HashSet<(int, int, int)> { start };
        var queue = new Queue<(int X, int Y, int Z)>([start]);
        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz)
                return (true, seen.Count);
            foreach (var (dx, dy, dz) in ((int, int, int)[])[(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)])
            {
                var next = (x + dx, y + dy, z + dz);
                bool outside = next.Item1 < 0 || next.Item2 < 0 || next.Item3 < 0 || next.Item1 >= nx || next.Item2 >= ny || next.Item3 >= nz;
                if (seen.Contains(next) || (!outside && Solid(next.Item1, next.Item2, next.Item3)))
                    continue;
                seen.Add(next);
                queue.Enqueue(next);
            }
        }

        return (false, seen.Count);
    }
}
