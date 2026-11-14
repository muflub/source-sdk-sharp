using System.Globalization;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dressing lint (task A10): each rule refuses a fixture that breaks
/// it, and each fixture's control (the same room without the bad piece)
/// is clean, so the finding is the piece's.
/// </summary>
public sealed class DescentDressLintTests
{
    private static readonly RoomTheme Theme = ActOne.Collar;
    private static readonly RoomKit Kit = RoomKit.Normal;
    private static float Floor => Kit.Sill;
    private static float M => Kit.Cell / 2f;

    private static PropModel Crate => Theme.Props.Corner[0];

    /// <summary>A chamber with doors west and east and a pack spawn north of its centre.</summary>
    private static Room Chamber(RoomEntry? entry = null) =>
        new RoomBuilder("c_a", Kit, Theme).WithHeight(512).WithDoors(CellFace.West, CellFace.East)
            .WithEntry(entry ?? RoomEntry.Default)
            .WithMarker(MarkerKind.PackSpawn, new(M, M + 256, Floor), radius: 64).Build();

    /// <summary>A drift 256 wide, doors west and east.</summary>
    private static Room Drift() =>
        new RoomBuilder("d_a", Kit, Theme).WithHeight(256).WithWidth(256).WithDoors(CellFace.West, CellFace.East).Build();

    private static RoomEntity Prop(PropModel m, Point at, int yaw = 0, RoomDresser.Role role = RoomDresser.Role.Floor, string cls = "prop_static") =>
        new(cls, at, [new("model", m.Model), new("angles", string.Create(CultureInfo.InvariantCulture, $"0 {yaw} 0")),
                      new("solid", m.Solid ? "6" : "0"), new(RoomDresser.RoleKey, role.ToString())], [], []);

    private static RoomEntity Light(Point at) => new("light", at, [.. Lamp.Default.Keys, new(RoomDresser.RoleKey, "Lamp")], [], []);

    private static Room With(Room room, params RoomEntity[] extra) => room with { Entities = [.. room.Entities.Where(e => e.ClassName != "light"), .. extra] };

    private static IEnumerable<string> Rules(Room built, Room dressed) => DressLint.Check(built, dressed, Theme).Select(f => f.Rule);

    /// <summary>A crate standing on the floor at (x, y).</summary>
    private static RoomEntity CrateAt(float x, float y) => Prop(Crate, new(x, y, Floor - Crate.Mins.Z));

    [Fact]
    public void The_fixture_chamber_with_a_crate_on_open_floor_is_clean()
    {
        Room built = Chamber();

        Assert.Empty(Rules(built, With(built, CrateAt(M - 200, M - 200))));
    }

    [Fact]
    public void Door_clear_refuses_a_crate_in_front_of_a_door()
    {
        Room built = Chamber();

        Assert.Contains(DressLint.DoorClear, Rules(built, With(built, CrateAt(Kit.Cell - Kit.WallDepth - 60, M))));
    }

    [Fact]
    public void Marker_clear_refuses_a_crate_on_a_marker()
    {
        Room built = Chamber();

        Assert.Contains(DressLint.MarkerClear, Rules(built, With(built, CrateAt(M, M + 256))));
    }

    [Fact]
    public void Giant_route_refuses_lockers_across_a_drift()
    {
        Room built = Drift();
        PropModel big = ActOne.Lockers;

        Assert.Contains(DressLint.GiantRoute, Rules(built, With(built, Prop(big, new(M, M, Floor - big.Mins.Z)))));
    }

    [Fact]
    public void Giant_route_lets_a_crate_stand_against_a_drifts_wall()
    {
        Room built = Drift();

        Assert.DoesNotContain(DressLint.GiantRoute, Rules(built, With(built, CrateAt(M, M - 128 + 30))));
    }

    [Fact]
    public void Jump_air_refuses_a_crate_on_a_mezzanine()
    {
        Room built = new RoomBuilder("c_a", Kit, Theme).WithHeight(512).WithDoors(CellFace.West, CellFace.East)
            .Mezzanine(CellFace.South, 200, 584, depth: 192).Build();
        float deck = built.World.Where(b => b.Mins.Y < 300 && b.Maxs.Z < 200 && b.Maxs.Z > Floor + 50).Max(b => b.Maxs.Z);

        Assert.Contains(DressLint.JumpAir, Rules(built, With(built, Prop(Crate, new(392, 100, deck - Crate.Mins.Z)))));
    }

    [Fact]
    public void Jump_air_refuses_a_crate_on_a_jump_rooms_open_floor()
    {
        Room built = Chamber(new RoomEntry([], Jump: true));

        Assert.Contains(DressLint.JumpAir, Rules(built, With(built, CrateAt(M - 200, M - 200))));
    }

    [Fact]
    public void In_brush_refuses_a_crate_through_a_wall()
    {
        Room built = Chamber();

        Assert.Contains(DressLint.InBrush, Rules(built, With(built, CrateAt(M - 200, Kit.WallDepth + 10))));
    }

    [Fact]
    public void Supported_refuses_a_crate_in_mid_air()
    {
        Room built = Chamber();

        Assert.Contains(DressLint.Supported, Rules(built, With(built, Prop(Crate, new(M - 200, M - 200, Floor + 200)))));
    }

    [Fact]
    public void Ceiling_run_refuses_a_pipe_hung_below_head_height()
    {
        Room built = Chamber();
        PropModel pipe = ActOne.WhitePipe;
        float y = Kit.Cell - Kit.WallDepth - pipe.Maxs.Y - 1;

        Assert.Contains(DressLint.CeilingRun, Rules(built, With(built, Prop(pipe, new(M - 300, y, Floor + 120), role: RoomDresser.Role.CeilingRun))));
    }

