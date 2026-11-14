using System.Collections.Immutable;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.MapFormats.Map2d;
using SourceSharp.MapFormats.Nav;
using TF2.Descent.Rules.Layout;

namespace TF2.Descent.Server.Level;

/// <summary>What a level ships beside its <c>.bsp</c>, as bytes, and what its name and host say of it.</summary>
/// <param name="Map">The map's name.</param>
/// <param name="Depth">Its depth (from the name, or Booting).</param>
/// <param name="Seed">The seed its rolls replay from, or null when nothing has said it yet.</param>
/// <param name="SeedFrom">Where <paramref name="Seed"/> came from (<c>map name</c>, <c>booting</c>), or why it is missing.</param>
/// <param name="Nav3d">The <c>maps/&lt;map&gt;.nav3d</c>, or null when the level has none.</param>
/// <param name="Map2d">The <c>maps/&lt;map&gt;.map2d</c>, or null when the level has none.</param>
public sealed record LevelFiles(string Map, int Depth, ulong? Seed, string SeedFrom, byte[]? Nav3d, byte[]? Map2d);

/// <summary>
/// Reads a linked Descent level's <c>.nav3d</c> and <c>.map2d</c> into <see cref="LevelData"/>
/// (plan_descent_logic WO1; docs/plans/tf2_descent/level_data_findings.md says what each file
/// holds). Pure: bytes in, data out.
/// <list type="bullet">
/// <item><b>Rooms</b>: the <c>.map2d</c>'s placements, in link order (a room's id is its index),
/// with their cells, turn, height and label; a room's role is its cell's transition role in the
/// <c>.nav3d</c> (up, down) or else its family in the layout codec (treasure, antechamber, arena).
/// Without a <c>.map2d</c>, DERIVED from the <c>.nav3d</c>'s placed cells: every cell a room covers
/// names it there, so a room is its south-west cell and the cells its name's footprint (turned
/// with it) covers; no labels.</item>
/// <item><b>Doors</b>: the <c>.map2d</c>'s open doors, one per joined socket pair: the middle of the
/// opening's sill, its width and height, the normal from the lower room id into the higher.
/// Without a <c>.map2d</c>, DERIVED from the <c>.nav3d</c>'s joined door points (position) and the
/// kit's opening, <see cref="KitDoorWidth"/> by <see cref="KitDoorHeight"/>.</item>
/// <item><b>Markers</b>: every <c>.nav3d</c> point of interest that is not a door point, in file
/// order, in the room of the cell it belongs to; a map marker takes its label from the
/// <c>.map2d</c> marker at its place.</item>
/// <item><b>Arrivals</b>: the <c>spawn</c> points of the up room (the link writes four), then its
/// <c>arrival</c> point; with none, the level's spawn point.</item>
/// <item><b>Exit</b>: the <c>.map2d</c>'s <c>exit_down</c> marker (the centre of the way down's
/// volume, labelled with the map it leads to); without one, DERIVED as the down room's arrival point.</item>
/// <item><b>Boss arena</b>: at a boss depth, the room of family <c>arena</c>.</item>
/// </list>
/// </summary>
public static class LevelReader
{
    /// <summary>The kit's doorway width (plan_descent_mine_levels E1): only for a level that ships no <c>.map2d</c>.</summary>
    public const float KitDoorWidth = 208;

    /// <summary>The kit's doorway height (E1).</summary>
    public const float KitDoorHeight = 192;

    /// <summary>How far apart two door ends may lie and still be one doorway seen from both sides.</summary>
    private const float SameDoorway = 1f;

    /// <summary>The <c>poi_type</c> of each marker kind (src/mapgen/Descent/Markers.cs, and the tools' own <c>spawn</c> and <c>arrival</c>).</summary>
    public static string PoiType(LevelMarkerKind kind) => kind switch
    {
        LevelMarkerKind.PackSpawn => "descent_spawn_pack",
        LevelMarkerKind.Shrine => "descent_mark_shrine",
        LevelMarkerKind.Container => "descent_mark_container",
        LevelMarkerKind.Dispenser => "descent_mark_dispenser",
        LevelMarkerKind.Barrel => "descent_mark_barrel",
        LevelMarkerKind.Trap => "descent_mark_trap",
        LevelMarkerKind.MapMarker => "descent_map_marker",
        LevelMarkerKind.Spawn => "spawn",
        LevelMarkerKind.Arrival => "arrival",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no such marker kind"),
    };

