using System.Globalization;
using System.Text;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// A room library for map-tools' rooms pipeline (plan task A2): every room
/// in its own cell along +x with a gap between cells, an <c>info_room</c> at
/// each cell's low corner, and the library-wide singletons in the first gap.
/// </summary>
/// <remarks>
/// The fog and tone map controllers are always written: a library is one
/// depth (plan E2), and they are what carries the depth's mood into every
/// level linked from it. The sun is written only when asked for, since the
/// mine's rooms are sealed and a <c>light_environment</c> with no sky face
/// lights nothing.
/// </remarks>
public sealed class RoomLibrary
{
    /// <summary>The default gap between two cells: a quarter of a normal cell, enough that no brush of one room is near another's.</summary>
    public const int DefaultGap = 256;

    private readonly List<Room> rooms = [];
    private Sun? sun;

    public RoomLibrary(RoomKit kit, int gap = DefaultGap, string sky = "sky_badlands_01")
    {
        Kit = kit;
        Gap = gap;
        Sky = sky;
    }

    public RoomKit Kit { get; }

    public int Gap { get; }

    public string Sky { get; }

    /// <summary>The fog every level of the library is drawn in.</summary>
    public Fog Fog { get; init; } = Fog.Mine;

    /// <summary>The tone map controller's keys.</summary>
    public ToneMap ToneMap { get; init; } = ToneMap.Mine;

    public IReadOnlyList<Room> Rooms => rooms;

    public RoomLibrary Add(Room room)
    {
        if (room.Kit != Kit)
            throw new ArgumentException($"room {room.Name} is on the {room.Kit.Name} kit, the library on {Kit.Name}", nameof(room));
        if (rooms.Any(r => r.Name == room.Name))
            throw new ArgumentException($"the library already has a room {room.Name}", nameof(room));
        rooms.Add(room);
        return this;
    }

    public RoomLibrary WithSun(Sun value)
    {
        sun = value;
        return this;
    }

    /// <summary>The low corner of the cell of the room at <paramref name="index"/>.</summary>
    public Point Corner(int index) => new(index * (Kit.Cell + Gap), 0, 0);

    /// <summary>Where the singletons stand: the middle of the gap after the first cell, at half a cell's height.</summary>
    public Point SingletonSpot => new(Kit.Cell + Gap / 2f, Kit.Cell / 2f, Kit.Cell / 2f);

    public VmfMap Build()
    {
        var map = new VmfMap();
        map.World.Add(new("skyname", Sky));
        if (Kit.NavVoxel != RoomKit.DefaultNavVoxel)
            map.World.Add(new("nav_voxel_size", Num(Kit.NavVoxel)));

        for (int i = 0; i < rooms.Count; i++)
            Place(map, rooms[i], Corner(i));

        Point at = SingletonSpot;
        if (sun is { } s)
            map.Entities.Add(Entity("light_environment", at, ("angles", s.Angles), ("pitch", s.Pitch),
                ("_light", s.Light), ("_ambient", s.Ambient)));
        map.Entities.Add(Entity("env_fog_controller", at, ("targetname", "descent_fog"), ("fogenable", "1"),
            ("fogcolor", Fog.Colour), ("fogcolor2", Fog.Colour), ("fogstart", Num(Fog.Start)), ("fogend", Num(Fog.End)),
            ("fogmaxdensity", Num(Fog.MaxDensity)), ("farz", "-1")));
        map.Entities.Add(Entity("env_tonemap_controller", at, ("targetname", "descent_tonemap"),
            ("TonemapScale", Num(ToneMap.Scale)), ("AutoExposureMin", Num(ToneMap.ExposureMin)),
            ("AutoExposureMax", Num(ToneMap.ExposureMax)), ("BloomScale", Num(ToneMap.Bloom))));
        return map;
    }

