using SourceSharp.Host.Contracts;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The level file <c>descent-level</c> writes is the host's: row 0 is the south
/// row and is written last, a rotation is counter-clockwise degrees written as
/// <c>@deg</c>, columns are padded to their widest cell.
/// </summary>
public sealed class DescentLevelFileTests
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
        RoomInfo hall = Assert.Single(infos, i => i.Name.StartsWith("hall_we-", StringComparison.Ordinal));
        Room built = RoomCatalogue.Named(rooms, "hall_we");
        Assert.Equal((built.Entry.Weight, built.Entry.MinDepth, built.Entry.MaxDepth), (hall.Weight, hall.MinDepth, hall.MaxDepth));
    }

    [Fact]
    public void A_manifest_entry_without_weight_or_depths_takes_the_hosts_defaults()
    {
        var (_, infos) = DescentLevel.ReadManifest("{\"library\":\"l\",\"rooms\":{\"a_x-e-l\":{\"tags\":[\"start\"]}}}");

        RoomInfo only = Assert.Single(infos);
        Assert.Equal((1, 1, 99), (only.Weight, only.MinDepth, only.MaxDepth));
        Assert.Equal(["start"], only.Tags);
    }
}
