using System.Text;
using TF2.Descent.Rules.Automap;
using Xunit;
using Map = TF2.Descent.Rules.Automap.Automap;

namespace TF2.Descent.Tests.Automap;

/// <summary>
/// The automap (plan_descent_logic RU12; plan_tf2_rpg 2m, R23). The level is a small fixed
/// graph whose room ids are words found nowhere else, so a fact can search the bytes a
/// client would receive for a room it must not know:
/// <code>
///   arrival_hall ─d1─ mess_room ─d2─ secret_vault (stairs)
///                        │
///                        d3
///                        │
///                     loft_room (storey 1, a shrine, a locked container)
/// </code>
/// </summary>
public sealed class AutomapTests
{
    private static readonly AutomapLevel Level = AutomapLevel.Create(
        [new AutomapRoom("arrival_hall"), new AutomapRoom("mess_room"), new AutomapRoom("secret_vault"), new AutomapRoom("loft_room", 1)],
        [new AutomapDoor("d1", "arrival_hall", "mess_room"), new AutomapDoor("d2", "mess_room", "secret_vault"), new AutomapDoor("d3", "mess_room", "loft_room")],
        [
            new LevelMarker("arrive", LevelMarkerKind.Arrival, "arrival_hall"),
            new LevelMarker("down", LevelMarkerKind.Stairs, "secret_vault"),
            new LevelMarker("altar", LevelMarkerKind.Shrine, "loft_room"),
            new LevelMarker("chest", LevelMarkerKind.LockedContainer, "loft_room"),
        ]);

    private static Map Visit(params string[] rooms) => rooms.Aggregate(Map.Empty, (m, r) => AutomapRules.Enter(m, Level, r));

    private static AutomapView View(Map map, string viewer = "ann", params MemberPosition[] party) =>
        AutomapRules.View(map, Level, viewer, party);

    private static string Bytes(Map map, string viewer = "ann", params MemberPosition[] party) =>
        Encoding.UTF8.GetString(AutomapRules.Serialise(View(map, viewer, party)));

    [Fact]
    public void An_unentered_room_is_absent_from_the_serialised_bytes()
    {
        var json = Bytes(Visit("arrival_hall", "mess_room"));

        Assert.Contains("mess_room", json);          // the stimulus: what was entered is there
        Assert.DoesNotContain("secret_vault", json);
        Assert.DoesNotContain("loft_room", json);
        Assert.DoesNotContain("Stairs", json);       // nor what stands in it
    }

    [Fact]
    public void Entering_a_room_reveals_exactly_that_room_and_its_door_stubs()
    {
        var map = Visit("mess_room");

        Assert.Equal(["mess_room"], map.RoomsSeen.ToArray());
        Assert.Equal(["d1", "d2", "d3"], map.DoorsSeen.ToArray());
    }

    [Fact]
    public void A_door_stub_leads_onward_without_naming_an_unentered_room()
    {
        var doors = View(Visit("arrival_hall", "mess_room")).Doors;

        Assert.Contains(new MapDoor("d1", "arrival_hall", "mess_room"), doors);
        Assert.Contains(new MapDoor("d2", "mess_room", null), doors);
        Assert.Contains(new MapDoor("d3", "mess_room", null), doors);
    }

    [Fact]
    public void A_party_shares_one_map_merged_from_each_members_reveals()
    {
        var ann = Visit("arrival_hall");
        var bob = Visit("mess_room", "loft_room");

        var party = AutomapRules.Merge(ann, bob);

        Assert.Equal(["arrival_hall", "loft_room", "mess_room"], party.RoomsSeen.ToArray());
        Assert.True(party.SameAs(AutomapRules.Merge(bob, ann)));
    }

    [Fact]
    public void Merging_a_map_with_itself_changes_nothing()
    {
        var map = AutomapRules.UseShrine(Visit("mess_room", "loft_room"), "altar");

        Assert.True(AutomapRules.Merge(map, map).SameAs(map));
        Assert.True(AutomapRules.Merge(AutomapRules.Merge(map, Map.Empty), map).SameAs(map));
    }

    [Fact]
    public void The_stairs_marker_appears_only_once_its_room_is_seen()
    {
        var before = View(Visit("mess_room")).Markers;
        var after = View(Visit("mess_room", "secret_vault")).Markers;

        Assert.DoesNotContain(before, m => m.Kind == MapMarkerKind.Stairs);
        Assert.Contains(new MapMarker(MapMarkerKind.Stairs, "secret_vault"), after);
    }

