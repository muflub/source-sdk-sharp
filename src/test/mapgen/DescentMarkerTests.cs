using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Descent markers (task A11): typed <c>info_poi</c> points named as
/// plan_tf2_rpg.md 6a names them, and the level map's markers and labels.
/// </summary>
public sealed class DescentMarkerTests
{
    private static RoomBuilder New() => new RoomBuilder("r", RoomKit.Normal, RoomTheme.Grey).WithHeight(512).WithoutDoorFrames();

    [Theory]
    [InlineData(MarkerKind.PackSpawn, "descent_spawn_pack")]
    [InlineData(MarkerKind.Shrine, "descent_mark_shrine")]
    [InlineData(MarkerKind.Container, "descent_mark_container")]
    [InlineData(MarkerKind.Dispenser, "descent_mark_dispenser")]
    [InlineData(MarkerKind.Barrel, "descent_mark_barrel")]
    [InlineData(MarkerKind.Trap, "descent_mark_trap")]
    public void A_marker_writes_the_poi_type_plan_6a_names(MarkerKind kind, string poiType)
    {
        Room room = New().WithMarker(kind, new Point(512, 512, 32), 90).Build();

        var poi = Assert.Single(room.Entities);
        Assert.Equal(("info_poi", poiType, "0 90 0"), (poi.ClassName, poi.Key("poi_type"), poi.Key("angles")));
    }

    [Fact]
    public void A_pack_spawn_carries_its_radius_and_tags()
    {
        var poi = Assert.Single(New().WithMarker(MarkerKind.PackSpawn, new Point(512, 512, 32), radius: 128, tags: "melee").Build().Entities);

        Assert.Equal(("128", "melee"), (poi.Key("poi_radius"), poi.Key("poi_tags")));
    }

    [Fact]
    public void A_map_marker_carries_its_icon_and_label()
    {
        var poi = Assert.Single(New().WithMapMarker(new Point(512, 512, 32), "lift", "Cage").Build().Entities);

        Assert.Equal(("descent_map_marker", "lift", "Cage"), (poi.Key("poi_type"), poi.Key("map_marker"), poi.Key("map_label")));
    }

    [Fact]
    public void A_map_marker_through_the_plain_marker_helper_is_refused()
    {
        Assert.Throws<ArgumentException>(() => New().WithMarker(MarkerKind.MapMarker, new Point(512, 512, 32)));
    }

    [Fact]
    public void A_rooms_map_label_is_written_on_its_info_room()
    {
        Room room = New().WithMapLabel("The Collar").Build();

        Assert.Equal("The Collar", RoomLibrary.InfoRoom(room, new Point(0, 0, 0)).KeyValues.Single(kv => kv.Key == "map_label").Value);
    }

    [Fact]
    public void A_room_lists_its_markers_by_kind_and_place()
    {
        Room room = New().WithMarker(MarkerKind.Shrine, new Point(100, 200, 32)).WithMarker(MarkerKind.Trap, new Point(300, 400, 32))
            .WithPoi("landmark", new Point(1, 1, 32)).Build();

        Assert.Equal([(MarkerKind.Shrine, new Point(100, 200, 32)), (MarkerKind.Trap, new Point(300, 400, 32))], room.Markers);
    }

    [Fact]
    public void Containers_shrines_and_dispensers_reward_a_jump_and_hazards_and_spawns_do_not()
    {
        Assert.Equal([MarkerKind.Shrine, MarkerKind.Container, MarkerKind.Dispenser], Enum.GetValues<MarkerKind>().Where(Markers.IsReward));
    }
}
