using System.Text.Json;
using System.Text.Json.Serialization;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>library.json</c> as the writer makes it (task A12), read back with the
/// host's schema (<c>TF2.Descent.MapForge.LibraryManifest</c> at 9a70c1c,
/// mirrored below by its property names).
/// </summary>
public sealed class DescentLibraryManifestTests
{
    private sealed class HostManifest
    {
        [JsonPropertyName("mod")] public string Mod { get; set; } = "";
        [JsonPropertyName("library")] public string Library { get; set; } = "";
        [JsonPropertyName("rooms")] public Dictionary<string, HostEntry> Rooms { get; set; } = [];
    }

    private sealed class HostEntry
    {
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
        [JsonPropertyName("weight")] public int Weight { get; set; } = 1;
        [JsonPropertyName("depths")] public int[] Depths { get; set; } = [1, 99];
        [JsonPropertyName("jump")] public bool Jump { get; set; }
        [JsonPropertyName("low")] public bool Low { get; set; }
        [JsonPropertyName("markers")] public List<string> Markers { get; set; } = [];
        [JsonPropertyName("doors")] public List<string> Doors { get; set; } = [];
    }

    private static RoomBuilder New(string name) => new(name, RoomKit.Normal, RoomTheme.Grey);

    private static Room Hall() => New("hall").WithHeight(1024).WithDoors(CellFace.West, CellFace.East)
        .WithEntry(new RoomEntry(["junction"], Weight: 3, MinDepth: 2, MaxDepth: 9, Jump: true))
        .WithMarker(MarkerKind.PackSpawn, new Point(512, 300, 32)).WithMarker(MarkerKind.PackSpawn, new Point(512, 700, 32))
        .WithMarker(MarkerKind.Barrel, new Point(300, 300, 32)).Build();

    private static Room Drift() => New("drift").WithHeight(256).WithWidth(256).WithDoors(CellFace.North, CellFace.South)
        .WithEntry(new RoomEntry(["corridor"], Low: true)).Build();

    private static HostManifest Read(string json) => JsonSerializer.Deserialize<HostManifest>(json)!;

    [Fact]
    public void The_manifest_names_the_mod_and_the_library()
    {
        var m = Read(LibraryManifestWriter.Write("depth01", [Hall()]));

        Assert.Equal(("tf2_descent", "depth01"), (m.Mod, m.Library));
    }

    [Fact]
    public void A_rooms_entry_carries_its_tags_weight_depths_jump_and_low()
    {
        var e = Read(LibraryManifestWriter.Write("l", [Hall()])).Rooms["hall"];

        Assert.Equal(["junction"], e.Tags);
        Assert.Equal([2, 9], e.Depths);
        Assert.Equal((3, true, false), (e.Weight, e.Jump, e.Low));
    }

    [Fact]
    public void A_rooms_markers_are_each_marker_type_it_holds_once()
    {
        var e = Read(LibraryManifestWriter.Write("l", [Hall()])).Rooms["hall"];

        Assert.Equal(["descent_mark_barrel", "descent_spawn_pack"], e.Markers);
    }

    [Fact]
    public void A_rooms_doors_are_the_walls_it_opens_on()
    {
        var m = Read(LibraryManifestWriter.Write("l", [Hall(), Drift()]));

        Assert.Equal(["east", "west"], m.Rooms["hall"].Doors);
        Assert.Equal(["north", "south"], m.Rooms["drift"].Doors);
    }

    [Fact]
    public void Every_room_of_the_library_has_an_entry()
    {
        Assert.Equal(["drift", "hall"], Read(LibraryManifestWriter.Write("l", [Hall(), Drift()])).Rooms.Keys.Order());
    }

    [Fact]
    public void A_tag_the_host_does_not_know_is_refused()
    {
        Room odd = New("x").WithEntry(new RoomEntry(["lobby"])).Build();

        Assert.Throws<ArgumentException>(() => LibraryManifestWriter.Write("l", [odd]));
    }

    [Fact]
    public void A_depth_range_outside_the_shaft_is_refused()
    {
        Room deep = New("x").WithEntry(new RoomEntry([], MinDepth: 3, MaxDepth: 16)).Build();

        Assert.Throws<ArgumentException>(() => LibraryManifestWriter.Write("l", [deep]));
    }

    [Fact]
    public void A_room_both_jump_and_low_is_refused()
    {
        Room both = New("x").WithEntry(new RoomEntry([], Jump: true, Low: true)).Build();

        Assert.Throws<ArgumentException>(() => LibraryManifestWriter.Write("l", [both]));
    }

    [Fact]
    public void Two_writes_of_the_same_rooms_are_the_same_bytes()
    {
        Assert.Equal(LibraryManifestWriter.Write("l", [Hall(), Drift()]), LibraryManifestWriter.Write("l", [Hall(), Drift()]));
    }
}
