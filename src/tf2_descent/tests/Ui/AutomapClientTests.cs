extern alias client;

using SourceSharp.Abi;

using TF2.Descent.Server.Automap;
using TF2.Descent.Server.Level;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Hero;
using TF2.Descent.Tests.Level;

using Xunit;

using AutomapRoomWire = TF2.Descent.Shared.AutomapRoom;
using CVector = SourceSharp.Abi.Vector;
using IClientUserMessage = client::SourceSharp.Abi.IUserMessage;
using ClientReader = client::SourceSharp.Abi.UserMessageReader;
using ClientMark = client::TF2.Descent.Shared.AutomapMarkKind;
using DescentMapMarker = TF2.Descent.Client.Ui.DescentMapMarker;
using IAutomapHook = client::TF2.Descent.Shared.IAutomapHook;
using IAutomapWorld = client::TF2.Descent.Client.Ui.IAutomapWorld;
using IGameClock = client::SourceSharp.Abi.IGameClock;
using RoomCache = client::TF2.Descent.Client.Automap.AutomapRoomCache;
using Source = client::TF2.Descent.Client.Ui.AutomapSource;
using Labels = client::TF2.Descent.Client.Ui.AutomapRoomLabelsSource;
using Prefs = client::TF2.Descent.Client.Ui.AutomapPrefsSource;
using ZoomConVar = client::TF2.Descent.Client.Ui.AutomapZoomConVar;
using LabelsConVar = client::TF2.Descent.Client.Ui.AutomapLabelsConVar;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The automap on the client (plan_descent_logic UI5): the cache holds exactly the rooms the
/// server sent, fed through the client's own handlers from the bytes the server's payloads write;
/// it forgets them at a sync and at a level's end; the source lays them on the canvas's grid with
/// the hero, his storey and heading, the door stubs and the marks.
/// </summary>
public sealed class AutomapClientTests
{
    private static readonly LevelData Level = LevelFixture.Committed().Read();

    private sealed class Hooks<T>(params T[] hooks) : client::SourceSharp.Abi.IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private static byte[] Bytes<T>(T payload) where T : IUserMessagePayload<T>
    {
        var buffer = new byte[255];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the payload fits");
        return buffer[..n];
    }

    /// <summary>The client's cache behind its two handlers.</summary>
    private sealed class ClientWire
    {
        public readonly RoomCache Cache = new();
        private readonly client::TF2.Descent.Shared.AutomapRoomMessage _room;
        private readonly client::TF2.Descent.Shared.AutomapSyncMessage _sync;

        public ClientWire()
        {
            var hooks = new Hooks<IAutomapHook>(Cache);
            _room = new(hooks);
            _sync = new(hooks);
        }

        public void Room(AutomapRoomWire room) => Deliver(_room, Bytes(room));

        public void Sync() => Deliver(_sync, Bytes(new AutomapSync()));

        /// <summary>Every automap message the server sent, delivered in order.</summary>
        public void Replay(RecordingEngine server)
        {
            foreach (var call in server.Calls.Where(c => c.Member == "UserMessages.Send"))
            {
                string key = call.ArgAs<string>(0);
                byte[] bytes = call.ArgAs<byte[]>(4);
                if (key == KeyOf<AutomapRoomWire>())
                    Deliver(_room, bytes);
                else if (key == KeyOf<AutomapSync>())
                    Deliver(_sync, bytes);
            }
        }

        private static void Deliver(IClientUserMessage handler, byte[] bytes) => handler.OnReceived(new ClientReader(bytes));
    }

    private static string KeyOf<T>() where T : IUserMessagePayload<T> => T.Key;

    private sealed class Levels : ILevelData
    {
        public LevelData? Current => Level;
        public TF2.Descent.Ai.Level.HordeLevel? Horde => null;
    }

