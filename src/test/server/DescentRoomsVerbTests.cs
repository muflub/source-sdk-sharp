using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>What <c>MapGen descent-rooms</c> writes (task A13).</summary>
public sealed class DescentRoomsVerbTests
{
    private static string Dir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "descent-rooms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void It_writes_the_library_its_manifest_and_a_level_per_depth()
    {
        string dir = Dir();
        try
        {
            var files = DescentRooms.Write(dir, 0, [1, 2, 3]).Select(Path.GetFileName);

            Assert.Equal(["descent_demo.vmf", "library.json", "descent_demo_d01.yaml", "descent_demo_d02.yaml", "descent_demo_d03.yaml"], files);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_sample_level_places_only_rooms_the_library_holds(int depth)
    {
        var names = DescentRooms.Rooms(0).Select(r => r.Name).ToHashSet();
        var placed = DescentRooms.Level(depth).Rows.SelectMany(r => r).OfType<string>().Select(c => c.Split('@')[0]).ToArray();

        Assert.NotEmpty(placed);
        Assert.All(placed, p => Assert.Contains(p, names));
    }

    [Fact]
    public void A_depth_outside_the_shaft_is_refused()
    {
        string dir = Dir();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DescentRooms.Write(dir, 0, [16]));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Every_room_of_the_written_library_has_a_manifest_entry()
    {
        string json = LibraryManifestWriter.Write(DescentRooms.Library, DescentRooms.Rooms(0));

        Assert.All(DescentRooms.Rooms(0), r => Assert.Contains($"\"{r.Name}\": {{", json));
    }
}