    /// <summary>The kind a <c>poi_type</c> names, or null for one that is not a Descent marker.</summary>
    public static LevelMarkerKind? KindOf(string poiType)
    {
        foreach (LevelMarkerKind kind in Enum.GetValues<LevelMarkerKind>())
            if (PoiType(kind) == poiType)
                return kind;
        return null;
    }

    /// <summary>Reads the level; never throws for a missing or unreadable file, which makes its pieces absent and says why in <see cref="LevelData.Notes"/>.</summary>
    public static LevelData Read(LevelFiles files)
    {
        var notes = ImmutableArray.CreateBuilder<string>();
        var sources = ImmutableDictionary.CreateBuilder<LevelPiece, PieceSource>();
        sources[LevelPiece.Seed] = files.Seed is null ? PieceSource.Absent : PieceSource.Shipped;

        Nav3dReader? nav = Open(files.Nav3d, $"maps/{files.Map}.nav3d", b => Nav3dReader.Open(b), notes);
        Map2dLevel? map = Open(files.Map2d, $"maps/{files.Map}.map2d", b => Map2dReader.Read(b), notes);
        if (map is not null && nav is not null && Disagrees(map, nav) is { } why)
        {
            notes.Add($"maps/{files.Map}.map2d is not the .nav3d's level ({why}); it is not read");
            map = null;
        }

        // The cell each placed room's cells belong to.
        int columns = nav?.Columns ?? map?.Columns ?? 0;
        ImmutableArray<LevelRoom> rooms;
        int[] roomOfCell;
        if (map is not null)
        {
            (rooms, roomOfCell) = RoomsOf(map, nav);
            sources[LevelPiece.Rooms] = PieceSource.Shipped;
        }
        else if (nav is not null)
        {
            (rooms, roomOfCell) = RoomsOf(nav);
            sources[LevelPiece.Rooms] = PieceSource.Derived;
            notes.Add("rooms derived from the .nav3d's named cells (no .map2d): a room's cells by its name's footprint, no labels");
        }
        else
        {
            rooms = [];
            roomOfCell = [];
            sources[LevelPiece.Rooms] = PieceSource.Absent;
        }

        ImmutableArray<LevelDoor> doors;
        if (map is not null)
        {
            doors = DoorsOf(map, rooms);
            sources[LevelPiece.Doors] = PieceSource.Shipped;
        }
        else if (nav is not null && rooms.Length > 0)
        {
            doors = DoorsOf(nav, rooms, roomOfCell);
            sources[LevelPiece.Doors] = PieceSource.Derived;
            notes.Add($"doors derived from the .nav3d's joined door points, {KitDoorWidth:0} by {KitDoorHeight:0} (the kit's opening)");
        }
        else
        {
            doors = [];
            sources[LevelPiece.Doors] = PieceSource.Absent;
        }

        ImmutableArray<LevelMarker> markers = nav is null ? [] : MarkersOf(nav, map, rooms, roomOfCell, columns);
        sources[LevelPiece.Markers] = nav is null ? PieceSource.Absent : PieceSource.Shipped;

        ImmutableArray<ArrivalPoint> arrivals = nav is null ? [] : ArrivalsOf(nav, markers, rooms);
        sources[LevelPiece.Arrivals] = arrivals.IsEmpty ? PieceSource.Absent : PieceSource.Shipped;

        bool hasDown = files.Depth is < 1 or > DepthTable.Depths || DepthTable.For(files.Depth).HasDownExit;
        LevelExit? exit = ExitOf(map, nav, markers, rooms, files.Depth, out PieceSource exitSource);
        if (exit is not null || hasDown)
            sources[LevelPiece.Exit] = exitSource;
        if (exitSource == PieceSource.Derived)
            notes.Add("the exit derived as the down room's arrival point (no exit_down on the .map2d)");

        int? arena = null;
        bool boss = files.Depth is >= 1 and <= DepthTable.Depths && DepthTable.For(files.Depth).IsBoss;
        int arenaRoom = rooms.IndexOf(rooms.FirstOrDefault(r => r.Role == LevelRoomRole.Arena)!);
        if (arenaRoom >= 0)
            arena = arenaRoom;
        if (boss || arena is not null)
            sources[LevelPiece.BossArena] = arena is null ? PieceSource.Absent : PieceSource.Shipped;

        return new LevelData(files.Map, files.Depth, files.Seed, files.SeedFrom, rooms, doors, markers, arrivals, exit, arena,
            sources.ToImmutable(), notes.ToImmutable());
    }