    [Fact]
    public void Static_only_refuses_a_lantern_as_a_static_prop()
    {
        Room built = Chamber();
        PropModel lantern = ActOne.Lantern;
        RoomTheme theme = Theme with { Lamps = Theme.Lamps! with { Fixture = lantern } };
        Room dressed = With(built, Prop(lantern, new(M - 200, M - 200, Floor), role: RoomDresser.Role.Lamp), Light(new(M - 200, M - 200, Floor + 24)));

        Assert.Contains(DressLint.StaticOnly, DressLint.Check(built, dressed, theme).Select(f => f.Rule));
    }

    [Fact]
    public void Static_only_refuses_a_dynamic_prop_over_the_themes_budget()
    {
        Room built = Chamber();
        PropModel lantern = ActOne.Lantern;
        RoomTheme theme = Theme with { Lamps = Theme.Lamps! with { Fixture = lantern }, DynamicBudget = 0 };
        Room dressed = With(built, Prop(lantern, new(M - 200, M - 200, Floor), role: RoomDresser.Role.Lamp, cls: "prop_dynamic"), Light(new(M - 200, M - 200, Floor + 24)));

        Assert.Contains(DressLint.StaticOnly, DressLint.Check(built, dressed, theme).Select(f => f.Rule));
    }

    [Fact]
    public void Lamp_light_refuses_a_lamp_that_gives_no_light()
    {
        Room built = Chamber();
        PropModel lamp = Theme.Lamps!.Fixture;

        Assert.Contains(DressLint.LampLight, Rules(built, With(built, Prop(lamp, new(M - 200, M - 200, 504 - lamp.Maxs.Z), role: RoomDresser.Role.Lamp))));
    }

    [Fact]
    public void Lamp_light_refuses_a_light_with_no_lamp()
    {
        Room built = Chamber();

        Assert.Contains(DressLint.LampLight, Rules(built, With(built, Light(new(M - 200, M - 200, 300)))));
    }

    [Fact]
    public void Lamp_light_refuses_a_shapes_light_left_in_a_themed_room()
    {
        Room built = new RoomBuilder("c_a", Kit, Theme).WithHeight(512).WithDoors(CellFace.West, CellFace.East).WithLamp(new(M, M, 400)).Build();

        Assert.Contains(DressLint.LampLight, Rules(built, built));
    }

    /// <summary>A drift 256 wide with a rail 8 high and 8 wide along it, a giant's half-width off its middle.</summary>
    private static Room RailedDrift() =>
        new RoomBuilder("d_a", Kit, Theme).WithHeight(256).WithWidth(256).WithDoors(CellFace.West, CellFace.East)
            .WithBox(new Point(Kit.WallDepth + RoomBuilder.DoorApproach, M + 40, Floor), new Point(Kit.Cell - Kit.WallDepth - RoomBuilder.DoorApproach, M + 48, Floor + 8)).Build();

    [Fact]
    public void A_rail_a_step_high_is_floor_a_giant_walks_over()
    {
        // The wide drift's tracks once split its halves into strips narrower than a giant.
        var plan = new FloorPlan(RailedDrift());
        var (i, j) = FloorPlan.ColumnOf(M, M + 44);

        Assert.True(plan.IsFloor(i, j));
        Assert.False(plan.RouteBroken());
    }

    [Fact]
    public void A_kerb_above_a_step_is_not_floor()
    {
        Room built = new RoomBuilder("d_a", Kit, Theme).WithHeight(256).WithWidth(256).WithDoors(CellFace.West, CellFace.East)
            .WithBox(new Point(400, M - 128, Floor), new Point(432, M + 128, Floor + RoomLint.Step + 6)).Build();
        var (i, j) = FloorPlan.ColumnOf(416, M);

        Assert.False(new FloorPlan(built).IsFloor(i, j));
    }

    /// <summary>A one-door chamber whose vault (west) opens off its body through a mouth 160 wide.</summary>
    private static Room Vault() =>
        new RoomBuilder("v_a", Kit, Theme).WithHeight(512).WithDoors(CellFace.East)
            .WithOutline(new Rect(512, 256, Kit.Cell - Kit.WallDepth, 768), new Rect(160, 256, 432, 768), new Rect(400, M - 80, 544, M + 80))
            .Build();

    [Fact]
    public void Cut_off_refuses_crates_that_close_a_vaults_mouth()
    {
        Room built = Vault();
        Room dressed = With(built, CrateAt(472, M - 56), CrateAt(472, M + 56));

        Assert.Contains(DressLint.CutOff, Rules(built, dressed));
    }

    [Fact]
    public void Cut_off_lets_a_crate_stand_in_the_vault()
    {
        Room built = Vault();

        Assert.DoesNotContain(DressLint.CutOff, Rules(built, With(built, CrateAt(240, 320))));
    }

    [Fact]
    public void Supported_lets_a_lamp_stand_on_its_crate()
    {
        Room built = Chamber();
        PropModel lantern = ActOne.Lantern;
        float top = Floor - Crate.Mins.Z + Crate.Maxs.Z;
        RoomEntity lamp = Prop(lantern, new(300, 300, top - lantern.Mins.Z), role: RoomDresser.Role.Lamp, cls: "prop_dynamic");

        Assert.DoesNotContain(DressLint.Supported, Rules(built, With(built, CrateAt(300, 300), lamp, Light(new(300, 300, top + 40)))));
    }
}
