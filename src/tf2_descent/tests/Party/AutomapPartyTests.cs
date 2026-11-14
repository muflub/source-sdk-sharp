using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Server.Automap;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Party;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Hero;
using TF2.Descent.Tests.Level;

using Xunit;

using AutomapRoomWire = TF2.Descent.Shared.AutomapRoom;

namespace TF2.Descent.Tests.Party;

/// <summary>
/// The party shares one map (WO9; plan_tf2_rpg 2m): a room a member walks into reaches every
/// member's client, and never a stranger's; a room nobody entered reaches nobody; a hero who joins
/// is sent the party's map; one who leaves keeps what he was shown and hears no more. Over the
/// committed depth 1 seed 1 level, three heroes: A and B, and C.
/// </summary>
public sealed class AutomapPartyTests
{
    private const int A = 3, B = 4, C = 5;

    private static readonly LevelData Level = LevelFixture.Committed().Read();

    private sealed class Levels : ILevelData
    {
        public LevelData? Current { get; } = Level;
        public TF2.Descent.Ai.Level.HordeLevel? Horde => null;
    }

    private readonly HeroRig _rig = new();
    private readonly RecordingEngine _wire = new();
    private readonly Dictionary<EntityHandle, Vector> _at = new();
    private readonly InMemoryParties _parties;
    private readonly AutomapService _automap;
    private readonly List<IPartyHook> _chain = [];

    public AutomapPartyTests()
    {
        _wire.ReturnsEachCall("Entities.GetOrigin", () => _at.GetValueOrDefault(_wire.Calls[^1].ArgAs<EntityHandle>(0), new Vector(-99_999, -99_999, -99_999)));
        _parties = new InMemoryParties(_rig.Rules, new HeroRig.ListHooks<IPartyHook>(_chain));
        var slots = new EnginePartySlots(_rig.Heroes, _rig.Keys());
        _automap = new AutomapService(new Levels(), _rig.Heroes, _rig.Sheets, _wire, _wire, new PartyRoster(_parties, slots), _parties, slots);
        _chain.Add(_automap);
        foreach (int slot in new[] { A, B, C })
            Assert.NotNull(_rig.Join(slot));
    }

    private string Key(int slot) => _rig.Keys().Of(slot)!;

    private async Task Party(params int[] slots) => Assert.True((await _parties.Make([.. slots.Select(Key)])).Ok);

    private void StandIn(int slot, int room)
    {
        var r = Level.Rooms[room];
        _at[HeroRig.PlayerOf(slot)] = new Vector(r.Centre.X, r.Centre.Y, r.Min.Z + 16);
        _automap.Poll();
    }

    /// <summary>The room ids sent to <paramref name="slot"/>'s client.</summary>
    private HashSet<int> RoomsOf(int slot) =>
    [
        .. _wire.Calls.Where(c => c.Member == "UserMessages.Send" && c.ArgAs<string>(0) == KeyOf<AutomapRoomWire>() && c.ArgAs<int>(2) == slot)
            .Select(c =>
            {
                var reader = new UserMessageReader(c.ArgAs<byte[]>(4));
                return AutomapRoomWire.Read(ref reader).View().Room;
            }),
    ];

    private static string KeyOf<T>() where T : IUserMessagePayload<T> => T.Key;

    private static int Arrival => Level.Rooms.Single(r => r.Role == LevelRoomRole.Arrival).Id;

    private static int Exit => Level.Exit!.Room;

    private static int Neighbour(int room) => Level.Neighbours(room).First(n => n != Exit);

    [Fact]
    public async Task A_room_a_member_walks_into_reaches_the_other_members_client()
    {
        await Party(A, B);

        StandIn(A, Arrival);

        Assert.Contains(Arrival, RoomsOf(B));
        Assert.Contains(Arrival, _automap.MapOf(B).RoomsSeen.Select(int.Parse));
    }

    [Fact]
    public async Task A_room_nobody_entered_reaches_no_member()
    {
        await Party(A, B);

        StandIn(A, Arrival);
        StandIn(A, Neighbour(Arrival));

        Assert.DoesNotContain(Exit, RoomsOf(B));
        Assert.Equal(2, RoomsOf(B).Count);
    }

    [Fact]
    public async Task A_strangers_client_is_sent_none_of_the_partys_rooms()
    {
        await Party(A, B);

        StandIn(A, Arrival);

        Assert.Empty(RoomsOf(C));
        Assert.Empty(_automap.MapOf(C).RoomsSeen);
    }

    [Fact]
    public async Task A_hero_who_joins_is_sent_the_partys_map_and_the_party_his()
    {
        int theirs = Neighbour(Arrival);
        await Party(A, B);
        StandIn(A, Arrival);
        StandIn(C, theirs);
        _at.Remove(HeroRig.PlayerOf(C));     // he stands nowhere now: whatever he is sent next is the party's
        Assert.DoesNotContain(Arrival, RoomsOf(C));

        await Party(A, B, C);

        Assert.Contains(Arrival, RoomsOf(C));
        Assert.Contains(theirs, RoomsOf(A));
        Assert.Contains(theirs, RoomsOf(B));
    }

    [Fact]
    public async Task One_who_leaves_keeps_what_he_was_shown_and_is_sent_no_more()
    {
        await Party(A, B, C);
        StandIn(A, Arrival);
        Assert.True((await _parties.Leave(Key(C))).Ok);

        StandIn(A, Neighbour(Arrival));

        Assert.Contains(Arrival, _automap.MapOf(C).RoomsSeen.Select(int.Parse));
        Assert.DoesNotContain(Neighbour(Arrival), RoomsOf(C));
        Assert.Contains(Neighbour(Arrival), RoomsOf(B));
    }

    [Fact]
    public async Task A_shrine_of_sight_reveals_the_level_to_the_takers_party()
    {
        await Party(A, B);

        _automap.RevealAll(A);

        Assert.Equal(Level.Rooms.Length, RoomsOf(B).Count);
        Assert.Empty(RoomsOf(C));
    }
}
