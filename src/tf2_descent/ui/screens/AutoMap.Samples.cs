using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

// The automap screen from fakes (the ui-screen loop's sample step): the three
// states plan §2.7 names, built through the same container the game uses.
// Room names are ui.Data (game copy, case kept); every #Descent_Map_* token
// lives in game/mod_tf2_descent/resource/descent_english.txt.

public static class AutoMapSamples
{
    /// <summary>The automap from a map fake; the window placed from
    /// <paramref name="store"/> (empty: the default 1280 x 720).</summary>
    public static UiScreen Build(UiSampleServices s, FakeBigMap map, MemoryWindowStore? store = null,
                                 bool labels = true)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        s.Add<IDescentAutomap>(map);
        s.Add<IAutomapPrefs>(new FakeMapPrefs { ShowLabelsByDefault = labels });
        s.Add<IAutomapRoomLabels>(map);
        return s.Create<AutoMap>();
    }

    /// <summary>Three storeys explored, the hero on the middle one, markers of
    /// ten kinds including the shrine and the exit portal.</summary>
    [UiSample("revealed")]
    private static UiScreen Revealed(UiSampleServices s) => Build(s, FakeBigMap.Revealed());

    /// <summary>The reveal rule: the hero entered one storey only, so the other
    /// storeys hold no rooms at all and the legend lists only what was seen.</summary>
    [UiSample("partial")]
    private static UiScreen Partial(UiSampleServices s) => Build(s, FakeBigMap.Partial());

    /// <summary>Room labels off (the caller's data starts the screen without them).</summary>
    [UiSample("labelless")]
    private static UiScreen Labelless(UiSampleServices s) => Build(s, FakeBigMap.Revealed(), labels: false);

    /// <summary>The window at its minimum size: chips, map and legend still fit.</summary>
    [UiSample("resized to minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Build(s, FakeBigMap.Revealed(), new MemoryWindowStore().With("automap", new UiWindowPlacement(300, 180, AutoMap.Spec(1920, 1080).MinW, AutoMap.Spec(1920, 1080).MinH)));
}

/// <summary>The automap's explored dungeon on the generator's 1024 grid: three
/// storeys, the hero on the middle one, and the markers the legend must name.</summary>
public sealed class FakeBigMap : IDescentAutomap, IAutomapRoomLabels
{
    /// <summary>The fully explored three-storey dungeon: town works above, the
    /// crypt with the shrine and loot around the hero, the robot foundry below
    /// with the boss and the exit portal.</summary>
    public static FakeBigMap Revealed() => new()
    {
        Rooms =
        [
            // Storey 1: town works.
            (128, 128, 256, 160, 1, "Workshop Row"),
            (448, 96, 192, 224, 1, "Foundry Floor"),
            (704, 160, 192, 128, 1, "Vent Shaft"),
            // Storey 2: the crypt the hero walks.
            (96, 448, 224, 160, 2, "Entry Hall"),
            (416, 384, 256, 256, 2, "Crypt"),
            (768, 448, 160, 160, 2, "Shrine Room"),
            (704, 704, 256, 160, 2, "Treasure Vault"),
            (96, 704, 224, 128, 2, "Guardroom"),
            // Storey 3: the robot works and the way out.
            (384, 768, 288, 160, 3, "Forge Hall"),
            (768, 800, 192, 128, 3, "Exit Chamber"),
        ],
        Doors =
        [
            (320, 512, 96, 4), (672, 512, 96, 4), (512, 640, 4, 128),
            (192, 608, 4, 96), (736, 512, 32, 4), (512, 256, 4, 128),
            (320, 192, 128, 4), (640, 160, 64, 4), (528, 736, 4, 64),
            (672, 864, 96, 4),
        ],
        Markers =
        [
            new(DescentMapMarker.Stairs, 2, 800, 224, 1),
            new(DescentMapMarker.Stairs, 4, 544, 448, 2),
            new(DescentMapMarker.Corpse, 4, 480, 448, 2),
            new(DescentMapMarker.Trap, 5, 816, 480, 2),
            new(DescentMapMarker.Party, 4, 608, 512, 2),
            new(DescentMapMarker.ShrineUnused, 5, 848, 528, 2),
            new(DescentMapMarker.Container, 6, 832, 768, 2),
            new(DescentMapMarker.ContainerLocked, 6, 896, 800, 2),
            new(DescentMapMarker.Fountain, 7, 160, 752, 2),
            new(DescentMapMarker.Boss, 8, 512, 832, 3),
            new(DescentMapMarker.Portal, 9, 848, 848, 3),
        ],
    };

    /// <summary>The hero entered the crypt only: the other two storeys never
    /// had a room arrive, which is how the reveal rule looks on screen.</summary>
    public static FakeBigMap Partial() => new()
    {
        Rooms =
        [
            (96, 448, 224, 160, 2, "Entry Hall"),
            (416, 384, 256, 256, 2, "Crypt"),
            (768, 448, 160, 160, 2, "Shrine Room"),
        ],
        Doors =
        [
            (320, 512, 96, 4), (672, 512, 96, 4),
        ],
        Markers =
        [
            new(DescentMapMarker.Stairs, 1, 624, 416, 2),
            new(DescentMapMarker.Corpse, 1, 480, 448, 2),
            new(DescentMapMarker.Party, 1, 544, 512, 2),
            new(DescentMapMarker.ShrineUnused, 2, 848, 528, 2),
            new(DescentMapMarker.ContainerLocked, 1, 592, 576, 2),
        ],
    };

    /// <summary>Rooms on the generator's grid: x, y, w, h, storey, name.</summary>
    public (float X, float Y, float W, float H, int Storey, string Label)[] Rooms { get; init; } = [];

    /// <summary>The door ticks across shared walls.</summary>
    public (float X, float Y, float W, float H)[] Doors { get; init; } = [];

    /// <summary>The revealed markers.</summary>
    public DescentMarker[] Markers { get; init; } = [];

    public int StoreyCount { get; init; } = 3;
    public int HeroStorey { get; init; } = 2;
    public float HeroX { get; init; } = 180;
    public float HeroY { get; init; } = 512;
    public float HeroHeadingDeg { get; init; } = 90;

    public int RoomCount => Rooms.Length;
    public float RoomX(int r) => Rooms[r].X;
    public float RoomY(int r) => Rooms[r].Y;
    public float RoomW(int r) => Rooms[r].W;
    public float RoomH(int r) => Rooms[r].H;
    public int RoomStorey(int r) => Rooms[r].Storey;
    public bool RoomCurrentStorey(int r) => Rooms[r].Storey == HeroStorey;
    public string RoomLabel(int r) => Rooms[r].Label;

    public int DoorStubCount => Doors.Length;
    public float DoorStubX(int i) => Doors[i].X;
    public float DoorStubY(int i) => Doors[i].Y;
    public float DoorStubW(int i) => Doors[i].W;
    public float DoorStubH(int i) => Doors[i].H;

    public int MarkerCount => Markers.Length;
    public DescentMarker Marker(int i) => Markers[i];

    // The storey the view shows: the map screen's chips set it, so the fake's
    // default is the hero's own.
    public int FocusStorey => HeroStorey;
}

/// <summary>The automap's viewer prefs, as the caller's data has them.</summary>
public sealed class FakeMapPrefs : IAutomapPrefs
{
    public bool ShowLabelsByDefault { get; init; } = true;
}
