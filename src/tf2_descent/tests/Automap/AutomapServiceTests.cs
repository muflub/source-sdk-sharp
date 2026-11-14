using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Server.Automap;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Level;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Hero;
using TF2.Descent.Tests.Level;

using Xunit;

using AutomapRoomWire = TF2.Descent.Shared.AutomapRoom;

namespace TF2.Descent.Tests.Automap;

/// <summary>
/// The automap revealed room by room by the server (plan_descent_logic UI5; plan_tf2_rpg 2m):
/// a room reaches the hero's client when he stands in it and never before, with its outline,
/// door stubs and markers; a spawn resyncs; a level starts every map empty. Over the committed
/// depth 1 seed 1 level.
/// </summary>
public sealed class AutomapServiceTests
{
    private static readonly LevelData Level = LevelFixture.Committed().Read();

    private sealed class Levels : ILevelData
    {
        public LevelData? Current { get; set; } = Level;
        public TF2.Descent.Ai.Level.HordeLevel? Horde => null;
    }

    private sealed class Kit
    {
        public readonly HeroRig Hero = new();
        public readonly RecordingEngine Wire = new();
        public readonly Levels Levels = new();
        public readonly AutomapService Automap;
        public readonly HeroComponent Component;
        public Vector At = new(-99_999, -99_999, -99_999);

        public Kit()
        {
            Wire.ReturnsEachCall("Entities.GetOrigin", () => At);
            Automap = new AutomapService(Levels, Hero.Heroes, Hero.Sheets, Wire, Wire, new Party.OneParty(),
                                         new Server.Party.InMemoryParties(Hero.Rules, new Party.PartyChanges().Hooks),
                                         new Server.Party.EnginePartySlots(Hero.Heroes, Hero.Keys()));
            Component = Hero.Join()!;
        }

        /// <summary>The hero on the floor of room <paramref name="room"/>, in its middle, and one look.</summary>
        public void StandIn(int room)
        {
            var r = Level.Rooms[room];
            At = new Vector(r.Centre.X, r.Centre.Y, r.Min.Z + 16);
            Assert.Equal(room, Level.RoomAt(At));       // the stimulus is where it means to be
            Automap.Poll();
        }

        public IReadOnlyList<(string Key, int Player, byte[] Bytes)> Sent =>
            [.. Wire.Calls.Where(c => c.Member == "UserMessages.Send").Select(c => (c.ArgAs<string>(0), c.ArgAs<int>(2), c.ArgAs<byte[]>(4)))];

        public IReadOnlyList<AutomapRoomView> Rooms(int from = 0) =>
            [.. Sent.Skip(from).Where(s => s.Key == KeyOf<AutomapRoomWire>()).Select(s =>
            {
                var reader = new UserMessageReader(s.Bytes);
                return AutomapRoomWire.Read(ref reader).View();
            })];
    }

    private static string KeyOf<T>() where T : IUserMessagePayload<T> => T.Key;

    private static int Arrival => Level.Rooms.Single(r => r.Role == LevelRoomRole.Arrival).Id;

    private static int Exit => Level.Exit!.Room;

    private static int Neighbour(int room) => Level.Neighbours(room).First();

    [Fact]
    public void Standing_in_no_room_sends_nothing()
    {
        var k = new Kit();

        k.Automap.Poll();

        Assert.Empty(k.Sent);
    }

    [Fact]
    public void Standing_in_the_arrival_room_sends_it_with_its_outline()
    {
        var k = new Kit();

        k.StandIn(Arrival);

        var room = Assert.Single(k.Rooms());
        var r = Level.Rooms[Arrival];
        Assert.Equal((Arrival, (int)r.Min.X, (int)r.Min.Y, (int)r.Max.X, (int)r.Max.Y), (room.Room, room.MinX, room.MinY, room.MaxX, room.MaxY));
        Assert.Equal(r.Label, room.Label);
    }