    [Fact]
    public void Reveal_all_reveals_every_room_and_every_door()
    {
        var map = AutomapRules.RevealAll(Map.Empty, Level);

        Assert.Equal(Level.Rooms.Select(r => r.Id).Order(StringComparer.Ordinal), map.RoomsSeen.ToArray());
        Assert.Equal(["d1", "d2", "d3"], map.DoorsSeen.ToArray());
        Assert.All(View(map).Doors, d => Assert.NotNull(d.To));
    }

    [Fact]
    public void The_map_is_a_pure_function_of_the_level_and_the_visit_order()
    {
        static byte[] Run()
        {
            var m = Visit("arrival_hall", "mess_room", "loft_room");
            m = AutomapRules.UseShrine(m, "altar");
            m = AutomapRules.PlacePortal(m, "mess_room");
            return AutomapRules.Serialise(AutomapRules.View(m, Level, "ann", [new MemberPosition("bob", "loft_room")]));
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void A_shrine_shows_unused_until_it_is_used()
    {
        var map = Visit("mess_room", "loft_room");

        Assert.Contains(new MapMarker(MapMarkerKind.ShrineUnused, "loft_room"), View(map).Markers);
        Assert.Contains(new MapMarker(MapMarkerKind.ShrineUsed, "loft_room"), View(AutomapRules.UseShrine(map, "altar")).Markers);
    }

    [Fact]
    public void A_locked_container_loses_its_marker_once_opened()
    {
        var map = Visit("mess_room", "loft_room");

        Assert.Contains(new MapMarker(MapMarkerKind.LockedContainer, "loft_room"), View(map).Markers);
        Assert.DoesNotContain(View(AutomapRules.OpenContainer(map, "chest")).Markers, m => m.Kind == MapMarkerKind.LockedContainer);
    }

    [Fact]
    public void A_viewer_sees_its_own_corpse_and_no_one_elses()
    {
        var map = AutomapRules.PlaceCorpse(AutomapRules.PlaceCorpse(Visit("arrival_hall", "mess_room"), "ann", "mess_room"), "bob", "arrival_hall");

        var ann = View(map, "ann").Markers.Where(m => m.Kind == MapMarkerKind.Corpse).ToList();

        Assert.Equal([new MapMarker(MapMarkerKind.Corpse, "mess_room", "ann")], ann);
        Assert.DoesNotContain("arrival_hall\",\"label\":\"bob", Bytes(map, "ann"));
    }

    [Fact]
    public void Party_members_show_where_they_stand_in_revealed_rooms()
    {
        var markers = View(Visit("arrival_hall", "mess_room"), "ann", new MemberPosition("bob", "mess_room"), new MemberPosition("cat", "arrival_hall")).Markers;

        Assert.Contains(new MapMarker(MapMarkerKind.PartyMember, "mess_room", "bob"), markers);
        Assert.Contains(new MapMarker(MapMarkerKind.PartyMember, "arrival_hall", "cat"), markers);
    }

    [Fact]
    public void A_member_reported_in_an_unentered_room_is_not_drawn()
    {
        var json = Bytes(Visit("arrival_hall"), "ann", new MemberPosition("bob", "secret_vault"));

        Assert.DoesNotContain("secret_vault", json);
        Assert.DoesNotContain("bob", json);
    }

    [Fact]
    public void The_portal_marker_shows_in_its_room()
    {
        var map = AutomapRules.PlacePortal(Visit("arrival_hall"), "arrival_hall");

        Assert.Contains(new MapMarker(MapMarkerKind.Portal, "arrival_hall"), View(map).Markers);
    }

    [Fact]
    public void Each_room_carries_the_storey_it_is_drawn_on()
    {
        var rooms = View(Visit("mess_room", "loft_room")).Rooms;

        Assert.Contains(new MapRoom("loft_room", 1), rooms);
        Assert.Contains(new MapRoom("mess_room", 0), rooms);
    }

    [Fact]
    public void A_level_refuses_a_door_to_a_room_it_does_not_have()
    {
        Assert.Throws<ArgumentException>(() => AutomapLevel.Create([new AutomapRoom("a")], [new AutomapDoor("d", "a", "nowhere")]));
    }
}
