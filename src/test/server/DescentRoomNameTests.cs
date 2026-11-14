using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using TF2.Descent.Rules.Layout;
using Xunit;
using CatalogueShape = SourceSharp.MapGen.Descent.RoomShape;

namespace SourceSharp.Tests;

/// <summary>
/// Every library room is named by the rules module's codec (plan E8), from its
/// own facts: its doors, its height class, <c>j</c> from its entry and a flag per
/// jump tier whose feature it holds.
/// </summary>
public sealed class DescentRoomNameTests
{
    /// <summary>Every room of every depth's library.</summary>
    private static IReadOnlyList<Room> Catalogue() =>
        [.. Enumerable.Range(1, 15).SelectMany(d => DescentRooms.Rooms(d, 0))];

    private static IEnumerable<JumpTier> TiersOf(JumpFlags flags)
    {
        if ((flags & JumpFlags.S2) != 0) yield return JumpTier.S2;
        if ((flags & JumpFlags.S3) != 0) yield return JumpTier.S3;
        if ((flags & JumpFlags.B1) != 0) yield return JumpTier.B1;
        if ((flags & JumpFlags.B2) != 0) yield return JumpTier.B2;
        if ((flags & JumpFlags.B3) != 0) yield return JumpTier.B3;
    }

    [Fact]
    public void Every_catalogue_room_parses_and_its_name_round_trips()
    {
        var rooms = Catalogue();

        Assert.NotEmpty(rooms);
        Assert.All(rooms, r => Assert.Equal(r.Name, RoomName.Format(RoomName.Parse(r.Name))));
    }

    [Fact]
    public void A_rooms_parsed_doors_are_its_own()
    {
        Assert.All(Catalogue(), r => Assert.Equal(LayoutNames.DoorsOf(r.Doors), RoomName.Parse(r.Name).Doors));
    }

    [Fact]
    public void A_rooms_parsed_height_is_the_class_of_its_room_height()
    {
        Assert.All(Catalogue(), r => Assert.Equal(LayoutNames.HeightClassOf(r.Height), RoomName.Parse(r.Name).Height));
    }

    [Fact]
    public void A_room_is_flagged_j_exactly_when_its_entry_says_jump()
    {
        var rooms = Catalogue();

        Assert.Contains(rooms, r => r.Entry.Jump);
        Assert.All(rooms, r => Assert.Equal(r.Entry.Jump, RoomName.Parse(r.Name).IsJumpRoom));
    }

    [Fact]
    public void A_rooms_tier_flags_are_the_tiers_of_the_features_it_holds()
    {
        var rooms = Catalogue();

        Assert.Contains(rooms, r => r.JumpTiers.Count > 0);
        Assert.All(rooms, r => Assert.Equal(r.JumpTiers.Order(), TiersOf(RoomName.Parse(r.Name).Jumps).Order()));
    }

    [Fact]
    public void The_hall_is_named_from_its_doors_height_and_features()
    {
        Room hall = RoomCatalogue.Named(DescentRooms.Rooms(1, 0), "hall_we");

        Assert.Equal("hall_we-ew-h-j.s2.b1", hall.Name);
    }

    [Theory]
    [InlineData(192, HeightClass.Low)]
    [InlineData(256, HeightClass.Low)]
    [InlineData(272, HeightClass.Chamber)]
    [InlineData(512, HeightClass.Chamber)]
    [InlineData(528, HeightClass.Hall)]
    [InlineData(1024, HeightClass.Hall)]
    [InlineData(1040, HeightClass.Shaft)]
    [InlineData(2048, HeightClass.Shaft)]
    public void A_height_falls_in_its_class(int height, HeightClass expected)
    {
        Assert.Equal(expected, LayoutNames.HeightClassOf(height));
    }

    [Fact]
    public void A_rocket_shelf_records_its_tier_on_the_room()
    {
        var b = new RoomBuilder("c_a", RoomKit.Normal, RoomTheme.Grey).WithHeight(1024).WithDoors(CellFace.East);
        Room bare = b.Build();
        Room shelved = b.RocketShelf(CellFace.North, 300, 600).Build();

        Assert.Empty(bare.JumpTiers);
        Assert.Equal([JumpTier.B1], shelved.JumpTiers);
    }

    [Fact]
    public void A_crate_step_is_no_jump_spot()
    {
        Room room = new RoomBuilder("c_a", RoomKit.Normal, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.East)
            .CrateSteps(300, 300, Compass.North).Build();

        Assert.Empty(room.JumpTiers);
    }

    [Fact]
    public void A_family_room_whose_variant_the_codec_cannot_spell_is_refused()
    {
        CatalogueShape bad = new("pit", (kit, theme, seed) => [new RoomBuilder("pit_A", kit, theme).WithHeight(512).WithDoors(CellFace.East).Build()]);

        Assert.Throws<ArgumentException>(() => RoomCatalogue.Rooms([bad], RoomKit.Normal, RoomTheme.Grey, 0));
    }
}