    [Fact]
    public void An_unentered_room_is_absent_from_every_byte_sent()
    {
        var k = new Kit();
        string exitLabel = Level.Rooms[Exit].Label;
        Assert.NotEmpty(exitLabel);                      // "Way down": a needle the exit room alone would carry
        Assert.NotEqual(Arrival, Exit);

        k.StandIn(Arrival);
        k.StandIn(Neighbour(Arrival));

        Assert.DoesNotContain(Exit, k.Rooms().Select(r => r.Room));
        var needle = System.Text.Encoding.UTF8.GetBytes(exitLabel);
        Assert.All(k.Sent, s => Assert.True(s.Bytes.AsSpan().IndexOf(needle) < 0, $"{s.Key} carries the exit room's label"));
    }

    [Fact]
    public void The_exit_room_is_sent_once_entered_and_carries_its_label()
    {
        var k = new Kit();

        k.StandIn(Exit);

        var room = Assert.Single(k.Rooms());
        Assert.Equal((Exit, Level.Rooms[Exit].Label), (room.Room, room.Label));
    }

    [Fact]
    public void Each_room_entered_is_sent_once_and_only_it()
    {
        var k = new Kit();
        int next = Neighbour(Arrival);

        k.StandIn(Arrival);
        k.StandIn(next);
        k.StandIn(Arrival);
        k.StandIn(next);

        Assert.Equal([Arrival, next], k.Rooms().Select(r => r.Room));
    }

    [Fact]
    public void A_room_carries_a_stub_for_every_door_it_has()
    {
        var k = new Kit();

        k.StandIn(Arrival);

        var room = Assert.Single(k.Rooms());
        var doors = Level.Doors.Where(d => d.RoomA == Arrival || d.RoomB == Arrival).ToList();
        Assert.NotEmpty(doors);
        Assert.Equal(doors.Select(d => ((int)d.Position.X, (int)d.Position.Y)).Order(),
                     room.Doors.Select(d => (d.X, d.Y)).Order());
    }

    [Fact]
    public void The_way_down_is_marked_only_once_its_room_is_revealed()
    {
        var k = new Kit();
        k.StandIn(Arrival);
        Assert.DoesNotContain(k.Rooms(), r => r.Room == Exit);
        int from = k.Sent.Count;

        k.StandIn(Exit);

        var room = Assert.Single(k.Rooms(from));
        var exit = Level.Exit!.Position;
        Assert.Contains(new AutomapMark(AutomapMarkKind.Stairs, (int)MathF.Round(exit.X), (int)MathF.Round(exit.Y)), room.Marks);
    }

    [Fact]
    public void The_way_in_is_marked_in_the_arrival_room()
    {
        var k = new Kit();

        k.StandIn(Arrival);

        var room = Assert.Single(k.Rooms());
        var arrival = Level.Markers.Single(m => m.Kind == TF2.Descent.Server.Level.LevelMarkerKind.Arrival && m.Room == Arrival).Position;
        Assert.Contains(new AutomapMark(AutomapMarkKind.Stairs, (int)MathF.Round(arrival.X), (int)MathF.Round(arrival.Y)), room.Marks);
    }

    [Fact]
    public void The_wire_of_a_room_the_map_has_not_revealed_is_refused()
    {
        var k = new Kit();
        k.StandIn(Arrival);
        var map = k.Automap.MapOf(HeroRig.Index);

        Assert.NotNull(k.Automap.Wire(map, LevelAdapters.RoomKey(Arrival), "viewer"));      // the positive arm
        Assert.Null(k.Automap.Wire(map, LevelAdapters.RoomKey(Exit), "viewer"));
    }

    [Fact]
    public void Rooms_go_to_the_hero_alone()
    {
        var k = new Kit();

        k.StandIn(Arrival);

        Assert.NotEmpty(k.Sent);
        Assert.All(k.Sent, s => Assert.Equal(HeroRig.Index, s.Player));
    }

