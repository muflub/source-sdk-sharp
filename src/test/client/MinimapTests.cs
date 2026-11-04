using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The minimap's outline, from a map's file, and who gets a dot.</summary>
public class MinimapTests
{
    /// <summary>A small BSP: a world of faces, each a rectangle at a height, and an entity lump.</summary>
    private sealed class Bsp
    {
        private readonly List<(float X0, float Y0, float X1, float Y1, float NormalZ, int Side, int Flags)> _faces = [];
        private readonly StringBuilder _entities = new();

        public Bsp Floor(float x0, float y0, float x1, float y1, float normalZ = 1f, int side = 0, int flags = 0)
        {
            _faces.Add((x0, y0, x1, y1, normalZ, side, flags));
            return this;
        }

        public Bsp Spawn(float x, float y)
        {
            _entities.Append($"{{\n\"classname\" \"info_player_deathmatch\"\n\"origin\" \"{x} {y} 0\"\n}}\n");
            return this;
        }

        public byte[] Bytes()
        {
            using var planes = new MemoryStream();
            using var vertexes = new MemoryStream();
            using var edges = new MemoryStream();
            using var surfEdges = new MemoryStream();
            using var faces = new MemoryStream();
            using var texInfo = new MemoryStream();
            var p = new BinaryWriter(planes);
            var v = new BinaryWriter(vertexes);
            var e = new BinaryWriter(edges);
            var se = new BinaryWriter(surfEdges);
            var f = new BinaryWriter(faces);
            var t = new BinaryWriter(texInfo);

            // Edge 0 is unused, as a real map's is: a surfedge's sign needs a non-zero index.
            e.Write((ushort)0);
            e.Write((ushort)0);

            int face = 0, vertex = 0, edge = 1, surfEdge = 0;
            foreach (var r in _faces)
            {
                p.Write(0f);
                p.Write(0f);
                p.Write(r.NormalZ);
                p.Write(0f);
                p.Write(0);

                t.Write(new byte[64]);
                t.Write(r.Flags);
                t.Write(0);

                (float, float)[] corners = [(r.X0, r.Y0), (r.X1, r.Y0), (r.X1, r.Y1), (r.X0, r.Y1)];
                foreach ((float x, float y) in corners)
                {
                    v.Write(x);
                    v.Write(y);
                    v.Write(0f);
                }

                for (int i = 0; i < 4; i++)
                {
                    e.Write((ushort)(vertex + i));
                    e.Write((ushort)(vertex + (i + 1) % 4));
                    se.Write(edge + i);
                }

                f.Write((ushort)face);
                f.Write((byte)r.Side);
                f.Write((byte)0);
                f.Write(surfEdge);
                f.Write((short)4);
                f.Write((short)face);
                f.Write(new byte[44]);

                face++;
                vertex += 4;
                edge += 4;
                surfEdge += 4;
            }

            using var models = new MemoryStream();
            var m = new BinaryWriter(models);
            m.Write(new byte[40]);
            m.Write(0);
            m.Write(face);
            m.Write(0);

            var lumps = new Dictionary<int, byte[]>
            {
                [0] = Encoding.ASCII.GetBytes(_entities.ToString()),
                [1] = planes.ToArray(),
                [3] = vertexes.ToArray(),
                [6] = texInfo.ToArray(),
                [7] = faces.ToArray(),
                [12] = edges.ToArray(),
                [13] = surfEdges.ToArray(),
                [14] = models.ToArray(),
            };

            using var file = new MemoryStream();
            var w = new BinaryWriter(file);
            w.Write(Encoding.ASCII.GetBytes("VBSP"));
            w.Write(20);
            int offset = 8 + 64 * 16 + 4;
            for (int i = 0; i < 64; i++)
            {
                int length = lumps.TryGetValue(i, out byte[]? l) ? l.Length : 0;
                w.Write(length > 0 ? offset : 0);
                w.Write(length);
                w.Write(0);
                w.Write(0);
                offset += length;
            }

            w.Write(0);
            for (int i = 0; i < 64; i++)
                if (lumps.TryGetValue(i, out byte[]? l))
                    w.Write(l);

            return file.ToArray();
        }
    }

    private static MapOutline Outline(Bsp bsp) => Assert.IsType<MapOutline>(MapOutline.Build(bsp.Bytes()));

    private const float Room = 320f;

    [Fact]
    public void What_is_not_a_map_has_no_outline()
    {
        Assert.Null(MapOutline.Build(new byte[2000]));
        Assert.Null(MapOutline.Build([1, 2, 3]));
    }

    [Fact]
    public void A_map_with_no_floors_has_no_outline()
    {
        Assert.Null(MapOutline.Build(new Bsp().Spawn(0f, 0f).Bytes()));
    }

