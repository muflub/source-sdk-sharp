using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Level;
using TF2.Descent.Tests.Horde;
using TF2.Descent.Tests.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The arena's layout as the horde takes it (the ARNA contract of 2026-11-16): a level that ships
/// one stands its boss on the layout's spot facing its yaw, and hands the horde its track and its
/// mouths; a level without one stands the boss as before (the arena's first pack marker).
/// </summary>
public sealed class ArenaLevelTests
{
    private static readonly LevelData Committed = LevelFixture.Committed().Read();

    private static int ArenaRoom => Committed.Rooms.Single(r => r.Role == LevelRoomRole.Treasure).Id;

    private static Vector SpotIn(int room)
    {
        var r = Committed.Rooms[room];
        return new Vector(r.Centre.X + 32, r.Centre.Y - 16, r.Min.Z + 8);
    }

    /// <summary>The committed level read as depth 5's, its treasure room the arena, with a layout when <paramref name="layout"/>.</summary>
    private static LevelData Level(ArenaLayout? layout) =>
        Committed with { Depth = 5, BossArena = ArenaRoom, Arena = layout };

    private static ArenaLayout Layout() => new(SpotIn(ArenaRoom), 135f,
        [new ArenaTrackPoint(new Vector(10, 20, 30), ArenaLayout.StationFlag), new ArenaTrackPoint(new Vector(40, 50, 60), 0),
         new ArenaTrackPoint(new Vector(70, 80, 90), ArenaLayout.StationFlag | ArenaLayout.FinalFlag)],
        [new ArenaMouth(new Vector(1, 2, 3), 90f, 1), new ArenaMouth(new Vector(4, 5, 6), 180f, 3)]);

    [Fact]
    public void A_shipped_layout_stands_the_boss_on_its_spot_facing_its_yaw()
    {
        var boss = LevelAdapters.ToHorde(Level(Layout()), "sir_nukesalot").Boss!;

        Assert.Equal(new AiVector(SpotIn(ArenaRoom).X, SpotIn(ArenaRoom).Y, SpotIn(ArenaRoom).Z), boss.Spot.Position);
        Assert.Equal(135f, boss.Spot.Yaw);
    }

    [Fact]
    public void The_roster_posts_the_boss_at_the_layout_s_spot_facing_its_yaw()
    {
        var level = LevelAdapters.ToHorde(Level(Layout()), "sir_nukesalot");
        var roster = RobotRoster.Create(TheRules, 66);

        roster.Load(level, 1);

        int row = Enumerable.Range(0, roster.Count).Single(roster.IsBoss);
        Assert.Equal(level.Boss!.Spot.Position, roster.Post(row));
        Assert.Equal(135f, roster.PostYaw(row));
    }

    [Fact]
    public void A_level_without_a_layout_stands_the_boss_on_the_arena_s_first_pack_marker()
    {
        var data = Level(null);
        var boss = LevelAdapters.ToHorde(data, "sir_nukesalot").Boss!;

        var first = data.In(ArenaRoom).First(m => m.Kind == LevelMarkerKind.PackSpawn);
        Assert.Equal(new AiVector(first.Position.X, first.Position.Y, first.Position.Z), boss.Spot.Position);
        Assert.Equal(first.Yaw, boss.Spot.Yaw);
        Assert.Null(boss.Layout);
    }

    [Fact]
    public void The_horde_s_boss_carries_the_track_in_file_order_with_its_flags()
    {
        var layout = LevelAdapters.ToHorde(Level(Layout()), "sir_nukesalot").Boss!.Layout!;

        Assert.Equal([new AiVector(10, 20, 30), new AiVector(40, 50, 60), new AiVector(70, 80, 90)], layout.Track.Select(p => p.Position));
        Assert.Equal([true, false, true], layout.Track.Select(p => p.Station));
        Assert.Equal([false, false, true], layout.Track.Select(p => p.Final));
    }

    [Fact]
    public void The_horde_s_boss_carries_the_mouths_with_their_legs()
    {
        var layout = LevelAdapters.ToHorde(Level(Layout()), "sir_nukesalot").Boss!.Layout!;

        Assert.Equal([(new AiVector(1, 2, 3), 90f, 1), (new AiVector(4, 5, 6), 180f, 3)], layout.Mouths.Select(m => (m.Position, m.Yaw, m.Leg)));
    }

    [Fact]
    public void The_boss_spot_of_a_level_with_a_layout_is_the_layout_s()
    {
        var spot = LevelAdapters.BossSpot(Level(Layout()))!.Value;

        Assert.Equal(135f, spot.Yaw);
        Assert.Equal(SpotIn(ArenaRoom).X, spot.Position.X);
    }

    internal static HordeArenaLayout HordeLayout(params (AiVector At, bool Station, bool Final)[] track) =>
        new([.. track.Select(t => new TankTrackPoint(t.At, t.Station, t.Final))], ImmutableArray<AddMouth>.Empty);
}