    [Fact]
    public void A_spawn_resyncs_the_whole_map_behind_a_sync()
    {
        var k = new Kit();
        k.StandIn(Arrival);
        k.StandIn(Neighbour(Arrival));
        int from = k.Sent.Count;

        k.Automap.OnSpawned(k.Component.Player, 0f);

        var sent = k.Sent.Skip(from).ToList();
        Assert.Equal(KeyOf<AutomapSync>(), sent[0].Key);
        Assert.Equal(new[] { Arrival, Neighbour(Arrival) }.Order(), k.Rooms(from).Select(r => r.Room).Order());
    }

    [Fact]
    public void A_new_level_starts_the_map_empty()
    {
        var k = new Kit();
        k.StandIn(Arrival);
        k.StandIn(Neighbour(Arrival));

        k.Automap.OnLevelShutdown();
        int from = k.Sent.Count;
        k.Automap.OnSpawned(k.Component.Player, 0f);
        k.StandIn(Arrival);

        var sync = k.Sent.Skip(from).First();
        var reader = new UserMessageReader(sync.Bytes);
        Assert.Equal(0, AutomapSync.Read(ref reader).Rooms);
        Assert.Equal([Arrival], k.Rooms(from).Select(r => r.Room));
    }

    [Fact]
    public void Off_a_level_nothing_is_revealed()
    {
        var k = new Kit();
        k.Levels.Current = null;

        k.At = Level.Rooms[Arrival].Centre;
        k.Automap.Poll();

        Assert.Empty(k.Sent);
    }

    [Fact]
    public void Reveal_all_sends_every_room_once()
    {
        var k = new Kit();
        k.StandIn(Arrival);
        int from = k.Sent.Count;

        k.Automap.RevealAll(HeroRig.Index);

        Assert.Equal(Level.Rooms.Select(r => r.Id).Where(r => r != Arrival).Order(), k.Rooms(from).Select(r => r.Room).Order());
    }

    [Fact]
    public void A_used_shrine_shows_used_on_the_map_of_a_hero_who_has_its_room()
    {
        var k = new Kit();
        var shrine = Level.Markers.First(m => m.Kind == TF2.Descent.Server.Level.LevelMarkerKind.Shrine);
        k.StandIn(shrine.Room);
        Assert.Contains(k.Rooms().Single().Marks, m => m.Kind == AutomapMarkKind.ShrineUnused);
        int from = k.Sent.Count;

        k.Automap.ShrineUsed(LevelAdapters.MarkerKey(shrine.Id));

        var room = Assert.Single(k.Rooms(from));
        Assert.Contains(room.Marks, m => m.Kind == AutomapMarkKind.ShrineUsed);
    }

    [Fact]
    public void Every_room_of_the_level_fits_one_user_message()
    {
        var k = new Kit();
        k.Automap.RevealAll(HeroRig.Index);
        var map = k.Automap.MapOf(HeroRig.Index);
        Assert.Equal(Level.Rooms.Length, map.RoomsSeen.Count);

        Span<byte> buffer = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        foreach (var room in map.RoomsSeen)
        {
            var wire = k.Automap.Wire(map, room, "viewer")!.Value;
            Assert.True(wire.Write(buffer) > 0, $"room {room} does not fit");
        }
    }

    [Fact]
    public void A_room_at_the_most_doors_and_marks_and_the_longest_label_fits_one_user_message()
    {
        var wire = new AutomapRoomWire
        {
            Room = 254, Storey = 3, MinX = short.MinValue, MinY = short.MinValue, MaxX = short.MaxValue, MaxY = short.MaxValue,
            Label = new string('x', AutomapRoomWire.MaxLabel),
            DoorX = new short[AutomapRoomWire.MaxDoors], DoorY = new short[AutomapRoomWire.MaxDoors],
            DoorWidth = new short[AutomapRoomWire.MaxDoors], DoorAlongY = new byte[AutomapRoomWire.MaxDoors],
            MarkKind = new byte[AutomapRoomWire.MaxMarks], MarkX = new short[AutomapRoomWire.MaxMarks], MarkY = new short[AutomapRoomWire.MaxMarks],
        };

        Span<byte> buffer = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        Assert.True(wire.Write(buffer) > 0);
    }
}