    [Fact]
    public void One_square_room_is_four_lines_round_the_square()
    {
        MapOutline outline = Outline(new Bsp().Floor(0f, 0f, Room, Room).Spawn(160f, 160f));

        Assert.Equal(4, outline.Segments.Length / 4);
        Assert.All(outline.Segments, v => Assert.InRange(v, -0.001f, 1.001f));
        Assert.Contains(outline.Segments, v => v < 0.01f);
        Assert.Contains(outline.Segments, v => v > 0.99f);
    }

    [Fact]
    public void A_position_projects_with_north_up_and_east_right()
    {
        MapOutline outline = Outline(new Bsp().Floor(0f, 0f, Room, Room).Spawn(160f, 160f));

        (float u, float v) = outline.Project(new Vector(Room, Room, 0f));
        (float u0, float v0) = outline.Project(new Vector(0f, 0f, 0f));

        Assert.True(u > u0, "east is right");
        Assert.True(v < v0, "north is up");
        Assert.Equal(0.5f, outline.Project(new Vector(160f, 160f, 0f)).U, 1);
    }

    [Fact]
    public void A_long_room_keeps_its_shape_centred_in_the_square()
    {
        MapOutline outline = Outline(new Bsp().Floor(0f, 0f, 1280f, 320f).Spawn(160f, 160f));

        (_, float bottom) = outline.Project(new Vector(0f, 0f, 0f));
        (_, float top) = outline.Project(new Vector(0f, 320f, 0f));

        Assert.Equal(0.25f, bottom - top, 1);
        Assert.Equal(0.5f, (top + bottom) / 2f, 1);
    }

    /// <summary>The 3D skybox is real floor, far off: nobody can reach it, so it is no part of the map.</summary>
    [Fact]
    public void A_floor_no_spawn_connects_to_is_left_out()
    {
        Bsp room = new Bsp().Floor(0f, 0f, Room, Room).Spawn(160f, 160f);
        int alone = Outline(room).Segments.Length;

        MapOutline withSkybox = Outline(room.Floor(8000f, 8000f, 8000f + Room, 8000f + Room));

        Assert.Equal(alone, withSkybox.Segments.Length);
        Assert.Equal(0.5f, withSkybox.Project(new Vector(160f, 160f, 0f)).U, 1);
    }

    [Fact]
    public void Floors_a_narrow_gap_apart_join_as_one_map()
    {
        MapOutline outline = Outline(new Bsp().Floor(0f, 0f, Room, Room).Floor(Room + 32f, 0f, 2f * Room, Room)
                                              .Spawn(160f, 160f));

        Assert.True(outline.Project(new Vector(2f * Room, 0f, 0f)).U > 0.99f, "the second room is in the frame");
    }

    [Theory]
    [InlineData(-1f, 0)]        // a ceiling
    [InlineData(0f, 0)]         // a wall
    [InlineData(1f, 1)]         // the back of an upward plane looks down
    public void Only_a_face_looking_up_is_a_floor(float normalZ, int side)
    {
        Bsp room = new Bsp().Floor(0f, 0f, Room, Room).Spawn(160f, 160f);
        int alone = Outline(room).Segments.Length;

        MapOutline with = Outline(room.Floor(Room, 0f, 2f * Room, Room, normalZ, side));

        Assert.Equal(alone, with.Segments.Length);
        Assert.True(with.Project(new Vector(Room, 0f, 0f)).U > 0.99f, "and the frame stays on the room");
    }

    [Theory]
    [InlineData(0x4)]           // SURF_SKY
    [InlineData(0x80)]          // SURF_NODRAW
    public void A_face_nobody_stands_on_is_no_floor(int flags)
    {
        MapOutline with = Outline(new Bsp().Floor(0f, 0f, Room, Room).Floor(Room, 0f, 2f * Room, Room, flags: flags)
                                           .Spawn(160f, 160f));

        Assert.True(with.Project(new Vector(Room, 0f, 0f)).U > 0.99f);
    }

    [Fact]
    public void A_huge_map_is_built_on_a_coarser_grid()
    {
        // Rooms in a line of gaps a busy grid would trace at length; the keep is bounded whatever the map.
        var bsp = new Bsp();
        for (int i = 0; i < 200; i++)
            bsp.Floor(i * 96f, 0f, i * 96f + 64f, 64f + i % 7 * 64f);
        bsp.Spawn(32f, 32f);

        Assert.InRange(Outline(bsp).Segments.Length / 4, 1, MapOutline.MaxSegments);
    }