    private static T? Open<T>(byte[]? bytes, string path, Func<byte[], T> read, ImmutableArray<string>.Builder notes) where T : class
    {
        if (bytes is null)
        {
            notes.Add($"no {path}");
            return null;
        }

        try
        {
            return read(bytes);
        }
        catch (InvalidDataException e)
        {
            notes.Add($"{path} is unreadable: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Why a <c>.map2d</c> is not the <c>.nav3d</c>'s level, or null: the same grid, and every
    /// placement's anchor cell names the same room in both. (The <c>.map2d</c>'s own binding, the
    /// map checksum, is not used: tools/descent-face-sides rewrites the map after the link, so every
    /// shipped level's checksum differs from the one its <c>.map2d</c> records.)
    /// </summary>
    private static string? Disagrees(Map2dLevel map, Nav3dReader nav)
    {
        if (map.Columns != nav.Columns || map.Rows != nav.Rows || map.CellSize != nav.CellSize)
            return string.Create(CultureInfo.InvariantCulture,
                $"grid {map.Columns}x{map.Rows} of {map.CellSize} against {nav.Columns}x{nav.Rows} of {nav.CellSize}");
        foreach (Map2dRoom room in map.Rooms)
        {
            int cell = room.CellY * nav.Columns + room.CellX;
            if ((uint)cell >= (uint)nav.CellCount || nav.Cell(cell).Room != room.Name)
                return $"room {room.Name} at ({room.CellX}, {room.CellY}) is not in that cell of the .nav3d";
        }
        return null;
    }

    private static (string Family, bool Low, bool Jump) Facts(string name) =>
        RoomName.TryParse(name, out RoomShape? shape, out _)
            ? (shape!.Family, shape.Height == HeightClass.Low, shape.IsJumpRoom)
            : (name, false, false);

    private static LevelRoomRole RoleOf(Nav3dRoomRole transit, string family) =>
        transit == Nav3dRoomRole.Up ? LevelRoomRole.Arrival
        : transit == Nav3dRoomRole.Down ? LevelRoomRole.Exit
        : family switch
        {
            "treasure" => LevelRoomRole.Treasure,
            RoomTags.AntechamberFamily => LevelRoomRole.Antechamber,
            "arena" => LevelRoomRole.Arena,
            _ => LevelRoomRole.Fill,
        };

    private static (ImmutableArray<LevelRoom>, int[]) RoomsOf(Map2dLevel map, Nav3dReader? nav)
    {
        float cell = map.CellSize;
        float originX = (float)(nav?.Origin.X ?? 0), originY = (float)(nav?.Origin.Y ?? 0), originZ = (float)(nav?.Origin.Z ?? 0);
        var roomOfCell = new int[map.Columns * map.Rows];
        Array.Fill(roomOfCell, -1);
        var rooms = ImmutableArray.CreateBuilder<LevelRoom>(map.Rooms.Length);
        for (int i = 0; i < map.Rooms.Length; i++)
        {
            Map2dRoom r = map.Rooms[i];
            for (int y = r.CellY; y < r.CellY + r.CellsY; y++)
                for (int x = r.CellX; x < r.CellX + r.CellsX; x++)
                    roomOfCell[y * map.Columns + x] = i;
            var (family, low, jump) = Facts(r.Name);
            Nav3dRoomRole transit = nav is null ? Nav3dRoomRole.None : nav.Cell(r.CellY * nav.Columns + r.CellX).Role;
            rooms.Add(new LevelRoom(i, r.Name, family, r.CellX, r.CellY, r.CellsX, r.CellsY, r.Rotation,
                new Vector(originX + r.CellX * cell, originY + r.CellY * cell, originZ),
                new Vector(originX + (r.CellX + r.CellsX) * cell, originY + (r.CellY + r.CellsY) * cell, originZ + r.Height),
                RoleOf(transit, family), low, jump, r.Label));
        }
        return (rooms.MoveToImmutable(), roomOfCell);
    }

    private static (ImmutableArray<LevelRoom>, int[]) RoomsOf(Nav3dReader nav)
    {
        float cell = nav.CellSize;
        var roomOfCell = new int[nav.CellCount];
        Array.Fill(roomOfCell, -1);
        var rooms = ImmutableArray.CreateBuilder<LevelRoom>();
        for (int c = 0; c < nav.CellCount; c++)
        {
            Nav3dCell placed = nav.Cell(c);
            if (placed.Room is not { } name || roomOfCell[c] >= 0)
                continue;
            // The south-west cell of a larger room comes first in cell order; its footprint (the
            // name's, turned with the room) says which of the cells after it, naming it too, are its.
            int x = c % nav.Columns, y = c / nav.Columns;
            int w = 1, d = 1;
            if (RoomName.TryParse(name, out RoomShape? shape, out _) && shape!.IsMultiCell)
                (w, d) = placed.Rotation % 2 == 0 ? (shape.CellsX, shape.CellsY) : (shape.CellsY, shape.CellsX);
            int top = nav.CellHeight(c);
            for (int dy = 0; dy < d; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    int at = (y + dy) * nav.Columns + x + dx;
                    if (x + dx < nav.Columns && y + dy < nav.Rows && nav.Cell(at).Room == name && roomOfCell[at] < 0)
                    {
                        roomOfCell[at] = rooms.Count;
                        top = Math.Max(top, nav.CellHeight(at));
                    }
                }
            var (family, low, jump) = Facts(name);
            rooms.Add(new LevelRoom(rooms.Count, name, family, x, y, w, d, placed.Rotation,
                new Vector((float)nav.Origin.X + x * cell, (float)nav.Origin.Y + y * cell, (float)nav.Origin.Z),
                new Vector((float)nav.Origin.X + (x + w) * cell, (float)nav.Origin.Y + (y + d) * cell,
                    (float)nav.Origin.Z + top * nav.VoxelSize),
                RoleOf(placed.Role, family), low, jump, string.Empty));
        }
        return (rooms.ToImmutable(), roomOfCell);
    }

    /// <summary>One door a joined doorway: the two sides' records are matched by the opening's middle.</summary>
    private static ImmutableArray<LevelDoor> DoorsOf(Map2dLevel map, ImmutableArray<LevelRoom> rooms)
    {
        var doors = new List<LevelDoor>();
        foreach (Map2dDoor d in map.Doors)
        {
            if (!d.Open || (uint)d.Neighbour >= (uint)rooms.Length || (uint)d.Placement >= (uint)rooms.Length || d.Neighbour == d.Placement)
                continue;
            int a = Math.Min(d.Placement, d.Neighbour), b = Math.Max(d.Placement, d.Neighbour);
            var mid = new Vector((d.X0 + d.X1) / 2, (d.Y0 + d.Y1) / 2, d.ZLow);
            int seen = doors.FindIndex(have => have.RoomA == a && have.RoomB == b && (have.Position - mid).Length <= SameDoorway);
            if (seen >= 0)
            {
                // The other side of a doorway already met: the lower room's socket names it.
                if (d.Placement == a)
                    doors[seen] = doors[seen] with { Socket = d.Socket };
                continue;
            }
            float dx = d.X1 - d.X0, dy = d.Y1 - d.Y0;
            doors.Add(new LevelDoor(doors.Count, a, b, mid, NormalInto(mid, new Vector(dx, dy, 0), rooms[b]), MathF.Sqrt(dx * dx + dy * dy),
                d.ZHigh - d.ZLow, d.Socket));
        }
        return [.. doors];
    }

    private static ImmutableArray<LevelDoor> DoorsOf(Nav3dReader nav, ImmutableArray<LevelRoom> rooms, int[] roomOfCell)
    {
        var doors = ImmutableArray.CreateBuilder<LevelDoor>();
        for (int p = 0; p < nav.PoiCount; p++)
        {
            Nav3dPoi poi = nav.Poi(p);
            if ((poi.Flags & (Nav3dPoiFlags.Door | Nav3dPoiFlags.Joined)) != (Nav3dPoiFlags.Door | Nav3dPoiFlags.Joined) || poi.Door < 0)
                continue;
            Nav3dDoor door = nav.Door(poi.Door);
            if (door.Other < 0 || (uint)door.Cell >= (uint)roomOfCell.Length)
                continue;
            Nav3dDoor other = nav.Door(door.Other);
            if ((uint)other.Cell >= (uint)roomOfCell.Length)
                continue;
            int ra = roomOfCell[door.Cell], rb = roomOfCell[other.Cell];
            if (ra < 0 || rb < 0 || ra == rb)
                continue;
            int a = Math.Min(ra, rb), b = Math.Max(ra, rb);
            var at = new Vector((float)poi.Position.X, (float)poi.Position.Y, (float)poi.Position.Z);
            if (doors.Any(have => have.RoomA == a && have.RoomB == b && (have.Position - at).Length <= SameDoorway))
                continue;
            // A door point faces out of its room across the cell face: the face runs across that facing.
            float yaw = poi.Yaw * MathF.PI / 180;
            var along = new Vector(-MathF.Sin(yaw), MathF.Cos(yaw), 0);
            doors.Add(new LevelDoor(doors.Count, a, b, at, NormalInto(at, along, rooms[b]), KitDoorWidth, KitDoorHeight,
                ra == a ? door.Name : string.Empty));
        }
        return doors.ToImmutable();
    }

    /// <summary>The unit normal of an opening that runs along <paramref name="along"/>, pointing into <paramref name="into"/>.</summary>
    private static Vector NormalInto(Vector at, Vector along, LevelRoom into)
    {
        var n = new Vector(along.Y, -along.X, 0).Normalized;
        Vector toward = into.Centre - at;
        return n.X * toward.X + n.Y * toward.Y >= 0 ? n : n * -1f;
    }

    private static ImmutableArray<LevelMarker> MarkersOf(Nav3dReader nav, Map2dLevel? map, ImmutableArray<LevelRoom> rooms, int[] roomOfCell, int columns)
    {
        var markers = ImmutableArray.CreateBuilder<LevelMarker>();
        for (int p = 0; p < nav.PoiCount; p++)
        {
            Nav3dPoi poi = nav.Poi(p);
            if ((poi.Flags & Nav3dPoiFlags.Door) != 0 || KindOf(poi.Type) is not { } kind)
                continue;
            var at = new Vector((float)poi.Position.X, (float)poi.Position.Y, (float)poi.Position.Z);
            int room = poi.Cell < (uint)roomOfCell.Length ? roomOfCell[poi.Cell] : -1;
            string label = string.Empty;
            if (map is not null && kind == LevelMarkerKind.MapMarker)
                label = map.Markers.FirstOrDefault(m => MathF.Abs(m.X - at.X) <= 1 && MathF.Abs(m.Y - at.Y) <= 1 && MathF.Abs(m.Z - at.Z) <= 1)?.Label ?? string.Empty;
            markers.Add(new LevelMarker(markers.Count, poi.Type, kind, at, poi.Yaw, room, label));
        }
        return markers.ToImmutable();
    }

    private static ImmutableArray<ArrivalPoint> ArrivalsOf(Nav3dReader nav, ImmutableArray<LevelMarker> markers, ImmutableArray<LevelRoom> rooms)
    {
        int up = rooms.IndexOf(rooms.FirstOrDefault(r => r.Role == LevelRoomRole.Arrival)!);
        var points = ImmutableArray.CreateBuilder<ArrivalPoint>();
        if (up >= 0)
        {
            foreach (LevelMarker m in markers.Where(m => m.Room == up && m.Kind == LevelMarkerKind.Spawn))
                points.Add(new ArrivalPoint(m.Position, m.Yaw, up, PieceSource.Shipped));
            foreach (LevelMarker m in markers.Where(m => m.Room == up && m.Kind == LevelMarkerKind.Arrival))
                points.Add(new ArrivalPoint(m.Position, m.Yaw, up, PieceSource.Shipped));
        }

        if (points.Count == 0 && nav.TryGetSpawn(out var spawn, out float yaw))
        {
            var at = new Vector((float)spawn.X, (float)spawn.Y, (float)spawn.Z);
            int room = -1;
            for (int i = 0; i < rooms.Length && room < 0; i++)
                if (rooms[i].Contains(at))
                    room = i;
            points.Add(new ArrivalPoint(at, yaw, room, PieceSource.Shipped));
        }
        return points.ToImmutable();
    }

    private static LevelExit? ExitOf(Map2dLevel? map, Nav3dReader? nav, ImmutableArray<LevelMarker> markers, ImmutableArray<LevelRoom> rooms,
        int depth, out PieceSource source)
    {
        if (map?.Markers.FirstOrDefault(m => m.Kind == "exit_down") is { } down && (uint)down.Placement < (uint)rooms.Length)
        {
            source = PieceSource.Shipped;
            return new LevelExit(new Vector(down.X, down.Y, down.Z), down.Placement, down.Label);
        }

        int exitRoom = rooms.IndexOf(rooms.FirstOrDefault(r => r.Role == LevelRoomRole.Exit)!);
        if (nav is not null && nav.DownArrivalPoi >= 0 && exitRoom >= 0)
        {
            var p = nav.PoiPosition(nav.DownArrivalPoi);
            source = PieceSource.Derived;
            return new LevelExit(new Vector((float)p.X, (float)p.Y, (float)p.Z), exitRoom,
                string.Create(CultureInfo.InvariantCulture, $"tf2_descent-{depth + 1}"));
        }

        source = PieceSource.Absent;
        return null;
    }
}
