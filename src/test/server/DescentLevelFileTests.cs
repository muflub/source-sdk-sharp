using SourceSharp.Host.Contracts;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The level file <c>descent-level</c> writes is the host's: row 0 is the south
/// row and is written last, a rotation is counter-clockwise degrees written as
/// <c>@deg</c>, columns are padded to their widest cell.
/// </summary>
public sealed class DescentLevelFileTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    /// <summary>
    /// A 2x2 checked by hand: south-west a room facing east, south-east one
    /// turned a quarter counter-clockwise, north-west one turned three quarters,
    /// north-east empty.
    /// </summary>
    private static LevelPlan TwoByTwo() => new(2, 2,
    [
        new("a_x-e-l", 0, 0, 0, "up"),
        new("b_x-w-l", 0, 1, 90, "path"),
        new("c_y-s-l", 1, 0, 270, "down"),
    ], "a_x-e-l", "c_y-s-l");

    [Fact]
    public void The_north_row_is_written_first_and_the_south_row_last()
    {
        string text = DescentLevel.Write(TwoByTwo(), "lib", null);

        Assert.Equal(
            "library: lib.vmf\nrows: 2\ncolumns: 2\ngrid:\n"
            + "  - [c_y-s-l@270, ~]\n"
            + "  - [a_x-e-l,     b_x-w-l@90]\n",
            text);
    }

    [Fact]
    public void A_negative_quarter_turn_is_written_as_its_counter_clockwise_degrees()
    {
        LevelPlan plan = new(1, 1, [new("a_x-e-l", 0, 0, -90, "up")], "a_x-e-l", "");

        Assert.EndsWith("  - [a_x-e-l@270]\n", DescentLevel.Write(plan, "lib", null), StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosts_sample_plan_is_written_as_the_hosts_fixture()
    {
        // TF2.Descent.MapForge.Tests: Samples.SampleLevelPlan and Fixtures/levels/sample-plan.yaml (host 9a70c1c).
        LevelPlan plan = new(3, 3,
        [
            new("corner", 0, 0, 90, "fill"), new("corner", 0, 1, 0, "fill"), new("corner", 0, 2, 90, "fill"),
            new("tee", 1, 0, 270, "fill"), new("cross", 1, 1, 0, "fill"), new("tee", 1, 2, 90, "fill"),
            new("end", 2, 0, 270, "up"), new("hall", 2, 1, 90, "fill"), new("corner", 2, 2, 270, "down"),
        ], "end", "corner");

        Assert.Equal(
            "library: crypt.vmf\nrows: 3\ncolumns: 3\ngrid:\n"
            + "  - [end@270,   hall@90, corner@270]\n"
            + "  - [tee@270,   cross,   tee@90]\n"
            + "  - [corner@90, corner,  corner@90]\n",
            DescentLevel.Write(plan, "crypt", null));
    }

    [Fact]
    public void The_hosts_rules_plan_fixture_with_its_keys_is_written_as_the_hosts()
    {
        // Fixtures/levels/fake-rules-depth3-seed42.yaml (host 9a70c1c).
        LevelPlan plan = new(1, 3, [new("end", 0, 0, 0, "up"), new("cross", 0, 1, 90, "path"), new("corner", 0, 2, 0, "down")], "end", "corner");

        Assert.Equal(
            "library: crypt.vmf\nrows: 1\ncolumns: 3\nup_map: tf2_descent-2\ndown_map: tf2_descent-4\ngrid:\n  - [end, cross@90, corner]\n",
            DescentLevel.Write(plan, "crypt", LevelKeys.ForDepth(3)));
    }

    [Fact]
    public void The_first_level_goes_up_to_the_town()
    {
        Assert.Equal(new LevelKeys("tf2_descent_town", "tf2_descent-2"), LevelKeys.ForDepth(1));
    }

    [Fact]
    public void The_bottom_level_has_no_way_down_and_says_so_before_its_way_up()
    {
        string text = DescentLevel.Write(new(1, 1, [new("a_x-e-l", 0, 0, 0, "up")], "a_x-e-l", ""), "lib", LevelKeys.ForDepth(15));

        Assert.Contains("columns: 1\ndown: none\nup_map: tf2_descent-14\ngrid:\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_rooms_in_one_cell_are_refused()
    {
        LevelPlan plan = new(1, 1, [new("a_x-e-l", 0, 0, 0, "up"), new("b_x-w-l", 0, 0, 0, "down")], "a_x-e-l", "b_x-w-l");

        Assert.Throws<ArgumentException>(() => DescentLevel.Write(plan, "lib", null));
    }

    [Fact]
    public void A_placement_off_the_grid_is_refused()
    {
        LevelPlan plan = new(1, 1, [new("a_x-e-l", 1, 0, 0, "up")], "a_x-e-l", "");

        Assert.Throws<ArgumentException>(() => DescentLevel.Write(plan, "lib", null));
    }

    [Fact]
    public void A_manifest_is_read_back_as_the_layouts_rooms_in_name_order()
    {
        var rooms = RoomCatalogue.Rooms(RoomCatalogue.Demonstration, RoomKit.Normal, RoomTheme.Grey, 0);
        var (library, infos) = DescentLevel.ReadManifest(LibraryManifestWriter.Write("lib_x", rooms));

        Assert.Equal("lib_x", library);
        Assert.Equal(rooms.Select(r => r.Name).Order(StringComparer.Ordinal), infos.Select(i => i.Name));
        RoomInfo hall = Assert.Single(infos, i => i.Name.StartsWith("gallery_b-", StringComparison.Ordinal));
        Room built = RoomCatalogue.Named(rooms, "gallery_b");
        Assert.Equal((built.Entry.Weight, built.Entry.MinDepth, built.Entry.MaxDepth), (hall.Weight, hall.MinDepth, hall.MaxDepth));
    }

    private static string CameraManifest() => LibraryManifestWriter.Write("lib", RoomCatalogue.Rooms(RoomCatalogue.Mine, RoomKit.Normal, RoomTheme.Grey, 0));

    /// <summary>A manifest without its rooms' own cameras, as an older library wrote it: the cameras fall back to the door line.</summary>
    private static string WithoutCameras(string json) => System.Text.RegularExpressions.Regex.Replace(json, @",\s*""camera"":\s*\[[^\]]*\]", "");

    [Fact]
    public void A_preview_gets_one_camera_per_family_placed_labelled_with_the_room()
    {
        var plan = new LevelPlan(1, 3, [new("drift_a-ew-l", 0, 0, 0, ""), new("drift_b-ew-l", 0, 1, 0, ""), new("gallery_b-ew-h-j.b1.b2", 0, 2, 0, "")], "", "");

        var cameras = DescentLevel.Cameras(plan, CameraManifest());

        Assert.Equal(2, cameras.Count);
        Assert.EndsWith(" drift_a-ew-l", cameras[0], StringComparison.Ordinal);
        Assert.EndsWith(" gallery_b-ew-h-j.b1.b2", cameras[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_rooms_camera_stands_inside_its_first_door_at_eye_height_looking_at_its_focus()
    {
        // drift_a's first door (the manifest lists east first) is east and its focus the cell's middle, 512 away: in the cell at
        // column 2 the camera stands 96 in from x = 3072 (448 short of the focus would be nearer the door), facing west.
        var plan = new LevelPlan(1, 3, [new("drift_a-ew-l", 0, 2, 0, "")], "", "");

        string[] camera = DescentLevel.Cameras(plan, WithoutCameras(CameraManifest()))[0].Split(' ');

        Assert.Equal(["2976", "512", (RoomKit.Normal.Sill + 64).ToString(System.Globalization.CultureInfo.InvariantCulture), "12", "180"], camera[..5]);
    }

    [Fact]
    public void A_turned_rooms_camera_turns_with_it()
    {
        // A quarter turn counter-clockwise takes the east door to the north: in the cell at row 1 the camera looks south.
        var plan = new LevelPlan(2, 1, [new("drift_a-ew-l", 1, 0, 90, "")], "", "");

        string[] camera = DescentLevel.Cameras(plan, WithoutCameras(CameraManifest()))[0].Split(' ');

        Assert.Equal(["512", "1952", "270"], [camera[0], camera[1], camera[4]]);
    }

    [Fact]
    public void A_rooms_own_camera_is_turned_and_placed_with_the_room()
    {
        // pillar_b's own corner camera (PreviewCamera), its room a quarter turn counter-clockwise in the cell at row 1.
        Room room = RoomCatalogue.Named(RoomCatalogue.Rooms(RoomCatalogue.Mine, RoomKit.Normal, RoomTheme.Grey, 0), "pillar_b");
        var (at, yaw) = PreviewCamera.Of(room);
        var plan = new LevelPlan(2, 1, [new(room.Name, 1, 0, 90, "")], "", "");

        string[] camera = DescentLevel.Cameras(plan, CameraManifest())[0].Split(' ');

        float m = RoomKit.Normal.Cell / 2f;
        string F(double v) => Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal([F(m - (at.Y - m)), F(RoomKit.Normal.Cell + m + (at.X - m)), F((yaw + 90) % 360)], [camera[0], camera[1], camera[4]]);
    }

    [Fact]
    public void A_larger_rooms_camera_without_its_own_stands_448_short_of_its_focus_from_its_door()
    {
        var rooms = libs.Rooms(10, 0);
        Room arena = rooms.Single(r => r.Entry.Tags.Contains("boss_arena"));
        var plan = new LevelPlan(1, 1, [new(arena.Name, 0, 0, 0, "")], "", "");

        string[] camera = DescentLevel.Cameras(plan, WithoutCameras(LibraryManifestWriter.Write("lib", rooms)))[0].Split(' ');

        float x = float.Parse(camera[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(arena.Focus.X + 448, x, 1f);
    }

    [Fact]
    public void A_preview_gets_no_more_than_twelve_cameras()
    {
        var families = RoomCatalogue.Rooms(RoomCatalogue.Mine, RoomKit.Normal, RoomTheme.Grey, 0)
            .GroupBy(r => RoomName.Parse(r.Name).Family).Select(g => g.First().Name).ToArray();
        var plan = new LevelPlan(1, families.Length, [.. families.Select((n, i) => new Placement(n, 0, i, 0, ""))], "", "");

        Assert.True(families.Length > DescentLevel.MaxCameras);
        Assert.Equal(DescentLevel.MaxCameras, DescentLevel.Cameras(plan, CameraManifest()).Count);
    }

    [Fact]
    public void A_manifest_entry_without_weight_or_depths_takes_the_hosts_defaults()
    {
        var (_, infos) = DescentLevel.ReadManifest("{\"library\":\"l\",\"rooms\":{\"a_x-e-l\":{\"tags\":[\"start\"]}}}");

        RoomInfo only = Assert.Single(infos);
        Assert.Equal((1, 1, 99), (only.Weight, only.MinDepth, only.MaxDepth));
        Assert.Equal(["start"], only.Tags);
    }

    [Fact]
    public void The_doorway_camera_looks_at_a_joint_from_the_tracked_room_first()
    {
        // Two joints: pillar-pillar first in reading order, drift-pillar the one a track stops at.
        var plan = new LevelPlan(1, 3, [new Placement("pillar_b-ew-c", 0, 0, 0, "up"), new Placement("pillar_a-ew-c", 0, 1, 0, "path"),
            new Placement("drift_a-ew-l", 0, 2, 0, "down")], "pillar_b-ew-c", "drift_a-ew-l");

        string[] camera = DescentLevel.DoorwayCamera(plan, new HashSet<string>(StringComparer.Ordinal) { "drift" })!.Split(' ');

        Assert.Equal("drift_a-ew-l", camera[^1]);
        Assert.InRange(float.Parse(camera[0], System.Globalization.CultureInfo.InvariantCulture), 2048, 3072);
    }
}