    private static AutomapRoomWire Room(int id, short minX, short minY, short maxX, short maxY, string label = "",
                                        short[]? doorX = null, short[]? doorY = null, byte[]? marks = null, bool discovered = false) =>
        new()
        {
            Discovered = (byte)(discovered ? 1 : 0),
            Room = (byte)id, Storey = 0, MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, Label = label,
            DoorX = doorX ?? [], DoorY = doorY ?? [], DoorWidth = [.. (doorX ?? []).Select(_ => (short)208)],
            DoorAlongY = [.. (doorX ?? []).Select(_ => (byte)1)],
            MarkKind = marks ?? [], MarkX = [.. (marks ?? []).Select(_ => (short)(minX + 100))],
            MarkY = [.. (marks ?? []).Select(_ => (short)(minY + 100))],
        };

    //-------------------------------------------------------------------------
    // The cache
    //-------------------------------------------------------------------------

    [Fact]
    public void An_unentered_room_is_absent_from_what_the_client_holds()
    {
        // The server's own sender, over the committed level: the hero walks the arrival room and one more.
        var hero = new HeroRig();
        var server = new RecordingEngine();
        var at = new Vector(-99_999, -99_999, -99_999);
        server.ReturnsEachCall("Entities.GetOrigin", () => at);
        var automap = new AutomapService(new Levels(), hero.Heroes, hero.Sheets, server, server, new Party.OneParty(),
                                         new Server.Party.InMemoryParties(hero.Rules, new Party.PartyChanges().Hooks),
                                         new Server.Party.EnginePartySlots(hero.Heroes, hero.Keys()));
        hero.Join();
        int arrival = Level.Rooms.Single(r => r.Role == LevelRoomRole.Arrival).Id;
        int next = Level.Neighbours(arrival).First();
        foreach (int room in new[] { arrival, next })
        {
            var r = Level.Rooms[room];
            at = new Vector(r.Centre.X, r.Centre.Y, r.Min.Z + 16);
            automap.Poll();
        }
        var client = new ClientWire();

        client.Replay(server);

        // Explored: the two entered. Discovered: only rooms through a door of one of them.
        Assert.Equal(new[] { arrival, next }.Order(), client.Cache.Rooms.Where(r => !r.Discovered).Select(r => r.Room));
        var beside = Level.Neighbours(arrival).Concat(Level.Neighbours(next)).ToHashSet();
        Assert.All(client.Cache.Rooms.Where(r => r.Discovered), r => Assert.Contains(r.Room, beside));
        var held = client.Cache.Rooms.Select(r => r.Room).ToHashSet();
        Assert.Contains(Level.Rooms, r => !held.Contains(r.Id));    // the level has rooms the client must not hold
    }

    [Fact]
    public void A_discovered_room_is_held_until_its_explored_one_replaces_it()
    {
        var w = new ClientWire();
        w.Room(Room(4, 0, 0, 1024, 1024, discovered: true));
        Assert.True(w.Cache.Rooms.Single().Discovered);

        w.Room(Room(4, 0, 0, 1024, 1024, "Crossing"));

        var room = w.Cache.Rooms.Single();
        Assert.Equal((false, "Crossing"), (room.Discovered, room.Label));
    }

    [Fact]
    public void A_discovered_outline_never_replaces_an_explored_room()
    {
        var w = new ClientWire();
        w.Room(Room(4, 0, 0, 1024, 1024, "Crossing"));

        w.Room(Room(4, 0, 0, 1024, 1024, discovered: true));

        Assert.Equal((false, "Crossing"), (w.Cache.Rooms.Single().Discovered, w.Cache.Rooms.Single().Label));
    }

    [Fact]
    public void A_room_sent_is_held_with_its_outline_and_label()
    {
        var client = new ClientWire();

        client.Room(Room(4, 1024, 2048, 2048, 4096, "Way in"));

        var room = Assert.Single(client.Cache.Rooms);
        Assert.Equal((4, 1024, 2048, 2048, 4096, "Way in"), (room.Room, room.MinX, room.MinY, room.MaxX, room.MaxY, room.Label));
    }

