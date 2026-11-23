using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Server.Automap;
using TF2.Descent.Server.Level;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Hero;
using TF2.Descent.Tests.Level;

using Xunit;

using AutomapRoomWire = TF2.Descent.Shared.AutomapRoom;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The boss on the automap (plan_descent_logic HO13, UI5's Boss mark): the arena, once revealed,
/// carries the boss's mark where it stands (the arena's first pack marker); no other room does,
/// and a level with no arena has none.
/// </summary>
public sealed class BossAutomapTests
{
    private static readonly LevelData Committed = LevelFixture.Committed().Read();

    private static readonly int ArenaRoom = Committed.Rooms.Single(r => r.Role == LevelRoomRole.Treasure).Id;

    private static string KeyOf<T>() where T : IUserMessagePayload<T> => T.Key;

    private sealed class Levels(LevelData level) : ILevelData
    {
        public LevelData? Current => level;
        public TF2.Descent.Ai.Level.HordeLevel? Horde => null;
    }

    private sealed class Kit
    {
        public readonly HeroRig Hero = new();
        public readonly RecordingEngine Wire = new();
        public readonly AutomapService Automap;
        public Vector At = new(-99_999, -99_999, -99_999);

        public Kit(LevelData level)
        {
            Level = level;
            Wire.ReturnsEachCall("Entities.GetOrigin", () => At);
            Automap = new AutomapService(new Levels(level), Hero.Heroes, Hero.Sheets, Wire, Wire, new Party.OneParty(),
                                         new Server.Party.InMemoryParties(Hero.Rules, new Party.PartyChanges().Hooks),
                                         new Server.Party.EnginePartySlots(Hero.Heroes, Hero.Keys()));
            Hero.Join();
        }

        public LevelData Level { get; }

        public void StandIn(int room)
        {
            var r = Level.Rooms[room];
            At = new Vector(r.Centre.X, r.Centre.Y, r.Min.Z + 16);
            Assert.Equal(room, Level.RoomAt(At));
            Automap.Poll();
        }

        public IReadOnlyList<AutomapRoomView> Rooms() =>
            [.. Wire.Calls.Where(c => c.Member == "UserMessages.Send" && c.ArgAs<string>(0) == KeyOf<AutomapRoomWire>()).Select(c =>
            {
                var reader = new UserMessageReader(c.ArgAs<byte[]>(4));
                return AutomapRoomWire.Read(ref reader).View();
            })];
    }

    [Fact]
    public void The_revealed_arena_carries_the_boss_s_mark_where_it_stands()
    {
        var k = new Kit(Committed with { Depth = 5, BossArena = ArenaRoom });

        k.StandIn(ArenaRoom);

        var arena = Assert.Single(k.Rooms(), r => r.Room == ArenaRoom);
        var spot = LevelAdapters.BossSpot(k.Level)!.Value;
        var mark = Assert.Single(arena.Marks, m => m.Kind == AutomapMarkKind.Boss);
        Assert.Equal(((int)MathF.Round(spot.Position.X), (int)MathF.Round(spot.Position.Y)), (mark.X, mark.Y));
    }

    [Fact]
    public void No_other_room_carries_the_boss_s_mark()
    {
        var k = new Kit(Committed with { Depth = 5, BossArena = ArenaRoom });
        int other = Committed.Rooms.First(r => r.Id != ArenaRoom).Id;

        k.StandIn(other);

        Assert.NotEmpty(k.Rooms());   // the stimulus: a room was sent
        Assert.DoesNotContain(k.Rooms().SelectMany(r => r.Marks), m => m.Kind == AutomapMarkKind.Boss);
    }

    /// <summary>Seen through a door, the arena is an outline: the boss's mark waits for a hero to enter it.</summary>
    [Fact]
    public void A_discovered_arena_carries_no_boss_s_mark()
    {
        var k = new Kit(Committed with { Depth = 5, BossArena = ArenaRoom });

        k.StandIn(Committed.Neighbours(ArenaRoom).First());

        var arena = Assert.Single(k.Rooms(), r => r.Room == ArenaRoom);
        Assert.True(arena.Discovered);
        Assert.Empty(arena.Marks);
    }

    [Fact]
    public void A_level_with_no_arena_marks_no_boss()
    {
        var k = new Kit(Committed);

        k.StandIn(ArenaRoom);

        Assert.NotEmpty(k.Rooms());
        Assert.DoesNotContain(k.Rooms().SelectMany(r => r.Marks), m => m.Kind == AutomapMarkKind.Boss);
    }
}
