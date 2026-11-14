using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// The one list of families (<see cref="RoomFamilies"/>) that the depth table, the
/// layout's synthetic catalogue and the map generator's real catalogue are held to,
/// so they agree on every family's name and tags.
/// </summary>
public sealed class RoomFamiliesTests
{
    private static string FamilyOf(string name) =>
        RoomName.TryParse(name, out var shape, out var error) ? shape!.Family : throw new InvalidOperationException(error);

    [Fact]
    public void Every_family_the_depth_table_names_is_a_known_family()
    {
        var named = Enumerable.Range(1, DepthTable.Depths).Select(DepthTable.For)
            .SelectMany(p => p.FamilyTargets.Select(f => f.Family).Concat(p.Families.Keys).Concat(p.ArrivalFamily is { } a ? [a] : []));

        Assert.DoesNotContain(named, f => !RoomFamilies.IsKnown(f));
    }

    [Fact]
    public void Every_room_of_the_synthetic_catalogue_is_of_a_known_family()
    {
        Assert.DoesNotContain(LayoutCatalogue.Rooms(), r => !RoomFamilies.IsKnown(FamilyOf(r.Name)));
    }

    [Fact]
    public void Every_room_of_the_synthetic_catalogue_carries_only_its_familys_tags()
    {
        var wrong = LayoutCatalogue.Rooms().Where(r => r.Tags.Any(t => !RoomFamilies.Tags[FamilyOf(r.Name)].Contains(t)));

        Assert.Empty(wrong.Select(r => $"{r.Name} [{string.Join(",", r.Tags)}]"));
    }

    [Fact]
    public void A_connector_family_is_a_hallway_to_the_layout()
    {
        var room = new LayoutRoom(new SourceSharp.Host.Contracts.RoomInfo("widedrift_a-ew-l", [], 1, 1, 15), RoomName.Parse("widedrift_a-ew-l"));

        Assert.True(room.IsConnector);
    }

    [Fact]
    public void A_crypt_is_a_low_room_but_not_a_hallway()
    {
        var room = new LayoutRoom(new SourceSharp.Host.Contracts.RoomInfo("crypt_a-ew-l", [], 1, 6, 15), RoomName.Parse("crypt_a-ew-l"));

        Assert.False(room.IsConnector);
        Assert.Equal(RoomClass.Chamber, room.Class);
    }
}