    [Fact]
    public void A_room_sent_again_replaces_the_one_held()
    {
        var client = new ClientWire();
        client.Room(Room(4, 0, 0, 1024, 1024, marks: [(byte)ClientMark.ShrineUnused]));

        client.Room(Room(4, 0, 0, 1024, 1024, marks: [(byte)ClientMark.ShrineUsed]));

        var room = Assert.Single(client.Cache.Rooms);
        Assert.Equal(ClientMark.ShrineUsed, Assert.Single(room.Marks).Kind);
    }

    [Fact]
    public void A_sync_forgets_every_room()
    {
        var client = new ClientWire();
        client.Room(Room(1, 0, 0, 1024, 1024));
        client.Room(Room(2, 1024, 0, 2048, 1024));

        client.Sync();

        Assert.Empty(client.Cache.Rooms);
    }

    [Fact]
    public void The_end_of_a_level_forgets_every_room()
    {
        var client = new ClientWire();
        client.Room(Room(1, 0, 0, 1024, 1024));
        Assert.Single(client.Cache.Rooms);

        ((client::SourceSharp.Client.IClientLevelHook)client.Cache).OnLevelShutdown();

        Assert.Empty(client.Cache.Rooms);
    }

    //-------------------------------------------------------------------------
    // The source
    //-------------------------------------------------------------------------

    private sealed class World : IAutomapWorld
    {
        public CVector? LocalOrigin { get; set; }
        public float LocalYaw { get; set; }
        public List<CVector> Party { get; } = [];
        public void PartyPositions(List<CVector> into)
        {
            into.Clear();
            into.AddRange(Party);
        }
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 1f;
    }

    private sealed class Kit
    {
        public readonly ClientWire Wire = new();
        public readonly World World = new();
        public readonly Clock Clock = new();
        public readonly ZoomConVar Zoom = DeclaredClientConVars.Fresh<ZoomConVar>();
        public readonly Source Source;

        public Kit() => Source = new Source(Wire.Cache, World, Clock, Zoom);

        public void Frame() => Clock.Now += 0.016f;
    }

    [Fact]
    public void Nothing_revealed_is_nothing_drawn()
    {
        var k = new Kit();

        Assert.Equal((0, 0, 0, 0), (k.Source.RoomCount, k.Source.DoorStubCount, k.Source.MarkerCount, k.Source.StoreyCount));
    }

