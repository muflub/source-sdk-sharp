using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The room catalogue (Phase 1): shape families as seeded generators, the
/// library and manifest they make, and the seed's determinism.
/// </summary>
public sealed class DescentRoomCatalogueTests
{
    private static IReadOnlyList<Room> Rooms(ulong seed) =>
        RoomCatalogue.Rooms(RoomCatalogue.Demonstration, RoomKit.Normal, RoomTheme.Grey, seed);

    private static string Vmf(ulong seed) => RoomCatalogue.Library(RoomKit.Normal, Rooms(seed)).Build().Write();

    [Fact]
    public void The_same_seed_writes_the_same_library_and_manifest_bytes()
    {
        Assert.Equal(Vmf(7), Vmf(7));
        Assert.Equal(LibraryManifestWriter.Write("l", Rooms(7)), LibraryManifestWriter.Write("l", Rooms(7)));
    }

    [Fact]
    public void Another_seed_varies_the_rooms()
    {
        Assert.Contains(Enumerable.Range(1, 8), s => Vmf((ulong)s) != Vmf(0));
    }

    [Fact]
    public void A_seed_stream_is_the_same_twice_and_differs_by_family()
    {
        ulong[] Draw(string family) { var r = new RoomSeed(42, family); return [r.Next(), r.Next(), r.Next()]; }

        Assert.Equal(Draw("hall"), Draw("hall"));
        Assert.NotEqual(Draw("hall"), Draw("drift"));
    }

    [Fact]
    public void A_seeded_whole_number_stays_in_its_range()
    {
        var r = new RoomSeed(1, "x");

        Assert.All(Enumerable.Range(0, 1000).Select(_ => r.Int(3, 7)), v => Assert.InRange(v, 3, 6));
    }

    [Fact]
    public void Every_room_is_named_for_its_family_and_named_once()
    {
        var names = Rooms(3).Select(r => r.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names, n => Assert.True(n.StartsWith("drift_", StringComparison.Ordinal) || n.StartsWith("gallery_", StringComparison.Ordinal), n));
    }

    [Fact]
    public void A_family_whose_room_is_not_named_for_it_is_refused()
    {
        RoomShape bad = new("pit", (kit, theme, seed) => [new RoomBuilder("hall_x", kit, theme).Build()]);

        Assert.Throws<ArgumentException>(() => RoomCatalogue.Rooms([bad], RoomKit.Normal, RoomTheme.Grey, 0));
    }

    [Fact]
    public void The_demonstration_drift_is_a_low_corridor_and_the_galleries_are_jump_rooms()
    {
        var rooms = Rooms(5);

        Assert.All(rooms.Where(r => r.Name.StartsWith("drift_", StringComparison.Ordinal)), r => Assert.True(r.Entry.Low && r.Entry.Tags.Contains("corridor")));
        Assert.All(rooms.Where(r => r.Name.StartsWith("gallery_", StringComparison.Ordinal)), r => Assert.True(r.Entry.Jump));
    }
}