    /// <summary>The <c>info_room</c> a room's cell is marked with, at the cell's corner.</summary>
    public static VmfEntity InfoRoom(Room room, Point corner)
    {
        var e = new VmfEntity { ClassName = "info_room" };
        e.Set("origin", corner.ToString()).Set("name", room.Name).Set("cell_size", Num(room.Kit.Cell))
         .Set("door_width", Num(room.Kit.DoorWidth)).Set("door_height", Num(room.Kit.DoorHeight))
         .Set("wall_depth", Num(room.Kit.WallDepth));
        if (room.IsShaped)
            e.Set("room_height", Num(room.Height));
        if (room.Role != RoomRole.None)
            e.Set("room_role", room.Role == RoomRole.Up ? "up" : "down");
        if (room.MapLabel is { } label)
            e.Set("map_label", label);
        return e;
    }

    private static void Place(VmfMap map, Room room, Point corner)
    {
        // Each brush is moved to its cell with its textures anchored at the
        // cell's corner, so a room's faces are textured the same in any cell.
        foreach (Brush brush in room.World)
            map.WorldSolids.Add(brush.Offset(corner).ToVmf(corner));

        foreach (RoomEntity entity in room.Entities)
        {
            var e = new VmfEntity { ClassName = entity.ClassName };
            if (entity.Origin is { } origin)
                e.Set("origin", (origin + corner).ToString());
            foreach (var kv in entity.Keys)
                e.Set(kv.Key, kv.Value);
            e.Connections.AddRange(entity.Wires);
            foreach (Brush brush in entity.Brushes)
                e.Solids.Add(brush.Offset(corner).ToVmf(corner));

            map.Entities.Add(e);
        }

        map.Entities.Add(InfoRoom(room, corner));
    }

    private static VmfEntity Entity(string className, Point at, params (string Key, string Value)[] keys)
    {
        var e = new VmfEntity { ClassName = className };
        e.Set("origin", at.ToString());
        foreach (var (key, value) in keys)
            e.Set(key, value);
        return e;
    }

    internal static string Num(float value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A library's sun: <c>light_environment</c>'s keys.</summary>
public sealed record Sun(string Angles, string Pitch, string Light, string Ambient);

/// <summary>A library's fog (<c>env_fog_controller</c>).</summary>
public sealed record Fog(string Colour, float Start, float End, float MaxDensity)
{
    /// <summary>Level 1's: a light haze that ends past a hall's length.</summary>
    public static Fog Mine { get; } = new("40 36 30", 512, 4096, 0.6f);
}

/// <summary>A library's tone map (<c>env_tonemap_controller</c>).</summary>
public sealed record ToneMap(float Scale, float ExposureMin, float ExposureMax, float Bloom)
{
    public static ToneMap Mine { get; } = new(1f, 0.5f, 2f, 0.3f);
}

/// <summary>
/// A level file for a hand-given grid (map-tools' level YAML): the north row
/// first, each row west to east, a cell a room name with an optional
/// <c>@</c> turn in degrees, or null for no room.
/// </summary>
public sealed record LevelGrid(string Library, IReadOnlyList<IReadOnlyList<string?>> Rows)
{
    /// <summary>The map above, for a level with an up room; null writes <c>up: none</c>.</summary>
    public string? UpMap { get; init; }

    /// <summary>The map below, for a level with a down room; null writes <c>down: none</c>.</summary>
    public string? DownMap { get; init; }

    public string Write()
    {
        int columns = Rows.Count == 0 ? 0 : Rows[0].Count;
        if (Rows.Any(r => r.Count != columns))
            throw new InvalidOperationException("every row of a level has the same number of columns");

        var text = new StringBuilder();
        text.Append("# generated by src/mapgen (Descent.LevelGrid)\n");
        text.Append(CultureInfo.InvariantCulture, $"library: {Library}\nrows: {Rows.Count}\ncolumns: {columns}\n");
        text.Append(UpMap is null ? "up: none\n" : $"up_map: {UpMap}\n");
        text.Append(DownMap is null ? "down: none\n" : $"down_map: {DownMap}\n");
        text.Append("grid:\n");
        foreach (var row in Rows)
            text.Append("  - [").Append(string.Join(", ", row.Select(cell => cell ?? "~"))).Append("]\n");
        return text.ToString();
    }
}