    [Fact]
    public void One_room_is_fitted_into_the_least_extent_and_centred()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024));

        Assert.Equal(1, k.Source.RoomCount);
        // 1024 units of room, padded 512 each side = 2048 < the least 4096: a quarter of the grid, centred.
        Assert.Equal(256f, k.Source.RoomW(0), 3);
        Assert.Equal(256f, k.Source.RoomH(0), 3);
        Assert.Equal(384f, k.Source.RoomX(0), 3);
        Assert.Equal(384f, k.Source.RoomY(0), 3);
    }

    [Fact]
    public void A_discovered_room_is_drawn_as_discovered_and_an_explored_one_is_not()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024, "Way in"));
        k.Wire.Room(Room(2, 1024, 0, 2048, 1024, discovered: true));

        Assert.Equal(2, k.Source.RoomCount);
        Assert.Equal((false, true), (k.Source.RoomDiscovered(0), k.Source.RoomDiscovered(1)));
    }

    [Fact]
    public void A_party_member_in_a_discovered_room_is_not_marked()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024, discovered: true));
        k.World.Party.Add(new CVector(512, 512, 0));

        Assert.Equal(0, k.Source.MarkerCount);
    }

    [Fact]
    public void North_is_up_on_the_grid()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024));
        k.Wire.Room(Room(2, 0, 1024, 1024, 2048));     // the room to the north

        Assert.True(k.Source.RoomY(1) < k.Source.RoomY(0));
    }

    [Fact]
    public void The_map_zooms_out_as_more_is_revealed()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024));
        float before = k.Source.RoomW(0);

        for (int i = 2; i <= 6; i++)
            k.Wire.Room(Room(i, (short)(1024 * (i - 1)), 0, (short)(1024 * i), 1024));
        k.Frame();

        Assert.True(k.Source.RoomW(0) < before, $"{k.Source.RoomW(0)} is not under {before}");
    }

    [Fact]
    public void The_hero_is_placed_and_faces_as_he_looks()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024));
        k.World.LocalOrigin = new CVector(512, 512, 0);
        k.World.LocalYaw = 0f;                          // looking east

        Assert.Equal((512f, 512f), (k.Source.HeroX, k.Source.HeroY));
        Assert.Equal(90f, k.Source.HeroHeadingDeg, 3);  // the canvas's 0 is north, clockwise
        k.World.LocalYaw = 90f;                         // north
        k.Frame();
        Assert.Equal(0f, k.Source.HeroHeadingDeg, 3);
    }

    [Fact]
    public void A_door_both_its_rooms_carry_is_one_stub()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024, doorX: [1024], doorY: [512]));
        k.Wire.Room(Room(2, 1024, 0, 2048, 1024, doorX: [1024], doorY: [512]));

        Assert.Equal(1, k.Source.DoorStubCount);
        Assert.True(k.Source.DoorStubH(0) > k.Source.DoorStubW(0));   // the wall runs north-south
    }

    [Fact]
    public void A_servers_mark_is_drawn_in_its_room()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024, marks: [(byte)ClientMark.Stairs]));

        var mark = k.Source.Marker(Assert.IsType<int>(k.Source.MarkerCount) - 1);
        Assert.Equal((DescentMapMarker.Stairs, 0, 1), (mark.Kind, mark.RoomIndex, mark.Storey));
    }

    [Fact]
    public void A_party_member_shows_only_in_a_revealed_room()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024));
        k.World.Party.Add(new CVector(5000, 5000, 0));   // in no revealed room
        Assert.Equal(0, k.Source.MarkerCount);

        k.World.Party.Add(new CVector(200, 200, 0));
        k.Frame();

        Assert.Equal(DescentMapMarker.Party, Assert.IsType<int>(k.Source.MarkerCount) == 1 ? k.Source.Marker(0).Kind : default);
    }

    [Fact]
    public void The_heros_storey_is_the_revealed_room_he_stands_in()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024));
        k.Wire.Room(Room(2, 1024, 0, 2048, 1024) with { Storey = 1 });
        k.World.LocalOrigin = new CVector(1500, 500, 0);

        Assert.Equal((2, 2), (k.Source.StoreyCount, k.Source.HeroStorey));
        Assert.False(k.Source.RoomCurrentStorey(0));
        Assert.True(k.Source.RoomCurrentStorey(1));
    }

    [Fact]
    public void Zoomed_in_the_map_centres_on_the_hero()
    {
        var k = new Kit();
        k.Wire.Room(Room(1, 0, 0, 1024, 1024));
        k.World.LocalOrigin = new CVector(100, 900, 0);
        Assert.NotEqual(512f, k.Source.HeroX);          // before: the fit's middle is the room's, not his

        k.Zoom.Hold("2");
        k.Frame();

        Assert.Equal((512f, 512f), (k.Source.HeroX, k.Source.HeroY));
    }

    [Fact]
    public void Room_labels_are_the_servers_by_the_sources_room_order()
    {
        var k = new Kit();
        k.Wire.Room(Room(7, 0, 0, 1024, 1024, "Way down"));
        k.Wire.Room(Room(3, 1024, 0, 2048, 1024, "Way in"));
        var labels = new Labels(k.Wire.Cache);

        Assert.Equal(("Way in", "Way down", ""), (labels.RoomLabel(0), labels.RoomLabel(1), labels.RoomLabel(2)));
    }

    [Fact]
    public void Labels_start_as_the_clients_convar_says()
    {
        var on = DeclaredClientConVars.Fresh<LabelsConVar>();
        var off = DeclaredClientConVars.Holding<LabelsConVar>("0");

        Assert.True(new Prefs(on).ShowLabelsByDefault);
        Assert.False(new Prefs(off).ShowLabelsByDefault);
    }
}