    private sealed class Files(byte[]? map) : IEngineFiles
    {
        public int Reads;
        public string? Asked;
        public int GameFileSize(string path) => 0;
        public string? ReadGameFile(string path) => null;
        public byte[]? ReadGameFileBytes(string path)
        {
            Reads++;
            Asked = path;
            return map;
        }
        public byte[]? ReadFileOnPath(string path, string pathId) => null;
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class World : IMinimapWorld
    {
        public Vector LocalOrigin { get; set; } = new(160f, 160f, 0f);
        public float LocalYaw { get; set; }
        public readonly List<(Vector Origin, bool Seen)> Others = [];
        public int Asked;

        public void OtherPlayers(List<Vector> into)
        {
            Asked++;
            into.AddRange(Others.Select(o => o.Origin));
        }

        public bool OnScreen(Vector origin) => Others.First(o => o.Origin.Equals(origin)).Seen;
    }

    private static readonly byte[] OneRoom = new Bsp().Floor(0f, 0f, Room, Room).Spawn(160f, 160f).Bytes();

    /// <summary>The same room, for the markers' facts.</summary>
    internal static byte[] OneRoomBsp => OneRoom;

    private static (Minimap Map, Files Files, Clock Clock, World World) Make(byte[]? file = null)
    {
        var files = new Files(file ?? OneRoom);
        var clock = new Clock();
        var world = new World();
        var map = new Minimap(files, clock, world);
        map.OnLevelInit("dm_lockdown");
        return (map, files, clock, world);
    }

    /// <summary>"Dots for what the player can see on screen": one behind a wall or behind them has none.</summary>
    [Fact]
    public void Only_a_player_on_screen_gets_a_dot()
    {
        var (map, _, _, world) = Make();
        world.Others.Add((new Vector(100f, 100f, 0f), true));
        world.Others.Add((new Vector(200f, 200f, 0f), false));

        Assert.Equal(1, map.DotCount);
        Assert.True(map.Dot(0).U < 0.5f, "and it is the seen one");
    }

    [Fact]
    public void A_dot_moves_and_goes_with_its_player_but_only_ten_times_a_second()
    {
        var (map, _, clock, world) = Make();
        world.Others.Add((new Vector(100f, 100f, 0f), true));
        Assert.Equal(1, map.DotCount);
        int asked = world.Asked;

        world.Others.Clear();
        clock.Now += Minimap.RefreshSeconds / 2f;
        Assert.Equal(1, map.DotCount);
        Assert.Equal(asked, world.Asked);

        clock.Now += Minimap.RefreshSeconds;
        Assert.Equal(0, map.DotCount);
    }

    [Fact]
    public void The_map_is_read_once_a_level_and_by_name()
    {
        var (map, files, _, _) = Make();

        Assert.NotEmpty(map.Outline);
        Assert.NotEmpty(map.Outline);

        Assert.Equal(1, files.Reads);
        Assert.Equal("maps/dm_lockdown.bsp", files.Asked);

        map.OnLevelInit("dm_other");
        Assert.NotEmpty(map.Outline);
        Assert.Equal(2, files.Reads);
    }

    /// <summary>TF2 takes hud.minimap: its panel never asks, so its server's map is never read here.</summary>
    [Fact]
    public void Nothing_is_read_until_something_asks()
    {
        var (_, files, _, _) = Make();

        Assert.Equal(0, files.Reads);
    }

    [Fact]
    public void A_map_that_cannot_be_read_has_no_outline_and_no_dots()
    {
        var files = new Files(null);
        var world = new World();
        world.Others.Add((new Vector(100f, 100f, 0f), true));
        var map = new Minimap(files, new Clock(), world);
        map.OnLevelInit("gone");

        Assert.Empty(map.Outline);
        Assert.Equal(0, map.DotCount);
    }

    [Theory]
    [InlineData(90f, 0f)]       // looking north: straight up on the map
    [InlineData(0f, 90f)]       // looking east: to the right
    [InlineData(-90f, 180f)]    // looking south
    public void Facing_turns_clockwise_from_north(float yaw, float degrees)
    {
        var (map, _, _, world) = Make();
        world.LocalYaw = yaw;

        Assert.Equal(degrees * (MathF.PI / 180f), map.SelfFacing, 3);
    }

    [Fact]
    public void A_level_going_forgets_the_map()
    {
        var (map, _, _, _) = Make();
        Assert.NotEmpty(map.Outline);

        map.OnLevelShutdown();

        Assert.Empty(map.Outline);
    }

    private sealed class NeighbourHook : IClientLevelHook;

    /// <summary>
    /// Two level hooks at one order throw when the chain first runs, and then no level hook runs at
    /// all: the first build of this class did that to every hook in the game, over ClientRopeSystem's
    /// default 0, and only a live run saw it.
    /// </summary>
    [Fact]
    public void The_map_takes_a_hook_order_a_neighbour_at_the_default_does_not_collide_with()
    {
        var (map, _, _, _) = Make();
        var services = new ServiceContainer();
        services.AddInstance(map);
        services.AddInstance(new NeighbourHook());
        var hooks = new Hooks<IClientLevelHook>(services, new RecordingLog());

        Assert.Equal(2, hooks.All.Count);
    }
}
